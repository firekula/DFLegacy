#include "ui.hpp"
#include "startup_prompt_resources.h"
#include <objidl.h>
#include <gdiplus.h>
#include <string>

namespace dfo {
namespace {
constexpr int MessageWidth = 286;
constexpr int MessageHeight = 170;
constexpr wchar_t MessageClass[] = L"DFOLauncher.ClassicMessage";
constexpr int ProgressWidth = 420;
constexpr int ProgressHeight = 140;
constexpr wchar_t ProgressClass[] = L"DFOLauncher.Progress";
constexpr UINT ProgressMessage = WM_APP + 1;

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
    ~GdiPlusSession() {
        if (token) Gdiplus::GdiplusShutdown(token);
    }
};

void Fill(HDC dc, const RECT& bounds, COLORREF color) noexcept {
    SetDCBrushColor(dc, color);
    FillRect(dc, &bounds, static_cast<HBRUSH>(GetStockObject(DC_BRUSH)));
}

void DrawTextBlock(HDC dc, HFONT font, RECT bounds, const wchar_t* message, UINT flags) noexcept {
    const auto previous = font ? SelectObject(dc, font) : nullptr;
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, RGB(244, 220, 175));
    DrawTextW(dc, message, -1, &bounds, flags | DT_NOPREFIX);
    if (previous) SelectObject(dc, previous);
}

struct Prompt {
    const wchar_t* message;
    bool confirmed = false;
    HFONT font = nullptr;
    SkinImage frame, confirm, pressed;
    ~Prompt() {
        if (font) DeleteObject(font);
    }
};

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
                                            98, 137, 80, 29, window, reinterpret_cast<HMENU>(static_cast<INT_PTR>(IDOK)),
                                            creation->hInstance, nullptr);
        return button ? 0 : -1;
    }
    case WM_COMMAND:
        if (LOWORD(wParam) == IDOK) {
            if (prompt) prompt->confirmed = true;
            DestroyWindow(window);
        }
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
                graphics.DrawImage(skin, item.rcItem.left, item.rcItem.top, 80, 29);
            } else {
                DrawTextBlock(item.hDC, prompt->font, {0, 6, 80, 29}, L"确认", DT_CENTER | DT_SINGLELINE);
            }
            return TRUE;
        }
        break;
    case WM_PAINT: {
        PAINTSTRUCT paint{};
        const auto dc = BeginPaint(window, &paint);
        Fill(dc, {0, 0, MessageWidth, MessageHeight}, RGB(44, 46, 35));
        if (prompt && prompt->frame.image) {
            Gdiplus::Graphics graphics(dc);
            graphics.DrawImage(prompt->frame.image, 0, 0, MessageWidth, MessageHeight);
        } else {
            Fill(dc, {2, 10, MessageWidth - 2, 38}, RGB(88, 26, 8));
            DrawTextBlock(dc, prompt ? prompt->font : nullptr, {15, 16, 110, 38}, L"提示信息",
                          DT_CENTER | DT_SINGLELINE);
        }
        if (prompt) {
            Fill(dc, {17, 42, 269, 132}, RGB(39, 40, 31));
            DrawTextBlock(dc, prompt->font, {24, 53, 262, 127}, prompt->message,
                          DT_CENTER | DT_WORDBREAK | DT_END_ELLIPSIS);
        }
        EndPaint(window, &paint);
        return 0;
    }
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

bool ShowClassicMessage(HINSTANCE instance, const wchar_t* message) noexcept {
    GdiPlusSession graphics;
    Prompt prompt{message};
    prompt.font = CreateFontW(-14, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, 0, 0,
                              NONANTIALIASED_QUALITY, 0, L"SimSun");
    if (graphics.token) {
        prompt.frame.Load(instance, DFL_STARTUP_PROMPT_FRAME);
        prompt.confirm.Load(instance, DFL_STARTUP_PROMPT_CONFIRM);
        prompt.pressed.Load(instance, DFL_STARTUP_PROMPT_CONFIRM_DOWN);
    }
    WNDCLASSW definition{};
    definition.lpfnWndProc = PromptProc;
    definition.hInstance = instance;
    definition.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    definition.lpszClassName = MessageClass;
    if (!RegisterClassW(&definition) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return false;
    RECT work{};
    if (!SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0))
        work = {0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)};
    const auto window = CreateWindowExW(WS_EX_DLGMODALFRAME, MessageClass, L"提示信息", WS_POPUP | WS_CLIPCHILDREN,
                                        work.left + (work.right - work.left - MessageWidth) / 2,
                                        work.top + (work.bottom - work.top - MessageHeight) / 2, MessageWidth,
                                        MessageHeight, nullptr, nullptr, instance, &prompt);
    if (!window) return false;
    const auto shape = CreateRoundRectRgn(0, 0, MessageWidth + 1, MessageHeight + 1, 14, 14);
    if (shape && !SetWindowRgn(window, shape, FALSE)) DeleteObject(shape);
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
        if (event.message == WM_KEYDOWN && event.wParam == VK_RETURN) {
            prompt.confirmed = true;
            DestroyWindow(window);
        } else if (event.message == WM_KEYDOWN && event.wParam == VK_ESCAPE) {
            DestroyWindow(window);
        } else if (!IsDialogMessageW(window, &event)) {
            TranslateMessage(&event);
            DispatchMessageW(&event);
        }
    }
    return prompt.confirmed;
}

struct ProgressInfo {
    std::size_t done;
    std::size_t total;
    std::wstring name;
};
}

bool Confirm(HINSTANCE instance, const wchar_t* message) {
    return ShowClassicMessage(instance, message);
}

void Error(HINSTANCE instance, const wchar_t* message) {
    ShowClassicMessage(instance, message);
}

struct ProgressWindow::Impl {
    explicit Impl(HINSTANCE hinstance) : instance(hinstance) {
        if (graphics.token) frame.Load(instance, DFL_STARTUP_PROMPT_FRAME);
        font = CreateFontW(-14, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, 0, 0,
                           NONANTIALIASED_QUALITY, 0, L"SimSun");
        WNDCLASSW definition{};
        definition.lpfnWndProc = Proc;
        definition.hInstance = instance;
        definition.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        definition.lpszClassName = ProgressClass;
        if (!RegisterClassW(&definition) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return;
        RECT work{};
        if (!SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0))
            work = {0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)};
        window = CreateWindowExW(WS_EX_TOOLWINDOW, ProgressClass, L"提示信息", WS_POPUP | WS_CLIPCHILDREN,
                                 work.left + (work.right - work.left - ProgressWidth) / 2,
                                 work.top + (work.bottom - work.top - ProgressHeight) / 2, ProgressWidth,
                                 ProgressHeight, nullptr, nullptr, instance, this);
        if (!window) return;
        const auto shape = CreateRoundRectRgn(0, 0, ProgressWidth + 1, ProgressHeight + 1, 14, 14);
        if (shape && !SetWindowRgn(window, shape, FALSE)) DeleteObject(shape);
        timer = SetTimer(window, 1, 200, nullptr);
    }

    ~Impl() {
        if (timer) KillTimer(window, timer);
        if (window) DestroyWindow(window);
        if (font) DeleteObject(font);
    }

    static LRESULT CALLBACK Proc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) noexcept {
        auto* impl = reinterpret_cast<Impl*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        switch (message) {
        case WM_NCCREATE: {
            const auto* creation = reinterpret_cast<const CREATESTRUCTW*>(lParam);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(creation->lpCreateParams));
            return DefWindowProcW(window, message, wParam, lParam);
        }
        case WM_TIMER:
            if (impl && impl->timer) {
                KillTimer(window, impl->timer);
                impl->timer = 0;
                ShowWindow(window, SW_SHOWNOACTIVATE);
                UpdateWindow(window);
            }
            return 0;
        case WM_CLOSE:
            return 0;
        case WM_ERASEBKGND:
            return 1;
        case ProgressMessage: {
            auto* info = reinterpret_cast<ProgressInfo*>(wParam);
            if (impl && info) {
                impl->done = info->done;
                impl->total = info->total;
                impl->text = info->name;
                InvalidateRect(window, nullptr, FALSE);
            }
            delete info;
            return 0;
        }
        case WM_PAINT:
            if (impl) {
                impl->Paint(window);
            } else {
                PAINTSTRUCT paint{};
                BeginPaint(window, &paint);
                EndPaint(window, &paint);
            }
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    void ShowNow() {
        if (timer) {
            KillTimer(window, timer);
            timer = 0;
        }
        ShowWindow(window, SW_SHOWNOACTIVATE);
        UpdateWindow(window);
    }

    void Paint(HWND target) {
        PAINTSTRUCT paint{};
        const auto dc = BeginPaint(target, &paint);
        Fill(dc, {0, 0, ProgressWidth, ProgressHeight}, RGB(44, 46, 35));
        if (frame.image) {
            Gdiplus::Graphics render(dc);
            render.DrawImage(frame.image, 0, 0, ProgressWidth, ProgressHeight);
        }
        Fill(dc, {16, 12, ProgressWidth - 16, ProgressHeight - 12}, RGB(39, 40, 31));
        DrawTextBlock(dc, font, {28, 20, ProgressWidth - 28, 44}, L"正在整理客户端",
                      DT_LEFT | DT_SINGLELINE);
        const std::wstring status =
            std::to_wstring(done) + L" / " + std::to_wstring(total) + (text.empty() ? L"" : L"  " + text);
        DrawTextBlock(dc, font, {28, 50, ProgressWidth - 28, 74}, status.c_str(),
                      DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS);
        RECT bar{28, 96, ProgressWidth - 28, 112};
        Fill(dc, bar, RGB(25, 26, 21));
        if (total != 0 && done != 0) {
            RECT progress = bar;
            progress.right = bar.left + static_cast<LONG>((bar.right - bar.left) * done / total);
            Fill(dc, progress, RGB(196, 160, 84));
        }
        FrameRect(dc, &bar, static_cast<HBRUSH>(GetStockObject(GRAY_BRUSH)));
        EndPaint(target, &paint);
    }

    HINSTANCE instance;
    GdiPlusSession graphics;
    SkinImage frame;
    HFONT font = nullptr;
    HWND window = nullptr;
    UINT_PTR timer = 0;
    std::size_t done = 0;
    std::size_t total = 0;
    std::wstring text = L"正在整理客户端…";
};

ProgressWindow::ProgressWindow(HINSTANCE instance) : impl_(new Impl(instance)) {}

ProgressWindow::~ProgressWindow() {
    delete impl_;
}

void ProgressWindow::Update(std::size_t done, std::size_t total, const std::wstring& name) {
    if (!impl_ || !impl_->window) return;
    auto* info = new ProgressInfo{done, total, name};
    if (!PostMessageW(impl_->window, ProgressMessage, reinterpret_cast<WPARAM>(info), 0)) delete info;
}

void ProgressWindow::Close() {
    if (!impl_ || !impl_->window) return;
    if (impl_->timer) {
        KillTimer(impl_->window, impl_->timer);
        impl_->timer = 0;
    }
    DestroyWindow(impl_->window);
    impl_->window = nullptr;
}
}
