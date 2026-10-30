#pragma once

namespace dfl::ijl15_config
{
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
        bool ExportGameplayTrace=false;
        bool ExportNativeLoopProfile=false;
    };

    // Load once from Config.ini beside the client executable. Missing or
    // invalid values fall back to the defaults below.
    bool Load() noexcept;

    const Settings& Get() noexcept;
}
