#include "presentation_scaling.h"
#include <algorithm>
#include <string_view>

namespace dfl::presentation
{
    Mode ParseMode(const char* value) noexcept
    {
        if (value == nullptr) return Mode::Legacy;
        std::string_view text(value);
        while (!text.empty() && (text.front() == ' ' || text.front() == '\t')) text.remove_prefix(1);
        while (!text.empty() && (text.back() == ' ' || text.back() == '\t')) text.remove_suffix(1);
        for (const auto mode : {Mode::Sharp, Mode::Point, Mode::Linear})
        {
            const std::string_view name(ModeName(mode));
            if (name.size() != text.size()) continue;
            bool same = true;
            for (std::size_t i = 0; i < name.size(); ++i)
            {
                const char lower = text[i] >= 'A' && text[i] <= 'Z' ? static_cast<char>(text[i] + ('a' - 'A')) : text[i];
                same = same && lower == name[i];
            }
            if (same) return mode;
        }
        return Mode::Legacy;
    }

    const char* ModeName(Mode mode) noexcept
    {
        switch (mode)
        {
        case Mode::Sharp: return "sharp";
        case Mode::Point: return "point";
        case Mode::Linear: return "linear";
        default: return "legacy";
        }
    }

    Plan MakePlan(std::uint32_t width, std::uint32_t height, Mode mode, const Capabilities& caps) noexcept
    {
        Plan plan;
        if (mode == Mode::Legacy) { plan.Reason = "legacy"; return plan; }
        if (mode != Mode::Sharp && mode != Mode::Point && mode != Mode::Linear) return plan;
        if (width < 640 || height < 480 || width > 8192 || height > 8192 || width * 3 != height * 4)
        { plan.Reason = "window-size"; return plan; }
        plan.Width = width; plan.Height = height; plan.Factor = (width + 639) / 640;
        plan.UseIntermediate = mode == Mode::Sharp && width % 640 != 0;
        auto allocate = [&](std::uint32_t w, std::uint32_t h, std::uint32_t& aw, std::uint32_t& ah)
        {
            auto power = [](std::uint32_t v) { std::uint32_t result = 1; while (result < v) result *= 2; return result; };
            aw = caps.PowerOfTwo ? power(w) : w;
            ah = caps.PowerOfTwo ? power(h) : h;
            if (caps.Square) aw = ah = std::max(aw, ah);
            return aw <= caps.MaxWidth && ah <= caps.MaxHeight;
        };
        if (!allocate(640, 480, plan.SourceAllocationWidth, plan.SourceAllocationHeight))
        { plan.Reason = "source-capability"; return plan; }
        if (plan.UseIntermediate)
        {
            plan.IntermediateWidth = 640 * plan.Factor; plan.IntermediateHeight = 480 * plan.Factor;
            if (!allocate(plan.IntermediateWidth, plan.IntermediateHeight, plan.IntermediateAllocationWidth, plan.IntermediateAllocationHeight))
            { plan.Reason = "intermediate-capability"; return plan; }
        }
        plan.Supported = true; plan.Filter = mode; plan.Reason = "ok";
        return plan;
    }
    Plan MakeWindowPlan(std::uint32_t width,std::uint32_t height,std::uint32_t physicalWidth,std::uint32_t physicalHeight,Mode mode,const Capabilities& caps) noexcept
    {
        if(width!=physicalWidth||height!=physicalHeight)
        {Plan unsupported;unsupported.Width=width;unsupported.Height=height;unsupported.Reason="dpi-virtualized";return unsupported;}
        return MakePlan(width,height,mode,caps);
    }
}
