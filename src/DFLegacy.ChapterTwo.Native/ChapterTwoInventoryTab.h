#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    bool InstallInventoryTabPreservation(unsigned char* image, std::size_t bytes) noexcept;

    namespace inventory_tab_detail
    {
        using PatchWriter = bool (*)(std::uintptr_t address, const void* bytes, std::size_t size);
        bool InstallImage(unsigned char* image, std::size_t bytes, PatchWriter write) noexcept;
    }
}
