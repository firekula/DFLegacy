#pragma once
#include <cstddef>
namespace dfl::client_paths
{
    bool BuildClientPath(const wchar_t* leaf,wchar_t* output,std::size_t capacity) noexcept;
    bool BuildLogPath(const wchar_t* leaf,wchar_t* output,std::size_t capacity) noexcept;
}
