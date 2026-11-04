#include "../src/payment_window_layout.h"
#include <cstdio>

int main()
{
    using dfl::payment_window_layout::Calculate;
    int failed = 0;
    const auto check = [&](bool okay, const char* name) {
        if (!okay) { std::fprintf(stderr, "FAIL: %s\n", name); ++failed; }
    };
    const auto native = Calculate(480, 120, 640, 480);
    check(native.Supported && native.ScreenX == 570 && native.ScreenY == 202
        && native.Width == 460 && native.Height == 316
        && native.LogicalX == 90 && native.LogicalY == 82, "native layout is unchanged");
    const auto large = Calculate(480, 120, 1600, 1200);
    check(large.Supported && large.ScreenX == 705 && large.ScreenY == 325
        && large.Width == 1150 && large.Height == 790, "browser viewport follows presentation scale");
    check(large.LogicalX == 90 && large.LogicalY == 82,
        "payment frame stays centered in the 640x480 renderer");
    const auto fractional = Calculate(-1000, 40, 800, 600);
    check(fractional.Supported && fractional.ScreenX == -888 && fractional.ScreenY == 142
        && fractional.Width == 575 && fractional.Height == 395,
        "fractional scaling rounds viewport and supports negative screen origins");
    check(!Calculate(0, 0, 1600, 900).Supported && !Calculate(0, 0, 0, 0).Supported,
        "unsupported presentation geometry is left untouched");
    return failed ? 1 : 0;
}
