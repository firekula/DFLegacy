#include "startup_prompt.h"
#include "startup_prompt_resources.h"
#include <objidl.h>
#include <gdiplus.h>
#include <windowsx.h>

namespace dflegacy::startup {
namespace {
constexpr int Width = 286, Height = 170;
constexpr RECT ConfirmButton{103, 125, 183, 154};
constexpr wchar_t WindowClass[] = L"DFLegacy.Startup.ClassicPrompt";
constexpr wchar_t Title[] = L"提示信息";

struct PromptDpiScope {
    const DPI_AWARENESS_CONTEXT previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    ~PromptDpiScope() { if (previous) SetThreadDpiAwarenessContext(previous); }
};

struct SkinImage {
    IStream* stream = nullptr;
    Gdiplus::Bitmap* image = nullptr;
    ~SkinImage() {
        delete image;
        if (stream) stream->Release();
    }
    void Load(HINSTANCE instance, WORD id) noexcept {
        const auto resource = FindResourceW(instance, MAKEINTRESOURCEW(id), RT_RCDATA);
        if (!resource) return;
        const auto size = SizeofResource(instance, resource);
        const auto bytes = LockResource(LoadResource(instance, resource));
        if (!size || !bytes || FAILED(CreateStreamOnHGlobal(nullptr, TRUE, &stream))) return;
        LARGE_INTEGER zero{};
        if (SUCCEEDED(stream->Write(bytes, size, nullptr)) && SUCCEEDED(stream->Seek(zero, STREAM_SEEK_SET, nullptr)))
            image = Gdiplus::Bitmap::FromStream(stream);
        if (image && image->GetLastStatus() != Gdiplus::Ok) {
            delete image;
            image = nullptr;
        }
    }
};

struct GdiPlusSession {
    ULONG_PTR token = 0;
    GdiPlusSession() {
        Gdiplus::GdiplusStartupInput input;
        if (Gdiplus::GdiplusStartup(&token, &input, nullptr) != Gdiplus::Ok) token = 0;
    }
    ~GdiPlusSession() { if (token) Gdiplus::GdiplusShutdown(token); }
};

struct Prompt {
    const wchar_t* message;
    const Gdiplus::Font& font;
    SkinImage frame, confirm, hoverImage, pressed;
    int dpi = USER_DEFAULT_SCREEN_DPI;
    bool hover = false;
    bool held = false;

    int Scale(int value) const noexcept { return MulDiv(value, dpi, USER_DEFAULT_SCREEN_DPI); }
};

Gdiplus::Status DrawSkin(Gdiplus::Graphics& graphics, Gdiplus::Image* image, int x, int y, int width, int height) noexcept {
    return graphics.DrawImage(image, Gdiplus::Rect(x, y, width, height), 0, 0,
        static_cast<INT>(image->GetWidth()), static_cast<INT>(image->GetHeight()), Gdiplus::UnitPixel);
}

bool HitConfirm(const Prompt& prompt, int x, int y) noexcept {
    return x >= prompt.Scale(ConfirmButton.left) && x < prompt.Scale(ConfirmButton.right)
        && y >= prompt.Scale(ConfirmButton.top) && y < prompt.Scale(ConfirmButton.bottom);
}

bool PaintPrompt(Gdiplus::Graphics& graphics, const Prompt& prompt) noexcept {
    // The back buffer starts transparent black. Source-over on premultiplied
    // pixels discards the RGB stored under the skin's fully transparent corners.
    graphics.Clear(Gdiplus::Color(0, 0, 0, 0));
    graphics.SetCompositingMode(Gdiplus::CompositingModeSourceOver);
    graphics.SetPageUnit(Gdiplus::UnitPixel);
    const auto scale = static_cast<float>(prompt.dpi) / USER_DEFAULT_SCREEN_DPI;
    graphics.ScaleTransform(scale, scale);
    graphics.SetInterpolationMode(Gdiplus::InterpolationModeNearestNeighbor);
    graphics.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHalf);
    graphics.SetTextRenderingHint(Gdiplus::TextRenderingHintSingleBitPerPixelGridFit);
    if (DrawSkin(graphics, prompt.frame.image, 0, 0, Width, Height) != Gdiplus::Ok) return false;
    auto* button = prompt.confirm.image;
    if (prompt.hover)
        button = prompt.held && prompt.pressed.image ? prompt.pressed.image
            : prompt.hoverImage.image ? prompt.hoverImage.image : button;
    if (DrawSkin(graphics, button, ConfirmButton.left, ConfirmButton.top,
        ConfirmButton.right - ConfirmButton.left, ConfirmButton.bottom - ConfirmButton.top) != Gdiplus::Ok) return false;

    // Match the WPF body and keep the outline inside the message area.
    const Gdiplus::RectF body(17.0f, 49.0f, 252.0f, 69.0f);
    Gdiplus::StringFormat format;
    format.SetAlignment(Gdiplus::StringAlignmentCenter);
    format.SetFormatFlags(Gdiplus::StringFormatFlagsLineLimit);
    format.SetTrimming(Gdiplus::StringTrimmingNone);
    Gdiplus::RectF measured;
    INT fitted = 0;
    if (graphics.MeasureString(prompt.message, -1, &prompt.font, body, &format, &measured, &fitted) != Gdiplus::Ok
        || fitted != static_cast<INT>(wcslen(prompt.message))) return false;
    const Gdiplus::SolidBrush outline(Gdiplus::Color(255, 20, 20, 16));
    for (int dy = -1; dy <= 1; ++dy) {
        for (int dx = -1; dx <= 1; ++dx) {
            if (!dx && !dy) continue;
            graphics.DrawString(prompt.message, -1, &prompt.font,
                Gdiplus::RectF(body.X + dx, body.Y + dy, body.Width, body.Height), &format, &outline);
        }
    }
    const Gdiplus::SolidBrush ink(Gdiplus::Color(255, 244, 220, 175));
    return graphics.DrawString(prompt.message, -1, &prompt.font, body, &format, &ink) == Gdiplus::Ok;
}

bool Render(const Prompt& prompt, HWND window, POINT* destination = nullptr) noexcept {
    const auto screen = GetDC(nullptr);
    if (!screen) return false;
    const auto memory = CreateCompatibleDC(screen);
    if (!memory) {
        ReleaseDC(nullptr, screen);
        return false;
    }
    const int width = prompt.Scale(Width);
    const int height = prompt.Scale(Height);
    BITMAPINFO info{};
    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth = width;
    info.bmiHeader.biHeight = -height;
    info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32;
    info.bmiHeader.biCompression = BI_RGB;
    void* pixels = nullptr;
    const auto bitmap = CreateDIBSection(screen, &info, DIB_RGB_COLORS, &pixels, nullptr, 0);
    if (!bitmap || !pixels) {
        if (bitmap) DeleteObject(bitmap);
        DeleteDC(memory);
        ReleaseDC(nullptr, screen);
        return false;
    }
    const auto previous = SelectObject(memory, bitmap);
    bool painted;
    {
        Gdiplus::Bitmap canvas(width, height, width * 4, PixelFormat32bppPARGB,
            static_cast<BYTE*>(pixels));
        Gdiplus::Graphics graphics(&canvas);
        painted = PaintPrompt(graphics, prompt);
    }
    SIZE size{width, height};
    POINT source{0, 0};
    BLENDFUNCTION blend{AC_SRC_OVER, 0, 255, AC_SRC_ALPHA};
    const bool updated = painted && UpdateLayeredWindow(window, screen, destination, &size, memory, &source, 0, &blend, ULW_ALPHA);
    SelectObject(memory, previous);
    DeleteObject(bitmap);
    DeleteDC(memory);
    ReleaseDC(nullptr, screen);
    return updated;
}

LRESULT CALLBACK PromptProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) noexcept {
    auto* prompt = reinterpret_cast<Prompt*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    switch (message) {
    case WM_NCCREATE: {
        const auto* creation = reinterpret_cast<const CREATESTRUCTW*>(lParam);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(creation->lpCreateParams));
        return DefWindowProcW(window, message, wParam, lParam);
    }
    case WM_PAINT: {
        PAINTSTRUCT paint{};
        BeginPaint(window, &paint);
        EndPaint(window, &paint);
        return 0;
    }
    case WM_ERASEBKGND:
        return 1;
    case WM_DPICHANGED:
        if (prompt) {
            const int previous = prompt->dpi;
            prompt->dpi = HIWORD(wParam);
            const auto& suggested = *reinterpret_cast<const RECT*>(lParam);
            POINT destination{suggested.left, suggested.top};
            if (!Render(*prompt, window, &destination)) prompt->dpi = previous;
        }
        return 0;
    case WM_MOUSEMOVE:
        if (prompt) {
            const auto hot = HitConfirm(*prompt, GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam));
            if (hot != prompt->hover) {
                prompt->hover = hot;
                Render(*prompt, window);
            }
            TRACKMOUSEEVENT track{sizeof(track), TME_LEAVE, window, 0};
            TrackMouseEvent(&track);
        }
        return 0;
    case WM_MOUSELEAVE:
        if (prompt && prompt->hover) {
            prompt->hover = false;
            Render(*prompt, window);
        }
        return 0;
    case WM_LBUTTONDOWN:
        if (prompt && HitConfirm(*prompt, GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam))) {
            prompt->hover = true;
            prompt->held = true;
            SetCapture(window);
            Render(*prompt, window);
        }
        return 0;
    case WM_LBUTTONUP:
        if (prompt && prompt->held) {
            prompt->held = false;
            ReleaseCapture();
            if (HitConfirm(*prompt, GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam))) {
                DestroyWindow(window);
                return 0;
            }
            prompt->hover = false;
            Render(*prompt, window);
        }
        return 0;
    case WM_CAPTURECHANGED:
        if (prompt && prompt->held) {
            prompt->held = false;
            Render(*prompt, window);
        }
        return 0;
    case WM_SETCURSOR:
        if (prompt && LOWORD(lParam) == HTCLIENT) {
            POINT point{};
            GetCursorPos(&point);
            ScreenToClient(window, &point);
            if (HitConfirm(*prompt, point.x, point.y)) {
                SetCursor(LoadCursorW(nullptr, IDC_HAND));
                return TRUE;
            }
        }
        break;
    case WM_KEYDOWN:
        if (wParam == VK_RETURN || wParam == VK_ESCAPE) {
            DestroyWindow(window);
            return 0;
        }
        break;
    case WM_COMMAND:
        if (LOWORD(wParam) == IDOK) {
            DestroyWindow(window);
            return 0;
        }
        break;
    case WM_CLOSE:
        DestroyWindow(window);
        return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}
}

void ShowStartupPrompt(HINSTANCE instance, const wchar_t* message) noexcept {
    const auto fallback = [message] { MessageBoxW(nullptr, message, Title, MB_OK | MB_ICONERROR); };
    PromptDpiScope dpiScope;
    GdiPlusSession graphics;
    if (!graphics.token) return fallback();
    Gdiplus::Font font(L"SimSun", 12.0f, Gdiplus::FontStyleRegular, Gdiplus::UnitPixel);
    if (font.GetLastStatus() != Gdiplus::Ok) return fallback();
    Prompt prompt{message, font};
    prompt.frame.Load(instance, DFL_STARTUP_PROMPT_FRAME);
    prompt.confirm.Load(instance, DFL_STARTUP_PROMPT_CONFIRM);
    prompt.hoverImage.Load(instance, DFL_STARTUP_PROMPT_CONFIRM_HOVER);
    prompt.pressed.Load(instance, DFL_STARTUP_PROMPT_CONFIRM_DOWN);
    if (!prompt.frame.image || !prompt.confirm.image) return fallback();
    WNDCLASSW definition{};
    definition.lpfnWndProc = PromptProc;
    definition.hInstance = instance;
    definition.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    definition.lpszClassName = WindowClass;
    if (!RegisterClassW(&definition) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return fallback();
    MONITORINFO monitor{sizeof(monitor)};
    const auto active = MonitorFromWindow(GetForegroundWindow(), MONITOR_DEFAULTTOPRIMARY);
    const RECT work = GetMonitorInfoW(active, &monitor) ? monitor.rcWork
        : RECT{0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)};
    const auto window = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOOLWINDOW, WindowClass, Title, WS_POPUP,
        work.left + (work.right - work.left) / 2, work.top + (work.bottom - work.top) / 2, Width, Height,
        nullptr, nullptr, instance, &prompt);
    if (!window) return fallback();
    if (const auto dpi = GetDpiForWindow(window)) prompt.dpi = static_cast<int>(dpi);
    POINT centered{work.left + (work.right - work.left - prompt.Scale(Width)) / 2,
        work.top + (work.bottom - work.top - prompt.Scale(Height)) / 2};
    if (!Render(prompt, window, &centered)) {
        DestroyWindow(window);
        return fallback();
    }
    ShowWindow(window, SW_SHOW);
    SetForegroundWindow(window);
    SetFocus(window);
    MSG event{};
    while (IsWindow(window)) {
        const int status = GetMessageW(&event, nullptr, 0, 0);
        if (status <= 0) {
            DestroyWindow(window);
            if (status == 0) PostQuitMessage(static_cast<int>(event.wParam));
            break;
        }
        TranslateMessage(&event);
        DispatchMessageW(&event);
    }
}
}
