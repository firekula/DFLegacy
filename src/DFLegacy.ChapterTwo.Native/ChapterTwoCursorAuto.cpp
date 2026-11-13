#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoCursorAuto.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <array>
#include <cstdio>
#include <cstring>
#include <limits>

namespace
{
    constexpr std::size_t ImageSize = 0xe57000, PatchSize = 6;
    constexpr unsigned char Automatic[]{
        0x55,0x8b,0xec,0x83,0xec,0x08,0x8b,0x0d,0x04,0xe8,0xf2,0x00,0x56,0x8b,0x75,0x08,
        0x57,0x8b,0x7d,0x0c,0x8d,0x45,0xf8,0x50,0x51,0x89,0x75,0xf8,0x89,0x7d,0xfc,0xff,
        0x15,0x6c,0xa4,0xae,0x00,0x8b,0x55,0xfc,0x8b,0x45,0xf8,0x52,0x50,0xff,0x15,0x68,
        0xa4,0xae,0x00,0x89,0x3d,0xf8,0xe7,0xf2,0x00,0x5f,0x89,0x35,0xf4,0xe7,0xf2,0x00,
        0x5e,0x8b,0xe5,0x5d,0xc3,
    };
    constexpr unsigned char Restore[]{
        0x55,0x8b,0xec,0x83,0xec,0x0c,0xa1,0xf8,0xe7,0xf2,0x00,0x89,0x45,0xfc,0xa1,0x40,
        0xe8,0xf2,0x00,0x85,0xc0,0x56,0x57,0x8b,0x3d,0xf4,0xe7,0xf2,0x00,0xc6,0x05,0x14,
        0xe8,0xf2,0x00,0x00,0x0f,0x84,0x7c,0x00,0x00,0x00,0xa1,0x44,0xe8,0xf2,0x00,0x53,
        0x8b,0x15,0x3c,0xe8,0xf2,0x00,0x8b,0xc8,0xc1,0xe9,0x08,0x8b,0x0c,0x8a,0x6a,0x00,
        0x25,0xff,0x00,0x00,0x00,0x8b,0x14,0x81,0x6a,0x00,0x52,0xe8,0x90,0xfd,0xff,0xff,
        0xa1,0x40,0xe8,0xf2,0x00,0x83,0xc4,0x0c,0x85,0xc0,0x74,0x49,0xa1,0x44,0xe8,0xf2,
        0x00,0x40,0x3d,0x00,0x01,0x00,0x00,0xa3,0x44,0xe8,0xf2,0x00,0x75,0x2f,0x8b,0x15,
        0x38,0xe8,0xf2,0x00,0x8b,0x0d,0x3c,0xe8,0xf2,0x00,0x8b,0x31,0x33,0xc0,0x4a,0x74,
        0x12,0x8b,0x5c,0x81,0x04,0x89,0x1c,0x81,0x8b,0x0d,0x3c,0xe8,0xf2,0x00,0x40,0x3b,
        0xc2,0x72,0xee,0x89,0x34,0x81,0x33,0xc0,0xa3,0x44,0xe8,0xf2,0x00,0xff,0x0d,0x40,
        0xe8,0xf2,0x00,0x75,0x8b,0x5b,0x8b,0x0d,0x04,0xe8,0xf2,0x00,0x8b,0x75,0xfc,0x8d,
        0x45,0xf4,0x50,0x51,0x89,0x7d,0xf4,0x89,0x75,0xf8,0xff,0x15,0x6c,0xa4,0xae,0x00,
        0x8b,0x55,0xf8,0x8b,0x45,0xf4,0x52,0x50,0xff,0x15,0x68,0xa4,0xae,0x00,0x89,0x3d,
        0xf4,0xe7,0xf2,0x00,0x5f,0x89,0x35,0xf8,0xe7,0xf2,0x00,0x5e,0x8b,0xe5,0x5d,0xc3,
    };

    // Preserve the API error and bound diagnostic work separately for each path
    // and result. No desktop coordinates or arbitrary input are recorded.
    void __cdecl RecordMove(unsigned path, BOOL result, int x, int y, const POINT* screenTarget) noexcept
    {
        const DWORD error = GetLastError();
        static volatile LONG counts[2][2]{};
        auto* const count = &counts[path][result != FALSE];
        if (InterlockedCompareExchange(count, 0, 0) < 8 && InterlockedIncrement(count) <= 8)
        {
            // Read the native function's already converted screen target. The
            // display adapter's conversion is intentionally call-site-specific.
            POINT physical{};
            const int systemMatch = screenTarget && GetCursorPos(&physical)
                ? (screenTarget->x==physical.x && screenTarget->y==physical.y ? 1 : 0) : -1;
            char text[256]{};
            std::snprintf(text, sizeof(text),
                "DFLegacy Chapter Two: cursor-auto path=%s ok=%u error=%lu target=%d,%d current=%d,%d system_match=%d\n",
                path ? "restore" : "automatic", result != FALSE, error, x, y,
                *reinterpret_cast<volatile int*>(0xf2e7f4),
                *reinterpret_cast<volatile int*>(0xf2e7f8), systemMatch);
            OutputDebugStringA(text);
        }
        SetLastError(error);
    }

    // The native tail restores its saved ESI/EDI. On failure, substitute the
    // current logical position before that tail commits the requested position.
    // In the restore path this is the position AFTER queued input was consumed.
    __declspec(naked) void CommitAutomatic() noexcept
    {
        __asm {
            pushfd
            pushad
            lea ecx,[ebp-8]
            push ecx
            push edi
            push esi
            push eax
            push 0
            call RecordMove
            add esp,20
            popad
            test eax,eax
            jnz commit
            mov esi,dword ptr ds:[0f2e7f4h]
            mov edi,dword ptr ds:[0f2e7f8h]
        commit:
            mov dword ptr ds:[0f2e7f8h],edi
            popfd
            ret
        }
    }
    __declspec(naked) void CommitRestore() noexcept
    {
        __asm {
            pushfd
            pushad
            lea ecx,[ebp-0ch]
            push ecx
            push esi
            push edi
            push eax
            push 1
            call RecordMove
            add esp,20
            popad
            test eax,eax
            jnz commit
            mov edi,dword ptr ds:[0f2e7f4h]
            mov esi,dword ptr ds:[0f2e7f8h]
        commit:
            mov dword ptr ds:[0f2e7f4h],edi
            popfd
            ret
        }
    }
    struct Site
    {
        std::size_t offset, commit, size;
        const unsigned char* original;
        void(*hook)() noexcept;
    };
    const Site Sites[]{
        {0x642260, 0x33, sizeof(Automatic), Automatic, CommitAutomatic},
        {0x642a20, 0xce, sizeof(Restore), Restore, CommitRestore},
    };
}

bool dfl::chapter_two::cursor_auto_detail::InstallImage(
    unsigned char* image, std::size_t bytes, PatchWriter write) noexcept
{
    if (!image || bytes != ImageSize || !write ||
        reinterpret_cast<std::uintptr_t>(image) > std::numeric_limits<std::uintptr_t>::max() - ImageSize)
        return false;
    std::array<std::array<unsigned char, PatchSize>, 2> patches{};
    unsigned installed = 0;
    for (unsigned i = 0; i < 2; ++i)
    {
        const auto& site = Sites[i];
        const auto* start = image + site.offset;
        if (std::memcmp(start, site.original, site.commit) ||
            std::memcmp(start + site.commit + PatchSize, site.original + site.commit + PatchSize,
                site.size - site.commit - PatchSize)) return false;
        auto& patch = patches[i];
        patch = {0xe8, 0, 0, 0, 0, 0x90};
        const auto delta = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(site.hook)
            - reinterpret_cast<std::uintptr_t>(start + site.commit) - 5);
        std::memcpy(patch.data() + 1, &delta, sizeof(delta));
        if (!std::memcmp(start + site.commit, patch.data(), PatchSize)) ++installed;
        else if (std::memcmp(start + site.commit, site.original + site.commit, PatchSize)) return false;
    }
    if (installed) return installed == 2; // Do not accept or extend a partial install.
    for (unsigned i = 0; i < 2; ++i)
    {
        const auto& site = Sites[i];
        if (write(reinterpret_cast<std::uintptr_t>(image + site.offset + site.commit),
            patches[i].data(), PatchSize)) continue;
        // The writer may fail after copying. Restore both attempted sites.
        for (unsigned j = 0; j <= i; ++j)
        {
            const auto& rollback = Sites[j];
            write(reinterpret_cast<std::uintptr_t>(image + rollback.offset + rollback.commit),
                rollback.original + rollback.commit, PatchSize);
        }
        return false;
    }
    return true;
}

bool dfl::chapter_two::InstallCursorAuto(unsigned char* image, std::size_t bytes) noexcept
{
    return image == reinterpret_cast<unsigned char*>(0x400000) &&
        cursor_auto_detail::InstallImage(image, bytes, memory_patch::WriteBytes);
}
