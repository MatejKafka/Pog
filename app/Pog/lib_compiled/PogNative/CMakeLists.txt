cmake_minimum_required(VERSION 3.15)
project(PogNative)

set(CMAKE_CXX_STANDARD 20)

add_compile_definitions(UNICODE _UNICODE)
add_compile_definitions(WIN32_LEAN_AND_MEAN)

add_library(PogNative SHARED lib/PogNative.cpp)

set_target_properties(PogNative PROPERTIES RUNTIME_OUTPUT_DIRECTORY_RELEASE "../..")