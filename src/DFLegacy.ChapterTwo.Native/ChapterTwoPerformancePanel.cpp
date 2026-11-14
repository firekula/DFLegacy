#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <psapi.h>
#include <objidl.h>
#include <gdiplus.h>
#include <windowsx.h>

#include <atomic>
#include <cmath>
#include <cwchar>
#include "ChapterTwoPerformance.h"
#include "ChapterTwoPerformanceHistory.h"
#include "ChapterTwoPerformanceLog.h"
#include "ChapterTwoType16Audio.h"
#include "ChapterTwoPanelCursor.h"
#include "PerformancePanelSkinResources.h"

namespace
{
    using namespace dfl::chapter_two::performance;
    constexpr wchar_t WindowClass[] = L"DFLegacy.ChapterTwo.Performance";
    constexpr COLORREF Background = RGB(20, 22, 17), Surface = RGB(36, 39, 30);
    constexpr COLORREF Text = RGB(244, 233, 202), Muted = RGB(215, 205, 174), Grid = RGB(98, 86, 56);
    constexpr COLORREF Blue = RGB(112, 201, 239), Gold = RGB(255, 216, 131), Spike = RGB(255, 123, 116);
    constexpr int PanelWidth = 708, PanelHeight = 552;
    constexpr int HeaderHeight = 57, ResizeEdge = 7, CloseButtonId = 101;
    constexpr std::size_t MemoryCapacity = 120;
    constexpr std::size_t SlowCapacity = 1024;
    constexpr ULONGLONG LogPeriodMs = 2000;
    constexpr double MiB = 1024.0 * 1024;

    struct MemorySample { float privateMiB, workingSetMiB; };
    History<MemorySample, MemoryCapacity> Memory;
    History<float, FrameCapacity> Frames;
    FrameMeasurements PendingFrames;
    PeriodStatistics PendingPeriod, Total;
    std::array<SlowFrame, SlowCapacity> PendingSlow{};
    std::size_t PendingSlowCount = 0;
    bool SlowOverflow = false;
    SRWLOCK FrameLock = SRWLOCK_INIT;
    std::atomic<unsigned> SkippedFrames{0}, PrivateTenthsMiB{0};
    std::atomic<DWORD> FrameThread{0};
    StageAccumulator StageTiming;
    bool TimingInstalled = false;
    // Odd epochs capture. A new epoch discards the interval across a focus change.
    std::atomic<unsigned> CaptureEpoch{0};
    HWND Panel = nullptr, GameWindow = nullptr;
    dfl::chapter_two::PanelCursor PanelPointer;
    bool Visible = false;
    ULONGLONG NextPaint = 0, NextMemory = 0;
    HFONT BodyFont = nullptr, HeadingFont = nullptr;
    WNDPROC DefaultCloseButtonProc = nullptr;

    // Keep the resource stream alive until its GDI+ bitmap is destroyed.
    struct SkinImage
    {
        IStream* stream = nullptr;
        Gdiplus::Bitmap* image = nullptr;
        ~SkinImage() { Reset(); }
        void Reset() noexcept
        {
            delete image;
            image = nullptr;
            if (stream) stream->Release();
            stream = nullptr;
        }
        void Load(HINSTANCE instance, WORD id) noexcept
        {
            const auto resource = FindResourceW(instance, MAKEINTRESOURCEW(id), MAKEINTRESOURCEW(10)); // RT_RCDATA
            if (!resource) return;
            const auto size = SizeofResource(instance, resource);
            const auto bytes = LockResource(LoadResource(instance, resource));
            if (!size || !bytes || FAILED(CreateStreamOnHGlobal(nullptr, TRUE, &stream))) return;
            LARGE_INTEGER zero{};
            if (SUCCEEDED(stream->Write(bytes, size, nullptr)) && SUCCEEDED(stream->Seek(zero, STREAM_SEEK_SET, nullptr)))
                image = Gdiplus::Bitmap::FromStream(stream);
            if (!image || image->GetLastStatus() != Gdiplus::Ok) Reset();
        }
    };

    struct PanelArtwork
    {
        ULONG_PTR token = 0;
        SkinImage frame, crest, close, closeDown;
        void Load(HINSTANCE instance) noexcept
        {
            if (token) return;
            Gdiplus::GdiplusStartupInput input;
            if (Gdiplus::GdiplusStartup(&token, &input, nullptr) != Gdiplus::Ok) return;
            frame.Load(instance, DFL_PERF_PANEL_FRAME);
            crest.Load(instance, DFL_PERF_PANEL_CREST);
            close.Load(instance, DFL_PERF_PANEL_CLOSE);
            closeDown.Load(instance, DFL_PERF_PANEL_CLOSE_DOWN);
        }
        void Reset() noexcept
        {
            frame.Reset();
            crest.Reset();
            close.Reset();
            closeDown.Reset();
            if (token) Gdiplus::GdiplusShutdown(token);
            token = 0;
        }
    };
    PanelArtwork Artwork;

    std::int64_t TickFrequency() noexcept
    {
        static const auto frequency = [] { LARGE_INTEGER f{}; QueryPerformanceFrequency(&f); return f.QuadPart; }();
        return frequency;
    }

    void SetCapture(bool capture) noexcept
    {
        if ((CaptureEpoch.load(std::memory_order_relaxed) % 2 != 0) != capture)
            CaptureEpoch.fetch_add(1, std::memory_order_release);
    }

    void Label(HDC dc, int x, int y, const wchar_t* value, COLORREF color = Text) noexcept
    {
        SetTextColor(dc, color);
        TextOutW(dc, x, y, value, static_cast<int>(std::wcslen(value)));
    }

    void TextIn(HDC dc, int left, int top, int right, const wchar_t* value, COLORREF color, UINT alignment = DT_LEFT) noexcept
    {
        if (right <= left) return;
        RECT line{left, top, right, top + 19};
        SetTextColor(dc, color);
        DrawTextW(dc, value, -1, &line, alignment | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS);
    }

    int TextWidth(HDC dc, const wchar_t* value) noexcept
    {
        SIZE size{};
        GetTextExtentPoint32W(dc, value, static_cast<int>(std::wcslen(value)), &size);
        return size.cx;
    }

    void Fill(HDC dc, const RECT& area, COLORREF color) noexcept
    {
        SetDCBrushColor(dc, color);
        FillRect(dc, &area, static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
    }

    void InsetSurface(HDC dc, const RECT& area) noexcept
    {
        const auto oldBrush = SelectObject(dc, GetStockObject(DC_BRUSH));
        const auto oldPen = SelectObject(dc, GetStockObject(DC_PEN));
        SetDCBrushColor(dc, Surface);
        SetDCPenColor(dc, Grid);
        RoundRect(dc, area.left, area.top, area.right, area.bottom, 16, 16);
        SelectObject(dc, oldPen);
        SelectObject(dc, oldBrush);
    }

    void PaintArtwork(HDC dc, int width, int height) noexcept
    {
        if (!Artwork.token) return;
        Gdiplus::Graphics graphics(dc);
        graphics.SetInterpolationMode(Gdiplus::InterpolationModeNearestNeighbor);
        graphics.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHalf);
        if (auto* frame = Artwork.frame.image)
        {
            // Stretch the strips between corners, keeping the native border and corner sizes.
            constexpr int edge = 12;
            const auto w = static_cast<int>(frame->GetWidth()), h = static_cast<int>(frame->GetHeight());
            const int sx[]{0, edge, w - edge, w}, sy[]{0, edge, h - edge, h};
            const int dx[]{0, edge, width - edge, width}, dy[]{0, edge, height - edge, height};
            for (int row = 0; row < 3; ++row)
                for (int column = 0; column < 3; ++column)
                    graphics.DrawImage(frame, Gdiplus::Rect(dx[column], dy[row], dx[column + 1] - dx[column], dy[row + 1] - dy[row]),
                        sx[column], sy[row], sx[column + 1] - sx[column], sy[row + 1] - sy[row], Gdiplus::UnitPixel);
        }
        if (auto* crest = Artwork.crest.image)
        {
            const auto w = static_cast<int>(crest->GetWidth()), h = static_cast<int>(crest->GetHeight());
            graphics.DrawImage(crest, (width - w) / 2, 5, w, h);
        }
    }

    void PaintCloseButton(const DRAWITEMSTRUCT& item) noexcept
    {
        auto* image = ((item.itemState & ODS_SELECTED) ? Artwork.closeDown : Artwork.close).image;
        Fill(item.hDC, item.rcItem, Background);
        if (image)
        {
            Gdiplus::Graphics graphics(item.hDC);
            graphics.DrawImage(image, item.rcItem.left, item.rcItem.top,
                item.rcItem.right - item.rcItem.left, item.rcItem.bottom - item.rcItem.top);
        }
        else
        {
            SetBkMode(item.hDC, TRANSPARENT);
            Label(item.hDC, item.rcItem.left + 6, item.rcItem.top + 3, L"×", Gold);
        }
    }

    void Guide(HDC dc, const RECT& plot, LONG y, const wchar_t* label, COLORREF color) noexcept
    {
        SetDCPenColor(dc, color);
        MoveToEx(dc, plot.left, y, nullptr);
        LineTo(dc, plot.right, y);
        RECT axis{ 25, y - 8, plot.left - 8, y + 11 };
        SetTextColor(dc, Muted);
        DrawTextW(dc, label, -1, &axis, DT_RIGHT | DT_SINGLELINE | DT_NOPREFIX);
    }

    template<class ValueAt>
    void Trace(HDC dc, const RECT& plot, std::size_t count, std::size_t capacity,
        float floor, float ceiling, ValueAt valueAt, COLORREF color, bool highlightSlow = false) noexcept
    {
        if (!count) return;
        std::array<POINT, FrameCapacity> points{};
        const auto width = plot.right - plot.left - 1, height = plot.bottom - plot.top - 1;
        for (std::size_t i = 0; i < count; ++i)
        {
            const auto position = static_cast<LONG>(capacity - count + i);
            const auto level = std::clamp((valueAt(i) - floor) / (ceiling - floor), 0.0f, 1.0f);
            points[i] = { plot.left + position * width / static_cast<LONG>(capacity - 1),
                plot.bottom - 1 - static_cast<LONG>(level * static_cast<float>(height)) };
        }
        SetDCPenColor(dc, color);
        if (count == 1) SetPixelV(dc, points[0].x, points[0].y, color);
        else Polyline(dc, points.data(), static_cast<int>(count));
        if (highlightSlow)
            for (std::size_t i = 0; i < count; ++i)
                if (valueAt(i) >= SlowFrameMs)
                {
                    const auto [x, y] = points[i];
                    Fill(dc, { x - 2, y - 2, x + 3, y + 3 }, Spike);
                }
    }

    void Paint(HDC dc, const RECT& area) noexcept
    {
        History<float, FrameCapacity> frames;
        AcquireSRWLockShared(&FrameLock);
        frames = Frames;
        ReleaseSRWLockShared(&FrameLock);
        const auto stats = Summarize(frames);

        const int width = area.right, height = area.bottom - HeaderHeight;
        Fill(dc, area, Background);
        if (width < 300 || height < 340) return;
        PaintArtwork(dc, width, area.bottom);
        SetBkMode(dc, TRANSPARENT);
        const auto oldFont = SelectObject(dc, HeadingFont);
        Label(dc, 25, 22, L"性能面板", Gold);
        POINT origin{};
        SetViewportOrgEx(dc, 0, HeaderHeight, &origin);
        const int footerTop = height - 68;
        const int memoryBottom = footerTop - 8;
        const int frameBottom = (memoryBottom - 8) * 57 / 100;
        const int memoryTop = frameBottom + 8;
        const int contentLeft = 25, contentRight = width - 25;
        InsetSurface(dc, { 12, 0, width - 12, frameBottom });
        InsetSurface(dc, { 12, memoryTop, width - 12, memoryBottom });
        InsetSurface(dc, { 12, footerTop, width - 12, height - 12 });
        const auto oldPen = SelectObject(dc, GetStockObject(DC_PEN));
        Label(dc, contentLeft, 14, L"帧时间", Gold);
        Label(dc, contentLeft, memoryTop + 11, L"进程内存", Gold);
        SelectObject(dc, BodyFont);
        const bool capturing = CaptureEpoch.load(std::memory_order_relaxed) % 2 != 0;
        TextIn(dc, 150, 17, contentRight, capturing ? L"正在记录前台游戏" : L"帧采样已暂停 · 游戏在后台",
            capturing ? Blue : Muted, DT_RIGHT);

        const wchar_t* captions[]{ L"FPS", L"平均", L"最快", L"P95", L"P99", L"最慢", L"慢帧 ≥33.3 ms" };
        const double values[]{ stats.fps, stats.average, stats.minimum, stats.p95, stats.p99,
            stats.maximum, static_cast<double>(stats.slow) };
        wchar_t label[96]{};
        // Leave the last metric room for its full unit label on narrow windows.
        constexpr int regularCount = static_cast<int>(std::size(captions)) - 1;
        const int slowLeft = contentRight - TextWidth(dc, captions[regularCount]);
        for (int i = 0; i < static_cast<int>(std::size(captions)); ++i)
        {
            const int x = contentLeft + i * (slowLeft - contentLeft) / regularCount;
            const int right = i == regularCount ? contentRight :
                contentLeft + (i + 1) * (slowLeft - contentLeft) / regularCount - 5;
            TextIn(dc, x, 54, right, captions[i], Muted);
            if (!frames.Size()) TextIn(dc, x, 73, right, L"—", Text);
            else
            {
                const auto format = i == 0 || i == 6 ? L"%.0f" : L"%.2f ms";
                swprintf_s(label, format, values[i]);
                TextIn(dc, x, 73, right, label, i == regularCount && stats.slow ? Spike : Text);
            }
        }

        const RECT framePlot{ 100, 103, contentRight, frameBottom - 27 };
        const float frameCeiling = std::max(40.0f, std::ceil(stats.maximum / 10.0f) * 10.0f);
        Fill(dc, framePlot, Background);
        swprintf_s(label, L"%.0f ms", static_cast<double>(frameCeiling));
        Guide(dc, framePlot, framePlot.top, label, Grid);
        Guide(dc, framePlot, framePlot.bottom - 1, L"0 ms", Grid);
        const auto slowY = framePlot.bottom - 1 - static_cast<LONG>(SlowFrameMs / frameCeiling
            * static_cast<float>(framePlot.bottom - framePlot.top - 1));
        Guide(dc, framePlot, slowY, L"33.3", Gold);
        Trace(dc, framePlot, frames.Size(), FrameCapacity, 0, frameCeiling,
            [&](std::size_t i) { return frames[i]; }, Blue, true);
        swprintf_s(label, L"最近 %zu / %zu 帧", frames.Size(), FrameCapacity);
        Label(dc, framePlot.left, framePlot.bottom + 5, label, Muted);
        TextIn(dc, framePlot.right - 80, framePlot.bottom + 5, framePlot.right, L"最新 →", Muted, DT_RIGHT);

        const auto memoryCount = Memory.Size();
        wchar_t memoryCaption[64]{};
        swprintf_s(memoryCaption, L"%zu / %zu 个样本 · 每秒", memoryCount, MemoryCapacity);
        const int memoryCaptionLeft = contentRight - TextWidth(dc, memoryCaption);
        float minMiB = 0, maxMiB = 64;
        if (memoryCount)
        {
            const auto latest = Memory[memoryCount - 1];
            minMiB = std::min(latest.privateMiB, latest.workingSetMiB);
            maxMiB = std::max(latest.privateMiB, latest.workingSetMiB);
            for (std::size_t i = 0; i < memoryCount; ++i)
            {
                minMiB = std::min({ minMiB, Memory[i].privateMiB, Memory[i].workingSetMiB });
                maxMiB = std::max({ maxMiB, Memory[i].privateMiB, Memory[i].workingSetMiB });
            }
            swprintf_s(label, L"私有提交  %.1f MiB", static_cast<double>(latest.privateMiB));
            TextIn(dc, contentLeft, memoryTop + 39, 207, label, Blue);
            swprintf_s(label, L"工作集  %.1f MiB", static_cast<double>(latest.workingSetMiB));
            TextIn(dc, 218, memoryTop + 39, memoryCaptionLeft - 8, label, Gold);
        }
        else Label(dc, contentLeft, memoryTop + 39, L"等待内存样本", Muted);
        TextIn(dc, memoryCaptionLeft, memoryTop + 39, contentRight, memoryCaption, Muted, DT_RIGHT);
        minMiB = std::floor(minMiB / 64.0f) * 64.0f;
        maxMiB = std::max(minMiB + 64.0f, std::ceil(maxMiB / 64.0f) * 64.0f);
        const RECT memoryPlot{ 100, memoryTop + 63, contentRight, memoryBottom - 12 };
        Fill(dc, memoryPlot, Background);
        swprintf_s(label, L"%.0f MiB", static_cast<double>(maxMiB));
        Guide(dc, memoryPlot, memoryPlot.top, label, Grid);
        swprintf_s(label, L"%.0f MiB", static_cast<double>(minMiB));
        Guide(dc, memoryPlot, memoryPlot.bottom - 1, label, Grid);
        Trace(dc, memoryPlot, memoryCount, MemoryCapacity, minMiB, maxMiB,
            [](std::size_t i) { return Memory[i].privateMiB; }, Blue);
        Trace(dc, memoryPlot, memoryCount, MemoryCapacity, minMiB, maxMiB,
            [](std::size_t i) { return Memory[i].workingSetMiB; }, Gold);
        const int footerColumn = (contentRight - contentLeft) / 3;
        swprintf_s(label, L"全程 %llu 帧", Total.frames.duration.count);
        TextIn(dc, contentLeft, footerTop + 9, contentLeft + footerColumn - 7, label, Text);
        swprintf_s(label, L"≥50 ms %llu 帧", Total.frames.slow50);
        TextIn(dc, contentLeft + footerColumn, footerTop + 9, contentLeft + 2 * footerColumn - 7, label, Text);
        swprintf_s(label, L"≥90 ms %llu 帧", Total.frames.slow90);
        TextIn(dc, contentLeft + 2 * footerColumn, footerTop + 9, contentRight, label, Text);
        swprintf_s(label, L"私有提交峰值 %.1f MiB", Total.privateMiB.maximum);
        TextIn(dc, contentLeft, footerTop + 31, contentLeft + 2 * footerColumn - 98, label, Text);
        TextIn(dc, contentLeft + 2 * footerColumn - 92, footerTop + 31, contentRight,
            L"Ctrl+F10 显示或隐藏 · 每 2 秒保存统计", Muted, DT_RIGHT);
        SelectObject(dc, oldPen);
        SelectObject(dc, oldFont);
        SetViewportOrgEx(dc, origin.x, origin.y, nullptr);
    }

    void HidePanel() noexcept
    {
        Visible = false;
        PanelPointer.Release();
        ShowWindow(Panel, SW_HIDE);
    }

    bool InsideCloseButton(HWND window, LPARAM position) noexcept
    {
        RECT bounds{};
        GetClientRect(window, &bounds);
        return PtInRect(&bounds, {GET_X_LPARAM(position), GET_Y_LPARAM(position)}) != FALSE;
    }

    LRESULT CALLBACK CloseButtonProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) noexcept
    {
        // The stock button takes keyboard focus on mouse-down, even under a
        // no-activate owner. Handle the pointer gesture without changing focus.
        switch (message)
        {
        case WM_LBUTTONDOWN:
        case WM_LBUTTONDBLCLK:
            SetCapture(window);
            SendMessageW(window, BM_SETSTATE, TRUE, 0);
            return 0;
        case WM_MOUSEMOVE:
            if (GetCapture() == window)
                SendMessageW(window, BM_SETSTATE, InsideCloseButton(window, lParam), 0);
            break;
        case WM_LBUTTONUP:
            if (GetCapture() == window)
            {
                const auto clicked = InsideCloseButton(window, lParam);
                ReleaseCapture();
                if (clicked)
                    SendMessageW(GetParent(window), WM_COMMAND, MAKEWPARAM(CloseButtonId, BN_CLICKED), reinterpret_cast<LPARAM>(window));
            }
            return 0;
        case WM_CAPTURECHANGED:
            SendMessageW(window, BM_SETSTATE, FALSE, 0);
            break;
        }
        return CallWindowProcW(DefaultCloseButtonProc, window, message, wParam, lParam);
    }

    LRESULT CALLBACK PanelProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) noexcept
    {
        switch (message)
        {
        case WM_NCCALCSIZE:
            return 0; // Paint the complete frame while keeping native resize hit tests.
        case WM_NCHITTEST:
        {
            RECT bounds{};
            GetWindowRect(window, &bounds);
            const auto x = GET_X_LPARAM(lParam), y = GET_Y_LPARAM(lParam);
            const bool left = x < bounds.left + ResizeEdge, right = x >= bounds.right - ResizeEdge;
            if (y < bounds.top + ResizeEdge) return left ? HTTOPLEFT : right ? HTTOPRIGHT : HTTOP;
            if (y >= bounds.bottom - ResizeEdge) return left ? HTBOTTOMLEFT : right ? HTBOTTOMRIGHT : HTBOTTOM;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            return HTCLIENT;
        }
        case WM_CREATE:
        {
            const auto creation = reinterpret_cast<const CREATESTRUCTW*>(lParam);
            const auto close = CreateWindowExW(0, L"BUTTON", L"关闭性能面板", WS_CHILD | WS_VISIBLE | BS_OWNERDRAW,
                0, 20, 25, 23, window, reinterpret_cast<HMENU>(CloseButtonId), creation->hInstance, nullptr);
            if (!close) return -1;
            DefaultCloseButtonProc = reinterpret_cast<WNDPROC>(
                SetWindowLongPtrW(close, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(CloseButtonProc)));
            return DefaultCloseButtonProc ? 0 : -1;
        }
        case WM_SIZE:
            if (const auto close = GetDlgItem(window, CloseButtonId))
                SetWindowPos(close, nullptr, LOWORD(lParam) - 43, 20, 25, 23, SWP_NOZORDER | SWP_NOACTIVATE);
            InvalidateRect(window, nullptr, FALSE);
            return 0;
        case WM_DRAWITEM:
            if (wParam == CloseButtonId)
            {
                PaintCloseButton(*reinterpret_cast<const DRAWITEMSTRUCT*>(lParam));
                return TRUE;
            }
            break;
        case WM_COMMAND:
            if (LOWORD(wParam) == CloseButtonId && HIWORD(wParam) == BN_CLICKED) HidePanel();
            return 0;
        case WM_MOUSEACTIVATE:
            return MA_NOACTIVATE;
        case WM_SETCURSOR:
            PanelPointer.Update(window,Visible);
            ::SetCursor(dfl::chapter_two::PanelCursor::Shape(LOWORD(lParam)));
            return TRUE; // Do not forward cursor hiding to the game owner.
        case WM_SHOWWINDOW:
            if(!wParam)PanelPointer.Release();
            break;
        case WM_DESTROY:
            PanelPointer.Release();
            break;
        case WM_CLOSE:
            HidePanel();
            return 0;
        case WM_ERASEBKGND:
            return 1;
        case WM_GETMINMAXINFO:
            reinterpret_cast<MINMAXINFO*>(lParam)->ptMinTrackSize = { 630, 464 };
            return 0;
        case WM_PAINT:
        {
            PAINTSTRUCT paint{};
            const HDC dc = BeginPaint(window, &paint);
            RECT area{};
            GetClientRect(window, &area);
            const auto buffer = CreateCompatibleDC(dc);
            const auto bitmap = buffer ? CreateCompatibleBitmap(dc, area.right, area.bottom) : nullptr;
            if (bitmap)
            {
                const auto oldBitmap = SelectObject(buffer, bitmap);
                Paint(buffer, area);
                BitBlt(dc, 0, 0, area.right, area.bottom, buffer, 0, 0, SRCCOPY);
                SelectObject(buffer, oldBitmap);
                DeleteObject(bitmap);
            }
            else Paint(dc, area);
            if (buffer) DeleteDC(buffer);
            EndPaint(window, &paint);
            return 0;
        }
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    void PlaceBesideGame() noexcept
    {
        RECT game{};
        if (!IsWindow(GameWindow) || !GetWindowRect(GameWindow, &game)) return;
        MONITORINFO monitor{};
        monitor.cbSize = sizeof(monitor);
        if (!GetMonitorInfoW(MonitorFromWindow(GameWindow, MONITOR_DEFAULTTONEAREST), &monitor)) return;
        const auto work = monitor.rcWork;
        RECT current{};
        GetWindowRect(Panel, &current);
        const LONG width = std::min<LONG>(current.right-current.left, work.right-work.left);
        const LONG height = std::min<LONG>(current.bottom-current.top, work.bottom-work.top);
        // Keep one owned pair in the normal z-order; never pin above other apps.
        if (current.left != game.right || current.top != game.top ||
            current.right-current.left != width || current.bottom-current.top != height)
            SetWindowPos(Panel, nullptr, game.right, game.top, width, height, SWP_NOACTIVATE | SWP_NOZORDER);
    }

    void TogglePanel(HWND game, HINSTANCE instance) noexcept
    {
        if (Visible) { HidePanel(); return; }
        GameWindow = game;
        if (!Panel)
        {
            Artwork.Load(instance);
            Panel = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WindowClass, L"第二章 Debug · 性能面板",
                WS_POPUP | WS_THICKFRAME | WS_CLIPCHILDREN,
                CW_USEDEFAULT, CW_USEDEFAULT, PanelWidth, PanelHeight, game, nullptr, instance, nullptr);
            if (!Panel)
            {
                OutputDebugStringA("DFLegacy Chapter Two: performance panel window creation failed.\n");
                return;
            }
        }
        SetWindowLongPtrW(Panel, GWLP_HWNDPARENT, reinterpret_cast<LONG_PTR>(game));
        PlaceBesideGame();
        NextPaint = 0;
        Visible = true;
        SetWindowPos(Panel, HWND_TOP, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    }

    void UpdatePanel(HINSTANCE instance, ULONGLONG now) noexcept
    {
        static bool wasDown = false;
        const bool down = (GetAsyncKeyState(VK_CONTROL) & 0x8000) && (GetAsyncKeyState(VK_F10) & 0x8000);
        const auto foreground = GetForegroundWindow();
        DWORD process = 0;
        if (foreground) GetWindowThreadProcessId(foreground, &process);
        const bool gameFocused = foreground && foreground != Panel && process == GetCurrentProcessId()
            && !IsIconic(foreground);
        if (down && !wasDown && gameFocused && instance) TogglePanel(foreground, instance);
        wasDown = down;
        if (Visible && Panel)
        {
            if (!IsWindow(GameWindow)) HidePanel();
            else if (IsIconic(GameWindow)) ShowWindow(Panel, SW_HIDE);
            else
            {
                PlaceBesideGame();
                if (!IsWindowVisible(Panel)) ShowWindow(Panel, SW_SHOWNOACTIVATE);
            }
        }
        PanelPointer.Update(Panel,Visible);
        SetCapture(gameFocused);
        if (now >= NextMemory)
        {
            NextMemory = now + 1000;
            PROCESS_MEMORY_COUNTERS_EX sample{};
            sample.cb = sizeof(sample);
            if (GetProcessMemoryInfo(GetCurrentProcess(), reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&sample), sizeof(sample)))
            {
                const double privateMiB = static_cast<double>(sample.PrivateUsage) / MiB;
                const double workingSetMiB = static_cast<double>(sample.WorkingSetSize) / MiB;
                Memory.Add({ static_cast<float>(privateMiB), static_cast<float>(workingSetMiB) });
                PendingPeriod.privateMiB.Add(privateMiB);
                PendingPeriod.workingSetMiB.Add(workingSetMiB);
                PrivateTenthsMiB.store(static_cast<unsigned>(privateMiB * 10 + 0.5), std::memory_order_relaxed);
            }
        }
        if (Visible && now >= NextPaint)
        {
            NextPaint = now + 250;
            InvalidateRect(Panel, nullptr, FALSE);
        }
    }

    DWORD WINAPI PanelThread(void* context) noexcept
    {
        auto instance = static_cast<HINSTANCE>(context);
        WNDCLASSW window{};
        window.lpfnWndProc = PanelProc;
        window.hInstance = instance;
        window.hCursor = LoadCursorW(nullptr, MAKEINTRESOURCEW(32512));
        window.lpszClassName = WindowClass;
        if (!RegisterClassW(&window)) instance = nullptr;
        const auto timer = SetTimer(nullptr, 0, 40, nullptr);
        if (!timer) return 1;
        BodyFont = CreateFontW(-14, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, 0, 0,
            NONANTIALIASED_QUALITY, 0, L"SimSun");
        HeadingFont = CreateFontW(-18, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET, 0, 0,
            NONANTIALIASED_QUALITY, 0, L"SimSun");
        _configthreadlocale(_ENABLE_PER_THREAD_LOCALE);
        std::setlocale(LC_NUMERIC, "C");
        CsvLog intervalLog(L"DFL_S1C2_PERF_INTERVAL_HANDLE"), totalLog(L"DFL_S1C2_PERF_TOTAL_HANDLE");
        CsvLog slowLog(L"DFL_S1C2_PERF_SLOW_HANDLE", true);
        std::array<SlowFrame, SlowCapacity> slowFrames{};
        const auto startTick = GetTickCount64();
        auto periodTick = startTick;
        SYSTEMTIME started{}, periodStarted{};
        GetSystemTime(&started);
        periodStarted = started;
        MSG message{};
        while (GetMessageW(&message, nullptr, 0, 0) > 0)
        {
            if (!message.hwnd && message.message == WM_TIMER && message.wParam == timer)
            {
                const auto now = GetTickCount64();
                UpdatePanel(instance, now);
                if (now - periodTick < LogPeriodMs) continue;
                dfl::chapter_two::LogType16AudioDiagnostics();
                SYSTEMTIME ended{};
                GetSystemTime(&ended);
                AcquireSRWLockExclusive(&FrameLock);
                PendingPeriod.frames = PendingFrames;
                PendingFrames = {};
                const auto slowCount = PendingSlowCount;
                const auto overflow = SlowOverflow;
                std::copy_n(PendingSlow.begin(), slowCount, slowFrames.begin());
                PendingSlowCount = 0;
                SlowOverflow = false;
                ReleaseSRWLockExclusive(&FrameLock);
                PendingPeriod.droppedFrames = SkippedFrames.exchange(0, std::memory_order_relaxed);
                Total.Merge(PendingPeriod);
                intervalLog.Append(periodStarted, ended, now - periodTick, PendingPeriod);
                totalLog.Append(started, ended, now - startTick, Total);
                for (std::size_t i = 0; i < slowCount; ++i) slowLog.AppendSlowFrame(slowFrames[i], TickFrequency());
                if (overflow) OutputDebugStringA("DFLegacy Chapter Two: slow-frame CSV buffer overflow.\n");
                periodStarted = ended;
                periodTick = now;
                PendingPeriod = {};
            }
            else
            {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
        KillTimer(nullptr, timer);
        SetCapture(false);
        PanelPointer.Release();
        if (Panel) DestroyWindow(Panel);
        Artwork.Reset();
        DeleteObject(HeadingFont);
        DeleteObject(BodyFont);
        return 0;
    }
}

void dfl::chapter_two::StartPerformancePanel(bool timingInstalled) noexcept
{
    TimingInstalled = timingInstalled;
    const auto thread = CreateThread(nullptr, 0, PanelThread, GetModuleHandleW(nullptr), 0, nullptr);
    if (thread) CloseHandle(thread);
}

void dfl::chapter_two::RecordPerformanceFrame() noexcept
{
    const auto epoch = CaptureEpoch.load(std::memory_order_acquire);
    if (epoch % 2 == 0) return;
    const auto frequency = TickFrequency();
    static LONGLONG previous = 0;
    static unsigned previousEpoch = 0;
    LARGE_INTEGER now{};
    if (!QueryPerformanceCounter(&now) || frequency <= 0) return;
    StageTimes stages;
    if (TimingInstalled)
    {
        const auto thread = GetCurrentThreadId();
        DWORD unset = 0;
        FrameThread.compare_exchange_strong(unset, thread, std::memory_order_relaxed);
        if (FrameThread.load(std::memory_order_relaxed) != thread) return;
        stages = StageTiming.Drain();
    }
    if (previousEpoch == epoch && now.QuadPart > previous)
    {
        const auto ticks = now.QuadPart - previous;
        const double elapsed = static_cast<double>(ticks) * 1000.0 / static_cast<double>(frequency);
        FILETIME utc{};
        // Keep every rendered shop frame, including fast ones for before/after comparisons.
        const bool detailed = TimingInstalled && (elapsed >= 50 || stages.shopPage != ShopPage::None);
        if (detailed) GetSystemTimePreciseAsFileTime(&utc);
        if (TryAcquireSRWLockExclusive(&FrameLock))
        {
            if (CaptureEpoch.load(std::memory_order_relaxed) == epoch)
            {
                Frames.Add(static_cast<float>(elapsed));
                PendingFrames.Add(elapsed);
                if (detailed)
                {
                    const auto end = (static_cast<std::uint64_t>(utc.dwHighDateTime) << 32) | utc.dwLowDateTime;
                    if (PendingSlowCount < PendingSlow.size()) PendingSlow[PendingSlowCount++] = { end, ticks, stages };
                    else SlowOverflow = true;
                }
            }
            ReleaseSRWLockExclusive(&FrameLock);
        }
        else SkippedFrames.fetch_add(1, std::memory_order_relaxed);
    }
    previous = now.QuadPart;
    previousEpoch = epoch;
}

dfl::chapter_two::performance::FrameStageScope::FrameStageScope(FrameStage stage, ShopPage page) noexcept
{
    if (stage == FrameStage::Count || CaptureEpoch.load(std::memory_order_relaxed) % 2 == 0 ||
        FrameThread.load(std::memory_order_relaxed) != GetCurrentThreadId()) return;
    LARGE_INTEGER now{};
    if (QueryPerformanceCounter(&now))
    {
        active_ = true;
        StageTiming.Begin(token_, stage, now.QuadPart, page);
    }
}

dfl::chapter_two::performance::FrameStageScope::~FrameStageScope() noexcept
{
    if (!active_) return;
    LARGE_INTEGER now{};
    StageTiming.End(token_, QueryPerformanceCounter(&now) ? now.QuadPart : token_.start - 1);
}

unsigned dfl::chapter_two::PerformancePrivateMemoryTenthsMiB() noexcept
{
    return PrivateTenthsMiB.load(std::memory_order_relaxed);
}
