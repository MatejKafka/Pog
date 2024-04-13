cmake_minimum_required(VERSION 3.15)
project(PogNative)

set(CMAKE_CXX_STANDARD 20)

add_compile_definitions(UNICODE _UNICODE)
add_compile_definitions(WIN32_LEAN_AND_MEAN NOMINMAX)

# enable more warnings
add_compile_options(/W4)
# do not generate a manifest, we overwrite it anyway
add_link_options(/MANIFEST:NO)

# enable LTO
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION TRUE)
# link msvc runtime library statically
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

if(NOT (CMAKE_BUILD_TYPE STREQUAL "Debug"))
    # in release builds, we want to cut down the stub size as much as we can, since it's copied around a lot

    # do not link CRT (it blows up the binary size from 8 kB to 114 kB)
    add_link_options(/NODEFAULTLIB)

    # disable RTTI and exception support (reduces binary size by a bit)
    add_compile_options(/GR- /EHs- /EHc-)

    # disable stack protector and stack paging code; since we don't link CRT, which provides the runtime implementation
    #  for these features, we'd have to implement them ourselves (search keyword: __chkstk)
    add_compile_options(/GS- /Gs9999999)
    # since we disable __chkstk, we must allocate the whole stack upfront, 128 kB should be plenty
    add_link_options(/STACK:0x20000,0x20000)
else()
    # use ASAN in debug build
    add_compile_options(/fsanitize=address)
    add_link_options(/fsanitize=address)
endif()

# when compiling outside of debug mode, include our stdlib.cpp polyfill
add_executable(PogExecutableStubTemplate src/pog_stub.cpp $<$<NOT:$<CONFIG:Debug>>:src/stdlib.cpp>)
# export release build binary to lib_compiled dir
set_target_properties(PogExecutableStubTemplate PROPERTIES RUNTIME_OUTPUT_DIRECTORY_RELEASE "../..")
