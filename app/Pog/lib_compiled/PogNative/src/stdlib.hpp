#pragma once

#include <cstddef>

using byte = unsigned char;

extern "C" __declspec(noreturn) void abort();

// clang-format off
template<class T> struct remove_pointer { using type = T; };
template<class T> struct remove_pointer<T*> { using type = T; };
template<class T> struct remove_pointer<T* const> { using type = T; };
template<class T> struct remove_pointer<T* volatile> { using type = T; };
template<class T> struct remove_pointer<T* const volatile> { using type = T; };
template<class T> using remove_pointer_t = typename remove_pointer<T>::type;

template<class T> struct remove_cv { typedef T type; };
template<class T> struct remove_cv<const T> { typedef T type; };
template<class T> struct remove_cv<volatile T> { typedef T type; };
template<class T> struct remove_cv<const volatile T> { typedef T type; };
template<class T> using remove_cv_t = typename remove_cv<T>::type;
// clang-format on

template<typename TPtr>
class span_impl {
private:
    using T = remove_pointer_t<TPtr>;

private:
    TPtr ptr_;
    size_t size_;

public:
    span_impl(TPtr ptr, size_t size) : ptr_{ptr}, size_{size} {}

    [[nodiscard]] const T& operator[](size_t i) const {
        return ptr_[i];
    }

    [[nodiscard]] TPtr data() const {
        return ptr_;
    }

    [[nodiscard]] size_t size() const {
        return size_;
    }

    [[nodiscard]] TPtr begin() const {
        return ptr_;
    }

    [[nodiscard]] TPtr end() const {
        return ptr_ + size_;
    }
};

template<typename T>
using span = span_impl<T*>;
using wstring_view = span_impl<const wchar_t*>;


struct nullopt_t {};
inline constexpr nullopt_t nullopt{};

template<typename T>
class optional {
private:
    struct nontrivial_dummy_type {
        // This default constructor is user-provided to avoid zero-initialization when objects are value-initialized.
        constexpr nontrivial_dummy_type() noexcept = default;
    };

private:
    union {
        nontrivial_dummy_type dummy_;
        remove_cv_t<T> value_;
    };
    bool has_value_;

public:
    optional() : dummy_{}, has_value_{false} {}

    optional(nullopt_t) : optional{} {} // NOLINT(*-explicit-constructor)

    optional(T&& value) : value_{static_cast<T&&>(value)}, has_value_{true} {} // NOLINT(*-explicit-constructor)

    ~optional() noexcept {
        if (has_value_) {
            value_.~T();
        }
    }

    operator bool() { // NOLINT(*-explicit-constructor)
        return has_value_;
    }

    T* operator->() {
        return &value_;
    }
};


template<class InputIt, class OutputIt>
inline OutputIt copy(InputIt first, InputIt last, OutputIt d_first) {
    for (; first != last; (void) ++first, (void) ++d_first) {
        *d_first = *first;
    }
    return d_first;
}

// implementation for `wcslen`, which is an intrinsic on some versions of MSVC, so we cannot redefine it
inline size_t wstr_size(const wchar_t* str) {
    DWORD size = 0;
    for (; str[size] != 0; size++) {}
    return size;
}
