#pragma once

template<typename T>
class Buffer {
    T* buffer_;
    size_t size_;

public:
    explicit Buffer(size_t size) : buffer_(new T[size]), size_(size) {}

    Buffer(Buffer&& s) noexcept: buffer_(s.buffer_), size_(s.size_) {
        s.buffer_ = nullptr;
        s.size_ = 0;
    }

    ~Buffer() {
        delete[] buffer_;
    }

    [[nodiscard]] T* data() {
        return buffer_;
    }

    [[nodiscard]] const T* data() const {
        return buffer_;
    }

    [[nodiscard]] const T* begin() const {
        return buffer_;
    }

    [[nodiscard]] const T* end() const {
        return buffer_ + size_;
    }

    [[nodiscard]] size_t size() const {
        return size_;
    }

    [[nodiscard]] size_t size_bytes() const {
        return size_ * sizeof(T);
    }

    bool append(T elem) {

    }
};

using CString = Buffer<char>;
using CWString = Buffer<wchar_t>;
