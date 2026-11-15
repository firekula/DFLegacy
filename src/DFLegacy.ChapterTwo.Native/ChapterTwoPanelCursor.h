#pragma once
#include <windows.h>

namespace dfl::chapter_two
{
    // Borrow only the increments owned by this panel; never reset the game's count.
    class PanelCursor
    {
        int raises_ = 0;
    public:
        void Update(bool hovered, bool visible, int (*show)(bool)) noexcept
        {
            if(hovered)
            {
                if(!visible)while(raises_<32){++raises_;if(show(true)>=0)break;}
            }
            else while(raises_>0){show(false);--raises_;}
        }
        void Update(HWND window, bool shown) noexcept
        {
            POINT point{};RECT bounds{};
            const bool inside = shown && IsWindowVisible(window) && !IsIconic(window) &&
                GetCursorPos(&point) && GetWindowRect(window,&bounds) && PtInRect(&bounds,point);
            const auto hit=inside?WindowFromPoint(point):nullptr;
            const bool hovered=shown && (::GetCapture()==window ||
                (inside && (hit==window || GetAncestor(hit,GA_ROOT)==window)));
            CURSORINFO cursor{sizeof(cursor)};
            const bool visible=!GetCursorInfo(&cursor) || (cursor.flags&CURSOR_SHOWING)!=0;
            Update(hovered,visible,[](bool value){return ShowCursor(value?TRUE:FALSE);});
        }
        void Release() noexcept {Update(false,true,[](bool value){return ShowCursor(value?TRUE:FALSE);});}
        static HCURSOR Shape(WORD hit) noexcept
        {
            const WORD id=hit==HTLEFT||hit==HTRIGHT?32644:hit==HTTOP||hit==HTBOTTOM?32645:
                hit==HTTOPLEFT||hit==HTBOTTOMRIGHT?32642:hit==HTTOPRIGHT||hit==HTBOTTOMLEFT?32643:32512;
            return LoadCursorW(nullptr,MAKEINTRESOURCEW(id));
        }
    };
}
