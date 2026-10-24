#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::party_combat_detail
{
    bool Install() noexcept;

    namespace detail
    {
        struct PendingInitialization
        {
            void* Actor = nullptr;
            void* ParserFrame = nullptr;
            std::uint8_t Mode = 0;
            int SceneState = -1;
        };

        struct ClientFunctions
        {
            void (__cdecl* Recalculate)(void* actor, bool initializeHp);
        };

        // A selection recovery notification may initialize only before
        // NOTI30 starts the handoff to the actual dungeon stage.
        bool ApplySelectionRecovery(void* actor, int sceneState,
            bool pureSelection, const ClientFunctions& functions) noexcept;

        struct TransitionInitialization
        {
            void* Actor = nullptr;
            void* User = nullptr;
            std::uint16_t UserId = 0;
            std::uint8_t Mode = 0;
            std::uint32_t QueuedAtMs = 0;
        };

        struct TransitionQueue
        {
            std::array<TransitionInitialization, 256> Entries{};
            std::size_t Count = 0;
        };

        using ValidateTransition = bool (__cdecl*)(const TransitionInitialization&);

        // AREA_USERS starts a native stage-4 fade while the secure scene is
        // still Dungeon. Only complete detail records from that fade qualify.
        bool QueueTownTransition(TransitionQueue& queue,
            const TransitionInitialization& entry, int sceneState,
            int mainModuleState) noexcept;

        // Consume before invoking callbacks, so re-entrant or repeated scene
        // changes cannot replay a reset. A stale/replaced user is never used.
        unsigned int CompleteTownTransition(TransitionQueue& queue,
            int oldScene, int newScene, int actualScene, std::uint32_t nowMs,
            ValidateTransition validate, const ClientFunctions& functions) noexcept;

        // Mode 1 and 3 load stats before equipment. Keep the early derived
        // recalculation, but initialize Town / pure-selection HP and MP after
        // the complete record. Handoff state2 must preserve battle values.
        void BeginDetail(
            PendingInitialization& pending,
            void* actor,
            void* parserFrame,
            int sceneState,
            std::uint8_t mode,
            bool nativeInitializeHp,
            const ClientFunctions& functions,
            bool pureSelection = false) noexcept;

        bool CompleteDetail(
            PendingInitialization& pending,
            void* actor,
            void* parserFrame,
            int sceneState,
            std::uint8_t mode,
            const ClientFunctions& functions,
            bool pureSelection = false) noexcept;
    }
}
