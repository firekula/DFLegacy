#pragma once

#include <cstddef>
#include <cstdint>

namespace dfl::priest_heal_target
{
    bool Install() noexcept;

    namespace detail
    {
        constexpr std::uint16_t PlayerObjectType = 0x0111;

        struct CastTarget
        {
            bool Present = false;
            std::uint32_t Reference = 0;
        };

        // Native state-10 parameters are a vector<int>, not the compressed
        // bytes of GP5. Phase one appends target.v3C() after its phase value.
        CastTarget ReadCastTarget(int state, const int* parameters,
            std::size_t count) noexcept;

        // Make a known local self target explicit before native GP5 packing.
        // Receivers never have to infer a target from an omitted parameter.
        bool ShouldIncludeSelfTarget(void* caster, void* localActor,
            void* nativeTarget, void* requestedTarget, int scene,
            int phase, int skill) noexcept;

        struct ClientFunctions
        {
            void* (__cdecl* LookupUserActor)(std::uint16_t userId);
            std::uint32_t (__cdecl* ObjectReference)(void* actor);
            bool (__cdecl* MapContains)(void* map, void* actor);
        };

        // Resolve only an explicit player reference in this live map. A
        // missing/stale reference must never fall back to healing the caster.
        void* ResolveForeignTarget(const CastTarget& target, void* caster,
            void* localActor, void* map, int scene, int skill,
            const ClientFunctions& functions) noexcept;
    }
}
