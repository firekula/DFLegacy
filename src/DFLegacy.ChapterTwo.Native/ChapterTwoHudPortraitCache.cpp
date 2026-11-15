#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoHudPortraitCache.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <array>
#include <atomic>
#include <cstring>
#include <limits>

namespace
{
    constexpr std::uintptr_t ImageBase = 0x400000;
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr std::size_t CacheOffset = 0x50e440, DispatchOffset = 0x51dbe7, TimerOffset = 0x5c0e00;
    constexpr std::size_t FallbackOffset = 0x1347d3;
    constexpr std::array<std::size_t, 4> CallOffsets{0x51dbf6, 0x51dbff, 0x50e4fa, 0x13482f};
    // Original C2 cache creation, UID/dirty gates, shared timer, count&7,
    // clear/draw and cache commit. Output coordinates following it stay intact.
    constexpr std::array<unsigned char, 286> Cache{
        0x55,0x8b,0xec,0x53,0x8b,0x5d,0x14,0x85,0xdb,0x56,0x8b,0xf1,0x0f,0x84,0xa5,0x01,
        0x00,0x00,0x57,0x0f,0xb6,0x7d,0x10,0x8b,0x84,0xbe,0x50,0x08,0x00,0x00,0x85,0xc0,
        0x75,0x73,0x8b,0x0d,0xc8,0x1f,0xe0,0x00,0x6a,0x00,0x8d,0x55,0x10,0x52,0xc7,0x45,
        0x10,0x00,0x00,0x00,0x00,0x8b,0x01,0x6a,0x0e,0xff,0x50,0x08,0x83,0xf8,0x01,0x74,
        0x19,0x68,0xfc,0x74,0xc1,0x00,0x6a,0x02,0x6a,0x01,0x6a,0x00,0x68,0x63,0xef,0xce,
        0xce,0xff,0x15,0xb0,0xe3,0xd4,0x00,0x83,0xc4,0x14,0x8b,0x4d,0x10,0x8b,0x01,0x6a,
        0x24,0x6a,0x24,0xff,0x50,0x0c,0x83,0xf8,0x01,0x74,0x19,0x68,0xfc,0x74,0xc1,0x00,
        0x6a,0x02,0x6a,0x01,0x6a,0x00,0x68,0xe0,0xb2,0x28,0x2c,0xff,0x15,0xb0,0xe3,0xd4,
        0x00,0x83,0xc4,0x14,0x8b,0x4d,0x10,0x89,0x8c,0xbe,0x50,0x08,0x00,0x00,0x5f,0x5e,
        0x5b,0x5d,0xc2,0x20,0x00,0x0f,0xb7,0x53,0x08,0x39,0x94,0xbe,0x70,0x08,0x00,0x00,
        0x75,0x37,0x8a,0x84,0x37,0x3c,0x10,0x00,0x00,0x84,0xc0,0x75,0x2c,0xff,0x15,0x0c,
        0xa5,0xae,0x00,0x50,0x8d,0x8e,0x90,0x08,0x00,0x00,0xe8,0x01,0x29,0x0b,0x00,0x3c,
        0x01,0x75,0x5b,0x8b,0x86,0xa0,0x08,0x00,0x00,0x25,0x07,0x00,0x00,0x80,0x79,0x05,
        0x48,0x83,0xc8,0xf8,0x40,0x3b,0xc7,0x75,0x45,0x8b,0x8c,0xbe,0x50,0x08,0x00,0x00,
        0x8b,0x11,0x6a,0x00,0xff,0x52,0x1c,0x8b,0x8c,0xbe,0x50,0x08,0x00,0x00,0x8b,0x55,
        0x18,0x8b,0x03,0x68,0xff,0x00,0x00,0x00,0x51,0x8b,0x4d,0x20,0x52,0x8b,0x55,0x1c,
        0x51,0x52,0x6a,0x00,0x6a,0x00,0x8b,0xcb,0xff,0x50,0x04,0xc6,0x84,0x37,0x3c,0x10,
        0x00,0x00,0x00,0x0f,0xb7,0x43,0x08,0x89,0x84,0xbe,0x70,0x08,0x00,0x00
    };
    constexpr std::array<unsigned char, 29> Dispatch{
        0x4e,0x74,0x13,0x83,0xee,0x02,0x74,0x05,0x83,0xee,0x07,0x75,0x10,0x8b,0xcf,0xe8,
        0x75,0x5b,0xff,0xff,0xeb,0x07,0x8b,0xcf,0xe8,0x6c,0x2b,0xff,0xff
    };
    constexpr std::array<unsigned char, 112> Timer{
        0x55,0x8b,0xec,0x8a,0x41,0x0a,0x84,0xc0,0x8b,0x55,0x08,0x56,0x75,0x20,0x8a,0x41,
        0x09,0x84,0xc0,0xc6,0x41,0x0a,0x01,0xc7,0x41,0x10,0x00,0x00,0x00,0x00,0x74,0x0b,
        0x8b,0x31,0x8b,0xc2,0x2b,0xc6,0x89,0x41,0x0c,0xeb,0x03,0x89,0x51,0x0c,0x8b,0x41,
        0x04,0x83,0xf8,0xff,0x74,0x05,0x39,0x41,0x10,0x7d,0x2e,0x8b,0x41,0x0c,0x8b,0x31,
        0x03,0xc6,0x3b,0xd0,0x7c,0x23,0x53,0x8a,0x59,0x08,0x84,0xdb,0x5b,0x74,0x0d,0xff,
        0x41,0x10,0x89,0x51,0x0c,0xb0,0x01,0x5e,0x5d,0xc2,0x04,0x00,0x89,0x41,0x0c,0xff,
        0x41,0x10,0xb0,0x01,0x5e,0x5d,0xc2,0x04,0x00,0x32,0xc0,0x5e,0x5d,0xc2,0x04,0x00
    };
    constexpr std::array<unsigned char, 97> Fallback{
        0x0f,0xb6,0x45,0x20,0x8b,0x4d,0xfc,0x8b,0x51,0x18,0x8b,0x0d,0xb8,0xa8,0xd7,0x00,
        0x68,0x00,0x00,0x80,0x4f,0x68,0x00,0x00,0x80,0x4f,0xc1,0xe0,0x18,0x0d,0xff,0xff,
        0xff,0x00,0x50,0x6a,0x00,0x68,0x00,0x00,0x80,0x3f,0x68,0x00,0x00,0x80,0x3f,0x52,
        0xe8,0xf8,0x43,0x4f,0x00,0x8b,0x55,0x14,0x50,0xb8,0x18,0x00,0x00,0x00,0x2b,0xc2,
        0x99,0x2b,0xc2,0x8b,0x55,0x10,0xd1,0xf8,0x2b,0xf0,0xb8,0x18,0x00,0x00,0x00,0x2b,
        0xc2,0x99,0x2b,0xc2,0xd1,0xf8,0x56,0x2b,0xf8,0x57,0x8b,0xcb,0xe8,0xfc,0xa2,0x50,
        0x00
    };
    constexpr std::array<unsigned char, 24> BlitFormats{
        0x83,0xf8,0x08,0x74,0x13,0x83,0xf8,0x0e,0x74,0x0e,0x83,0xf8,0x0f,0x74,0x09,0x83,
        0xf8,0x10,0x0f,0x85,0x84,0x01,0x00,0x00
    };
    constexpr std::uint32_t SimpleBlit = 0x00a139d0;

    using DrawGroup = void (__thiscall*)(void*);
    using CheckTimer = bool (__thiscall*)(void*, int);
    const auto OriginalTimer = reinterpret_cast<CheckTimer>(ImageBase + TimerOffset);
    // Adding TLS would shift the rebuilt game's required gameTls offset.
    // Only the bound rendering thread reads or writes the stack context.
    std::atomic<DWORD> RenderingThread{0};
    struct DrawScope;
    DrawScope* CurrentScope = nullptr;
    struct DrawScope
    {
        DrawScope* previous;
        void* timer;
        bool sampled = false, result = false;
        explicit DrawScope(void* base) noexcept : previous(CurrentScope),
            timer(static_cast<unsigned char*>(base) + 0x890) { CurrentScope = this; }
        ~DrawScope() { CurrentScope = previous; }
        DrawScope(const DrawScope&) = delete;
        DrawScope& operator=(const DrawScope&) = delete;
    };
    void Render(void* base, std::uintptr_t original)
    {
        const auto thread = GetCurrentThreadId();
        DWORD unbound = 0;
        RenderingThread.compare_exchange_strong(unbound, thread, std::memory_order_relaxed);
        if (RenderingThread.load(std::memory_order_relaxed) != thread)
        {
            reinterpret_cast<DrawGroup>(original)(base);
            return;
        }
        DrawScope scope(base);
        reinterpret_cast<DrawGroup>(original)(base);
    }
    void __fastcall RenderBattle(void* base, void*) { Render(base, 0x913770); }
    void __fastcall RenderTown(void* base, void*) { Render(base, 0x910770); }
    bool __fastcall SampleTimer(void* timer, void*, int tick)
    {
        if (RenderingThread.load(std::memory_order_relaxed) != GetCurrentThreadId())
            return OriginalTimer(timer, tick);
        auto* scope = CurrentScope;
        if (!scope || scope->timer != timer) return OriginalTimer(timer, tick);
        if (!scope->sampled)
        {
            scope->result = OriginalTimer(timer, tick);
            scope->sampled = true;
        }
        return scope->result;
    }
    void __fastcall DrawPrivateFallback(void* target, void*, int x, int y, void* sprite,
        unsigned sx, unsigned sy, unsigned zero, unsigned color, unsigned px, unsigned py)
    {
        // C2's scaled sprite method accepts only the type7 main renderer. A
        // private-room peer has no actor, so its job icon otherwise never reaches
        // the type14 HUD cache. The native unscaled method supports 1555 targets.
        auto* scope = RenderingThread.load(std::memory_order_relaxed) == GetCurrentThreadId() ? CurrentScope : nullptr;
        if (scope && target && sprite &&
            sx == 0x3f800000 && sy == 0x3f800000 && zero == 0 && color == 0xffffffff &&
            px == 0x4f800000 && py == 0x4f800000 &&
            *reinterpret_cast<const unsigned*>(static_cast<unsigned char*>(target) + 8) == 14 &&
            *reinterpret_cast<const unsigned*>(static_cast<unsigned char*>(sprite) + 8) == 14)
        {
            auto** methods = *static_cast<void***>(sprite);
            auto* surfaces = static_cast<unsigned char*>(scope->timer) - 0x40;
            if (methods && methods[0xbc / 4] == reinterpret_cast<void*>(SimpleBlit))
                for (unsigned slot = 0; slot < 8; ++slot)
                    if (*reinterpret_cast<void**>(surfaces + slot * 4) == target)
                    {
                        using DrawSimple = void (__thiscall*)(void*, int, int, void*);
                        reinterpret_cast<DrawSimple>(0xa3ea80)(target, x, y, sprite);
                        return;
                    }
        }
        using DrawScaled = void (__thiscall*)(void*, int, int, void*, unsigned, unsigned, unsigned, unsigned, unsigned, unsigned);
        reinterpret_cast<DrawScaled>(0xa3eb30)(target, x, y, sprite, sx, sy, zero, color, px, py);
    }
    using Call = std::array<unsigned char, 5>;
    bool MakeCall(std::uintptr_t from, std::uintptr_t to, Call& call) noexcept
    {
        const auto relative = static_cast<std::int64_t>(to) - from - 5;
        if (relative < std::numeric_limits<std::int32_t>::min() || relative > std::numeric_limits<std::int32_t>::max())
            return false;
        call[0] = 0xe8;
        const auto displacement = static_cast<std::int32_t>(relative);
        std::memcpy(call.data() + 1, &displacement, sizeof(displacement));
        return true;
    }
}

bool dfl::chapter_two::hud_portrait_detail::InstallImage(
    unsigned char* image, std::size_t bytes, PatchWriter write) noexcept
{
    if (!image || bytes != ImageSize || !write ||
        reinterpret_cast<std::uintptr_t>(image) > std::numeric_limits<std::uintptr_t>::max() - ImageSize)
        return false;
    if (std::memcmp(image + TimerOffset, Timer.data(), Timer.size()) != 0 ||
        std::memcmp(image + 0x613a66, BlitFormats.data(), BlitFormats.size()) != 0 ||
        std::memcmp(image + 0x8308e0, &SimpleBlit, sizeof(SimpleBlit)) != 0) return false;

    const std::array<std::uintptr_t, 4> targets{reinterpret_cast<std::uintptr_t>(&RenderBattle),
        reinterpret_cast<std::uintptr_t>(&RenderTown), reinterpret_cast<std::uintptr_t>(&SampleTimer),
        reinterpret_cast<std::uintptr_t>(&DrawPrivateFallback)};
    std::array<Call, 4> calls{}, originals{};
    for (std::size_t i = 0; i < calls.size(); ++i)
        if (!MakeCall(reinterpret_cast<std::uintptr_t>(image + CallOffsets[i]), targets[i], calls[i])) return false;
    auto patchedCache = Cache;
    auto patchedDispatch = Dispatch;
    auto patchedFallback = Fallback;
    std::memcpy(patchedCache.data() + CallOffsets[2] - CacheOffset, calls[2].data(), calls[2].size());
    for (std::size_t i = 0; i < 2; ++i)
        std::memcpy(patchedDispatch.data() + CallOffsets[i] - DispatchOffset, calls[i].data(), calls[i].size());
    std::memcpy(patchedFallback.data() + CallOffsets[3] - FallbackOffset, calls[3].data(), calls[3].size());
    if (std::memcmp(image + CacheOffset, patchedCache.data(), patchedCache.size()) == 0 &&
        std::memcmp(image + DispatchOffset, patchedDispatch.data(), patchedDispatch.size()) == 0 &&
        std::memcmp(image + FallbackOffset, patchedFallback.data(), patchedFallback.size()) == 0)
        return true;
    if (std::memcmp(image + CacheOffset, Cache.data(), Cache.size()) != 0 ||
        std::memcmp(image + DispatchOffset, Dispatch.data(), Dispatch.size()) != 0 ||
        std::memcmp(image + FallbackOffset, Fallback.data(), Fallback.size()) != 0)
        return false;

    // Validate every call and its context before the first mutation. This is
    // startup installation, before any game thread can enter the target code.
    for (std::size_t i = 0; i < calls.size(); ++i)
        std::memcpy(originals[i].data(), image + CallOffsets[i], originals[i].size());
    for (std::size_t i = 0; i < calls.size(); ++i)
        if (!write(reinterpret_cast<std::uintptr_t>(image + CallOffsets[i]), calls[i].data(), calls[i].size()))
        {
            // The writer can fail after copying (cache/protection restoration).
            // Restore the failed call as well as all preceding mutations.
            for (std::size_t j = 0; j <= i; ++j)
                write(reinterpret_cast<std::uintptr_t>(image + CallOffsets[j]), originals[j].data(), originals[j].size());
            return false;
        }
    return true;
}

bool dfl::chapter_two::InstallHudPortraitCache(unsigned char* image, std::size_t bytes) noexcept
{
    // The original routines use fixed game VAs; a detached image is suitable
    // for signature tests, but must never receive executable runtime hooks.
    if (reinterpret_cast<std::uintptr_t>(image) != ImageBase) return false;
    return hud_portrait_detail::InstallImage(image, bytes, memory_patch::WriteBytes);
}
