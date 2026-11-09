#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include "inventory_tab.h"
#include "memory_patch.h"
#include <array>
#include <cstring>

namespace
{
    constexpr std::uintptr_t SelectorTailOffset = 0x3AAB4D;
    constexpr std::size_t StoreOffset = 5;
    constexpr std::size_t StoreSize = 3;
    // Automatic selector 7AAAF0 is called by item insertion 45E280 and amount
    // increase 45CC10. Retain its computation/return and suppress only its final
    // mov [esi+3C],edx. The neighboring branch and ret4 must remain compatible.
    constexpr std::array<unsigned char, 13> Original{
        0x83,0xFA,0xFF,0x74,0x03,0x89,0x56,0x3C,0x5E,0x5D,0xC2,0x04,0x00};

    bool Matches(std::uintptr_t address, const unsigned char* bytes) noexcept
    {
        __try { return std::memcmp(reinterpret_cast<const void*>(address), bytes, Original.size()) == 0; }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
}

namespace dfl::inventory_tab
{
    bool detail::InstallImage(std::uintptr_t image, PatchWriter write) noexcept
    {
        if (!image || !write || image > UINT32_MAX - SelectorTailOffset - Original.size())
            return false;
        const auto site = image + SelectorTailOffset;
        auto installed = Original;
        for (std::size_t i = 0; i < StoreSize; ++i) installed[StoreOffset + i] = 0x90;
        if (Matches(site, installed.data())) return true;
        if (!Matches(site, Original.data())) return false;
        // Automatic synchronization retains the last user-selected tab whether
        // visible or hidden. Model/owner/amount updates and manual/drag writes
        // remain native; no window-visibility or additional state hook is needed.
        if (!write(site + StoreOffset, installed.data() + StoreOffset, StoreSize))
        {
            // The writer may fail after a protection/cache operation has written.
            write(site + StoreOffset, Original.data() + StoreOffset, StoreSize);
            return false;
        }
        return true;
    }

    bool Install() noexcept
    {
        const auto image = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
        return image == 0x00400000 && detail::InstallImage(image, memory_patch::WriteBytes);
    }
}
