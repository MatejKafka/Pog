#pragma once

#include <Windows.h>
#include <cstddef>
#include <cstdint>
#include "stdlib.hpp"
#include "util.hpp"

// https://learn.microsoft.com/en-us/windows/win32/procthread/environment-variables, including null terminator
constexpr size_t MAX_ENV_VAR_SIZE = 32'768;
using ShimDataBuffer = span<const byte>;

inline ShimDataBuffer load_shim_data() {
    auto resource_handle = FindResource(nullptr, MAKEINTRESOURCE(1), RT_RCDATA);
    if (resource_handle == nullptr) {
        panic(L"Pog shim not configured yet.");
    }
    auto loaded_resource = CHECK_ERROR(LoadResource(nullptr, resource_handle));
    auto resource_ptr = CHECK_ERROR(LockResource(loaded_resource));
    auto resource_size = CHECK_ERROR_V(0, SizeofResource(nullptr, resource_handle));

    return {(const byte*) resource_ptr, resource_size};
}

template<typename Callback>
concept WcharPtrCallback = requires(Callback cb, const wchar_t* str) { cb(str); };
template<typename Callback>
concept EnvironmentVariableCallback = requires(Callback cb, const wchar_t* str) { cb(str, str); };

// for documentation of these enums, see `ShimDataEncoder.cs`
enum class ShimFlag : uint16_t { REPLACE_ARGV0 = 1, NULL_TARGET = 2, };
enum class EnvVarTokenFlag : uint16_t { ENV_VAR_NAME = 1, NEW_LIST_ITEM = 2, LAST_SEGMENT = 4, };

struct ShimHeader {
    uint16_t version;
    ShimFlag flags;
    uint32_t target_offset;
    uint32_t working_directory_offset;
    uint32_t argument_offset;
    uint32_t environment_offset;
};
// ensure that no padding is added
static_assert(sizeof(ShimHeader) == 2 * 2 + 4 * 4);

class ShimDataEnvironmentVariable {
private:
    // do not make this struct packed, since then alignof() is 1, which breaks `.next()`
    struct EnvSegmentHeader {
        uint32_t size;
        EnvVarTokenFlag flags;

        [[nodiscard]] const wchar_t* str() const {
            // return pointer after the last member
            return (const wchar_t*) (&flags + 1);
        }

        [[nodiscard]] wstring_view str_view() const {
            return {str(), size};
        }

        [[nodiscard]] const EnvSegmentHeader* next() const {
            // ensure that the header is correctly aligned, serializer should insert padding so that this works
            return ALIGN_UP(const EnvSegmentHeader, str() + size + 1); // +1 to skip null terminator
        }
    };

public:
    static void get_value(void* start_ptr, WcharPtrCallback auto value_cb) {
        auto first_segment = (const EnvSegmentHeader*) start_ptr;

        // fast path for the most common case of a single segment
        if (HAS_FLAG(first_segment->flags, LAST_SEGMENT)) {
            get_single_segment_value(first_segment, value_cb);
            return;
        }

        // output buffer
        wchar_t out[MAX_ENV_VAR_SIZE];
        wchar_t* out_end = out + MAX_ENV_VAR_SIZE;
        wchar_t* out_it = out;

        // these booleans track whether we need to add the PATH separator when we write something
        auto prev_empty = true;
        auto cur_empty = true;

        auto append = [&](wstring_view str) {
            if (str.size() == 0) {
                return;
            }

            auto needs_separator = !prev_empty && cur_empty;
            auto total_size = str.size() + (needs_separator ? 1 : 0);

            if (out_end - out_it < (ptrdiff_t)total_size) {
                panic(L"Interpolated environment variable too long.");
            }

            if (needs_separator) *out_it++ = L';';
            out_it = copy(str.begin(), str.end(), out_it);

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

        if (out_it == out_end) {
            panic(L"Interpolated environment variable too long.");
        }
        *out_it++ = L'\0';

        // call the callback with the composed value
        value_cb(out);
    }

private:
    static void get_single_segment_value(const EnvSegmentHeader* segment, auto value_cb) {
        if (HAS_FLAG(segment->flags, ENV_VAR_NAME)) {
            if (!read_env_var(segment->str(), [&](auto env_value) { value_cb(env_value.data()); })) {
                value_cb(L""); // env var does not exist
            }
        } else {
            value_cb(segment->str());
        }
    }

    static bool read_env_var(const wchar_t* var_name, auto callback) {
        wchar_t env_var_buffer[MAX_ENV_VAR_SIZE];
        auto ret = GetEnvironmentVariable(var_name, env_var_buffer, MAX_ENV_VAR_SIZE);
        if (ret != 0 && ret <= MAX_ENV_VAR_SIZE) {
            // ok
            callback(wstring_view{env_var_buffer, ret});
            return true;
        } else if (ret > MAX_ENV_VAR_SIZE) {
            // should not happen
            panic(L"Env var value is too long.");
        } else {
            // error
            if (GetLastError() == ERROR_ENVVAR_NOT_FOUND) {
                return false;
            } else {
                panic(L"read_env_var");
            }
        }
    }
};

// We intentionally don't do any bound checking. The worst that can happen is that the shim will crash...
class ShimData {
private:
    const ShimDataBuffer buffer;

public:
    explicit ShimData(const ShimDataBuffer& buffer) : buffer(buffer) {}

    [[nodiscard]] uint32_t version() const {
        return header().version;
    }

    [[nodiscard]] ShimFlag flags() const {
        return header().flags;
    }

    [[nodiscard]] const wchar_t* get_target() const {
        return read_wstring(header().target_offset);
    }

    [[nodiscard]] const wchar_t* get_working_directory() const {
        if (header().working_directory_offset == 0) return nullptr;
        return read_wstring(header().working_directory_offset);
    }

    [[nodiscard]] optional<wstring_view> get_arguments() const {
        if (header().argument_offset == 0) return nullopt;
        // arguments are stored as length-prefixed wchar buffer
        return wstring_view{read_wstring(header().argument_offset + sizeof(uint32_t)),
                            read_uint(header().argument_offset)};
    }

    void enumerate_environment_variables(EnvironmentVariableCallback auto callback) const {
        if (header().environment_offset == 0) return;
        auto count = read_uint(header().environment_offset);

        auto it = (const uint32_t*) &buffer[header().environment_offset + sizeof(uint32_t)];
        auto end = it + count * 2;
        while (it != end) {
            auto* name = read_wstring(*it++);
            auto value_offset = *it++;
            ShimDataEnvironmentVariable::get_value((void*) &buffer[value_offset], [&](auto value) {
                callback(name, value);
            });
        }
    }

private:
    [[nodiscard]] const ShimHeader& header() const {
        return *(const ShimHeader*) buffer.data();
    }

    [[nodiscard]] const wchar_t* read_wstring(size_t offset) const {
        return (const wchar_t*) &buffer[offset];
    }

    [[nodiscard]] uint32_t read_uint(size_t offset) const {
        return *(const uint32_t*) &buffer[offset];
    }
};
