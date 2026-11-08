cmake_minimum_required(VERSION 3.24)
project(CrashCaptureFixture LANGUAGES CXX)
set(CMAKE_MSVC_RUNTIME_LIBRARY MultiThreaded)
add_executable(DNF fixture.cpp)
