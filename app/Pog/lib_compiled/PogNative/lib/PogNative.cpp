#pragma once

#include <string>
#include <memory>
#include "PogNative.h"
#include "resource_lib.hpp"

[[maybe_unused]]
BSTR prepare_stub_executable_resources(const wchar_t* stub_path, const wchar_t* target_path,
                                       const void* stub_data, size_t stub_data_size) {
    return wrap_pog_api([&] {
        PE::Resources::LibraryModule src_module{target_path};
        PE::Resources::ResourceUpdater updater{stub_path};

        auto copy_resource_type = [&](auto type) {
            src_module.enumerate_resources(type, [&](auto name) {
                updater.update_resource(type, name, src_module.load_resource(type, name));
            });
        };

        // copy all icons and icon groups
        copy_resource_type(RT_ICON);
        copy_resource_type(RT_GROUP_ICON);
        // copy version info
        copy_resource_type(RT_VERSION);

        // add the stub data
        std::span<std::byte> resource{(std::byte*)stub_data, stub_data_size};
        updater.update_resource(RT_RCDATA, 1, resource);

        // update the stub
        updater.commit();
    });
}

[[maybe_unused]]
BSTR read_stub_data(const wchar_t* stub_path, void** stub_library_handle,
                    void** stub_data, size_t* stub_data_size) {
    return wrap_pog_api([&] {
        auto stub = std::make_unique<PE::Resources::LibraryModule>(stub_path);

        // TODO: figure out how to detect that the resource does not exist
        auto resource = stub->load_resource(RT_RCDATA, 1);

        // release the unique_ptr, caller is responsible for calling `close_stub_data`
        *stub_library_handle = reinterpret_cast<void*>(stub.release());
        *stub_data = resource.data();
        *stub_data_size = resource.size_bytes();
    });
}

[[maybe_unused]]
void close_stub_data(void* stub_library_handle) {
    delete reinterpret_cast<PE::Resources::LibraryModule*>(stub_library_handle);
}
