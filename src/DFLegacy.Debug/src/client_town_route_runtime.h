#pragma once
#include "client_navigation.h"
#include "town_route_controller.h"

namespace dfl::client_town_route
{
    // DLRT/v1 shares only the96-byte representation with DLNV. Its town/area
    // name the final goal; all pointer/identity fields bind the starting map.
    const char* ValidateGoalRequest(const client_navigation::Request&,
        const client_state::Snapshot&, std::uint64_t now, std::uint64_t consumed) noexcept;
    town_route::Observation Observe(const client_state::Snapshot&) noexcept;
    using ApplyDestination = bool (*)(const client_navigation::Request&) noexcept;
    using SupersedeLegacy = void (*)() noexcept;
    void Initialize(const wchar_t* settings, const wchar_t* directory,
                    client_state::Reader, ApplyDestination, SupersedeLegacy, bool diagnostics = true, bool commands = true) noexcept;
    bool BeginCanvasGoal(const client_state::Snapshot&, const client_navigation::CanvasGoal&) noexcept;
    void OnFrame(const client_state::Snapshot&) noexcept;
    void LegacyTakeover() noexcept;
    void PumpWorker() noexcept;
    void Shutdown() noexcept;
}
