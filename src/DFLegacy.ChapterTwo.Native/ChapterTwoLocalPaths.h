#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    // Must run before the native CRT and the first client-path resolution.
    // Relative paths keep the isolated client CWD; absolute session paths pass through.
    bool InstallLocalPaths(unsigned char* image,std::size_t bytes) noexcept;
    namespace local_paths_detail
    {
        using PatchWriter=bool(*)(std::uintptr_t,const void*,std::size_t);
        bool InstallImage(unsigned char* image,std::size_t bytes,PatchWriter write) noexcept;
    }
}
