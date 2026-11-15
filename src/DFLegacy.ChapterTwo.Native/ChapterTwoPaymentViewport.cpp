#include "ChapterTwoPaymentViewport.h"
#include "../DFLegacy.Ijl15/src/payment_window_layout.h"
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>

namespace
{
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr std::size_t BrowserCreateCall = 0x4fb004;
    constexpr std::size_t BrowserCreate = 0x5c0ca0;
    constexpr std::size_t BrowserLogicalBounds = 0x4fb028;
    constexpr std::size_t BackgroundDraw = 0x4fae12;
    using CreateBrowser = HWND (WINAPI*)(HWND, HINSTANCE, RECT*, BOOL);
    CreateBrowser OriginalCreate;

    HWND WINAPI CreateScaledBrowser(HWND owner, HINSTANCE instance, RECT* requested, BOOL modal)
    {
        if (!requested) return nullptr;
        RECT outer{}, client{};
        // The payment creator passes screen coordinates and dimensions in RECT fields.
        if (!owner || requested->right != 460 || requested->bottom != 316 ||
            !GetWindowRect(owner, &outer) || !GetClientRect(owner, &client) ||
            outer.right - outer.left != client.right || outer.bottom - outer.top != client.bottom ||
            requested->left != outer.left + (client.right - 460) / 2 ||
            requested->top != outer.top + (client.bottom - 316) / 2)
            return OriginalCreate(owner, instance, requested, modal);

        const auto layout = dfl::payment_window_layout::Calculate(outer.left, outer.top, client.right, client.bottom);
        if (!layout.Supported) return OriginalCreate(owner, instance, requested, modal);

        // The browser initializes its OLE viewport from its client area during creation.
        RECT physical{layout.ScreenX, layout.ScreenY, layout.Width, layout.Height};
        const HWND browser = OriginalCreate(owner, instance, &physical, modal);
        if (browser)
        {
            // The original caller subtracts its saved outer origin before drawing payweb.img.
            *requested = {outer.left + layout.LogicalX, outer.top + layout.LogicalY, 460, 316};
        }
        return browser;
    }
}

bool dfl::chapter_two::InstallPaymentViewport(unsigned char* image, std::size_t bytes) noexcept
{
    static_assert(sizeof(void*) == 4, "Chapter Two host must be x86.");
    if (!image || bytes != ImageSize) return false;

    constexpr unsigned char createCall[]{0xe8, 0x97, 0x5c, 0x0c, 0x00};
    constexpr unsigned char createPrologue[]{0x55, 0x8b, 0xec, 0x83, 0xec, 0x30};
    constexpr unsigned char logicalBounds[]{
        0x8b, 0x7d, 0xe0, 0x8b, 0x55, 0xe4, 0x8b, 0x45, 0xf0, 0x8b, 0x4d, 0xf4, 0x2b, 0xc7,
        0x8b, 0x3d, 0xf0, 0xa3, 0xae, 0x00, 0x2b, 0xca, 0x8b, 0x55, 0xf8, 0x89, 0x46, 0x28,
        0x03, 0xc2, 0x89, 0x46, 0x30, 0x8b, 0x45, 0xfc, 0x89, 0x4e, 0x2c, 0x03, 0xc8, 0x89,
        0x4e, 0x34,
    };
    constexpr unsigned char backgroundDraw[]{
        0x8b, 0x4e, 0x40, 0x6a, 0x00, 0xe8, 0xe4, 0xdd, 0x12, 0x00, 0x8b, 0x4e, 0x28, 0x50,
        0x8b, 0x46, 0x2c, 0x83, 0xe8, 0x17, 0x83, 0xe9, 0x18, 0x50, 0x51, 0x8b, 0x0d, 0xd4,
        0x1f, 0xe0, 0x00, 0xe8, 0x4a, 0x3c, 0x14, 0x00,
    };
    if (std::memcmp(image + BrowserCreateCall, createCall, sizeof(createCall)) != 0 ||
        std::memcmp(image + BrowserCreate, createPrologue, sizeof(createPrologue)) != 0 ||
        std::memcmp(image + BrowserLogicalBounds, logicalBounds, sizeof(logicalBounds)) != 0 ||
        std::memcmp(image + BackgroundDraw, backgroundDraw, sizeof(backgroundDraw)) != 0) return false;

    OriginalCreate = reinterpret_cast<CreateBrowser>(image + BrowserCreate);
    const auto relative = reinterpret_cast<std::uintptr_t>(&CreateScaledBrowser)
        - reinterpret_cast<std::uintptr_t>(image + BrowserCreateCall + sizeof(createCall));
    std::memcpy(image + BrowserCreateCall + 1, &relative, sizeof(relative));
    return true;
}
