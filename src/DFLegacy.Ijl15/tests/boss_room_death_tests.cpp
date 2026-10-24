#include "boss_room_death.h"

namespace
{
    struct Actor { int Hp = 100; int Calls = 0; bool Forced = false; void* Source = nullptr; };
    void __cdecl RecordSetHp(void* value, int hp, bool force, void* source)
    {
        auto& actor = *static_cast<Actor*>(value);
        actor.Hp = hp;
        actor.Calls++;
        actor.Forced = force;
        actor.Source = source;
    }
}

bool TestBossRoomLateSpawnDeath()
{
    using namespace dfl::boss_room_death::detail;
    Actor actor;
    const SpawnSnapshot released{&actor, true, true, true, 529, 100, false, true, 100};
    if (!KillSpawnAfterBoss(released, &RecordSetHp) || actor.Hp != 0
        || actor.Calls != 1 || !actor.Forced || actor.Source != nullptr)
    {
        return false;
    }
    for (int variant = 0; variant < 12; ++variant)
    {
        auto ignored = released;
        switch (variant)
        {
        case 0: ignored.Actor = nullptr; break;
        case 1: ignored.CurrentDungeonMap = false; break;
        case 2: ignored.BossDefeated = false; break;
        case 3: ignored.InMap = false; break;
        case 4: ignored.Kind = 273; break;
        case 5: ignored.Team = 0; break; // Friendly summons stay alive.
        case 6: ignored.Team = 1; break;
        case 7: ignored.Dead = true; break;
        case 8: ignored.LocalAuthority = false; break;
        case 9: ignored.CurrentHp = 0; break;
        case 10: ignored.CurrentHp = -1; break;
        case 11: ignored.Kind = 33; break; // Unopened barrels stay intact.
        }
        if (KillSpawnAfterBoss(ignored, &RecordSetHp) || actor.Calls != 1)
        {
            return false;
        }
    }
    return !KillSpawnAfterBoss(released, nullptr);
}
