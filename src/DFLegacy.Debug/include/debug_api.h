#pragma once
#include <cstdint>

namespace dfl::debug_api
{
    constexpr std::uint32_t Version = 2;
    using DrawText = void (__stdcall*)(int x, int y, std::uint32_t color, const char* text);
    struct Host
    {
        std::uint32_t size;
        std::uint32_t version;
        DrawText drawText;
        void* gameWindow;
        const wchar_t* settingsPath;
    };
    struct Frame
    {
        std::uint32_t size;
        std::uint32_t scene;
        std::uint32_t userId;
        std::uint32_t bossDead;
        std::uint64_t enteredFileTime;
    };
    using Initialize = int (__stdcall*)(const Host*, const wchar_t* directory);
    using Render = void (__stdcall*)(const Frame*);
    using Shutdown = int (__stdcall*)();
}
