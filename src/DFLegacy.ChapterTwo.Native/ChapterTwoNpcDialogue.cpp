#include "ChapterTwoNpcDialogue.h"
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace
{
    constexpr std::size_t ImageSize = 0xe57000;
    constexpr std::size_t BodyGapSetup = 0x4f126d;
    constexpr std::size_t BodyFontSetup = 0x4f1283;
    constexpr std::size_t BodyFontOperand = 0x4f1291;
    constexpr std::size_t BodyFontCall = 0x4f129c;
    constexpr std::size_t CompactFontSetup = 0xb8c8;
    constexpr std::size_t CompactFontName = 0x6eb8cc;
    constexpr std::size_t GraphicsFactorySetup = 0xb844;
    constexpr std::size_t CompactFontPrepare = 0xb88d;
    constexpr std::size_t CompactFontPrepareCall = 0xb893;
    constexpr std::size_t GraphicsFactory = 0xa01fd0;
    constexpr std::size_t PrepareFont = 0x63d820;
    constexpr std::size_t SetWidgetFont = 0x638af0;
    constexpr int CompactSize = 12;
    constexpr int DefaultSize = 16;
    constexpr int DefaultGap = 3;

    unsigned char* gameImage = nullptr;
    void* customFont = nullptr;
    bool fontAttempted = false;
    int bodyFontSize = DefaultSize;

    template <typename Function>
    Function VirtualMethod(void* instance, std::size_t index) noexcept
    {
        return reinterpret_cast<Function>((*static_cast<void***>(instance))[index]);
    }

    using NativePrepareFont = int (__thiscall*)(void*);
    using NativeSetWidgetFont = void (__thiscall*)(void*, void*);

    int __fastcall PrepareCompactFont(void* font, void*) noexcept
    {
        // The native factory owns its fonts and destroys them with graphics.
        // Discard the borrowed reference whenever it initializes a new set.
        customFont = nullptr;
        fontAttempted = false;
        return reinterpret_cast<NativePrepareFont>(gameImage + PrepareFont)(font);
    }

    void __fastcall SetBodyFont(void* widget, void*, void* compactFont) noexcept
    {
        if (!fontAttempted && compactFont)
        {
            fontAttempted = true;
            auto* factory = *reinterpret_cast<void**>(gameImage + GraphicsFactory);
            void* candidate = nullptr;
            using CreateFontResource = int (__thiscall*)(void*, int, void**);
            using DefineFont = int (__thiscall*)(void*, int, const char*, int, float, float);
            using GetFontHeight = int (__thiscall*)(void*);
            if (factory && VirtualMethod<CreateFontResource>(factory, 1)(factory, 4, &candidate) >= 0 && candidate &&
                reinterpret_cast<NativePrepareFont>(gameImage + PrepareFont)(candidate) >= 0 &&
                VirtualMethod<DefineFont>(candidate, 2)(candidate, 0,
                    reinterpret_cast<const char*>(gameImage + CompactFontName), bodyFontSize, 1.0f, 1.0f) >= 0)
            {
                // A substituted Windows font must also fit the five-row body.
                const int height = VirtualMethod<GetFontHeight>(candidate, 5)(candidate);
                if (height > 0 && height <= 20) customFont = candidate;
            }
            char message[96]{};
            std::snprintf(message, sizeof(message), "DFLegacy Chapter Two: NPC dialogue size requested=%d active=%d.\n",
                bodyFontSize, customFont ? bodyFontSize : CompactSize);
            OutputDebugStringA(message);
        }
        reinterpret_cast<NativeSetWidgetFont>(gameImage + SetWidgetFont)(widget, customFont ? customFont : compactFont);
    }

    template <typename Function>
    void RedirectCall(unsigned char* call, Function target) noexcept
    {
        const auto displacement = reinterpret_cast<std::uintptr_t>(target) -
            reinterpret_cast<std::uintptr_t>(call + 5);
        std::memcpy(call + 1, &displacement, sizeof(displacement));
    }
}

bool dfl::chapter_two::InstallNpcDialogueLayout(unsigned char* image, std::size_t bytes) noexcept
{
    if (!image || bytes != ImageSize) return false;

    // The NPC body has its own gap setter and font selection. Keep the
    // shared UI fonts unchanged and retain the five original visible rows.
    constexpr unsigned char bodyGap[]{
        0x6a, 0x03, 0x8b, 0xc8, 0xc7, 0x45, 0xfc, 0xff, 0xff, 0xff, 0xff,
        0x89, 0x86, 0xe0, 0x00, 0x00, 0x00, 0xe8, 0x6d, 0x77, 0x14, 0x00,
    };
    constexpr unsigned char bodyFont[]{
        0x8b, 0x8e, 0xe0, 0x00, 0x00, 0x00, 0x6a, 0x05, 0xe8, 0x20, 0x77, 0x14, 0x00,
        0xa1, 0x48, 0x41, 0xd5, 0x00, 0x8b, 0x8e, 0xe0, 0x00, 0x00, 0x00, 0x50,
        0xe8, 0x4f, 0x78, 0x14, 0x00,
    };
    constexpr unsigned char compactFont[]{
        0x8b, 0x0d, 0x44, 0x41, 0xd5, 0x00, 0x8b, 0x11, 0x68, 0x00, 0x00, 0x80, 0x3f,
        0x68, 0x00, 0x00, 0x80, 0x3f, 0x6a, 0x0c, 0x68, 0xcc, 0xb8, 0xae, 0x00,
        0x53, 0xff, 0x52, 0x08,
    };
    constexpr char compactName[] = "SimSun";
    if (std::memcmp(image + BodyGapSetup, bodyGap, sizeof(bodyGap)) != 0 ||
        std::memcmp(image + BodyFontSetup, bodyFont, sizeof(bodyFont)) != 0 ||
        std::memcmp(image + CompactFontSetup, compactFont, sizeof(compactFont)) != 0 ||
        std::memcmp(image + CompactFontName, compactName, sizeof(compactName)) != 0) return false;

    char sizeText[4]{};
    const auto sizeLength = GetEnvironmentVariableA("DFL_S1C2_DIALOGUE_SIZE", sizeText, _countof(sizeText));
    const int size = sizeLength == 2 && sizeText[0] == '1' &&
        (sizeText[1] == '2' || sizeText[1] == '4' || sizeText[1] == '6' || sizeText[1] == '8')
        ? 10 + sizeText[1] - '0' : DefaultSize;
    char gapText[3]{};
    const auto gapLength = GetEnvironmentVariableA("DFL_S1C2_DIALOGUE_GAP", gapText, _countof(gapText));
    const int gap = gapLength == 1 && gapText[0] >= '0' && gapText[0] <= '4'
        ? gapText[0] - '0' : DefaultGap;
    if (size != CompactSize)
    {
        constexpr unsigned char factorySetup[]{
            0x8b, 0x0d, 0xd0, 0x1f, 0xe0, 0x00, 0x8b, 0x11, 0x68, 0x44, 0x41, 0xd5, 0x00, 0x6a, 0x04, 0xff, 0x52, 0x04,
        };
        constexpr unsigned char prepare[]{
            0x8b, 0x0d, 0x44, 0x41, 0xd5, 0x00, 0xe8, 0x88, 0x1f, 0x63, 0x00,
        };
        if (std::memcmp(image + GraphicsFactorySetup, factorySetup, sizeof(factorySetup)) != 0 ||
            std::memcmp(image + CompactFontPrepare, prepare, sizeof(prepare)) != 0) return false;
        gameImage = image;
        bodyFontSize = size;
        RedirectCall(image + CompactFontPrepareCall, PrepareCompactFont);
        RedirectCall(image + BodyFontCall, SetBodyFont);
    }
    image[BodyGapSetup + 1] = static_cast<unsigned char>(gap);
    image[BodyFontOperand] = 0x44;
    return true;
}
