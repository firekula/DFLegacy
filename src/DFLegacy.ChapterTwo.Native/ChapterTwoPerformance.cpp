#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
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

    using BeginText = void(__thiscall*)(void*, const TextState*);
    using EndText = void(__thiscall*)(void*);
    class TextBatch
    {
    public:
        TextBatch(void* renderer, const TextState& state, BeginText begin, EndText end) noexcept
            : renderer_(renderer), end_(end), owned_(
                static_cast<const unsigned char*>(renderer)[0x2d] == 0)
        {
            if (owned_) begin(renderer_, &state);
        }
        ~TextBatch() noexcept { if (owned_) end_(renderer_); }
        TextBatch(const TextBatch&) = delete;
        TextBatch& operator=(const TextBatch&) = delete;
    private:
        void* renderer_;
        EndText end_;
        bool owned_;
    };

    float ReadFps() noexcept
    {
        const auto read = reinterpret_cast<float(__cdecl*)()>(GameImage + 0x3c080);
        return read();
    }

    char* MemoryText() noexcept
    {
        static unsigned previous = 0;
        static char memoryText[64] = "Committed : --";
        const auto current = dfl::chapter_two::PerformancePrivateMemoryTenthsMiB();
        if (current != previous)
        {
            previous = current;
            std::snprintf(memoryText, sizeof(memoryText), "Committed : %u.%u MB", current / 10, current % 10);
        }
        return memoryText;
    }

    int __fastcall DrawPerformance(void* state, void*) noexcept
    {
        using Original = int(__thiscall*)(void*);
        using DrawText = int(__thiscall*)(void*, int, int, int, char*);
        // Preserve the client's own diagnostics if its original flag is on.
        if (state && static_cast<unsigned char*>(state)[0x2f0] == 1)
            reinterpret_cast<Original>(GameImage + 0x512e00)(state);
        dfl::chapter_two::RecordPerformanceFrame();
        auto* renderer = *reinterpret_cast<void**>(GameImage + 0xa01fd4);
        if (!renderer) return 0;
        auto** methods = *reinterpret_cast<void***>(renderer);
        if (!methods || !methods[0x4c / 4] || !methods[0x50 / 4] || !methods[0x54 / 4]) return 0;

        const float fps = ReadFps();
        static float lastFps = 0;
        static char fpsText[32] = "FPS : 0";
        if (fps != lastFps)
        {
            lastFps = fps;
            std::snprintf(fpsText, sizeof(fpsText), "FPS : %.0f", static_cast<double>(fps));
        }
        char* const memoryText = MemoryText();
        TextState text{0, 1, -1, 1, 0, 0, *reinterpret_cast<std::uint32_t*>(GameImage + 0x954144)};
        // C2 BeginText (RVA 0x614f20) retains this pointer in renderer+0x40.
        // End our own batch before text expires; an active native batch belongs
        // to its caller. Keep the captured renderer even if its global changes.
        TextBatch batch(renderer, text, reinterpret_cast<BeginText>(methods[0x4c / 4]),
            reinterpret_cast<EndText>(methods[0x50 / 4]));
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
    constexpr std::array<unsigned char, 24> beginText{0x55, 0x8b, 0xec, 0x8a, 0x41, 0x2d, 0x84, 0xc0,
        0x75, 0x0a, 0x8b, 0x45, 0x08, 0x89, 0x41, 0x40, 0xc6, 0x41, 0x2d, 0x01, 0x5d, 0xc2, 0x04, 0x00};
    constexpr std::array<unsigned char, 16> endText{0x8a, 0x51, 0x2d, 0x33, 0xc0, 0x3a, 0xd0, 0x74,
        0x06, 0x89, 0x41, 0x40, 0x88, 0x41, 0x2d, 0xc3};
    constexpr std::array<std::uint32_t, 3> textMethods{0x00a14f20, 0x00a14f40, 0x00a15520};
    if (std::memcmp(image + FrameGate, gate.data(), gate.size()) != 0 ||
        std::memcmp(image + 0x3c080, fpsReader.data(), fpsReader.size()) != 0 ||
        std::memcmp(image + 0x512e00, rendererPrologue.data(), rendererPrologue.size()) != 0 ||
        std::memcmp(image + 0x614f20, beginText.data(), beginText.size()) != 0 ||
        std::memcmp(image + 0x614f40, endText.data(), endText.size()) != 0 ||
        std::memcmp(image + 0x82ff60, textMethods.data(), sizeof(textMethods)) != 0) return false;

    GameImage = image;
    const auto next = reinterpret_cast<std::uintptr_t>(image + FrameCall + 5);
    const auto displacement = reinterpret_cast<std::uintptr_t>(&DrawPerformance) - next;
    std::memcpy(image + FrameCall + 1, &displacement, sizeof(displacement));
    image[FrameGate + 7] = image[FrameGate + 8] = 0x90;
    return true;
}
