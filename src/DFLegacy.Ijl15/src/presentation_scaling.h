#pragma once
#include <cstdint>

namespace dfl::presentation
{
    enum class Mode { Legacy, Sharp, Point, Linear };
    struct Capabilities
    {
        std::uint32_t MaxWidth;
        std::uint32_t MaxHeight;
        bool PowerOfTwo;
        bool Square;
    };
    struct Plan
    {
        Mode Filter = Mode::Legacy;
        bool Supported = false;
        bool UseIntermediate = false;
        std::uint32_t Width = 0, Height = 0, Factor = 0;
        std::uint32_t SourceAllocationWidth = 0, SourceAllocationHeight = 0;
        std::uint32_t IntermediateWidth = 0, IntermediateHeight = 0;
        std::uint32_t IntermediateAllocationWidth = 0, IntermediateAllocationHeight = 0;
        const char* Reason = "unsupported";
    };
    Mode ParseMode(const char* value) noexcept;
    const char* ModeName(Mode mode) noexcept;
    Plan MakePlan(std::uint32_t width, std::uint32_t height, Mode mode, const Capabilities& caps) noexcept;
    Plan MakeWindowPlan(std::uint32_t width,std::uint32_t height,std::uint32_t physicalWidth,std::uint32_t physicalHeight,Mode mode,const Capabilities& caps) noexcept;
}
