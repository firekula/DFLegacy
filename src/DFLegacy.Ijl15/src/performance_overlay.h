#pragma once
#include <cstdint>

namespace dfl::performance_overlay
{
    // Redirect the 2008DF performance-draw call to the dual native/IJL15 hook.
    bool InstallCallHook() noexcept;
    void DrawOverlayText(int x, int y, std::uint32_t color, const char* text) noexcept;
}
