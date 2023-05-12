#pragma once

#include <system_error>
#include <string_view>
#include <windows.h>
#include <comutil.h>

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
#define CHECK_ERROR_V(V, fn) check_error(#fn, V, fn)


std::wstring wstring_from_utf8(const char* in) {
    int out_size = CHECK_ERROR_V(0, MultiByteToWideChar(CP_UTF8, 0, in, -1, nullptr, 0));
    std::wstring out(out_size, 0);
    MultiByteToWideChar(CP_UTF8, 0, in, -1, &out[0], out_size);
    return out;
}

template<typename Thunk>
BSTR wrap_pog_api(Thunk thunk) {
    try {
        thunk();
        return nullptr;
    } catch (std::exception& e) {
        // 2 copies, but we shouldn't hit this too often
        auto what_wide = wstring_from_utf8(e.what());
        return SysAllocString(what_wide.c_str());
    } catch (...) {
        return SysAllocString(L"Unknown error");
    }
}