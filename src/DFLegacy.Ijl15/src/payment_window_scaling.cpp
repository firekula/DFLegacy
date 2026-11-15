#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstring>
#include "memory_patch.h"
#include "payment_window_layout.h"
#include "payment_window_scaling.h"

namespace
{
    // 2008DF sub_820280 opens the pay/diamond page. Its sole browser-create
    // call is followed by screen-origin subtraction at 0x82040A; sub_820190
    // draws interface/payweb.img using those offsets minus (24,23).
    constexpr std::uintptr_t CreateCall = 0x008203E6;
    using CreateBrowser = HWND (WINAPI*)(HWND, HINSTANCE, RECT*, BOOL);
    const auto OriginalCreate = reinterpret_cast<CreateBrowser>(0x008AB260);

    HWND WINAPI CreateScaledBrowser(HWND owner, HINSTANCE instance, RECT* requested, BOOL modal)
    {
        RECT outer{}, client{};
        // The native parameter is {screen x, screen y, width, height}, not LTRB.
        // Only this payment callsite is intercepted; other browser hosts are untouched.
        if (!requested || requested->right != 460 || requested->bottom != 316
            || !GetWindowRect(owner, &outer) || !GetClientRect(owner, &client)
            || outer.right - outer.left != client.right || outer.bottom - outer.top != client.bottom
            || requested->left != outer.left + (client.right - 460) / 2
            || requested->top != outer.top + (client.bottom - 316) / 2)
            return OriginalCreate(owner, instance, requested, modal);
        const auto layout = dfl::payment_window_layout::Calculate(
            outer.left, outer.top, client.right, client.bottom);
        if (!layout.Supported) return OriginalCreate(owner, instance, requested, modal);
        RECT physical{layout.ScreenX, layout.ScreenY, layout.Width, layout.Height};
        // Create at the final size: native WM_CREATE initializes the OLE viewport
        // from GetClientRect. Resizing afterwards would leave that viewport stale.
        const HWND browser = OriginalCreate(owner, instance, &physical, modal);
        if (browser)
        {
            // The caller subtracts the saved owner screen origin and stores these
            // offsets for interface/payweb.img, drawn into the 640x480 backbuffer.
            *requested = {outer.left + layout.LogicalX, outer.top + layout.LogicalY, 460, 316};
        }
        return browser;
    }
}

namespace dfl::payment_window_scaling
{
    bool Install() noexcept
    {
        static bool installed = false;
        if (installed) return true;
        if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)) != 0x00400000) return false;
        constexpr std::array<unsigned char, 5> call{0xE8, 0x75, 0xAE, 0x08, 0x00};
        constexpr std::array<unsigned char, 6> callee{0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x30};
        constexpr std::array<unsigned char, 44> stores{
            0x8B,0x7D,0xE0,0x8B,0x55,0xE4,0x8B,0x45,0xF0,0x8B,0x4D,0xF4,
            0x2B,0xC7,0x8B,0x3D,0x44,0x84,0x99,0x00,0x2B,0xCA,0x8B,0x55,
            0xF8,0x89,0x46,0x34,0x03,0xC2,0x89,0x46,0x3C,0x8B,0x45,0xFC,
            0x89,0x4E,0x38,0x03,0xC8,0x89,0x4E,0x40};
        if (std::memcmp(reinterpret_cast<const void*>(CreateCall), call.data(), call.size())
            || std::memcmp(reinterpret_cast<const void*>(0x008AB260), callee.data(), callee.size())
            || std::memcmp(reinterpret_cast<const void*>(0x0082040A), stores.data(), stores.size()))
            return false;
        installed = memory_patch::WriteRelativeCall(CreateCall,
            reinterpret_cast<std::uintptr_t>(&CreateScaledBrowser));
        return installed;
    }
}
