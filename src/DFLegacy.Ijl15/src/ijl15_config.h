#pragma once

#include <cstddef>
#include "presentation_scaling.h"

namespace dfl::ijl15_config
{
    // Includes the entire base before the fixed /client/... page suffix.
    inline constexpr std::size_t MaximumClientServiceUrlLength = 1000;
    inline constexpr std::size_t ClientPageUrlCapacity = 1032;
    enum class ScaleFilterMode
    {
        Native,
        Linear,
        Point,
        Anisotropic,
    };

    struct Settings
    {
        int WindowWidth;
        int WindowHeight;
        bool EnablePerformanceOverlay;
        bool EnableMouseWheel;
        bool EnableAutoInput;
        ScaleFilterMode ScaleFilter;

        // Network dump (see net_dump.h): off by default because it writes one
        // line per packet. Turn it on in Config.ini when a capture is needed.
        bool DumpNetwork;
        // Payload bytes written per packet; 0 dumps the whole payload.
        int DumpNetworkPayloadBytes;
        // Stop writing once the log reaches this size, so an unattended client
        // cannot fill the disk.
        int DumpNetworkMaxMegabytes;
        bool EnableDebugOverlay;
        // Fixed module-owned storage: patched client pointers remain valid for
        // the entire module lifetime. Load() never changes them after first use.
        char ClientPayUrl[ClientPageUrlCapacity];
        char ClientDiamondUrl[ClientPageUrlCapacity];
        bool ExportGameplayTrace=false;
        bool ExportNativeLoopProfile=false;
        presentation::Mode PresentationFilter=presentation::Mode::Legacy;
        bool InvalidPresentationFilter=false;
    };

    // Load once from Config.ini beside the client executable. Missing or
    // invalid values fall back to the defaults below.
    bool Load() noexcept;

    const Settings& Get() noexcept;
}
