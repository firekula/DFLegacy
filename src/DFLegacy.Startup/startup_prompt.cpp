#include "startup_prompt.h"
#include "startup_prompt_resources.h"
#include <objidl.h>
#include <gdiplus.h>

namespace dflegacy::startup {
namespace {
constexpr int Width = 286, Height = 170;
constexpr wchar_t WindowClass[] = L"DFLegacy.Startup.ClassicPrompt";

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
    HFONT font = nullptr;
    SkinImage frame, confirm, pressed;
    ~Prompt() { if (font) DeleteObject(font); }
};

void Fill(HDC dc, const RECT& bounds, COLORREF color) noexcept {
    SetDCBrushColor(dc, color);
    FillRect(dc, &bounds, static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
}

void DrawTextBlock(HDC dc, HFONT font, RECT bounds, const wchar_t* message) noexcept {
    const auto previous = font ? SelectObject(dc, font) : nullptr;
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, RGB(244, 220, 175));
    DrawTextW(dc, message, -1, &bounds, DT_CENTER | DT_NOPREFIX | DT_WORDBREAK | DT_END_ELLIPSIS);
    if (previous) SelectObject(dc, previous);
}

void DrawSkin(Gdiplus::Graphics& graphics, Gdiplus::Image* image, int x, int y, int width, int height) noexcept {
    if (!image) return;
    // The classic panel PNG stores its transparent corners as alpha-zero
    // magenta pixels. Set a color key as well so a window DC without alpha
    // compositing never paints the raw magenta.
    Gdiplus::ImageAttributes attributes;
    const auto magenta = Gdiplus::Color(255, 255, 0, 255);
    attributes.SetColorKey(magenta, magenta, Gdiplus::ColorAdjustTypeDefault);
    graphics.DrawImage(image, Gdiplus::Rect(x, y, width, height), 0, 0,
        static_cast<INT>(image->GetWidth()), static_cast<INT>(image->GetHeight()), Gdiplus::UnitPixel, &attributes);
}

LRESULT CALLBACK PromptProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) noexcept {
    auto* prompt = reinterpret_cast<Prompt*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    switch (message) {
    case WM_NCCREATE: {
        const auto* creation = reinterpret_cast<const CREATESTRUCTW*>(lParam);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(creation->lpCreateParams));
        return DefWindowProcW(window, message, wParam, lParam);
    }
    case WM_CREATE: {
        const auto* creation = reinterpret_cast<const CREATESTRUCTW*>(lParam);
        const auto button = CreateWindowExW(0, L"BUTTON", L"确认", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
            98, 137, 80, 29, window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(IDOK)), creation->hInstance, nullptr);
        return button ? 0 : -1;
    }
    case WM_COMMAND:
        if (LOWORD(wParam) == IDOK) DestroyWindow(window);
        return 0;
    case WM_CLOSE:
        DestroyWindow(window);
        return 0;
    case WM_ERASEBKGND:
        return 1;
    case WM_DRAWITEM:
        if (wParam == IDOK && prompt) {
            const auto& item = *reinterpret_cast<const DRAWITEMSTRUCT*>(lParam);
            auto* skin = (item.itemState & ODS_SELECTED ? prompt->pressed : prompt->confirm).image;
            Fill(item.hDC, item.rcItem, RGB(36, 39, 30));
            if (skin) {
                Gdiplus::Graphics graphics(item.hDC);
                DrawSkin(graphics, skin, item.rcItem.left, item.rcItem.top,
                    item.rcItem.right - item.rcItem.left, item.rcItem.bottom - item.rcItem.top);
            } else {
                DrawTextBlock(item.hDC, prompt->font, {0, 6, 80, 29}, L"确认");
            }
            return TRUE;
        }
        break;
    case WM_PAINT: {
        PAINTSTRUCT paint{};
        const auto dc = BeginPaint(window, &paint);
        Fill(dc, {0, 0, Width, Height}, RGB(44, 46, 35));
        if (prompt && prompt->frame.image) {
            Gdiplus::Graphics graphics(dc);
            DrawSkin(graphics, prompt->frame.image, 0, 0, Width, Height);
        } else {
            Fill(dc, {2, 10, Width - 2, 38}, RGB(88, 26, 8));
            DrawTextBlock(dc, prompt ? prompt->font : nullptr, {15, 16, 110, 38}, L"提示信息");
        }
        if (prompt) {
            // Give the error text a solid panel so the original texture stays
            // readable around it and Chinese text can wrap without clipping.
            Fill(dc, {17, 42, 269, 132}, RGB(39, 40, 31));
            DrawTextBlock(dc, prompt->font, {24, 53, 262, 127}, prompt->message);
        }
        EndPaint(window, &paint);
        return 0;
    }
    }
    return DefWindowProcW(window, message, wParam, lParam);
}
}

void ShowStartupPrompt(HINSTANCE instance, const wchar_t* message) noexcept {
    GdiPlusSession graphics;
    Prompt prompt{message};
    prompt.font = CreateFontW(-14, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
        0, 0, NONANTIALIASED_QUALITY, 0, L"SimSun");
    if (graphics.token) {
        prompt.frame.Load(instance, DFL_STARTUP_PROMPT_FRAME);
        prompt.confirm.Load(instance, DFL_STARTUP_PROMPT_CONFIRM);
        prompt.pressed.Load(instance, DFL_STARTUP_PROMPT_CONFIRM_DOWN);
    }
    WNDCLASSW definition{};
    definition.lpfnWndProc = PromptProc;
    definition.hInstance = instance;
    definition.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    definition.lpszClassName = WindowClass;
    if (!RegisterClassW(&definition) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return;
    RECT work{};
    if (!SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0))
        work = {0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)};
    const auto window = CreateWindowExW(WS_EX_DLGMODALFRAME, WindowClass, L"提示信息", WS_POPUP | WS_CLIPCHILDREN,
        work.left + (work.right - work.left - Width) / 2,
        work.top + (work.bottom - work.top - Height) / 2, Width, Height,
        nullptr, nullptr, instance, &prompt);
    if (!window) return;
    // Keep the prompt rectangular. The classic panel PNG has transparent
    // rounded corners; a window region exposes whatever the game clears to
    // (magenta in the DirectInput failure path) and can clip the frame edge.
    // The panel is drawn over the opaque fill instead.
    ShowWindow(window, SW_SHOW);
    SetFocus(GetDlgItem(window, IDOK));
    MSG event{};
    while (IsWindow(window)) {
        const int status = GetMessageW(&event, nullptr, 0, 0);
        if (status <= 0) {
            DestroyWindow(window);
            if (status == 0) PostQuitMessage(static_cast<int>(event.wParam));
            break;
        }
        if (event.message == WM_KEYDOWN && (event.wParam == VK_RETURN || event.wParam == VK_ESCAPE))
            DestroyWindow(window);
        else if (!IsDialogMessageW(window, &event)) {
            TranslateMessage(&event);
            DispatchMessageW(&event);
        }
    }
}
}
