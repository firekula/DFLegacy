#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <psapi.h>

#include <atomic>
#include <cmath>
#include <cwchar>
#include "ChapterTwoPerformance.h"
#include "ChapterTwoPerformanceHistory.h"
#include "ChapterTwoPerformanceLog.h"
#include "ChapterTwoType16Audio.h"
#include "ChapterTwoPanelCursor.h"

namespace
{
    using namespace dfl::chapter_two::performance;
    constexpr wchar_t WindowClass[] = L"DFLegacy.ChapterTwo.Performance";
    constexpr COLORREF Background = RGB(16, 21, 29), Surface = RGB(24, 32, 44);
    constexpr COLORREF Text = RGB(230, 239, 250), Muted = RGB(161, 178, 199), Grid = RGB(52, 67, 85);
    constexpr COLORREF Blue = RGB(73, 182, 250), Gold = RGB(255, 195, 100), Spike = RGB(255, 123, 116);
    constexpr int PanelWidth = 708, PanelHeight = 552;
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

    void Fill(HDC dc, const RECT& area, COLORREF color) noexcept
    {
        SetDCBrushColor(dc, color);
        FillRect(dc, &area, static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
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

        const int width = area.right, height = area.bottom;
        if (width < 300 || height < 340) return;
        const int split = height * 58 / 100;
        Fill(dc, area, Background);
        Fill(dc, { 12, 11, width - 12, split - 7 }, Surface);
        Fill(dc, { 12, split + 3, width - 12, height - 54 }, Surface);
        SetBkMode(dc, TRANSPARENT);
        const auto oldFont = SelectObject(dc, HeadingFont);
        const auto oldPen = SelectObject(dc, GetStockObject(DC_PEN));
        Label(dc, 25, 21, L"帧时间");
        Label(dc, 25, split + 13, L"进程内存");
        SelectObject(dc, BodyFont);
        const bool capturing = CaptureEpoch.load(std::memory_order_relaxed) % 2 != 0;
        Label(dc, width - 203, 22, capturing ? L"正在记录前台游戏" : L"帧采样已暂停 · 游戏在后台", capturing ? Blue : Muted);

        const wchar_t* captions[]{ L"FPS", L"平均", L"最快", L"P95", L"P99", L"最慢", L"慢帧 ≥33.3 ms" };
        const double values[]{ stats.fps, stats.average, stats.minimum, stats.p95, stats.p99,
            stats.maximum, static_cast<double>(stats.slow) };
        wchar_t label[96]{};
        for (int i = 0; i < static_cast<int>(std::size(captions)); ++i)
        {
            const int x = 25 + i * (width - 47) / static_cast<int>(std::size(captions));
            Label(dc, x, 54, captions[i], Muted);
            if (!frames.Size()) Label(dc, x, 73, L"—");
            else
            {
                const auto format = i == 0 || i == 6 ? L"%.0f" : L"%.2f ms";
                swprintf_s(label, format, values[i]);
                Label(dc, x, 73, label, i == 6 && stats.slow ? Spike : Text);
            }
        }

        const RECT framePlot{ 100, 109, width - 26, split - 36 };
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
        Label(dc, framePlot.right - 55, framePlot.bottom + 5, L"最新 →", Muted);

        const auto memoryCount = Memory.Size();
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
            Label(dc, 25, split + 41, label, Blue);
            swprintf_s(label, L"工作集  %.1f MiB", static_cast<double>(latest.workingSetMiB));
            Label(dc, 218, split + 41, label, Gold);
        }
        else Label(dc, 25, split + 41, L"等待内存样本", Muted);
        swprintf_s(label, L"%zu / %zu 个样本 · 每秒", memoryCount, MemoryCapacity);
        Label(dc, width - 203, split + 41, label, Muted);
        minMiB = std::floor(minMiB / 64.0f) * 64.0f;
        maxMiB = std::max(minMiB + 64.0f, std::ceil(maxMiB / 64.0f) * 64.0f);
        const RECT memoryPlot{ 100, split + 68, width - 26, height - 69 };
        Fill(dc, memoryPlot, Background);
        swprintf_s(label, L"%.0f MiB", static_cast<double>(maxMiB));
        Guide(dc, memoryPlot, memoryPlot.top, label, Grid);
        swprintf_s(label, L"%.0f MiB", static_cast<double>(minMiB));
        Guide(dc, memoryPlot, memoryPlot.bottom - 1, label, Grid);
        Trace(dc, memoryPlot, memoryCount, MemoryCapacity, minMiB, maxMiB,
            [](std::size_t i) { return Memory[i].privateMiB; }, Blue);
        Trace(dc, memoryPlot, memoryCount, MemoryCapacity, minMiB, maxMiB,
            [](std::size_t i) { return Memory[i].workingSetMiB; }, Gold);
        swprintf_s(label, L"全程 %llu 帧   ·   ≥50 ms %llu 帧   ·   ≥90 ms %llu 帧   ·   私有提交峰值 %.1f MiB",
            Total.frames.duration.count, Total.frames.slow50, Total.frames.slow90, Total.privateMiB.maximum);
        Label(dc, 25, height - 47, label);
        Label(dc, 25, height - 24, L"Ctrl+F10 显示或隐藏   ·   每 2 秒保存分段和累计统计", Muted);
        SelectObject(dc, oldPen);
        SelectObject(dc, oldFont);
    }

    void HidePanel() noexcept
    {
        Visible = false;
        PanelPointer.Release();
        ShowWindow(Panel, SW_HIDE);
    }

    LRESULT CALLBACK PanelProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) noexcept
    {
        switch (message)
        {
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
            Panel = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WindowClass, L"第二章 Debug · 性能面板",
                WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME,
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
            CLEARTYPE_QUALITY, 0, L"Microsoft YaHei UI");
        HeadingFont = CreateFontW(-18, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET, 0, 0,
            CLEARTYPE_QUALITY, 0, L"Microsoft YaHei UI");
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
