#pragma once

#include <Windows.h>
#include <cstdint>
#include <span>
#include <string_view>
#include <optional>
#include "util.hpp"

using StubDataBuffer = std::span<const std::byte>;

StubDataBuffer load_stub_data() {
    auto resource_handle = CHECK_ERROR(FindResource(nullptr, MAKEINTRESOURCE(1), RT_RCDATA));
    auto loaded_resource = CHECK_ERROR(LoadResource(nullptr, resource_handle));

    auto resource_ptr = CHECK_ERROR(LockResource(loaded_resource));
    auto resource_size = CHECK_ERROR_V(0, SizeofResource(nullptr, resource_handle));

    return {(std::byte*)resource_ptr, resource_size};
}

enum class StubFlag : uint32_t {
    NONE = 0,
    REPLACE_ARGV0 = 1,
};

bool operator&(StubFlag f1, StubFlag f2) {
    using T = std::underlying_type_t<StubFlag>;
    return ((T)f1 & (T)f2) != 0;
}

#pragma pack(push, 1)
struct StubHeader {
    uint32_t version;
    StubFlag flags;
    uint32_t target_offset;
    uint32_t working_directory_offset;
    uint32_t argument_offset;
    uint32_t environment_offset;
};
#pragma pack(pop)

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
    void enumerate_environment_variables(Callback callback) const {
        if (header().environment_offset == 0) return;
        auto count = read_uint(header().environment_offset);

        auto it = (uint32_t*)&buffer[header().environment_offset + sizeof(uint32_t)];
        auto end = it + count * 2;
        while (it != end) {
            auto* name = read_wstring(*it++);
            auto* value = read_wstring(*it++);
            callback(name, value);
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
