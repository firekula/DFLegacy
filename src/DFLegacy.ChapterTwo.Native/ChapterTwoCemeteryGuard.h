#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    bool InstallCemeteryGuard(unsigned char* image, std::size_t size) noexcept;

    namespace cemetery_guard_detail
    {
        using PatchWriter = bool(*)(std::uintptr_t, const void*, std::size_t);
        bool InstallImage(unsigned char* image, std::size_t size, PatchWriter write) noexcept;
    }
}
