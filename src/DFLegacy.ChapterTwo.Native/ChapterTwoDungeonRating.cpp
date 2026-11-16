#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <atomic>
#include <cstring>
#include "ChapterTwoDungeonRating.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"

namespace dfl::chapter_two::dungeon_rating_detail
{
    namespace
    {
        constexpr std::uintptr_t ImageBase = 0x400000;
        constexpr std::size_t ImageSize = 0xE57000;
        constexpr std::uintptr_t NativeRank = 0x4FD0B0;
        // Native Ver_OB_216 NOTI 28 (new dungeon) and NOTI 29 (change room).
        constexpr std::uintptr_t DungeonInfoEntry = 0x42F5D4;
        constexpr std::uint32_t DungeonInfoHandler = 0x423B5F;
        constexpr std::uint32_t RoomEntryHandler = 0x424233;
        constexpr std::array<std::uintptr_t, 8> RankCalls{
            0x424F9E, 0x424FB6, // CMD 49 rank and diagnostic
            0x42594C, 0x425962, // NOTI 34 validation and diagnostic
            0x51B2DB, // Combat HUD
            0x955FD6, 0x9560C6, 0x9579F7, // Settlement display
        };
        constexpr char Marker[] = "@DFLegacy:dungeon-rating:sss:v1@";
        std::atomic<std::uint32_t> ActiveGame{0};
        bool Installed = false;

        bool ReadNative(void*, std::uint32_t address, void* output, std::size_t size)
        {
            __try
            {
                std::memcpy(output, reinterpret_cast<const void*>(address), size);
                return true;
            }
            __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
        }

        int __stdcall UseForcedRank() noexcept
        {
            const auto game = ActiveGame.load(std::memory_order_relaxed);
            const loot_gather_detail::Runtime runtime{nullptr, &ReadNative, nullptr};
            return game && loot_gather_detail::IsDungeonScene(runtime, game);
        }

        void __stdcall ResetRating() noexcept
        {
            ActiveGame.store(0, std::memory_order_relaxed);
        }

        __declspec(naked) void RankHook()
        {
            __asm
            {
                pushfd
                pushad
                call UseForcedRank
                test eax, eax
                jz nativeRank
                popad
                popfd
                mov eax, 105
                ret
            nativeRank:
                popad
                popfd
                push 004FD0B0h
                ret
            }
        }

        __declspec(naked) void DungeonInfoHook()
        {
            __asm
            {
                pushfd
                pushad
                call ResetRating
                popad
                popfd
                push 00423B5Fh
                ret
            }
        }

        using CallBytes = std::array<unsigned char, 5>;

        CallBytes EncodeCall(std::uintptr_t address, std::uintptr_t target) noexcept
        {
            CallBytes bytes{0xE8};
            const auto relative = static_cast<std::uint32_t>(target - address - bytes.size());
            std::memcpy(bytes.data() + 1, &relative, sizeof(relative));
            return bytes;
        }
    }

    bool TryActivate(const loot_gather_detail::Runtime& runtime, const char* body, unsigned int uid) noexcept
    {
        if (!Installed || !body || !runtime.Read) return false;
        std::array<char, sizeof(Marker)> copy{};
        if (!runtime.Read(runtime.State, reinterpret_cast<std::uint32_t>(body), copy.data(), copy.size())
            || std::memcmp(copy.data(), Marker, copy.size())) return false;

        loot_gather_detail::Request request{};
        if (!loot_gather_detail::Capture(runtime, uid, request, false)) return false;
        if (ActiveGame.exchange(request.Game, std::memory_order_relaxed) != request.Game)
            OutputDebugStringA("DFLegacy Chapter Two: local dungeon rating locked to SSS.\n");
        return true;
    }

    bool InstallImage(unsigned char* image, std::size_t bytes, loot_gather_detail::PatchWriter write) noexcept
    {
        if (!image || bytes != ImageSize || !write) return false;
        constexpr unsigned char dungeonInfoStart[]{0x33, 0xC0, 0xA3, 0x38, 0x79, 0xD5, 0x00};
        constexpr unsigned char nativeRankStart[]{0xE9, 0xA9, 0x5A, 0xAA, 0x00};
        if (std::memcmp(image + DungeonInfoHandler - ImageBase, dungeonInfoStart, sizeof(dungeonInfoStart))
            || std::memcmp(image + NativeRank - ImageBase, nativeRankStart, sizeof(nativeRankStart))
            || std::memcmp(image + DungeonInfoEntry - ImageBase + sizeof(DungeonInfoHandler),
                &RoomEntryHandler, sizeof(RoomEntryHandler))) return false;

        auto* entry = image + DungeonInfoEntry - ImageBase;
        const auto hook = reinterpret_cast<std::uint32_t>(&DungeonInfoHook);
        const bool installed = !std::memcmp(entry, &hook, sizeof(hook));
        if (!installed && std::memcmp(entry, &DungeonInfoHandler, sizeof(DungeonInfoHandler))) return false;

        std::array<CallBytes, RankCalls.size()> original{}, replacement{};
        for (std::size_t i = 0; i < RankCalls.size(); ++i)
        {
            const auto site = reinterpret_cast<std::uintptr_t>(image + RankCalls[i] - ImageBase);
            original[i] = EncodeCall(RankCalls[i], NativeRank);
            replacement[i] = EncodeCall(site, reinterpret_cast<std::uintptr_t>(&RankHook));
            const auto& expected = installed ? replacement[i] : original[i];
            if (std::memcmp(reinterpret_cast<const void*>(site), expected.data(), expected.size())) return false;
        }
        if (installed) return true;

        std::size_t count = 0;
        bool written = true;
        while (written && count < RankCalls.size())
        {
            const auto site = reinterpret_cast<std::uintptr_t>(image + RankCalls[count] - ImageBase);
            written = write(site, replacement[count].data(), replacement[count].size());
            ++count;
        }
        if (written)
        {
            if (write(reinterpret_cast<std::uintptr_t>(entry), &hook, sizeof(hook))) return true;
            write(reinterpret_cast<std::uintptr_t>(entry), &DungeonInfoHandler, sizeof(DungeonInfoHandler));
        }

        for (std::size_t i = 0; i < count; ++i)
        {
            const auto site = reinterpret_cast<std::uintptr_t>(image + RankCalls[i] - ImageBase);
            write(site, original[i].data(), original[i].size());
        }
        return false;
    }
}

bool dfl::chapter_two::InstallDungeonRating(unsigned char* image, std::size_t bytes) noexcept
{
    using namespace dungeon_rating_detail;
    Installed = reinterpret_cast<std::uintptr_t>(image) == ImageBase
        && InstallImage(image, bytes, memory_patch::WriteBytes);
    return Installed;
}
