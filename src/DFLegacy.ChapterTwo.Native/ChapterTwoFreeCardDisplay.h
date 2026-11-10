#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    bool InstallFreeCardDisplay(unsigned char* image, std::size_t bytes) noexcept;

    namespace free_card_detail
    {
        using PatchWriter = bool (*)(std::uintptr_t address, const void* data, std::size_t bytes);
        bool InstallImage(unsigned char* image, std::size_t bytes, PatchWriter write) noexcept;
    }
}
