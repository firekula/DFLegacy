#include <cstdio>
#include <cstring>
#include "../src/presentation_scaling.h"

int main()
{
    using namespace dfl::presentation;
    int failed = 0;
    const auto check = [&](bool ok, const char* what) { if (!ok) { std::fprintf(stderr, "FAIL: %s\n", what); ++failed; } };
    const Capabilities modern{4096, 4096, false, false};
    const auto small = MakePlan(640, 480, Mode::Sharp, modern);
    check(small.Supported && !small.UseIntermediate && small.Factor == 1, "native size is a direct point copy");
    const auto sharp = MakePlan(800, 600, Mode::Sharp, modern);
    const auto virtualized=MakeWindowPlan(800,600,1000,750,Mode::Sharp,modern);
    check(!virtualized.Supported&&std::strcmp(virtualized.Reason,"dpi-virtualized")==0,"DPI virtualization cannot silently add a second system stretch");
    check(MakeWindowPlan(800,600,800,600,Mode::Sharp,modern).Supported,"physical pixel window uses the requested enhanced output");
    check(sharp.Supported && sharp.UseIntermediate && sharp.Factor == 2 && sharp.IntermediateWidth == 1280 && sharp.IntermediateHeight == 960,
        "800 sharp uses a 1280x960 nearest intermediate");
    const auto fractional = MakePlan(1024, 768, Mode::Sharp, modern);
    check(fractional.Supported && fractional.Factor == 2 && fractional.UseIntermediate, "noninteger sizes round the intermediate factor upward");
    const auto twice = MakePlan(1280, 960, Mode::Sharp, modern);
    check(twice.Supported && !twice.UseIntermediate && twice.Factor == 2, "integer size bypasses smoothing");
    const auto large = MakePlan(1600, 1200, Mode::Sharp, modern);
    check(large.Supported && large.Factor == 3 && large.IntermediateWidth == 1920 && large.IntermediateHeight == 1440, "1600 sharp uses factor three");
    check(!MakePlan(800, 480, Mode::Sharp, modern).Supported, "enhanced output rejects a distorted ratio");
    check(!MakePlan(0, 0, Mode::Sharp, modern).Supported, "minimized zero output is not allocated");
    check(!MakePlan(800, 600, Mode::Legacy, modern).Supported, "legacy never allocates enhanced resources");
    check(!MakePlan(800, 600, static_cast<Mode>(99), modern).Supported, "unknown mode cannot reach GPU draws");
    const Capabilities pot{4096, 4096, true, false};
    const auto padded = MakePlan(800, 600, Mode::Sharp, pot);
    check(padded.Supported && padded.SourceAllocationWidth == 1024 && padded.SourceAllocationHeight == 512
        && padded.IntermediateAllocationWidth == 2048 && padded.IntermediateAllocationHeight == 1024, "POT dimensions remain distinct from valid pixels");
    const Capabilities square{4096, 4096, true, true};
    const auto paddedSquare = MakePlan(800, 600, Mode::Sharp, square);
    check(paddedSquare.Supported && paddedSquare.SourceAllocationHeight == 1024 && paddedSquare.IntermediateAllocationHeight == 2048, "square-only hardware uses square allocations");
    const Capabilities constrained{1024, 512, true, false};
    check(!MakePlan(800, 600, Mode::Sharp, constrained).Supported, "oversized intermediate requests a visible fallback");
    check(MakePlan(800, 600, Mode::Point, constrained).Supported, "point output need not allocate a sharp intermediate");
    check(ParseMode(nullptr) == Mode::Legacy && ParseMode("unknown") == Mode::Legacy, "missing and unknown keys preserve legacy mode");
    check(ParseMode(" SHARP ") == Mode::Sharp && ParseMode("point") == Mode::Point && ParseMode("linear") == Mode::Linear,
        "managed/native mode spellings agree");
    if (failed == 0) std::puts("Presentation scaling policy: 17 checks passed");
    return failed == 0 ? 0 : 1;
}
