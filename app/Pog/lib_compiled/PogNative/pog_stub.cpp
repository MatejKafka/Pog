// heavily inspired by https://github.com/schemea/scoop-better-shim/blob/master/shim.c

#include <Windows.h>
#include <cassert>
#include "StubData.hpp"
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

struct c_wstring {
    size_t size;
    wchar_t* str;

    explicit c_wstring(size_t size) : size(size), str(new wchar_t[size]) {}

    c_wstring(c_wstring&& s) noexcept: size(s.size), str(s.str) {
        s.size = 0;
        s.str = nullptr;
    }

    ~c_wstring() {
        delete[] str;
    }
};

const wchar_t* find_argv0_end(const wchar_t* cmd_line) {
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

c_wstring build_command_line(std::optional<std::wstring_view> prefixed_args, const wchar_t* target_override = nullptr) {
    const wchar_t* const_cmd_line = GetCommandLine();
    auto* const_argv0_end = find_argv0_end(const_cmd_line);
    auto argv0_len = target_override ? wcslen(target_override) : const_argv0_end - const_cmd_line;
    auto args_len = wcslen(const_argv0_end);

    c_wstring cmd_line{argv0_len + (prefixed_args ? 1 + prefixed_args->size() : 0) + args_len + 1};

    // build the command line
    auto it_out = cmd_line.str;
    // copy argv[0], or insert target_override
    it_out = target_override ? std::copy(target_override, target_override + argv0_len, it_out)
                             : std::copy(const_cmd_line, const_argv0_end, it_out);
    if (prefixed_args) {
        // add space
        *it_out++ = L' ';
        // copy prefixed_args
        it_out = std::copy(prefixed_args.value().cbegin(), prefixed_args.value().cend(), it_out);
    }
    // copy remaining args, they're already prefixed with a whitespace and suffixed with null
    it_out = std::copy(const_argv0_end, const_argv0_end + args_len + 1, it_out);

    assert(cmd_line.str + cmd_line.size == it_out);
    return cmd_line;
}

HANDLE create_child_job() {
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

struct ChildHandles {
    HANDLE job;
    HANDLE process;

    void close_all() const {
        CHECK_ERROR_B(CloseHandle(process));
        CHECK_ERROR_B(CloseHandle(job));
    }
};

ChildHandles run_target(const wchar_t* target, wchar_t* command_line, const wchar_t* working_directory) {
    STARTUPINFO startup_info{.cb = sizeof(startup_info)};
    PROCESS_INFORMATION process_info{};
    DWORD flags = INHERIT_PARENT_AFFINITY | CREATE_SUSPENDED;

    // assign the child to a job so that the child is killed if this stub process dies
    auto job_handle = create_child_job();
    CHECK_ERROR_B(CreateProcess(target, command_line, nullptr, nullptr, true, flags,
                                nullptr, working_directory, &startup_info, &process_info));

    // assign the child to the job and resume it
    CHECK_ERROR_B(AssignProcessToJobObject(job_handle, process_info.hProcess));
    CHECK_ERROR_V((DWORD)-1, ResumeThread(process_info.hThread));
    // we don't need the thread handle anymore, close it
    CHECK_ERROR_B(CloseHandle(process_info.hThread));

    return {.job = job_handle, .process = process_info.hProcess};
}

[[noreturn]] void real_main() {
    StubDataBuffer stub_data_buffer;
    try {
        stub_data_buffer = load_stub_data();
    } catch (std::system_error&) {
        throw std::exception("Pog stub not configured yet.");
    }

    StubData stub_data{stub_data_buffer};

    if (stub_data.version() != 1) {
        throw std::exception("Unknown Pog stub data version.");
    }

    auto target = stub_data.get_target();
    auto working_dir = stub_data.get_working_directory();
    auto extra_args = stub_data.get_arguments();
    auto cmd_line = build_command_line(extra_args, stub_data.flags() & StubFlag::REPLACE_ARGV0 ? target : nullptr);

    DBG_LOG(L"override argv: %ls\n", stub_data.flags() & StubFlag::REPLACE_ARGV0 ? L"yes" : L"no");
    DBG_LOG(L"target: %ls\n", target);
    DBG_LOG(L"command line: %ls\n", cmd_line.str);
    if (working_dir) {
        DBG_LOG(L"working directory: %ls\n", working_dir);
    }

    // set extracted environment variables to our environment
    stub_data.enumerate_environment_variables(SetEnvironmentVariable);

    // ignore signals, let the child handle them
    CHECK_ERROR_B(SetConsoleCtrlHandler(ctrl_handler, TRUE));

    // run the target
    auto handles = run_target(target, cmd_line.str, working_dir);

    // wait until the child stops
    CHECK_ERROR_V((DWORD)-1, WaitForSingleObject(handles.process, INFINITE));
    // retrieve the exit code
    DWORD exit_code;
    CHECK_ERROR_B(GetExitCodeProcess(handles.process, &exit_code));

    // clean up handles
    handles.close_all();

    // forward the exit code
    ExitProcess(exit_code);
}

int wmain() {
    panic_on_exception(real_main);
    return 0;
}
