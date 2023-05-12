#pragma once

#include <cstddef>
#include <comutil.h>

#ifdef __cplusplus
#define EXPORT extern "C" [[maybe_unused]] __stdcall  __declspec(dllexport)
#else
#define EXPORT __declspec(dllexport)
#endif


EXPORT BSTR prepare_stub_executable_resources(const wchar_t* stub_path, const wchar_t* target_path,
                                              const void* stub_data, size_t stub_data_size);

EXPORT BSTR read_stub_data(const wchar_t* stub_path, void** stub_library_handle,
                           void** stub_data, size_t* stub_data_size);

EXPORT void close_stub_data(void* stub_library_handle);


#undef LIBRARY