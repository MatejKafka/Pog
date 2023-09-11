#pragma once

#include <system_error>
#include <string_view>
#include <Windows.h>
#include <array>

#ifndef NDEBUG
#define DBG_LOG(...) fwprintf(stderr, L"[LOG] " __VA_ARGS__)
#else
#define DBG_LOG(...) if (false) fwprintf(stderr, "[LOG] " __VA_ARGS__)
#endif

// https://artificial-mind.net/blog/2020/09/26/dont-deduce
// a "hack" to prevent the compiler from deducing template arguments
template<typename T>
using dont_deduce = typename std::common_type<T>::type;

std::system_error system_error_from_win32(std::string_view error_msg) {
    auto error = GetLastError();
    std::error_code ec((int)error, std::system_category());
    return {ec, error_msg.data()};
}

template<typename T>
static inline T check_error(const std::string& fn, dont_deduce<T> error_sentinel, T return_value) {
    if (return_value == error_sentinel) {
        throw system_error_from_win32(fn);
    }
    return return_value;
}

#define CHECK_ERROR(fn) check_error(#fn, nullptr, fn)
#define CHECK_ERROR_B(fn) check_error(#fn, FALSE, fn)
#define CHECK_ERROR_H(fn) check_error(#fn, INVALID_HANDLE_VALUE, fn)
#define CHECK_ERROR_V(V, fn) check_error(#fn, V, fn)


std::wstring wstring_from_utf8(const char* in) {
    int out_size = CHECK_ERROR_V(0, MultiByteToWideChar(CP_UTF8, 0, in, -1, nullptr, 0));
    std::wstring out(out_size, 0);
    MultiByteToWideChar(CP_UTF8, 0, in, -1, &out[0], out_size);
    return out;
}

// uses char instead of wchar_t, because exceptions always hold a char*
void show_error(const char* error_message) {
    if (CHECK_ERROR_H(GetStdHandle(STD_ERROR_HANDLE)) == nullptr) {
        // stderr not connected, show a message box
        MessageBoxA(nullptr, error_message, "Pog error", MB_OK | MB_ICONERROR);
    } else {
        // stderr is attached to something
        fprintf(stderr, "POG ERROR: %s\n", error_message);
    }
}

template<typename Thunk>
void panic_on_exception(Thunk thunk) {
    try {
        thunk();
    } catch (std::exception& e) {
        show_error(e.what());
        exit(100);
    } catch (...) {
        show_error("Unknown error");
        exit(100);
    }
}

/// Checks if enum f1 used as a bitfield contains some of the flags in f2.
template<typename EnumT>
inline bool has_flag(EnumT f1, EnumT f2) {
    using T = std::underlying_type_t<EnumT>;
    return ((T)f1 & (T)f2) != 0;
}

#define HAS_FLAG(e, f) has_flag(e, decltype(e)::f)

// align `ptr` up to get an address aligned to alignof(type)
#define ALIGN_UP(type, ptr) ((type*)((((uintptr_t)(ptr)) + (alignof(type) - 1)) & (~(alignof(type) - 1))))