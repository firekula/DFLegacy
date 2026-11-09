#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    bool InstallHudPortraitCache(unsigned char* image, std::size_t bytes) noexcept;

    namespace hud_portrait_detail
    {
        using PatchWriter = bool (*)(std::uintptr_t address, const void* bytes, std::size_t size);
        bool InstallImage(unsigned char* image, std::size_t bytes, PatchWriter write) noexcept;
    }
}
