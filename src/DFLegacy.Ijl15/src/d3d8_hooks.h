#pragma once
#include "ijl15_config.h"
#include "presentation_scaling.h"
#include "d3d8_abi.h"

namespace dfl::d3d8_hooks
{
    struct Options
    {
        presentation::Mode Filter=presentation::Mode::Legacy;
        ijl15_config::ScaleFilterMode LegacyFilter=ijl15_config::ScaleFilterMode::Native;
        void (*Pump)()=nullptr;
        bool InvalidMode=false;
    };
    // Used by the fixed-image IAT adapter and by test-owned D3D8 factories.
    // Borrowed COM pointer: contexts/resources are retired at original final Release.
    bool AttachFactory(void* factory,const Options& options) noexcept;
    bool UsesFixedFrameInput(HWND window) noexcept;
}
