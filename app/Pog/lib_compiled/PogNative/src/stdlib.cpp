// This file is compiled and linked in non-debug builds and provides replacements for some of the primitives
// normally implemented in CRT, which we don't link to keep the binary size small.

#include <Windows.h>
#include <cstddef>
#include "util.hpp"

extern "C" __declspec(noreturn) void abort() {
    ExitProcess(100);
}

void* operator new(size_t count) {
    return CHECK_ERROR(HeapAlloc(GetProcessHeap(), HEAP_NO_SERIALIZE, count));
}

void* operator new[](size_t count) {
    return CHECK_ERROR(HeapAlloc(GetProcessHeap(), HEAP_NO_SERIALIZE, count));
}

void operator delete(void* ptr) noexcept {
    CHECK_ERROR_B(HeapFree(GetProcessHeap(), HEAP_NO_SERIALIZE, ptr));
}

void operator delete(void* ptr, size_t) noexcept {
    CHECK_ERROR_B(HeapFree(GetProcessHeap(), HEAP_NO_SERIALIZE, ptr));
}

void operator delete[](void* ptr) noexcept {
    CHECK_ERROR_B(HeapFree(GetProcessHeap(), HEAP_NO_SERIALIZE, ptr));
}


extern int wmain();

// we don't have the luxury of a `main` here
[[maybe_unused]] int wmainCRTStartup() {
    ExitProcess((DWORD) wmain());
}
