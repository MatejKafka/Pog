#pragma once

#include <Windows.h>
#include "stdlib.hpp"
#include "Buffer.hpp"

// clang-format off
#ifndef NDEBUG
#include <cstdio>
#include <cwchar>
#define DBG_LOG(...) fwprintf(stderr, L"[LOG] " __VA_ARGS__)
#else
void ignore(auto...) {}
#define DBG_LOG(...) if (false) ignore(L"[LOG] " __VA_ARGS__)
#endif
// clang-format on

// https://artificial-mind.net/blog/2020/09/26/dont-deduce
// a "hack" to prevent the compiler from deducing template arguments
template<class T>
struct dont_deduce_t {
    using type = T;
};
template<class T>
using dont_deduce = typename dont_deduce_t<T>::type;


/// Checks if enum f1 used as a bitfield contains some of the flags in f2.
template<typename EnumT>
inline bool has_flag(EnumT f1, EnumT f2) {
    using T = __underlying_type (EnumT); // MSVC-specific
    return (static_cast<T>(f1) & static_cast<T>(f2)) != 0;
}

#define HAS_FLAG(e, f) has_flag(e, decltype(e)::f)

// align `ptr` up to get an address aligned to alignof(type)
#define ALIGN_UP(type, ptr_) ((type*)((((uintptr_t)(ptr_)) + (alignof(type) - 1)) & (~(alignof(type) - 1))))


inline CString utf8_from_wstring(const wchar_t* in) {
    int out_size = WideCharToMultiByte(CP_UTF8, 0, in, -1, nullptr, 0, nullptr, nullptr);
    if (out_size == 0) {
        abort();
    }
    CString out((size_t) out_size);
    WideCharToMultiByte(CP_UTF8, 0, in, -1, out.data(), (int) out.size(), nullptr, nullptr);
    return out;
}

inline void write_file_all(HANDLE handle, const void* buffer, size_t size) {
    do {
        DWORD bytes_written;
        if (!WriteFile(handle, buffer, (DWORD) size, &bytes_written, nullptr)) {
            abort(); // cannot show error message, writing to output failed
        }
        buffer = (const char*) buffer + bytes_written;
        size -= bytes_written;
    } while (size != 0);
}

inline void write_file_all(HANDLE handle, const wchar_t* str) {
    auto utf8_str = utf8_from_wstring(str);
    write_file_all(handle, utf8_str.data(), utf8_str.size_bytes());
}


inline void show_error(const wchar_t* error_message) {
    auto stderr_handle = GetStdHandle(STD_ERROR_HANDLE);
    if (stderr_handle == INVALID_HANDLE_VALUE || stderr_handle == nullptr) {
        // stderr not connected or opening failed, show a message box
        MessageBox(nullptr, error_message, L"Pog error", MB_OK | MB_ICONERROR);
    } else {
        // stderr is attached to something, either a file/pipe or a console

        // UTF8 is a more natural encoding, since we do not have to change console mode for it,
        //  and it works well even when redirected or over SSH
        // set output mode to UTF8, ignore failures
        (void) SetConsoleOutputCP(CP_UTF8);
        write_file_all(stderr_handle, "POG ERROR: ", 11);
        write_file_all(stderr_handle, error_message);
        write_file_all(stderr_handle, "\n", 1);
    }
}

[[noreturn]] inline void panic(const wchar_t* error_message) {
    show_error(error_message);
    abort();
}

template<typename T>
inline T check_error(const wchar_t* fn, dont_deduce<T> error_sentinel, T return_value) {
    if (return_value == error_sentinel) {
        panic(fn);
    }
    return return_value;
}

#define CHECK_ERROR_V(sentinel, fn) check_error(L#fn, sentinel, fn)
#define CHECK_ERROR(fn) CHECK_ERROR_V(nullptr, fn)
#define CHECK_ERROR_B(fn) CHECK_ERROR_V(FALSE, fn)
