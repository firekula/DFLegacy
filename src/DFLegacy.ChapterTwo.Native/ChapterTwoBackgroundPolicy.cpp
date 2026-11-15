#include "ChapterTwoBackgroundPolicy.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <array>
#include <cstring>
#include <limits>

namespace
{
    constexpr std::size_t ImageSize = 0xe57000;
    // C2 A3CEE8: real activation flag, inactive Sleep, then native frame update.
    // Skip only Sleep; do not forge the activation flag used by native input.
    constexpr unsigned char MainLoop[]{
        0x8b,0x47,0x20,0x85,0xc0,0x77,0x0a,0x8b,0x57,0x0c,0x52,0xff,0x15,0xcc,0xa2,0xae,
        0x00,0x8b,0x47,0x7c,0x85,0xc0,0x74,0xa7,0x80,0x3d,0x16,0xe8,0xf2,0x00,0x01,0x75,
        0x0e,0x6a,0x00,0x6a,0x00,0x6a,0x00,0xe8,0xec,0x58,0x00,0x00,0x83,0xc4,0x0c,0xe8,
        0xf4,0x67,0x00,0x00,0x8b,0x4f,0x7c,0x8b,0x01,0xff,0x50,0x0c,0xeb,0x81
    };
    // C2 WM_ACTIVATE: write LOWORD(wParam) to +20, then choose process policy.
    // Normal/zero wait remains the original code, as does the event continuation.
    constexpr unsigned char Activation[]{
        0x0f,0xb7,0xc3,0x85,0xc0,0x89,0x46,0x20,0x76,0x1b,0x6a,0x20,0xff,0x15,0xe4,0xa2,
        0xae,0x00,0x50,0xff,0x15,0x2c,0xa2,0xae,0x00,0xc7,0x46,0x0c,0x00,0x00,0x00,0x00,
        0xe9,0xa1,0x01,0x00,0x00,0x68,0x00,0x40,0x00,0x00,0xff,0x15,0xe4,0xa2,0xae,0x00,
        0x50,0xff,0x15,0x2c,0xa2,0xae,0x00,0xc7,0x46,0x0c,0x0f,0x00,0x00,0x00,0xe9,0x83,
        0x01,0x00,0x00
    };
    // C2 constructor's type 1 sends Shell_TrayWnd WM_COMMAND/0x19F.
    constexpr unsigned char Constructor[]{
        0x57,0x68,0x6c,0xb8,0xae,0x00,0xff,0x15,0xc8,0xa3,0xae,0x00,0x3b,0xc7,0x74,0x32,
        0x57,0x68,0x9f,0x01,0x00,0x00,0x68,0x11,0x01,0x00,0x00,0x50,0xff,0x15,0xc4,0xa3,
        0xae,0x00,0x5f,0x8b,0xc6,0x5e,0x8b,0x4d,0xf4,0x64,0x89,0x0d,0x00,0x00,0x00,0x00,
        0x8b,0xe5,0x5d,0xc2,0x04,0x00,0x56,0x68,0xa0,0xbc,0x40,0x00,0xff,0x15,0xec,0xa3,
        0xae,0x00,0x8b,0x4d,0xf4,0x5f,0x8b,0xc6,0x5e,0x64,0x89,0x0d,0x00,0x00,0x00,0x00,
        0x8b,0xe5,0x5d,0xc2,0x04,0x00
    };
    // CRT table CB2088 -> AE0F80 constructs D564B0 with type 1, then registers
    // AE6D90 for exit. Skip both to avoid registering an unmatched shell restore.
    constexpr unsigned char StaticEvent[]{
        0x6a,0x01,0xb9,0xb0,0x64,0xd5,0x00,0xe8,0x84,0xc4,0x92,0xff,0x68,0x90,0x6d,0xae,
        0x00,0xe8,0xd0,0xee,0xf7,0xff,0x59,0xc3
    };
    constexpr unsigned char ExitRestore[]{0xb9,0xb0,0x64,0xd5,0x00,0xe9,0x06,0x60,0x92,0xff};
    constexpr unsigned char Jump[]{0xeb}, Nops[]{0x90,0x90}, Return[]{0xc3,0x90};

    struct Patch
    {
        std::size_t offset;
        const unsigned char* original;
        std::size_t length;
        std::size_t edit;
        const unsigned char* replacement;
        std::size_t count;
    };

    int State(const unsigned char* image, const Patch& patch) noexcept
    {
        const auto* current = image + patch.offset;
        if (std::memcmp(current, patch.original, patch.length) == 0) return 0;
        if (std::memcmp(current, patch.original, patch.edit) != 0 ||
            std::memcmp(current + patch.edit, patch.replacement, patch.count) != 0 ||
            std::memcmp(current + patch.edit + patch.count, patch.original + patch.edit + patch.count,
                patch.length - patch.edit - patch.count) != 0) return -1;
        return 1;
    }

    bool Valid(unsigned char* image, std::size_t bytes,
        dfl::chapter_two::background_policy_detail::PatchWriter write) noexcept
    {
        return image && bytes == ImageSize && write &&
            reinterpret_cast<std::uintptr_t>(image) <= std::numeric_limits<std::uintptr_t>::max() - ImageSize;
    }

    bool InstallPair(unsigned char* image, const std::array<Patch, 2>& patches,
        dfl::chapter_two::background_policy_detail::PatchWriter write) noexcept
    {
        const int first = State(image, patches[0]), second = State(image, patches[1]);
        if (first < 0 || second < 0 || first != second) return false;
        if (first == 1) return true;
        for (std::size_t index = 0; index < patches.size(); ++index)
        {
            const auto& patch = patches[index];
            if (!write(reinterpret_cast<std::uintptr_t>(image + patch.offset + patch.edit),
                    patch.replacement, patch.count))
            {
                // A writer can fail after copying (cache flush/protection restore).
                // Restore the failed span and every earlier span in this policy.
                for (std::size_t rollback = index + 1; rollback > 0; --rollback)
                {
                    const auto& prior = patches[rollback - 1];
                    write(reinterpret_cast<std::uintptr_t>(image + prior.offset + prior.edit),
                        prior.original + prior.edit, prior.count);
                }
                return false;
            }
        }
        return true;
    }
}

bool dfl::chapter_two::background_policy_detail::InstallBackground(
    unsigned char* image, std::size_t bytes, PatchWriter write) noexcept
{
    if (!Valid(image, bytes, write)) return false;
    return InstallPair(image, {{{0x63cee8, MainLoop, sizeof(MainLoop), 5, Jump, sizeof(Jump)},
        {0x63d386, Activation, sizeof(Activation), 8, Nops, sizeof(Nops)}}}, write);
}

bool dfl::chapter_two::background_policy_detail::InstallStartup(
    unsigned char* image, std::size_t bytes, PatchWriter write) noexcept
{
    if (!Valid(image, bytes, write)) return false;
    constexpr unsigned char tableEntry[]{0x80,0x0f,0xae,0x00};
    constexpr char shellClass[] = "Shell_TrayWnd";
    if (std::memcmp(image + 0x8b2088, tableEntry, sizeof(tableEntry)) != 0 ||
        std::memcmp(image + 0x6eb86c, shellClass, sizeof(shellClass)) != 0 ||
        std::memcmp(image + 0x6e6d90, ExitRestore, sizeof(ExitRestore)) != 0) return false;
    return InstallPair(image, {{{0xd470, Constructor, sizeof(Constructor), 14, Jump, sizeof(Jump)},
        {0x6e0f80, StaticEvent, sizeof(StaticEvent), 0, Return, sizeof(Return)}}}, write);
}

bool dfl::chapter_two::InstallBackgroundPolicy(unsigned char* image, std::size_t bytes) noexcept
{
    return background_policy_detail::InstallBackground(image, bytes, memory_patch::WriteBytes);
}

bool dfl::chapter_two::InstallStartupWindowPolicy(unsigned char* image, std::size_t bytes) noexcept
{
    return background_policy_detail::InstallStartup(image, bytes, memory_patch::WriteBytes);
}
