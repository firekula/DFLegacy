#pragma once
#include <cstdint>

namespace dfl::chapter_two::debug_control
{
    // Only active in the debug client: the launcher creates the shared flags
    // block and the host exposes the two toggles. Release hosts are built
    // without DFL_CHAPTER_TWO_DEBUG_CONTROL and never link this module.
    bool TeleportEnabled() noexcept;
    bool InvincibleEnabled() noexcept;
    void Start() noexcept;
    void Tick() noexcept;
    bool TryTeleport(std::uint32_t actor, int x, int y) noexcept;
}
