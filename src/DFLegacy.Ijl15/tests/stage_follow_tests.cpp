#include <array>
#include <cstring>
#include <vector>

#include "stage_follow.h"

namespace
{
    using namespace dfl::stage_follow::detail;

    struct Actor
    {
        bool Destroyed = false;
    };

    struct Map
    {
        std::vector<Actor*> Actors;
    };

    std::array<unsigned char, StageMapOffset + sizeof(void*)> Stage{};
    std::vector<void*> Writes;
    void* Selection = nullptr;
    bool SelectionSawClearedTarget = false;
    int Selections = 0;

    void* ReadTarget()
    {
        void* target = nullptr;
        std::memcpy(&target, Stage.data() + FollowTargetOffset, sizeof(target));
        return target;
    }

    void __cdecl Set(void* stage, void* actor)
    {
        Writes.push_back(actor);
        std::memcpy(static_cast<unsigned char*>(stage) + FollowTargetOffset,
            &actor, sizeof(actor));
    }

    void* __cdecl Select(void*, void*)
    {
        ++Selections;
        SelectionSawClearedTarget = ReadTarget() == nullptr;
        return Selection;
    }

    bool __cdecl Contains(void* map, void* actor)
    {
        for (Actor* member : static_cast<Map*>(map)->Actors)
        {
            if (member == actor)
            {
                return true;
            }
        }
        return false;
    }

    const ClientFunctions Functions{&Select, &Set, &Contains};

    void Reset(Map* map, Actor* target, Actor* selection)
    {
        Stage.fill(0);
        std::memcpy(Stage.data() + StageMapOffset, &map, sizeof(map));
        std::memcpy(Stage.data() + FollowTargetOffset, &target, sizeof(target));
        Writes.clear();
        Selections = 0;
        Selection = selection;
        SelectionSawClearedTarget = false;
    }

    bool RenderReferencesLiveActor()
    {
        const auto* target = static_cast<const Actor*>(ReadTarget());
        return target == nullptr || !target->Destroyed;
    }
}

bool TestStageFollowRepair()
{
    Actor removed;
    Actor survivor;
    Actor outsideMap;
    Map map{{&removed, &survivor}};
    Map unrelated;
    Reset(&map, &removed, &survivor);

    // Recreate the native lifetime boundary: erase map membership, repair,
    // detach/free actor, then render before another packet can be dispatched.
    map.Actors.erase(map.Actors.begin());
    if (!RepairFollowTarget(Stage.data(), &map, &removed, &survivor, 3, Functions)
        || Writes.size() != 2 || Writes[0] != nullptr
        || Writes[1] != &survivor || !SelectionSawClearedTarget)
    {
        return false;
    }
    removed.Destroyed = true;
    if (!RenderReferencesLiveActor()
        || RepairFollowTarget(Stage.data(), &map, &removed, &survivor, 3, Functions)
        || Writes.size() != 2)
    {
        return false;
    }

    // A stale selector result, an unmapped result, or no spectator candidate
    // must all leave the target empty rather than resurrect a stale pointer.
    for (Actor* selection : {&removed, &outsideMap, static_cast<Actor*>(nullptr)})
    {
        Reset(&map, &removed, selection);
        if (!RepairFollowTarget(Stage.data(), &map, &removed, &survivor, 3, Functions)
            || ReadTarget() != nullptr || Writes.size() != 1
            || !SelectionSawClearedTarget || !RenderReferencesLiveActor())
        {
            return false;
        }
    }

    Reset(&map, &removed, &survivor);
    if (!RepairFollowTarget(Stage.data(), &map, &removed, nullptr, 3, Functions)
        || ReadTarget() != nullptr || Selections != 0 || Writes.size() != 1)
    {
        return false;
    }

    // A town transition removes the local actor from the old map before
    // reusing it in the destination map. Its camera target must survive.
    removed.Destroyed = false;
    Reset(&map, &removed, &survivor);
    if (RepairFollowTarget(Stage.data(), &map, &removed, &removed, 3, Functions)
        || ReadTarget() != &removed || !Writes.empty() || Selections != 0
        || !RenderReferencesLiveActor())
    {
        return false;
    }

    // Prove the entire non-dungeon path exits before even reading Stage.
    // In town, B75A14 is not the town actor (the latter uses B75A18).
    for (const int state : {-1, 0, 1, 2, 4, 5})
    {
        Reset(&map, &removed, &survivor);
        if (RepairFollowTarget(reinterpret_cast<void*>(1), &map,
                &removed, &survivor, state, Functions)
            || ReadTarget() != &removed || !Writes.empty() || Selections != 0)
        {
            return false;
        }
    }

    Reset(&map, &survivor, &survivor);
    if (RepairFollowTarget(Stage.data(), &map, &removed, &survivor, 3, Functions))
    {
        return false;
    }
    Reset(&map, &removed, &survivor);
    return !RepairFollowTarget(nullptr, &map, &removed, &survivor, 3, Functions)
        && !RepairFollowTarget(Stage.data(), nullptr, &removed, &survivor, 3, Functions)
        && !RepairFollowTarget(Stage.data(), &map, nullptr, &survivor, 3, Functions)
        && !RepairFollowTarget(Stage.data(), &unrelated, &removed, &survivor, 3, Functions)
        && Writes.empty() && Selections == 0;
}
