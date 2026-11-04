#pragma once
#include "d3d8_abi.h"

namespace dfl::window_input_scaling
{
    bool ClientToLogical(HWND window,POINT& point) noexcept;
    bool LogicalToClient(HWND window,POINT& point) noexcept;
    bool Translate(const MSG& physical,MSG& logical) noexcept;
    bool Install() noexcept;
}
