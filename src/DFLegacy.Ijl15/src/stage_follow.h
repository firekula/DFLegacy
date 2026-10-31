#pragma once

#include <cstddef>

namespace dfl::stage_follow
{
    // Called after map membership is removed, before actor virtual +0x50.
    bool Install() noexcept;

    namespace detail
    {
        inline constexpr std::size_t FollowTargetOffset = 0x58;
        inline constexpr std::size_t StageMapOffset = 0xB0;

        struct ClientFunctions
        {
            void* (__cdecl* Select)(void* stage, void* localActor);
            void (__cdecl* Set)(void* stage, void* actor);
            bool (__cdecl* Contains)(void* map, void* actor);
        };

        // The same conditional repair is exercised with an isolated map in
        // the native tests. It never changes life flags or countdown state.
        bool RepairFollowTarget(
            void* stage,
            void* map,
            void* removedActor,
            void* localActor,
            int sceneState,
            const ClientFunctions& functions) noexcept;
    }
}
