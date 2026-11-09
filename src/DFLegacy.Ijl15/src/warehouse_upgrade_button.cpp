#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include "warehouse_upgrade_button.h"
#include "memory_patch.h"
#include <array>
#include <cstring>

namespace
{
    constexpr std::uintptr_t StoreOffset = 0x424C45;
    constexpr std::size_t ImmediateIndex = 9;
    // Original ACT1 storage slot-zero layout (8241D0) shows Upgrade below
    // grade 7, but 824C48 explicitly disables it. Restore only that immediate;
    // the native click handler, product selection and paid CMD67 remain intact.
    constexpr unsigned char RenderEntry[]{
        0x55,0x8B,0xEC,0x81,0xEC,0xB4,0x00,0x00,0x00,0x8B,0x45,0x08,0x53,0x8B,0xD9};
    constexpr unsigned char RenderSlot[]{0xD0,0x41,0x82,0x00};
    constexpr unsigned char GradeBranch[]{
        0x83,0xFE,0x07,0x7C,0x0F,0x8B,0x4B,0x38,0xC6,0x81,0x20,0x01,0x00,0x00,0x00,
        0xE9,0xB4,0x00,0x00,0x00,0x8B,0x53,0x38,0xC6,0x82,0x20,0x01,0x00,0x00,0x01};
    constexpr std::array<unsigned char, 10> OriginalStore{
        0x8B,0x4B,0x38,0xC6,0x81,0x21,0x01,0x00,0x00,0x00};
    constexpr unsigned char EventGate[]{
        0x8B,0x46,0x10,0x8B,0x88,0x24,0x0F,0x00,0x00,0x39,0x59,0x10,0x75,0x5A,
        0x8B,0x46,0x38,0x8A,0x88,0x21,0x01,0x00,0x00,0x05,0x10,0x01,0x00,0x00,
        0x84,0xC9,0x74,0x48,0x8B,0x15,0x84,0x51,0xB5,0x00,0x3B,0x50,0x0C,0x75,0x3D};
    constexpr unsigned char BusyGate[]{
        0x55,0x8B,0xEC,0x56,0x8B,0xF1,0x8A,0x86,0xD0,0x25,0x00,0x00,0x84,0xC0,
        0x0F,0x85,0x1E,0x02,0x00,0x00};
    struct Guard { std::uintptr_t Offset; const unsigned char* Bytes; std::size_t Size; };
    constexpr Guard Guards[]{
        {0x4241D0, RenderEntry, sizeof(RenderEntry)},
        {0x681E74, RenderSlot, sizeof(RenderSlot)},
        {0x424B87, GradeBranch, sizeof(GradeBranch)},
        {0x423D36, EventGate, sizeof(EventGate)},
        {0x464B60, BusyGate, sizeof(BusyGate)}};

    bool Matches(std::uintptr_t address, const unsigned char* bytes, std::size_t size) noexcept
    {
        __try { return std::memcmp(reinterpret_cast<const void*>(address), bytes, size) == 0; }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
}

namespace dfl::warehouse_upgrade_button
{
    bool detail::InstallImage(std::uintptr_t image, PatchWriter write) noexcept
    {
        if (!image || !write || image > UINT32_MAX - 0x681E74 - sizeof(RenderSlot))
            return false;
        for (const auto& guard : Guards)
            if (!Matches(image + guard.Offset, guard.Bytes, guard.Size)) return false;

        auto installed = OriginalStore;
        installed[ImmediateIndex] = 1;
        const auto site = image + StoreOffset;
        if (Matches(site, installed.data(), installed.size())) return true;
        // Includes the entire seven-byte C6 81 21 01 00 00 00 instruction.
        if (!Matches(site, OriginalStore.data(), OriginalStore.size())) return false;
        if (!write(site + ImmediateIndex, installed.data() + ImmediateIndex, 1))
        {
            // A writer can fail after changing bytes (cache/protection failure).
            // Revert only our immediate, leaving every original branch intact.
            write(site + ImmediateIndex, OriginalStore.data() + ImmediateIndex, 1);
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
