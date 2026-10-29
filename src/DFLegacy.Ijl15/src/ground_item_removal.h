#pragma once

#include <cstddef>
#include <cstdint>

namespace dfl::ground_item_removal
{
    bool Install() noexcept;

    namespace detail
    {
        constexpr std::uint16_t GroundItemObjectType = 0x0121;

        struct PickupRemoval
        {
            std::uint16_t PickerUserId = 0;
            std::uint32_t ItemId = 0;
            std::uint16_t GroundId = 0;
        };

        // Private NOTI86 extension: retain the official seven-byte subtype-5
        // prefix, then append the actual ground id. The seven-byte packet and
        // every other subtype must continue through the original client code.
        bool ReadExtension(const unsigned char* payload, std::size_t size,
            PickupRemoval& removal) noexcept;

        struct GroundItem
        {
            void* Actor = nullptr;
            void* Map = nullptr;
            std::uint16_t ObjectType = 0;
            std::uint16_t GroundId = 0;
            bool Active = false;
        };

        struct ClientFunctions
        {
            GroundItem (__cdecl* Find)(void* map, std::uint16_t objectType,
                std::uint16_t groundId);
            void (__cdecl* Deactivate)(void* actor);
        };

        struct ContextReader
        {
            void* State = nullptr;
            bool (__cdecl* Read)(void*, std::uintptr_t, void*, std::size_t) = nullptr;
        };
        struct MapContext
        {
            void* Stage = nullptr;
            void* UiStage = nullptr;
            void* Map = nullptr;
            void* Actor = nullptr;
            void* ActorMap = nullptr;
            int Scene = -1;
            bool DeathKnown = false;
            bool Dead = false;
            const char* Reason = "context-unavailable";
        };
        // Read the actual receiver's current dispatcher/UI context. No native
        // secure getter is called: protected mirrors are decoded read-only.
        void* ReadCurrentMap(void* dispatcher, const ContextReader& reader,
            MapContext& context) noexcept;

        // This only marks the matched ground actor inactive, as NOTI39 does.
        // The existing map frame then removes it and destroys its descriptor.
        // There is no inventory grant or descriptor detachment on this path.
        bool ApplyRemoval(void* map, const PickupRemoval& removal,
            const ClientFunctions& functions, const char** outcome = nullptr) noexcept;
    }
}
