#pragma once
#include <windows.h>
#include <array>
#include <cstdint>

namespace dfl::debug_window
{
    struct Content
    {
        bool visible = false;
        unsigned count = 0;
        std::array<std::array<wchar_t, 180>, 260> lines{};
        std::array<std::uint32_t, 260> colors{};
        bool dropReady = false, dropAllowed = false, dropEnabled = false;
        std::uint8_t dropResult = 0;
        std::uint16_t userId = 0;
        std::int64_t runId = 0, dropRequestId = 0;
        std::uint64_t fileTime = 0;
    };
    using RequestDropTest = std::int64_t (*)(const Content&, bool) noexcept;
    // All window operations run on the diagnostic worker, never the render thread.
    bool Create(HWND owner, const wchar_t* settingsPath, RequestDropTest request = nullptr) noexcept;
    void Update(const Content& content) noexcept;
    void Destroy() noexcept;
}
