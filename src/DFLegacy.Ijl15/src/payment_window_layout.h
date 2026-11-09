#pragma once

namespace dfl::payment_window_layout
{
    struct Layout
    {
        bool Supported;
        int ScreenX, ScreenY, Width, Height;
        int LogicalX, LogicalY;
    };

    inline Layout Calculate(int left, int top, int width, int height) noexcept
    {
        if (width < 640 || height < 480 || width > 8192 || height > 8192
            || width * 3 != height * 4)
            return {};
        const int popupWidth = (width * 460 + 320) / 640;
        const int popupHeight = (height * 316 + 240) / 480;
        return {true, left + (width - popupWidth) / 2, top + (height - popupHeight) / 2,
            popupWidth, popupHeight, 90, 82};
    }
}
