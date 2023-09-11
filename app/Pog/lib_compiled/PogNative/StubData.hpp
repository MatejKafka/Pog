#pragma once

#include <Windows.h>
#include <cstdint>
#include <span>
#include <string_view>
#include <optional>
#include <iostream>
#include "util.hpp"

using StubDataBuffer = std::span<const std::byte>;

inline StubDataBuffer load_stub_data() {
    auto resource_handle = CHECK_ERROR(FindResource(nullptr, MAKEINTRESOURCE(1), RT_RCDATA));
    auto loaded_resource = CHECK_ERROR(LoadResource(nullptr, resource_handle));

    auto resource_ptr = CHECK_ERROR(LockResource(loaded_resource));
    auto resource_size = CHECK_ERROR_V(0, SizeofResource(nullptr, resource_handle));

    return {(std::byte*)resource_ptr, resource_size};
}

template<typename Callback>
concept WcharPtrCallback = requires(Callback cb, const wchar_t* str) { cb(str); };
template<typename Callback>
concept EnvironmentVariableCallback = requires(Callback cb, const wchar_t* str) { cb(str, str); };

enum class StubFlag : uint16_t { REPLACE_ARGV0 = 1 };
enum class EnvVarTokenFlag : uint16_t { ENV_VAR_NAME = 1, NEW_LIST_ITEM = 2, LAST_SEGMENT = 4, };

struct StubHeader {
    uint16_t version;
    StubFlag flags;
    uint32_t target_offset;
    uint32_t working_directory_offset;
    uint32_t argument_offset;
    uint32_t environment_offset;
};
// ensure that no padding is added
static_assert(sizeof(StubHeader) == 2 * 2 + 4 * 4);

class StubDataEnvironmentVariable {
private:
    // do not make this struct packed, since then alignof() is 1, which breaks `.next()`
    struct EnvSegmentHeader {
        uint32_t size;
        EnvVarTokenFlag flags;

        [[nodiscard]] const wchar_t* str() const {
            // return pointer after the last member
            return (const wchar_t*)(&flags + 1);
        }

        [[nodiscard]] std::wstring_view str_view() const {
            return {str(), size};
        }

        [[nodiscard]] const EnvSegmentHeader* next() const {
            // ensure that the header is correctly aligned, serializer should insert padding so that this works
            return ALIGN_UP(const EnvSegmentHeader, str() + size + 1); // +1 to skip null terminator
        }
    };

private:
    const wchar_t* var_name;
    const EnvSegmentHeader* first_segment;

public:
    StubDataEnvironmentVariable(const wchar_t* var_name, void* start_ptr)
            : var_name(var_name), first_segment((const EnvSegmentHeader*)start_ptr) {}

    template<typename Callback>
    void get_value(Callback value_cb) requires WcharPtrCallback<Callback> {
        // fast path for the most common case of a single segment
        if (HAS_FLAG(first_segment->flags, LAST_SEGMENT)) {
            get_single_segment_value(value_cb);
            return;
        }

        // output buffer
        std::wstring out{};
        // these booleans track whether we need to add the PATH separator when we write something
        auto prev_empty = true;
        auto cur_empty = true;

        auto append = [&](std::wstring_view str) {
            if (!prev_empty && cur_empty) {
                out += L";";
            }
            out += str;
            cur_empty = false;
        };

        // iterate over the segments and build the env var value
        for (auto it = first_segment;; it = it->next()) {
            DBG_LOG(L"- env segment: size=%u flags=%hu str=%ls\n", it->size, it->flags, it->str());

            if (HAS_FLAG(it->flags, NEW_LIST_ITEM)) {
                // start new segment
                prev_empty = prev_empty && cur_empty;
                cur_empty = true;
            }

            if (HAS_FLAG(it->flags, ENV_VAR_NAME)) {
                read_env_var(it->str(), append);
            } else {
                append(it->str_view());
            }

            if (HAS_FLAG(it->flags, LAST_SEGMENT)) {
                break;
            }
        }

        // call the callback with the composed value
        value_cb(out.c_str());
    }

private:
    template<typename Callback>
    void get_single_segment_value(Callback value_cb) {
        if (HAS_FLAG(first_segment->flags, ENV_VAR_NAME)) {
            if (!read_env_var(var_name, [&](auto env_value) { value_cb(env_value.data()); })) {
                value_cb(L""); // env var does not exist
            }
        } else {
            value_cb(first_segment->str());
        }
    }

    template<typename Callback>
    static bool read_env_var(const wchar_t* var_name, Callback callback) {
        constexpr size_t env_var_buffer_size = 32'767; // max size of env var
        wchar_t env_var_buffer[env_var_buffer_size];
        auto ret = GetEnvironmentVariable(var_name, env_var_buffer, env_var_buffer_size);
        if (ret != 0) {
            callback(std::wstring_view{env_var_buffer, ret});
            return true;
        } else {
            if (GetLastError() == ERROR_ENVVAR_NOT_FOUND) {
                return false;
            } else {
                throw system_error_from_win32("read_env_var");
            }
        }
    }
};

// We intentionally don't do any bound checking. The worst that can happen is that the stub will crash...
class StubData {
private:
    const StubDataBuffer buffer;

public:
    explicit StubData(const StubDataBuffer& buffer) : buffer(buffer) {}

    [[nodiscard]] uint32_t version() const {
        return header().version;
    }

    [[nodiscard]] StubFlag flags() const {
        return header().flags;
    }

    [[nodiscard]] const wchar_t* get_target() const {
        return read_wstring(header().target_offset);
    }

    [[nodiscard]] const wchar_t* get_working_directory() const {
        if (header().working_directory_offset == 0) return nullptr;
        return read_wstring(header().working_directory_offset);
    }

    [[nodiscard]] std::optional<std::wstring_view> get_arguments() const {
        if (header().argument_offset == 0) return std::nullopt;
        // arguments are stored as length-prefixed wchar buffer
        return std::wstring_view{read_wstring(header().argument_offset + sizeof(uint32_t)),
                                 read_uint(header().argument_offset)};
    }

    template<typename Callback>
    void enumerate_environment_variables(Callback callback) const requires EnvironmentVariableCallback<Callback> {
        if (header().environment_offset == 0) return;
        auto count = read_uint(header().environment_offset);

        auto it = (uint32_t*)&buffer[header().environment_offset + sizeof(uint32_t)];
        auto end = it + count * 2;
        while (it != end) {
            auto* name = read_wstring(*it++);
            auto value_offset = *it++;
            StubDataEnvironmentVariable{name, (void*)&buffer[value_offset]}.get_value([&](auto value) {
                callback(name, value);
            });
        }
    }

private:
    [[nodiscard]] const StubHeader& header() const {
        return *(const StubHeader*)buffer.data();
    }

    [[nodiscard]] const wchar_t* read_wstring(size_t offset) const {
        return (const wchar_t*)&buffer[offset];
    }

    [[nodiscard]] uint32_t read_uint(size_t offset) const {
        return *(const uint32_t*)&buffer[offset];
    }
};
