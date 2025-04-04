// inspired by https://github.com/schemea/scoop-better-shim/blob/master/shim.c

#include <Windows.h>
#include <cassert>
#include "ShimData.hpp"
#include "Buffer.hpp"
#include "stdlib.hpp"
#include "util.hpp"

// disable argv and envp parsing, we don't need it
// https://learn.microsoft.com/en-us/previous-versions/zay8tzh6(v=vs.85)
// https://github.com/icidicf/library/blob/267ca3c87b44ccbf4eaa6b8e7416f3e38b269332/microsoft/CRT/SRC/INTERNAL.H#L499
extern "C" [[maybe_unused]] void __cdecl _setargv() {}
extern "C" [[maybe_unused]] void __cdecl _wsetargv() {}
extern "C" [[maybe_unused]] void __cdecl _setenvp() {}
extern "C" [[maybe_unused]] void __cdecl _wsetenvp() {}

BOOL WINAPI ctrl_handler(DWORD ctrl_type) {
    // ignore all events, and let the child process handle them
    switch (ctrl_type) {
        case CTRL_C_EVENT:
        case CTRL_CLOSE_EVENT:
        case CTRL_LOGOFF_EVENT:
        case CTRL_BREAK_EVENT:
        case CTRL_SHUTDOWN_EVENT:
            return TRUE;
        default:
            return FALSE;
    }
}

static const wchar_t* find_argv0_end(const wchar_t* cmd_line) {
    // https://learn.microsoft.com/en-us/cpp/c-language/parsing-c-command-line-arguments?view=msvc-170
    // The first argument (argv[0]) is treated specially. It represents the program name. Because it
    // must be a valid pathname, parts surrounded by double quote marks (") are allowed. The double
    // quote marks aren't included in the argv[0] output. The parts surrounded by double quote marks
    // prevent interpretation of a space or tab character as the end of the argument.

    // CommandLineToArgvW treats whitespace outside of quotation marks as argument delimiters.
    // However, if lpCmdLine starts with any amount of whitespace, CommandLineToArgvW will consider
    // the first argument to be an empty string. Excess whitespace at the end of lpCmdLine is ignored.

    // find the end of argv[0]
    auto inside_quotes = false;
    auto it = cmd_line;
    for (; *it != 0; it++) {
        if (*it == L'"') {
            inside_quotes = !inside_quotes;
            continue;
        }
        if (!inside_quotes && (*it == L' ' || *it == L'\t')) {
            // found the end
            break;
        }
    }
    return it;
}

static CWString build_command_line(optional<wstring_view> prefixed_args, const wchar_t* target_override = nullptr) {
    const wchar_t* const_cmd_line = GetCommandLine();
    auto* const_argv0_end = find_argv0_end(const_cmd_line);
    auto argv0_len = target_override ? wstr_size(target_override) : const_argv0_end - const_cmd_line;
    auto args_len = wstr_size(const_argv0_end);

    // allocate the cmd_line buffer
    CWString cmd_line{argv0_len + (target_override ? 2 : 0) // +2 for quotes around `target_override`
                      + (prefixed_args ? 1 + prefixed_args->size() : 0) + args_len + 1};

    // build the command line
    auto it_out = cmd_line.data();

    // copy argv[0], or insert target_override
    if (target_override) {
        // quote `target_override`, in case it contains spaces; when .cmd files are invoked, cmd.exe looks at argv[0],
        //  not on the lpApplicationName value, and spaces would throw it off without quotes
        *it_out++ = L'"';
        it_out = copy(target_override, target_override + argv0_len, it_out);
        *it_out++ = L'"';
    } else {
        it_out = copy(const_cmd_line, const_argv0_end, it_out);
    }

    if (prefixed_args) {
        // add space
        *it_out++ = L' ';
        // copy prefixed_args
        it_out = copy(prefixed_args->begin(), prefixed_args->end(), it_out);
    }
    // copy remaining args, they're already prefixed with a whitespace and suffixed with null
    it_out = copy(const_argv0_end, const_argv0_end + args_len + 1, it_out);

    assert(cmd_line.data() + cmd_line.size() == it_out);
    return cmd_line;
}

static HANDLE create_child_job() {
    // extended limit info must be used to set the LimitFlags
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION job_info{
        .BasicLimitInformation = {
            .LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                          JOB_OBJECT_LIMIT_BREAKAWAY_OK | JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK
        }
    };

    auto job_handle = CHECK_ERROR(CreateJobObject(nullptr, nullptr));
    CHECK_ERROR_B(SetInformationJobObject(job_handle, JobObjectExtendedLimitInformation,
                                          &job_info, sizeof(job_info)));
    return job_handle;
}

class ProcThreadAttributeList {
private:
    byte* attribute_list_;
public:
    ProcThreadAttributeList() {
        size_t size;
        // get the attribute list size; do not check for error here, it's expected that we'll get one
        InitializeProcThreadAttributeList(nullptr, 1, 0, &size);

        attribute_list_ = new byte[size];
        CHECK_ERROR_B(InitializeProcThreadAttributeList(*this, 1, 0, &size));
    }

    ~ProcThreadAttributeList() {
        DeleteProcThreadAttributeList(*this);
        delete[] attribute_list_;
    }

    operator LPPROC_THREAD_ATTRIBUTE_LIST() { // NOLINT(google-explicit-constructor)
        return (LPPROC_THREAD_ATTRIBUTE_LIST) attribute_list_;
    }

    void add_attribute(DWORD_PTR attribute, void* attr_value, size_t attr_size) {
        CHECK_ERROR_B(UpdateProcThreadAttribute(*this, 0, attribute, attr_value, attr_size, nullptr, nullptr));
    }
};

struct ChildHandles {
    HANDLE job;
    HANDLE process;

    void close_all() const {
        CHECK_ERROR_B(CloseHandle(process));
        CHECK_ERROR_B(CloseHandle(job));
    }
};

static ChildHandles run_target(const wchar_t* target, wchar_t* command_line, const wchar_t* working_directory) {
    // create a job object to wrap the child in
    auto job_handle = create_child_job();

    // assign the spawned process to the job (https://devblogs.microsoft.com/oldnewthing/20230209-00/?p=107812)
    ProcThreadAttributeList attr_list{};
    attr_list.add_attribute(PROC_THREAD_ATTRIBUTE_JOB_LIST, &job_handle, sizeof(job_handle));

    STARTUPINFOEX startup_info{.StartupInfo = {.cb = sizeof(startup_info)}, .lpAttributeList = attr_list};
    PROCESS_INFORMATION process_info;

    // spawn the process
    CHECK_ERROR_B(CreateProcess(
        target, command_line, nullptr, nullptr, true, INHERIT_PARENT_AFFINITY | EXTENDED_STARTUPINFO_PRESENT,
        nullptr, working_directory, &startup_info.StartupInfo, &process_info));

    // we don't need the thread handle, close it
    CHECK_ERROR_B(CloseHandle(process_info.hThread));

    return {.job = job_handle, .process = process_info.hProcess};
}

int wmain() {
    ShimDataBuffer shim_data_buffer = load_shim_data();
    ShimData shim_data{shim_data_buffer};

    if (shim_data.version() != 4) {
        panic(L"Incorrect Pog shim data version, this shim expects v4.");
    }

    auto flags = shim_data.flags();
    auto null_target = HAS_FLAG(flags, NULL_TARGET);
    auto replace_argv0 = HAS_FLAG(flags, REPLACE_ARGV0) || null_target;

    auto target = shim_data.get_target();
    auto working_dir = shim_data.get_working_directory();
    auto extra_args = shim_data.get_arguments();
    auto cmd_line = build_command_line(extra_args, replace_argv0 ? target : nullptr);

    if (null_target) {
        // null target makes CreateProcess parse lpCommandLine (`cmd_line`) and use argv[0] as target
        target = nullptr;
    }

    DBG_LOG(L"override argv[0]: %ls\n", replace_argv0 ? L"yes" : L"no");
    DBG_LOG(L"null target: %ls\n", null_target ? L"yes" : L"no");
    DBG_LOG(L"target: %ls\n", target);
    DBG_LOG(L"command line: %ls\n", cmd_line.data());
    if (working_dir) {
        DBG_LOG(L"working directory: %ls\n", working_dir);
    }

    // write extracted environment variables to our environment
    shim_data.enumerate_environment_variables([](auto name, auto value) {
        DBG_LOG(L"env var '%ls': %ls\n", name, value);
        CHECK_ERROR_B(SetEnvironmentVariable(name, value));
    });

    // ignore signals, let the child handle them
    CHECK_ERROR_B(SetConsoleCtrlHandler(ctrl_handler, TRUE));

    // run the target
    auto handles = run_target(target, cmd_line.data(), working_dir);

    // wait until the child stops
    CHECK_ERROR_V((DWORD) -1, WaitForSingleObject(handles.process, INFINITE));
    // retrieve the exit code
    DWORD exit_code;
    CHECK_ERROR_B(GetExitCodeProcess(handles.process, &exit_code));

    // clean up handles
    handles.close_all();

    // forward the exit code
    return (int) exit_code;
}
