cmake_minimum_required(VERSION 3.15)
project(PogNative)

set(CMAKE_CXX_STANDARD 20)

add_compile_definitions(UNICODE _UNICODE)
add_compile_definitions(WIN32_LEAN_AND_MEAN NOMINMAX)

# enable more warnings
add_compile_options(/W4)
add_link_options(/MANIFEST:NO)


add_executable(PogExecutableStubTemplate pog_stub.cpp)
# export release build binary to lib_compiled dir
set_target_properties(PogExecutableStubTemplate PROPERTIES RUNTIME_OUTPUT_DIRECTORY_RELEASE "../..")
