#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "ChapterTwoGuildNavigation.h"
#include <cstdint>
#include <cstring>

namespace
{
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr std::size_t ClickAction = 0x4c98bd;
    constexpr std::size_t CloseGuildPanel = 0x4c9a77;
    void __cdecl RequestGuildWebsite() noexcept
    {
        OutputDebugStringA("DFL_S1C2_GUILD_OPEN");
    }
}

bool dfl::chapter_two::InstallGuildNavigation(unsigned char* image, std::size_t bytes) noexcept
{
    static_assert(sizeof(void*) == 4, "Chapter Two host must be x86.");
    if (!image || bytes != ImageSize) return false;

    // The guild tab proc checks the button at +0x94 and the native click id.
    // Its old action created an IE instance and navigated to qbar with account
    // data in the URL. Keep the click checks and the original panel close.
    constexpr unsigned char click[]{
        0x8b,0x86,0x94,0,0,0,0x8a,0x48,0x11,0x84,0xc9,0x0f,0x84,0xd0,0x01,0,0,
        0x8b,0x0d,0xbc,0xdf,0xd4,0,0x3b,0x48,0x0c,0x0f,0x85,0xc1,0x01,0,0,
        0x33,0xc0,0x89,0x45,0xce,0x89,0x45,0xd2,0x89,0x45,0xd6};
    constexpr unsigned char close[]{0x8b,0x16,0x8b,0xce,0xff,0x52,0x24,0x5f,0x5e,0x5b,0x8b,0xe5,0x5d,0xc3};
    constexpr unsigned char navigate[]{0x8b,0x10,0x50,0xff,0x52,0x2c};
    constexpr wchar_t legacyUrl[]{L"http://qbar.dnf.qq.com/gh%d%s"};
    if (std::memcmp(image + 0x4c989d, click, sizeof(click)) != 0 ||
        std::memcmp(image + 0x4c99fc, navigate, sizeof(navigate)) != 0 ||
        std::memcmp(image + CloseGuildPanel, close, sizeof(close)) != 0 ||
        std::memcmp(image + 0x815634, legacyUrl, sizeof(legacyUrl)) != 0) return false;

    unsigned char patch[]{0xe8,0,0,0,0,0xe9,0,0,0,0,0x90};
    const auto call = reinterpret_cast<std::uintptr_t>(&RequestGuildWebsite)
        - reinterpret_cast<std::uintptr_t>(image + ClickAction + 5);
    constexpr std::uint32_t resume = CloseGuildPanel - (ClickAction + 10);
    std::memcpy(patch + 1, &call, sizeof(call));
    std::memcpy(patch + 6, &resume, sizeof(resume));
    std::memcpy(image + ClickAction, patch, sizeof(patch));
    return true;
}
