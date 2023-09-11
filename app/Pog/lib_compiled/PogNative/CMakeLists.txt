cmake_minimum_required(VERSION 3.15)
project(PogNative)

set(CMAKE_CXX_STANDARD 20)

add_compile_definitions(UNICODE _UNICODE)
add_compile_definitions(WIN32_LEAN_AND_MEAN NOMINMAX)

# enable more warnings
add_compile_options(/W4)
# do not generate a manifest, we overwrite it anyway
add_link_options(/MANIFEST:NO)

# link msvc runtime library statically
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
# enable LTO
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION TRUE)

# use sanitizers in debug build
set(CMAKE_CXX_FLAGS_DEBUG "${CMAKE_CXX_FLAGS_DEBUG} /fsanitize=address")


add_executable(PogExecutableStubTemplate pog_stub.cpp)
# export release build binary to lib_compiled dir
set_target_properties(PogExecutableStubTemplate PROPERTIES RUNTIME_OUTPUT_DIRECTORY_RELEASE "../..")
