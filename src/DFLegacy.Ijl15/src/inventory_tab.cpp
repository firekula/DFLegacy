#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include "inventory_tab.h"
#include "memory_patch.h"
#include <array>
#include <cstring>

namespace
{
    constexpr std::uintptr_t RefreshCallOffset = 0x1D1FB;
    // NOTI13 full-list insertion: push autoSelect; push item; push slot;
    // ECX=inventory; call45E280. Validate the complete original call target.
    constexpr std::array<unsigned char, 28> Original{
        0x6A,0x01,0x8B,0x8D,0x98,0xEF,0xFF,0xFF,0x51,
        0x0F,0xB7,0x95,0xAC,0xEF,0xFF,0xFF,0x52,
        0x8B,0x8D,0xB8,0xEF,0xFF,0xFF,0xE8,0x69,0x10,0x04,0x00};

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
        if (!image || !write || image > UINT32_MAX - RefreshCallOffset - Original.size())
            return false;
        const auto site = image + RefreshCallOffset;
        auto installed = Original;
        installed[1] = 0;
        if (Matches(site, installed.data())) return true;
        if (!Matches(site, Original.data())) return false;
        // Only full-list auto-selection changes. Native model/owner updates,
        // constructor tab0, clicks, drag and other insertion callers survive.
        if (!write(site + 1, &installed[1], 1))
        {
            // The writer may fail after a protection/cache operation has written.
            write(site + 1, &Original[1], 1);
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
