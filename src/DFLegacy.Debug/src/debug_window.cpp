#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>
#include <algorithm>
#include <cwchar>
#include "debug_window.h"

namespace
{
    constexpr wchar_t ClassName[] = L"DFLegacy.DebugOverlay.v2";
    constexpr int TitleHeight = 30, ControlHeight = 36, DotSize = 30, Border = 6;
    HWND Window = nullptr, Owner = nullptr;
    HINSTANCE Instance = nullptr;
    HFONT Font = nullptr;
    wchar_t Settings[MAX_PATH]{};
    dfl::debug_window::Content PanelContent{};
    RECT Bounds{10, 65, 510, 325}; // relative to the game's client origin
    bool Minimized = false, Hidden = false, F9Down = false, F10Down = false;
    bool DotDragging = false, DotMoved = false;
    POINT DotStart{}, DotOrigin{};
    int Scroll = 0, ContentHeight = 0;
    dfl::debug_window::RequestDropTest RequestDrop = nullptr;
    std::int64_t PendingDrop = 0, LastDropRequest = 0;
    std::int64_t ControlRun = 0;
    std::uint16_t ControlUser = 0;
    ULONGLONG PendingSince = 0;
    bool DropError = false;
    int CursorRaises = 0;

    void UpdateCursor(bool shown) noexcept
    {
        POINT point{};
        RECT rect{};
        const bool inside = shown && GetCursorPos(&point) && GetWindowRect(Window, &rect) && PtInRect(&rect, point);
        const long dx = point.x - rect.left - DotSize / 2, dy = point.y - rect.top - DotSize / 2;
        const bool hovered = shown && (GetCapture() == Window
            || (inside && (!Minimized || dx * dx + dy * dy <= DotSize * DotSize / 4)));
        if (hovered)
        {
            CURSORINFO info{sizeof(info)};
            if (GetCursorInfo(&info) && (info.flags & CURSOR_SHOWING) == 0)
            {
                // Cross-thread owner/owned windows share cursor state. Borrow
                // only our own increments and balance them when the pointer leaves.
                while (CursorRaises < 32)
                {
                    ++CursorRaises;
                    if (ShowCursor(TRUE) >= 0) break;
                }
            }
        }
        else while (CursorRaises > 0) { ShowCursor(FALSE); --CursorRaises; }
    }

    bool DropAvailable() noexcept
    {
        FILETIME time{}; GetSystemTimeAsFileTime(&time);
        const auto now = (static_cast<std::uint64_t>(time.dwHighDateTime) << 32) | time.dwLowDateTime;
        return PanelContent.visible && PanelContent.dropReady && PanelContent.dropAllowed
            && PanelContent.fileTime <= now && now - PanelContent.fileTime <= 15000000ULL
            && PendingDrop == 0 && RequestDrop != nullptr;
    }

    RECT OwnerRect() noexcept
    {
        RECT rect{};
        GetClientRect(Owner, &rect);
        MapWindowPoints(Owner, nullptr, reinterpret_cast<POINT*>(&rect), 2);
        return rect;
    }

    void Save() noexcept
    {
        if (Settings[0] == 0) return;
        const int values[] = {Bounds.left, Bounds.top, Bounds.right - Bounds.left, Bounds.bottom - Bounds.top};
        const wchar_t* keys[] = {L"X", L"Y", L"Width", L"Height"};
        for (int i = 0; i < 4; ++i)
        {
            wchar_t value[24]{};
            swprintf_s(value, L"%d", values[i]);
            WritePrivateProfileStringW(L"DebugOverlay", keys[i], value, Settings);
        }
    }

    void Remember() noexcept
    {
        RECT rect{}; GetWindowRect(Window, &rect);
        const auto owner = OwnerRect();
        const LONG width = Bounds.right - Bounds.left, height = Bounds.bottom - Bounds.top;
        Bounds.left = rect.left - owner.left; Bounds.top = rect.top - owner.top;
        Bounds.right = Bounds.left + (Minimized ? width : rect.right - rect.left);
        Bounds.bottom = Bounds.top + (Minimized ? height : rect.bottom - rect.top);
    }

    void Place() noexcept
    {
        const auto owner = OwnerRect();
        const int availableWidth = std::max(1L, owner.right - owner.left);
        const int availableHeight = std::max(1L, owner.bottom - owner.top);
        const int width = Minimized ? DotSize : std::min<int>(Bounds.right - Bounds.left, availableWidth);
        const int height = Minimized ? DotSize : std::min<int>(Bounds.bottom - Bounds.top, availableHeight);
        const int x = std::clamp<int>(Bounds.left, 0, std::max(0, availableWidth - width));
        const int y = std::clamp<int>(Bounds.top, 0, std::max(0, availableHeight - height));
        RECT current{}; GetWindowRect(Window, &current);
        if (current.left != owner.left + x || current.top != owner.top + y
            || current.right - current.left != width || current.bottom - current.top != height)
            SetWindowPos(Window, nullptr, owner.left + x, owner.top + y, width, height,
                SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_DEFERERASE);
    }

    void ToggleMinimized() noexcept
    {
        Remember();
        Minimized = !Minimized;
        if (Minimized)
            SetWindowRgn(Window, CreateEllipticRgn(0, 0, DotSize, DotSize), TRUE);
        else SetWindowRgn(Window, nullptr, TRUE);
        Place(); Save();
        InvalidateRect(Window, nullptr, FALSE);
    }

    void Paint(HDC target) noexcept
    {
        RECT rect{}; GetClientRect(Window, &rect);
        const HDC dc = CreateCompatibleDC(target);
        const HBITMAP bitmap = CreateCompatibleBitmap(target, std::max(1L, rect.right), std::max(1L, rect.bottom));
        const auto previousBitmap = SelectObject(dc, bitmap);
        const auto previousFont = SelectObject(dc, Font);
        const HBRUSH background = CreateSolidBrush(RGB(24, 29, 38));
        FillRect(dc, &rect, background); DeleteObject(background);
        SetBkMode(dc, TRANSPARENT); SetTextColor(dc, RGB(220, 234, 247));
        if (Minimized)
        {
            const HBRUSH dot = CreateSolidBrush(RGB(57, 141, 185));
            FillRect(dc, &rect, dot); DeleteObject(dot);
            DrawTextW(dc, L"D", -1, &rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        }
        else
        {
            RECT title{0, 0, rect.right, TitleHeight};
            const HBRUSH bar = CreateSolidBrush(RGB(42, 55, 72));
            FillRect(dc, &title, bar); DeleteObject(bar);
            RECT label{10, 0, rect.right - 64, TitleHeight};
            DrawTextW(dc, L"DF DEBUG  ·  拖动标题移动", -1, &label, DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
            RECT minimize{rect.right - 60, 0, rect.right - 30, TitleHeight};
            RECT close{rect.right - 30, 0, rect.right, TitleHeight};
            DrawTextW(dc, L"−", -1, &minimize, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
            DrawTextW(dc, L"×", -1, &close, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
            const bool available = DropAvailable();
            RECT checkbox{10, TitleHeight + 10, 26, TitleHeight + 26};
            DrawFrameControl(dc, &checkbox, DFC_BUTTON, DFCS_BUTTONCHECK
                | (PanelContent.dropEnabled ? DFCS_CHECKED : 0) | (available ? 0 : DFCS_INACTIVE));
            wchar_t control[160]{};
            const wchar_t* state = !PanelContent.dropReady ? L"等待服务端"
                : PanelContent.dropEnabled ? L"已开启" : L"已关闭";
            const wchar_t* detail = PendingDrop ? L"  ·  等待确认"
                : DropError ? L"  ·  提交失败，请重试"
                : PanelContent.dropReady && !PanelContent.dropAllowed ? L"  ·  仅 test 可操作" : L"";
            swprintf_s(control, L"掉落测试：%s%s", state, detail);
            RECT controlText{34, TitleHeight, rect.right - 10, TitleHeight + ControlHeight};
            SetTextColor(dc, RGB(available ? 220 : 150, 200, 180));
            DrawTextW(dc, control, -1, &controlText, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
            RECT body{10, TitleHeight + ControlHeight + 6, rect.right - 10, rect.bottom - 22};
            IntersectClipRect(dc, body.left, body.top, body.right, body.bottom);
            int y = body.top - Scroll;
            for (unsigned i = 0; i < PanelContent.count; ++i)
            {
                RECT line{body.left, y, body.right, y};
                DrawTextW(dc, PanelContent.lines[i].data(), -1, &line, DT_WORDBREAK | DT_CALCRECT | DT_NOPREFIX);
                const auto color = PanelContent.colors[i]; // native text colors are AABBGGRR
                SetTextColor(dc, RGB(color & 255, (color >> 8) & 255, (color >> 16) & 255));
                DrawTextW(dc, PanelContent.lines[i].data(), -1, &line, DT_WORDBREAK | DT_NOPREFIX);
                y = line.bottom + 5;
            }
            ContentHeight = y + Scroll - body.top;
            SelectClipRgn(dc, nullptr);
            RECT footer{10, rect.bottom - 20, rect.right - 10, rect.bottom};
            SetTextColor(dc, RGB(140, 160, 180));
            DrawTextW(dc, L"F9 圆点收起  ·  F10 隐藏  ·  滚轮查看  ◢", -1, &footer,
                DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
            const HBRUSH edge = CreateSolidBrush(RGB(76, 100, 122));
            FrameRect(dc, &rect, edge); DeleteObject(edge);
        }
        BitBlt(target, 0, 0, rect.right, rect.bottom, dc, 0, 0, SRCCOPY);
        SelectObject(dc, previousFont); SelectObject(dc, previousBitmap);
        DeleteObject(bitmap); DeleteDC(dc);
    }

    LRESULT CALLBACK Procedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) noexcept
    {
        switch (message)
        {
        case WM_SETCURSOR:
        {
            UpdateCursor(IsWindowVisible(window) != FALSE);
            const auto hit = LOWORD(lparam);
            const wchar_t* cursor = hit == HTLEFT || hit == HTRIGHT ? IDC_SIZEWE
                : hit == HTTOP || hit == HTBOTTOM ? IDC_SIZENS
                : hit == HTTOPLEFT || hit == HTBOTTOMRIGHT ? IDC_SIZENWSE
                : hit == HTTOPRIGHT || hit == HTBOTTOMLEFT ? IDC_SIZENESW : IDC_ARROW;
            SetCursor(LoadCursorW(nullptr, cursor));
            return TRUE; // don't let the game owner's WM_SETCURSOR hide it again
        }
        case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
        case WM_NCCALCSIZE: return 0;
        case WM_ERASEBKGND: return 1;
        case WM_PAINT:
        {
            PAINTSTRUCT paint{}; const HDC dc = BeginPaint(window, &paint);
            Paint(dc); EndPaint(window, &paint); return 0;
        }
        case WM_GETMINMAXINFO:
        {
            auto& info = *reinterpret_cast<MINMAXINFO*>(lparam);
            const auto owner = OwnerRect();
            if (Minimized)
            {
                info.ptMinTrackSize = info.ptMaxTrackSize = {DotSize, DotSize};
                return 0;
            }
            info.ptMinTrackSize = {std::min(260L, owner.right - owner.left), std::min(140L, owner.bottom - owner.top)};
            info.ptMaxTrackSize = {owner.right - owner.left, owner.bottom - owner.top};
            return 0;
        }
        case WM_NCHITTEST:
        {
            if (Minimized) return HTCLIENT;
            POINT point{GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam)};
            ScreenToClient(window, &point);
            RECT rect{}; GetClientRect(window, &rect);
            const bool left = point.x < Border, right = point.x >= rect.right - Border;
            const bool top = point.y < Border, bottom = point.y >= rect.bottom - Border;
            if (top) return left ? HTTOPLEFT : right ? HTTOPRIGHT : HTTOP;
            if (bottom) return left ? HTBOTTOMLEFT : right ? HTBOTTOMRIGHT : HTBOTTOM;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (point.y < TitleHeight && point.x < rect.right - 60) return HTCAPTION;
            return HTCLIENT;
        }
        case WM_NCLBUTTONDBLCLK:
            if (wparam == HTCAPTION) { ToggleMinimized(); return 0; }
            break;
        case WM_LBUTTONDOWN:
            if (Minimized)
            {
                DotDragging = true; DotMoved = false;
                GetCursorPos(&DotStart);
                RECT rect{}; GetWindowRect(window, &rect); DotOrigin = {rect.left, rect.top};
                SetCapture(window);
            }
            else if (GET_Y_LPARAM(lparam) < TitleHeight)
            {
                RECT rect{}; GetClientRect(window, &rect);
                if (GET_X_LPARAM(lparam) >= rect.right - 30) { Hidden = true; Save(); ShowWindow(window, SW_HIDE); }
                else if (GET_X_LPARAM(lparam) >= rect.right - 60) ToggleMinimized();
            }
            else if (GET_Y_LPARAM(lparam) < TitleHeight + ControlHeight && DropAvailable())
            {
                PendingDrop = RequestDrop(PanelContent, !PanelContent.dropEnabled);
                LastDropRequest = PendingDrop; PendingSince = GetTickCount64();
                DropError = PendingDrop == 0;
                InvalidateRect(window, nullptr, FALSE);
            }
            return 0;
        case WM_MOUSEMOVE:
            if (DotDragging)
            {
                POINT point{}; GetCursorPos(&point);
                const int dx = point.x - DotStart.x, dy = point.y - DotStart.y;
                if (abs(dx) > 3 || abs(dy) > 3) DotMoved = true;
                if (DotMoved)
                {
                    SetWindowPos(window, nullptr, DotOrigin.x + dx, DotOrigin.y + dy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                    Remember(); Place();
                }
            }
            return 0;
        case WM_LBUTTONUP:
            if (DotDragging)
            {
                DotDragging = false; ReleaseCapture();
                if (!DotMoved) ToggleMinimized(); else Save();
            }
            return 0;
        case WM_CAPTURECHANGED: DotDragging = false; return 0;
        case WM_EXITSIZEMOVE: Remember(); Place(); Save(); return 0;
        case WM_SIZE: Scroll = 0; InvalidateRect(window, nullptr, FALSE); return 0;
        case WM_MOUSEWHEEL:
        {
            RECT rect{}; GetClientRect(window, &rect);
            const int maximum = std::max(0, ContentHeight - static_cast<int>(rect.bottom) + TitleHeight + ControlHeight + 28);
            Scroll = std::clamp(Scroll - GET_WHEEL_DELTA_WPARAM(wparam) / WHEEL_DELTA * 45, 0, maximum);
            InvalidateRect(window, nullptr, FALSE); return 0;
        }
        case WM_CLOSE: Hidden = true; Save(); ShowWindow(window, SW_HIDE); return 0;
        }
        return DefWindowProcW(window, message, wparam, lparam);
    }
}

namespace dfl::debug_window
{
    bool Create(HWND owner, const wchar_t* settingsPath, RequestDropTest request) noexcept
    {
        if (owner == nullptr) return true; // headless host for protocol/decoder tests
        Owner = owner;
        RequestDrop = request;
        if (settingsPath != nullptr) wcsncpy_s(Settings, settingsPath, _TRUNCATE);
        if (Settings[0] != 0)
        {
            Bounds.left = std::clamp<int>(GetPrivateProfileIntW(L"DebugOverlay", L"X", 10, Settings), -8192, 8192);
            Bounds.top = std::clamp<int>(GetPrivateProfileIntW(L"DebugOverlay", L"Y", 65, Settings), -8192, 8192);
            Bounds.right = Bounds.left + std::clamp<int>(GetPrivateProfileIntW(L"DebugOverlay", L"Width", 500, Settings), 260, 4096);
            Bounds.bottom = Bounds.top + std::clamp<int>(GetPrivateProfileIntW(L"DebugOverlay", L"Height", 260, Settings), 140, 4096);
        }
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&Procedure), &Instance);
        WNDCLASSEXW cls{sizeof(cls)};
        cls.hInstance = Instance; cls.lpfnWndProc = &Procedure; cls.lpszClassName = ClassName;
        cls.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        if (RegisterClassExW(&cls) == 0) return false;
        Font = CreateFontW(-14, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
            OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Microsoft YaHei UI");
        Window = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST, ClassName, L"DF DEBUG",
            WS_POPUP | WS_THICKFRAME, 0, 0, 500, 260, Owner, nullptr, Instance, nullptr);
        if (Window == nullptr) { Destroy(); return false; }
        // Custom client border/title, retaining native resize/move loops.
        SetWindowPos(Window, nullptr, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        Place();
        return true;
    }

    void Update(const Content& content) noexcept
    {
        if (Window == nullptr) return;
        if (content.visible && !PanelContent.visible) Hidden = false;
        if (!content.visible || content.userId != ControlUser
            || (content.dropReady && content.runId != ControlRun))
        {
            PendingDrop = LastDropRequest = 0; DropError = false;
        }
        ControlUser = content.userId;
        if (content.dropReady) ControlRun = content.runId;
        PanelContent = content;
        if (LastDropRequest != 0 && content.dropRequestId == LastDropRequest)
        {
            PendingDrop = 0; DropError = content.dropResult != 1;
        }
        else if (PendingDrop != 0 && GetTickCount64() - PendingSince > 5000)
        {
            PendingDrop = 0; DropError = true;
        }
        const auto foreground = GetForegroundWindow();
        const bool focused = foreground == Owner || foreground == Window;
        const bool f9 = focused && (GetAsyncKeyState(VK_F9) & 0x8000) != 0;
        const bool f10 = focused && (GetAsyncKeyState(VK_F10) & 0x8000) != 0;
        if (f9 && !F9Down) ToggleMinimized();
        if (f10 && !F10Down) Hidden = !Hidden;
        F9Down = f9; F10Down = f10;
        const bool show = PanelContent.visible && !Hidden && focused && IsWindowVisible(Owner) && !IsIconic(Owner);
        if (show)
        {
            if (GetCapture() != Window) Place();
            // Reordering an owned window every frame can synchronously wait for
            // its game thread. Topmost is fixed at creation; visibility changes
            // are posted instead of repeatedly touching the owner's Z order.
            if (!IsWindowVisible(Window)) ShowWindowAsync(Window, SW_SHOWNOACTIVATE);
            InvalidateRect(Window, nullptr, FALSE);
        }
        else if (IsWindowVisible(Window)) ShowWindowAsync(Window, SW_HIDE);
        UpdateCursor(show);
    }

    void Destroy() noexcept
    {
        UpdateCursor(false);
        if (Window != nullptr) Save();
        if (Window != nullptr) DestroyWindow(Window);
        Window = nullptr;
        if (Font != nullptr) DeleteObject(Font);
        Font = nullptr;
        if (Instance != nullptr) UnregisterClassW(ClassName, Instance);
        Owner = nullptr; Instance = nullptr; PanelContent = {}; Settings[0] = 0;
        Bounds = {10, 65, 510, 325}; Minimized = Hidden = F9Down = F10Down = false; Scroll = 0;
        RequestDrop = nullptr; PendingDrop = LastDropRequest = 0; PendingSince = 0; DropError = false;
        ControlRun = 0; ControlUser = 0;
    }
}
