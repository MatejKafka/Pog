#pragma once

#include <Windows.h>
#include <vector>
#include <span>
#include "util.hpp"

namespace PE::Resources {
    using RType = LPWSTR;
    using RName = LPWSTR;
    using ResourceID = uint16_t;


    class LibraryHandle {
    private:
        HMODULE handle;

    public:
        explicit LibraryHandle(const wchar_t* file_path)
                : handle(CHECK_ERROR(LoadLibrary(file_path))) {}

        // prevent copying
        LibraryHandle(const LibraryHandle&) = delete;
        LibraryHandle& operator=(const LibraryHandle&) = delete;

        operator HMODULE() const { // NOLINT(google-explicit-constructor)
            return handle;
        }

        ~LibraryHandle() {
            CHECK_ERROR_V(false, FreeLibrary(handle));
        }
    };


    class LibraryModule {
    private:
        LibraryHandle handle;

    public:
        explicit LibraryModule(const wchar_t* file_path) : handle(file_path) {}

        std::span<std::byte> load_resource(RType resource_type, RName resource_name) const {
            // locate the resource in the loaded module
            HRSRC resource_handle = CHECK_ERROR(FindResource(handle, resource_name, resource_type));
            // load the resource into global memory
            HGLOBAL loaded_resource = CHECK_ERROR(LoadResource(handle, resource_handle));

            // retrieve the resource pointer and size
            void* resource_ptr = CHECK_ERROR(LockResource(loaded_resource));
            auto resource_size = CHECK_ERROR_V(0, SizeofResource(handle, resource_handle));
            return {(std::byte*)resource_ptr, resource_size};
        }

        std::span<std::byte> load_resource(RType resource_type, ResourceID resource_id) const {
            return load_resource(resource_type, MAKEINTRESOURCE(resource_id));
        }

        template<typename Callback>
        void enumerate_resources(RType resource_type, Callback callback) {
            auto enum_fn = [](auto m, auto t, RName resource_name, LONG_PTR param) {
                Callback& cb = *(Callback*)std::bit_cast<void*>(param);
                cb(resource_name);
                return 1; // continue enumeration
            };

            // pass callback through the extra param and restore it in the callback lambda
            CHECK_ERROR_V(false, EnumResourceNames(handle, resource_type, enum_fn,
                                                    std::bit_cast<LONG_PTR>((void*)&callback)));
        }
    };


    class ResourceUpdater {
    private:
        HANDLE update_handle;

    public:
        // open the module
        explicit ResourceUpdater(const wchar_t* file_path, bool delete_existing_resources = false)
                : update_handle{
                CHECK_ERROR(BeginUpdateResource(file_path, delete_existing_resources))} {}

        template<typename T>
        void update_resource(RType resource_type, RName resource_name, std::span<T> resource) {
            auto lang_id = MAKELANGID(LANG_NEUTRAL, SUBLANG_NEUTRAL);
            CHECK_ERROR_V(false,
                          UpdateResource(update_handle, resource_type, resource_name,
                                         lang_id, (void*)resource.data(), resource.size_bytes()));
        }

        template<typename T>
        void update_resource(RType resource_type, ResourceID resource_id, std::span<T> resource) {
            return update_resource(resource_type, MAKEINTRESOURCE(resource_id), resource);
        }

        // apply the update and close the module
        void commit() {
            CHECK_ERROR_V(false, EndUpdateResource(update_handle, false));
            update_handle = INVALID_HANDLE_VALUE;
        }
    };
}