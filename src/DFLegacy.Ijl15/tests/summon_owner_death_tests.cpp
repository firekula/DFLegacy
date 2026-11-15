#include <array>

#include "summon_owner_death.h"

namespace
{
    using namespace dfl::summon_owner_death::detail;

    struct Monster
    {
        void* Owner;
        void* Authority;
        unsigned char Policy;
        bool QueuedOwnerLoss = false;
        bool NetworkStateSent = false;
        int NativeState = 0;
        int OwnerLossCalls = 0;
    };

    void* LocalActor = nullptr;

    void __cdecl LoseOwner(void* value)
    {
        auto& actor = *static_cast<Monster*>(value);
        actor.Owner = nullptr;
        ++actor.OwnerLossCalls;
        if (actor.Policy == 1)
        {
            actor.NativeState = 101;
        }
        if (actor.Policy == 2 && actor.Authority == LocalActor)
        {
            actor.QueuedOwnerLoss = true;
        }
    }

    bool __cdecl HasQueuedOwnerLoss(void* value)
    {
        return static_cast<Monster*>(value)->QueuedOwnerLoss;
    }

    void __cdecl FlushQueuedOwnerLoss(void* value)
    {
        auto& actor = *static_cast<Monster*>(value);
        actor.NetworkStateSent = true;
    }

    // Reproduce the independently verified 4B34E0 callback sequence. Policy
    // 2 checks authority in 4AF280, after 4B34E0 already cleared that field.
    void NativeRemovedOwner(Monster& actor, void* owner)
    {
        if (actor.Authority == owner)
        {
            actor.Authority = nullptr;
        }
        if (actor.Owner == owner)
        {
            LoseOwner(&actor);
        }
    }

    ActorSnapshot Snapshot(Monster& actor, unsigned short kind = 529)
    {
        return {&actor, actor.Owner, actor.Authority, kind, actor.Policy};
    }

    const ClientFunctions Functions{
        &LoseOwner, &HasQueuedOwnerLoss, &FlushQueuedOwnerLoss};
}

bool TestSummonOwnerLossOrdering()
{
    int owner;
    int teammate;
    LocalActor = &owner;

    Monster unpatched{&owner, &owner, 2};
    NativeRemovedOwner(unpatched, &owner);
    const auto alreadyDetached = Snapshot(unpatched);
    if (unpatched.QueuedOwnerLoss || unpatched.NetworkStateSent
        || ApplyOwnerLossBeforeRemoval(&alreadyDetached, 1, &owner,
            LocalActor, 3, Functions).Applied != 0)
    {
        return false;
    }

    // Local death can broadcast map removal before the later life=false
    // notification. Repair at the map entrance, before any callback clears refs;
    // a later life notification then has nothing left to do a second time.
    // For the last living member, life=false can skip map removal completely.
    for (const bool ownerIsLastLiving : {false, true})
    {
        Monster owned{&owner, &owner, 2};
        const auto before = Snapshot(owned);
        const RemovedOwnerSnapshot deadPlayer{&owner, true, 0};
        const Result result = ownerIsLastLiving
            ? ApplyOwnerLossBeforeRemoval(&before, 1, &owner, LocalActor, 3, Functions)
            : ApplyOwnerLossBeforeMapRemoval(&before, 1, deadPlayer, LocalActor, 3, Functions);
        if (!ownerIsLastLiving)
        {
            NativeRemovedOwner(owned, &owner);
        }
        if (result.Applied != 1 || result.Flushed != 1
            || owned.Owner != nullptr || owned.OwnerLossCalls != 1
            || !owned.QueuedOwnerLoss || !owned.NetworkStateSent)
        {
            return false;
        }

        // Repeated death notifications and a subsequent revival have no
        // owner-linked old summon left to queue or broadcast a second time.
        const auto after = Snapshot(owned);
        if (ApplyOwnerLossBeforeRemoval(&after, 1, &owner, LocalActor,
                3, Functions).Applied != 0 || owned.OwnerLossCalls != 1)
        {
            return false;
        }
    }

    // Alive player transfers and Monster-owner reparenting must take the
    // original callback with their owner/authority still intact.
    for (const RemovedOwnerSnapshot removed : {
            RemovedOwnerSnapshot{&owner, true, 1},
            RemovedOwnerSnapshot{&owner, true, 1517},
            RemovedOwnerSnapshot{&owner, false, 0},
            RemovedOwnerSnapshot{&owner, false, -10}})
    {
        Monster unchanged{&owner, &owner, 2};
        const auto before = Snapshot(unchanged);
        if (ApplyOwnerLossBeforeMapRemoval(&before, 1, removed, LocalActor,
                3, Functions).Applied != 0
            || unchanged.Owner != &owner || unchanged.Authority != &owner
            || unchanged.OwnerLossCalls != 0 || unchanged.QueuedOwnerLoss
            || unchanged.NetworkStateSent)
        {
            return false;
        }
    }
    Monster overkill{&owner, &owner, 2};
    const auto overkillBefore = Snapshot(overkill);
    if (ApplyOwnerLossBeforeMapRemoval(&overkillBefore, 1,
            RemovedOwnerSnapshot{&owner, true, -1}, LocalActor,
            3, Functions).Flushed != 1)
    {
        return false;
    }

    // Capture and process every dependent summon before the map invokes the
    // removed player's own callback or broadcasts to individual monsters.
    Monster firstOwned{&owner, &owner, 2};
    Monster secondOwned{&owner, &owner, 2};
    Monster anotherOwner{&teammate, &owner, 2};
    const std::array<ActorSnapshot, 3> mapBefore{
        Snapshot(firstOwned), Snapshot(secondOwned), Snapshot(anotherOwner)};
    const auto mapResult = ApplyOwnerLossBeforeMapRemoval(
        mapBefore.data(), mapBefore.size(), RemovedOwnerSnapshot{&owner, true, 0},
        LocalActor, 3, Functions);
    NativeRemovedOwner(firstOwned, &owner);
    NativeRemovedOwner(secondOwned, &owner);
    const std::array<ActorSnapshot, 2> afterCallbacks{
        Snapshot(firstOwned), Snapshot(secondOwned)};
    if (mapResult.Applied != 2 || mapResult.Flushed != 2
        || firstOwned.OwnerLossCalls != 1 || secondOwned.OwnerLossCalls != 1
        || !firstOwned.NetworkStateSent || !secondOwned.NetworkStateSent
        || anotherOwner.OwnerLossCalls != 0
        || ApplyOwnerLossBeforeRemoval(afterCallbacks.data(), afterCallbacks.size(),
            &owner, LocalActor, 3, Functions).Applied != 0)
    {
        return false;
    }

    Monster remoteAuthority{&owner, &teammate, 2};
    Monster localPolicyOne{&owner, &owner, 1};
    Monster unrelated{&teammate, &owner, 2};
    Monster persistent{&owner, &owner, 0};
    Monster creaturePet{&owner, &owner, 2};
    const std::array<ActorSnapshot, 5> actors{
        Snapshot(remoteAuthority), Snapshot(localPolicyOne),
        Snapshot(unrelated), Snapshot(persistent), Snapshot(creaturePet, 129)};
    const Result result = ApplyOwnerLossBeforeRemoval(
        actors.data(), actors.size(), &owner, LocalActor, 3, Functions);
    if (result.Applied != 2 || result.Flushed != 0
        || remoteAuthority.Owner != nullptr || remoteAuthority.NetworkStateSent
        || localPolicyOne.NativeState != 101 || unrelated.OwnerLossCalls != 0
        || persistent.OwnerLossCalls != 0 || creaturePet.OwnerLossCalls != 0)
    {
        return false;
    }

    // Prove non-dungeon and missing context never inspect the candidate list.
    for (const int scene : {0, 1, 2, 4, 5})
    {
        if (ApplyOwnerLossBeforeRemoval(
                reinterpret_cast<const ActorSnapshot*>(1), 1,
                &owner, LocalActor, scene, Functions).Applied != 0)
        {
            return false;
        }
    }
    return ApplyOwnerLossBeforeRemoval(nullptr, 1, &owner, LocalActor,
               3, Functions).Applied == 0
        && ApplyOwnerLossBeforeRemoval(actors.data(), actors.size(), nullptr,
               LocalActor, 3, Functions).Applied == 0
        && ApplyOwnerLossBeforeRemoval(actors.data(), actors.size(), &owner,
               nullptr, 3, Functions).Applied == 0;
}
