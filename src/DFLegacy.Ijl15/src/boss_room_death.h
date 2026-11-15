#pragma once

#include <cstdint>

namespace dfl::boss_room_death
{
    bool Install() noexcept;
}

namespace dfl::boss_room_death::detail
{
    struct SpawnSnapshot
    {
        void* Actor;
        bool CurrentDungeonMap;
        bool BossDefeated;
        bool InMap;
        std::uint32_t Kind;
        int Team;
        bool Dead;
        bool LocalAuthority;
        int CurrentHp;
    };

    using SetHp = void (__cdecl*)(void* actor, int hp, bool force, void* source);

    bool KillSpawnAfterBoss(const SpawnSnapshot& spawn, SetHp setHp) noexcept;

}
