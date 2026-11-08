#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <array>
#include <cstdint>
#include <cstring>

#include "boss_room_death.h"
#include "memory_patch.h"
#include "loot_gather.h"

namespace
{
    constexpr std::uintptr_t FrameCallSite = 0x00433B47;
    constexpr std::array<unsigned char, 5> OriginalFrameCall{
        0xE8, 0x94, 0x8E, 0x35, 0x00};

    // NOTI127 sets Stage+110 after native Map::allDie(100). Unlike an empty
    // NOTI38, the native HP setter queues the death action and its normal
    // MONSTER_DIE report. Run after the UI's frame update, when box/bunker
    // creation has finished assigning HP, faction and local authority.
    bool ReadSpawn(void* map, void* actor,
        dfl::boss_room_death::detail::SpawnSnapshot& spawn) noexcept
    {
        __try
        {
            if (map == nullptr || actor == nullptr)
            {
                return false;
            }
            const auto ui = *reinterpret_cast<const std::uintptr_t*>(0x00B5DCE0);
            const auto game = *reinterpret_cast<const std::uintptr_t*>(0x00B5D6F0);
            if (ui == 0 || game == 0)
            {
                return false;
            }
            const auto stage = *reinterpret_cast<const std::uintptr_t*>(ui + 0x200010);
            using ReadScene = int (__thiscall*)(const void*);
            if (stage == 0 || *reinterpret_cast<void* const*>(stage + 0xB0) != map
                || reinterpret_cast<ReadScene>(0x00402030)(reinterpret_cast<void*>(game + 16)) != 3
                || *reinterpret_cast<const unsigned char*>(stage + 0x110) == 0)
            {
                return false;
            }

            using Contains = unsigned char (__thiscall*)(void*, void*);
            if (reinterpret_cast<Contains>(0x004C5310)(map, actor) == 0)
            {
                return false;
            }
            const auto bytes = reinterpret_cast<std::uintptr_t>(actor);
            const auto table = *reinterpret_cast<const std::uintptr_t*>(bytes);
            // Method identity, not kind alone, establishes the exact Monster
            // layout and the HP setter ABI on every supported variant.
            if (*reinterpret_cast<const std::uintptr_t*>(table + 0x34) != 0x004073D0
                || *reinterpret_cast<const std::uintptr_t*>(table + 0x138) != 0x0047F480
                || *reinterpret_cast<const std::uintptr_t*>(table + 0x18C) != 0x0042C990
                || *reinterpret_cast<const std::uintptr_t*>(table + 0x318) != 0x00652A00
                || *reinterpret_cast<const std::uintptr_t*>(table + 0x328) != 0x00481000
                || *reinterpret_cast<const std::uintptr_t*>(table + 0x330) != 0x00652A20
                || *reinterpret_cast<const std::uint32_t*>(bytes + 0xD0) != 529)
            {
                return false;
            }

            using Predicate = unsigned char (__thiscall*)(void*);
            using ReadHp = int (__thiscall*)(void*);
            spawn = {actor, true, true, true,
                529, *reinterpret_cast<const int*>(bytes + 0x424),
                reinterpret_cast<Predicate>(0x00652A00)(actor) != 0,
                reinterpret_cast<Predicate>(0x00473620)(actor) != 0,
                reinterpret_cast<ReadHp>(0x00652A20)(actor)};
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    void __cdecl SetHp(void* actor, int hp, bool force, void* source)
    {
        using Function = void (__thiscall*)(void*, int, bool, void*);
        reinterpret_cast<Function>(0x00481000)(actor, hp, force, source);
    }

    bool CaptureBossMap(void*& map, std::array<void*, 4096>& actors,
        std::size_t& count) noexcept
    {
        __try
        {
            const auto ui = *reinterpret_cast<const std::uintptr_t*>(0x00B5DCE0);
            const auto game = *reinterpret_cast<const std::uintptr_t*>(0x00B5D6F0);
            if (ui == 0 || game == 0) return false;
            const auto stage = *reinterpret_cast<const std::uintptr_t*>(ui + 0x200010);
            using ReadScene = int (__thiscall*)(const void*);
            if (stage == 0 || *reinterpret_cast<const unsigned char*>(stage + 0x110) == 0
                || reinterpret_cast<ReadScene>(0x00402030)(reinterpret_cast<void*>(game + 16)) != 3)
                return false;
            map = *reinterpret_cast<void* const*>(stage + 0xB0);
            if (map == nullptr) return false;
            const auto bytes = reinterpret_cast<std::uintptr_t>(map);
            const auto begin = *reinterpret_cast<const std::uintptr_t*>(bytes + 0x10);
            const auto end = *reinterpret_cast<const std::uintptr_t*>(bytes + 0x14);
            if (end < begin || (end - begin) % sizeof(void*) != 0
                || (end - begin) / sizeof(void*) > actors.size()) return false;
            count = (end - begin) / sizeof(void*);
            if (count != 0) std::memcpy(actors.data(), reinterpret_cast<const void*>(begin), end - begin);
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }

    void __stdcall AfterNativeFrame() noexcept
    {
        // Share the existing UI frame boundary; loot commands also work before
        // boss completion and must not depend on the boss flag below.
        dfl::loot_gather::OnFrame();
        // Snapshot only for this frame. A previous death can remove another
        // actor, so membership is checked again before interpreting its bytes.
        // Nothing retains an actor pointer across a frame or scene change.
        std::array<void*, 4096> actors{};
        std::size_t count = 0;
        void* map = nullptr;
        if (!CaptureBossMap(map, actors, count)) return;
        for (std::size_t index = 0; index < count; ++index)
        {
            dfl::boss_room_death::detail::SpawnSnapshot spawn{};
            if (ReadSpawn(map, actors[index], spawn))
                dfl::boss_room_death::detail::KillSpawnAfterBoss(spawn, &SetHp);
        }
    }

    __declspec(naked) void FrameHook()
    {
        __asm
        {
            pushfd
            pushad
            call AfterNativeFrame
            popad
            popfd
            // Preserve the original no-stack-argument thiscall and its return.
            push 0078C9E0h
            ret
        }
    }

    template<std::size_t Size>
    bool Matches(std::uintptr_t address,
        const std::array<unsigned char, Size>& expected) noexcept
    {
        __try
        {
            return std::memcmp(reinterpret_cast<const void*>(address),
                expected.data(), expected.size()) == 0;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
    }
}

namespace dfl::boss_room_death
{
    bool Install() noexcept
    {
        if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)) != 0x00400000)
        {
            return false;
        }
        constexpr std::array<unsigned char, 6> FrameEntry{
            0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x24};
        constexpr std::array<unsigned char, 7> StageGetter{
            0x8B, 0x81, 0x10, 0x00, 0x20, 0x00, 0xC3};
        constexpr std::array<unsigned char, 10> BossFlagSetter{
            0x55, 0x8B, 0xEC, 0x8A, 0x45, 0x08, 0x88, 0x81, 0x10, 0x01};
        constexpr std::array<unsigned char, 6> HpSetter{
            0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x0C};
        constexpr std::array<unsigned char, 6> Membership{
            0x55, 0x8B, 0xEC, 0x8B, 0x51, 0x10};
        std::array<unsigned char, 5> patch{0xE8};
        const auto displacement = static_cast<std::uint32_t>(
            reinterpret_cast<std::uintptr_t>(&FrameHook) - FrameCallSite - 5);
        std::memcpy(patch.data() + 1, &displacement, sizeof(displacement));
        const auto frameInstalled = Matches(FrameCallSite, patch);
        if ((!frameInstalled && !Matches(FrameCallSite, OriginalFrameCall))
            || !Matches(0x0078C9E0, FrameEntry)
            || !Matches(0x00438850, StageGetter)
            || !Matches(0x00417C10, BossFlagSetter)
            || !Matches(0x00481000, HpSetter)
            || !Matches(0x004C5310, Membership))
        {
            return false;
        }
        if (!frameInstalled && !memory_patch::WriteBytes(FrameCallSite, patch.data(), patch.size()))
        {
            memory_patch::WriteBytes(FrameCallSite, OriginalFrameCall.data(), OriginalFrameCall.size());
            return false;
        }
        return true;
    }
}

namespace dfl::boss_room_death::detail
{
    bool KillSpawnAfterBoss(const SpawnSnapshot& spawn, SetHp setHp) noexcept
    {
        if (spawn.Actor == nullptr || !spawn.CurrentDungeonMap || !spawn.BossDefeated
            || !spawn.InMap || spawn.Kind != 529 || spawn.Team != 100
            || spawn.Dead || !spawn.LocalAuthority || spawn.CurrentHp <= 0 || setHp == nullptr)
        {
            return false;
        }
        setHp(spawn.Actor, 0, true, nullptr);
        return true;
    }
}
