#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <psapi.h>
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "ChapterTwoPerformance.h"

namespace
{
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr std::size_t FrameGate = 0x51e2c0;
    constexpr std::size_t FrameCall = 0x51e2cb;
    unsigned char* GameImage;

    struct TextState
    {
        std::uint8_t flag0, flag1;
        std::int16_t color, mode;
        std::uint8_t flag2, flag3;
        std::uint32_t font;
    };
    static_assert(sizeof(TextState) == 12);

    float ReadFps() noexcept
    {
        const auto read = reinterpret_cast<float(__cdecl*)()>(GameImage + 0x3c080);
        return read();
    }

    char* SampleFrame(float fps) noexcept
    {
        static LARGE_INTEGER frequency = [] { LARGE_INTEGER value{}; QueryPerformanceFrequency(&value); return value; }();
        static long long previous = 0, sampled = 0, longest = 0;
        static unsigned slow = 0;
        static char memoryText[64] = "Committed : --";
        LARGE_INTEGER now{};
        QueryPerformanceCounter(&now);
        if (previous != 0 && frequency.QuadPart > 0)
        {
            const auto elapsed = (now.QuadPart - previous) * 1000 / frequency.QuadPart;
            longest = std::max(longest, elapsed);
            if (elapsed >= 50) ++slow;
        }
        previous = now.QuadPart;
        if (sampled == 0 || now.QuadPart - sampled >= frequency.QuadPart * 2)
        {
            PROCESS_MEMORY_COUNTERS_EX counters{};
            counters.cb = sizeof(counters);
            const double committed = GetProcessMemoryInfo(GetCurrentProcess(),
                reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters), sizeof(counters))
                ? static_cast<double>(counters.PagefileUsage) / 1048576.0 : -1;
            if (committed >= 0)
                std::snprintf(memoryText, sizeof(memoryText), "Committed : %.1f MB", committed);
            else
                std::snprintf(memoryText, sizeof(memoryText), "Committed : --");
            if (sampled != 0 && slow != 0)
            {
                char summary[128]{};
                std::snprintf(summary, sizeof(summary), "CHAPTER_TWO_PERF fps=%.1f commitMiB=%.1f slowFrames=%u longestMs=%lld",
                    static_cast<double>(fps), committed, slow, longest);
                OutputDebugStringA(summary);
            }
            sampled = now.QuadPart;
            longest = 0;
            slow = 0;
        }
        return memoryText;
    }

    int __fastcall DrawPerformance(void* state, void*) noexcept
    {
        using Original = int(__thiscall*)(void*);
        using BeginText = void(__thiscall*)(void*, const TextState*);
        using DrawText = int(__thiscall*)(void*, int, int, int, char*);
        // Preserve the client's own diagnostics if its original flag is on.
        if (state && static_cast<unsigned char*>(state)[0x2f0] == 1)
            reinterpret_cast<Original>(GameImage + 0x512e00)(state);
        auto* renderer = *reinterpret_cast<void**>(GameImage + 0xa01fd4);
        if (!renderer) return 0;
        auto** methods = *reinterpret_cast<void***>(renderer);
        if (!methods || !methods[0x4c / 4] || !methods[0x54 / 4]) return 0;

        const float fps = ReadFps();
        static float lastFps = 0;
        static char fpsText[32] = "FPS : 0";
        if (fps != lastFps)
        {
            lastFps = fps;
            std::snprintf(fpsText, sizeof(fpsText), "FPS : %.0f", static_cast<double>(fps));
        }
        char* const memoryText = SampleFrame(fps);
        TextState text{0, 1, -1, 1, 0, 0, *reinterpret_cast<std::uint32_t*>(GameImage + 0x954144)};
        reinterpret_cast<BeginText>(methods[0x4c / 4])(renderer, &text);
        const auto draw = reinterpret_cast<DrawText>(methods[0x54 / 4]);
        constexpr auto color = static_cast<int>(0xff99c6e3u);
        draw(renderer, 440, 35, color, fpsText);
        return draw(renderer, 440, 48, color, memoryText);
    }
}

bool dfl::chapter_two::InstallPerformanceOverlay(unsigned char* image, std::size_t bytes) noexcept
{
    static_assert(sizeof(void*) == 4);
    if (!image || bytes != ImageSize) return false;
    constexpr std::array<unsigned char, 16> gate{0x80, 0xbf, 0xf0, 0x02, 0, 0, 1,
        0x75, 0x07, 0x8b, 0xcf, 0xe8, 0x30, 0x4b, 0xff, 0xff};
    constexpr std::array<unsigned char, 7> fpsReader{0xd9, 0x05, 0x84, 0x6d, 0xd5, 0, 0xc3};
    constexpr std::array<unsigned char, 9> rendererPrologue{0x55, 0x8b, 0xec, 0x81, 0xec, 0x38, 0x01, 0, 0};
    if (std::memcmp(image + FrameGate, gate.data(), gate.size()) != 0 ||
        std::memcmp(image + 0x3c080, fpsReader.data(), fpsReader.size()) != 0 ||
        std::memcmp(image + 0x512e00, rendererPrologue.data(), rendererPrologue.size()) != 0) return false;

    GameImage = image;
    const auto next = reinterpret_cast<std::uintptr_t>(image + FrameCall + 5);
    const auto displacement = reinterpret_cast<std::uintptr_t>(&DrawPerformance) - next;
    std::memcpy(image + FrameCall + 1, &displacement, sizeof(displacement));
    image[FrameGate + 7] = image[FrameGate + 8] = 0x90;
    return true;
}
