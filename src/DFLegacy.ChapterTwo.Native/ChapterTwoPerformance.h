#pragma once
#include <cstddef>

namespace dfl::chapter_two
{
    bool InstallPerformanceOverlay(unsigned char* image, std::size_t bytes) noexcept;
    bool InstallSlowFrameTiming(unsigned char* image, std::size_t bytes) noexcept;
    bool InstallKeyboardMessageTiming(unsigned char* image, std::size_t bytes) noexcept;
    void StartPerformancePanel(bool timingInstalled = false) noexcept;
    void RecordPerformanceFrame() noexcept;
    unsigned PerformancePrivateMemoryTenthsMiB() noexcept;
}
