#pragma once

#include <cstddef>
#include <cstdint>

namespace dfl::summon_owner_death
{
    bool Install() noexcept;
}

namespace dfl::summon_owner_death::detail
{
    // Capture these fields before native map removal clears authority. The
    // adapter must validate Monster identity before interpreting its layout.
    struct ActorSnapshot
    {
        void* Actor;
        void* Owner;
        void* Authority;
        std::uint16_t Kind;
        std::uint8_t OwnerLossPolicy;
    };

    struct ClientFunctions
    {
        void (__cdecl* LoseOwner)(void* actor);
        bool (__cdecl* HasQueuedOwnerLoss)(void* actor);
        void (__cdecl* FlushQueuedOwnerLoss)(void* actor);
    };

    struct Result
    {
        std::size_t Applied = 0;
        std::size_t Flushed = 0;
    };

    struct RemovedOwnerSnapshot
    {
        void* Actor;
        bool SupportedPlayer;
        int CurrentHp;
    };

    // The local death path can remove a player before NOTI32 arrives. Apply
    // owner loss before any native map-removal callbacks clear references,
    // only for a dead validated player. Living transfers stay native.
    Result ApplyOwnerLossBeforeMapRemoval(
        const ActorSnapshot* actors,
        std::size_t count,
        const RemovedOwnerSnapshot& removedOwner,
        void* localActor,
        int sceneState,
        const ClientFunctions& functions) noexcept;

    // Invoke from the common life=false path, before the conditional native
    // map removal. This intentionally has no living-party-count condition.
    Result ApplyOwnerLossBeforeRemoval(
        const ActorSnapshot* actors,
        std::size_t count,
        void* dyingOwner,
        void* localActor,
        int sceneState,
        const ClientFunctions& functions) noexcept;
}
