#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <imm.h>
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include "ChapterTwoFrameStages.h"
#include "ChapterTwoKeyboardTiming.h"
#include "ChapterTwoPerformance.h"
#include "../DFLegacy.Ijl15/src/pe_import.h"

namespace
{
    using dfl::chapter_two::performance::FrameStage;
    using dfl::chapter_two::performance::FrameStageScope;
    using dfl::chapter_two::performance::ShopPageFromMode;
    constexpr std::uintptr_t Base = 0x400000;
    unsigned char* GameImage = nullptr;
    const unsigned char* KeyboardImage = nullptr;
    DWORD KeyboardThread = 0;
    dfl::chapter_two::performance::ImeGameplayKey PendingImeGameplayKey{};
    bool UniformPopupTile = false, AvatarTooltip = false;
    struct ShopDrawPass
    {
        void* noticeOwner = nullptr;
        unsigned deferredNotices = 0;
    };
    ShopDrawPass* CurrentShopDraw = nullptr;
    unsigned ShopNoticeRepeats = 1;

    struct TooltipFill
    {
        int x, y, columns, rows;
        const void* sprite = nullptr;
    };
    TooltipFill* ActiveTooltipFill = nullptr;

    template<class T>
    T Original(std::uintptr_t address) noexcept
    {
        return reinterpret_cast<T>(GameImage + (address - Base));
    }

    using TwoArguments = std::uintptr_t (__thiscall*)(void*, std::uintptr_t, std::uintptr_t);

    template<FrameStage Stage, std::uintptr_t Address>
    std::uintptr_t __fastcall MeasureTwo(void* self, void*, std::uintptr_t first, std::uintptr_t second)
    {
        FrameStageScope scope(Stage);
        return Original<TwoArguments>(Address)(self, first, second);
    }

    std::uintptr_t __fastcall MeasureProjectile(void* self, void*, std::uintptr_t argument)
    {
        FrameStageScope scope(FrameStage::ProjectileEffect);
        using Function = std::uintptr_t (__thiscall*)(void*, std::uintptr_t);
        return Original<Function>(0x625d30)(self, argument);
    }

    std::uintptr_t __fastcall MeasureHit(void* self, void*, std::uintptr_t first, std::uintptr_t second,
        std::uintptr_t third, std::uintptr_t fourth, std::uintptr_t fifth)
    {
        FrameStageScope scope(FrameStage::HitEffect);
        using Function = std::uintptr_t (__thiscall*)(void*, std::uintptr_t, std::uintptr_t,
            std::uintptr_t, std::uintptr_t, std::uintptr_t);
        return Original<Function>(0x483530)(self, first, second, third, fourth, fifth);
    }

    HRESULT __fastcall MeasurePresent(void* device, void*)
    {
        FrameStageScope scope(FrameStage::Present);
        using Present = HRESULT (WINAPI*)(void*, const RECT*, const RECT*, HWND, const RGNDATA*);
        const auto function = reinterpret_cast<Present>((*static_cast<void***>(device))[15]);
        return function(device, nullptr, nullptr, nullptr, nullptr);
    }

    HRESULT __fastcall MeasureEndScene(void* device, void*)
    {
        FrameStageScope scope(FrameStage::EndScene);
        using EndScene = HRESULT (WINAPI*)(void*);
        const auto function = reinterpret_cast<EndScene>((*static_cast<void***>(device))[35]);
        return function(device);
    }

    void __fastcall MeasureShopUi(void* shop, void*)
    {
        using Update = void (__thiscall*)(void*);
        const auto original = Original<Update>(0x947960);
        const auto window = *reinterpret_cast<unsigned char* const*>(static_cast<unsigned char*>(shop) + 0x68);
        FrameStageScope scope(window && window[0x10] ? FrameStage::ShopUi : FrameStage::Count);
        original(shop);
    }

    std::uintptr_t __fastcall MeasureShopDraw(void* shop, void*, void* widget)
    {
        const auto* data = static_cast<const unsigned char*>(shop);
        const auto* window = *reinterpret_cast<unsigned char* const*>(data + 0x68);
        const bool visible = window && window[0x10];
        const auto page = visible ? ShopPageFromMode(*reinterpret_cast<const std::uint32_t*>(data + 0x60))
                                  : dfl::chapter_two::performance::ShopPage::None;
        FrameStageScope scope(visible ? FrameStage::ShopDraw : FrameStage::Count, page);
        using Draw = std::uintptr_t (__thiscall*)(void*, void*);
        return Original<Draw>(0x942480)(shop, widget);
    }

    template<FrameStage Stage, std::uintptr_t Address>
    std::uintptr_t __fastcall MeasureNoArguments(void* self, void*)
    {
        FrameStageScope scope(Stage);
        using Function = std::uintptr_t (__thiscall*)(void*);
        return Original<Function>(Address)(self);
    }

    std::uintptr_t __fastcall MeasureAvatarDraw(void* animation, void*, void* renderer, int x, int y, int options)
    {
        FrameStageScope scope(FrameStage::ShopPreview);
        using Draw = std::uintptr_t (__thiscall*)(void*, void*, int, int, int);
        return Original<Draw>(0x97b420)(animation, renderer, x, y, options);
    }

    void* __cdecl MeasureShopIcon(const char* item)
    {
        FrameStageScope scope(FrameStage::ShopIcon);
        using FindIcon = void* (__cdecl*)(const char*);
        return Original<FindIcon>(0x46dc80)(item);
    }

    void WINAPI MeasureTextureQueueWait(LPCRITICAL_SECTION lock)
    {
        FrameStageScope scope(FrameStage::TextureQueueWait);
        using Enter = void (WINAPI*)(LPCRITICAL_SECTION);
        (*Original<Enter*>(0xaea274))(lock);
    }

    void __cdecl DrawShopNoticeLabel(int x, int y, int textColor, int backgroundColor, int alignment,
        const char* text, int opacity, int style, int font, int width, int clamp, int force)
    {
        // Match the tint built up by the original source-over draws of this panel.
        // Pass the original text through once at its native opacity.
        if (ShopNoticeRepeats > 1 && opacity == 13 && style == 0)
        {
            constexpr double remainingPerDraw = (255.0 - 13.0) / 255.0;
            double remaining = 1.0;
            for (unsigned draw = 0; draw < ShopNoticeRepeats; ++draw) remaining *= remainingPerDraw;
            opacity = static_cast<int>(255.0 * (1.0 - remaining) + 0.5);
        }
        using Draw = decltype(&DrawShopNoticeLabel);
        Original<Draw>(0x91a710)(x, y, textColor, backgroundColor, alignment, text,
            opacity, style, font, width, clamp, force);
    }

    void DrawNativeShopNotice(void* shop, unsigned repeats = 1)
    {
        FrameStageScope scope(FrameStage::ShopNotice);
        using Draw = void (__thiscall*)(void*);
        const auto previous = ShopNoticeRepeats;
        ShopNoticeRepeats = repeats;
        Original<Draw>(0x93f040)(shop);
        ShopNoticeRepeats = previous;
    }

    void __fastcall DrawShopNotice(void* shop, void*)
    {
        const auto* data = static_cast<const unsigned char*>(shop);
        const auto tab = *reinterpret_cast<const unsigned*>(data + 0x60);
        const auto notice = *Original<const unsigned*>(0xdfac74);
        const auto* renderer = *Original<void* const*>(0xe01fd4);
        const auto* window = *reinterpret_cast<unsigned char* const*>(data + 0x68);
        if (!CurrentShopDraw || !renderer || !window || !window[0x10] || tab > 2 || notice >= 3 ||
            !*reinterpret_cast<void* const*>(data + 0x1f4) ||
            (CurrentShopDraw->noticeOwner && CurrentShopDraw->noticeOwner != shop))
        {
            DrawNativeShopNotice(shop);
            return;
        }
        CurrentShopDraw->noticeOwner = shop;
        if (tab == 1)
        {
            // The native tail sets these two widget flags even on repeated draws.
            // Keep that tail whenever another callback changed either flag.
            const unsigned char desired = notice != 0 && notice != 2 ? 1 : 0;
            for (const auto offset : {0x1f4, 0x1f8})
            {
                const auto* button = *reinterpret_cast<unsigned char* const*>(data + offset);
                if (button && button[0x11] != desired)
                {
                    DrawNativeShopNotice(shop);
                    return;
                }
            }
        }
        FrameStageScope scope(FrameStage::ShopNoticeSkipped);
        ++CurrentShopDraw->deferredNotices;
    }

    int __fastcall DrawShopTree(void* tree, void*)
    {
        ShopDrawPass pass;
        const auto previous = CurrentShopDraw;
        CurrentShopDraw = &pass;
        using Draw = int (__thiscall*)(void*);
        const auto result = Original<Draw>(0xa3cbf0)(tree);
        CurrentShopDraw = previous;
        // The tree has now drawn every product card. Draw before the native
        // hovered-item overlay and before the renderer closes this frame.
        if (pass.deferredNotices) DrawNativeShopNotice(pass.noticeOwner, pass.deferredNotices);
        return result;
    }

    bool IsAvatarShopVisible() noexcept
    {
        const auto* game = *Original<unsigned char* const*>(0xd56d24);
        const auto* shop = game ? *reinterpret_cast<unsigned char* const*>(game + 0x1288) : nullptr;
        const auto* window = shop ? *reinterpret_cast<unsigned char* const*>(shop + 0x68) : nullptr;
        return window && window[0x10] && *reinterpret_cast<const unsigned*>(shop + 0x60) == 0;
    }

    void __fastcall DrawAvatarTooltipFrame(void* tooltip, void*, void* widget)
    {
        const auto wasTooltip = AvatarTooltip;
        AvatarTooltip = IsAvatarShopVisible() &&
            widget == *reinterpret_cast<void* const*>(static_cast<const unsigned char*>(tooltip) + 8);
        FrameStageScope scope(AvatarTooltip ? FrameStage::AvatarTooltipBackground : FrameStage::Count);
        using Draw = void (__thiscall*)(void*, void*);
        Original<Draw>(0x88bad0)(tooltip, widget);
        AvatarTooltip = wasTooltip;
    }

    void __cdecl DrawTooltipTiles(int x, int y, int columns, int rows, int style, int stripe, int variant, int alpha)
    {
        const auto previous = ActiveTooltipFill;
        // The verified skin uses 6-pixel borders around a 12 x 13 center tile.
        // Bounds keep the merged draw in the same logical backbuffer.
        const bool merge = AvatarTooltip && UniformPopupTile && style == 1 &&
            columns >= 8 && columns <= 52 && rows >= 8 && rows <= 36 &&
            x >= 0 && y >= 0 && x <= 640 - 12 - 12 * columns && y <= 480 - 12 - 13 * rows;
        TooltipFill fill{merge ? x + 6 : x, merge ? y + 6 : y, columns, rows};
        ActiveTooltipFill = merge ? &fill : nullptr;
        using Draw = void (__cdecl*)(int, int, int, int, int, int, int, int);
        Original<Draw>(0x88b6f0)(x, y, columns, rows, style, stripe, variant, alpha);
        ActiveTooltipFill = previous;
    }

    void __fastcall DrawTooltipCenter(void* renderer, void*, int x, int y, const void* sprite,
        float scaleX, float scaleY, int angle, unsigned color, float pivotX, float pivotY)
    {
        using Draw = void (__thiscall*)(void*, int, int, const void*, float, float, int, unsigned, float, float);
        const auto draw = Original<Draw>(0xa3eb30);
        if (auto* fill = ActiveTooltipFill)
        {
            if (fill->sprite && fill->sprite == sprite) return;
            const auto* bytes = static_cast<const unsigned char*>(sprite);
            constexpr std::array<int, 4> spriteBounds{12, 13, 0, 0};
            if (x == fill->x && y == fill->y && bytes &&
                std::memcmp(bytes + 0x14, spriteBounds.data(), sizeof(spriteBounds)) == 0 &&
                scaleX == 1.0f && scaleY == 1.0f && angle == 0)
            {
                FrameStageScope scope(FrameStage::AvatarTooltipFill);
                fill->sprite = sprite;
                draw(renderer, x, y, sprite, static_cast<float>(fill->columns), static_cast<float>(fill->rows),
                    angle, color, pivotX, pivotY);
                return;
            }
            ActiveTooltipFill = nullptr;
        }
        draw(renderer, x, y, sprite, scaleX, scaleY, angle, color, pivotX, pivotY);
    }

    bool VerifyUniformPopupTile() noexcept
    {
        // Popup.img frame 0x8a decompresses to 156 identical 0x8000 pixels.
        // Custom skins and missing resources retain the original per-tile draw.
        constexpr unsigned char tile[]{
            15,0,0,0, 6,0,0,0, 12,0,0,0, 13,0,0,0, 14,0,0,0, 0,0,0,0, 0,0,0,0,
            12,0,0,0, 13,0,0,0, 0x78,0x9c,0x63,0x68,0x60,0x18,0x85,0x64,0x40,0x00,0x8b,0xf9,0x4e,0x01
        };
        const auto file = CreateFileW(L"ImagePacks2\\sprite_interface.NPK", GENERIC_READ, FILE_SHARE_READ,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return false;
        LARGE_INTEGER size{}, position{};
        position.QuadPart = 21153082;
        unsigned char bytes[sizeof(tile)]{};
        DWORD length = 0;
        const bool match = GetFileSizeEx(file, &size) && size.QuadPart == 25594354 &&
            SetFilePointerEx(file, position, nullptr, FILE_BEGIN) &&
            ReadFile(file, bytes, sizeof(bytes), &length, nullptr) &&
            length == sizeof(bytes) && std::memcmp(bytes, tile, sizeof(tile)) == 0;
        CloseHandle(file);
        return match;
    }

    void WINAPI MeasureMainLoopSleep(DWORD duration)
    {
        FrameStageScope scope(FrameStage::MainLoopSleep);
        using Wait = void (WINAPI*)(DWORD);
        (*Original<Wait*>(0xaea2cc))(duration);
    }

    BOOL WINAPI MeasurePeekMessage(LPMSG message, HWND window, UINT first, UINT last, UINT remove)
    {
        FrameStageScope scope(FrameStage::MessageQueue);
        if (GetCurrentThreadId() == KeyboardThread) PendingImeGameplayKey = {};
        return PeekMessageA(message, window, first, last, remove);
    }

    enum class ChatFocus { Unknown, Inactive, Editor };

    ChatFocus ReadChatFocus() noexcept
    {
        // These offsets follow the native chat open/close routine. The inactive
        // edit control can still be enabled, so its distinct focus ID matters.
        __try
        {
            const auto* manager = *reinterpret_cast<const unsigned char* const*>(KeyboardImage + 0x956d24);
            if (!manager || *reinterpret_cast<const std::uintptr_t*>(manager) != 0xaedb24) return ChatFocus::Unknown;
            const auto* chat = *reinterpret_cast<const unsigned char* const*>(manager + 0x60);
            if (!chat || *reinterpret_cast<const std::uintptr_t*>(chat) != 0xc11c04) return ChatFocus::Unknown;
            const auto* fallback = *reinterpret_cast<const unsigned char* const*>(chat + 0x9c);
            const auto* editor = *reinterpret_cast<const unsigned char* const*>(chat + 0xa8);
            if (!fallback || !editor || *reinterpret_cast<const std::uintptr_t*>(fallback) != 0xc123ac ||
                *reinterpret_cast<const std::uintptr_t*>(editor) != 0xc123ac) return ChatFocus::Unknown;
            const auto focus = *reinterpret_cast<const std::uint32_t*>(KeyboardImage + 0x94dfdc);
            const auto editorId = *reinterpret_cast<const std::uint32_t*>(editor + 0xc);
            if (chat[0x1c4] != 0 || (editorId != 0 && focus == editorId)) return ChatFocus::Editor;
            return dfl::chapter_two::performance::ChatFallbackHasFocus(chat[0x1c4], focus,
                *reinterpret_cast<const std::uint32_t*>(fallback + 0xc),
                editorId) ? ChatFocus::Inactive : ChatFocus::Unknown;
        }
        __except (GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH)
        {
            return ChatFocus::Unknown;
        }
    }

    UINT InactiveChatImeKey(const MSG& message) noexcept
    {
        if (message.message != WM_KEYDOWN || message.wParam != VK_PROCESSKEY || !message.hwnd ||
            !KeyboardImage || GetCurrentThreadId() != KeyboardThread) return 0;
        const auto gameWindow = *reinterpret_cast<const HWND*>(KeyboardImage + 0xb2e634);
        if (message.hwnd != gameWindow || GetFocus() != gameWindow || GetForegroundWindow() != gameWindow) return 0;
        const auto language = LOWORD(reinterpret_cast<std::uintptr_t>(GetKeyboardLayout(0)));
        if (PRIMARYLANGID(language) != LANG_CHINESE ||
            ((GetKeyState(VK_SHIFT) | GetKeyState(VK_CONTROL) | GetKeyState(VK_MENU) | GetKeyState(VK_RETURN)) & 0x8000) != 0 ||
            ReadChatFocus() != ChatFocus::Inactive) return 0;

        // The IME can replace the key without preserving a useful scan code.
        // Windows exposes the original key until TranslateMessage is called.
        const auto originalKey = ImmGetVirtualKey(gameWindow);
        return dfl::chapter_two::performance::IsChineseImeLetterDown(
            message.message, message.wParam, message.lParam, language, originalKey) ? originalKey : 0;
    }

    BOOL WINAPI MeasureTranslateMessage(const MSG* message)
    {
        const auto stage = message && dfl::chapter_two::performance::ClassifyKeyMessage(
            message->message, message->wParam, message->lParam) != FrameStage::Count
            ? FrameStage::KeyTranslate : FrameStage::Count;
        FrameStageScope scope(stage);
        if (message)
        {
            const auto key = InactiveChatImeKey(*message);
            if (key)
            {
                // The loop dispatches this message next. Supply the original
                // key to the host's keyboard state merge without changing
                // the IME message forwarded to the game.
                PendingImeGameplayKey = {message->hwnd, message->lParam, key};
                return FALSE;
            }
        }
        return TranslateMessage(message);
    }

    struct CallSite { std::uintptr_t address, target; const void* hook; };
    const CallSite Calls[]{
        { 0x9cd93a, 0xa49e70, reinterpret_cast<const void*>(&MeasureTwo<FrameStage::AnimationLookup, 0xa49e70>) },
        { 0x5a5cc4, 0x625d30, reinterpret_cast<const void*>(&MeasureProjectile) },
        { 0x483bfb, 0x483530, reinterpret_cast<const void*>(&MeasureHit) },
        { 0x486bf5, 0x486530, reinterpret_cast<const void*>(&MeasureTwo<FrameStage::HitEffect, 0x486530>) },
        { 0x4d296a, 0x482780, reinterpret_cast<const void*>(&MeasureTwo<FrameStage::HitEffect, 0x482780>) },
        { 0x4d2b63, 0x482780, reinterpret_cast<const void*>(&MeasureTwo<FrameStage::HitEffect, 0x482780>) },
        { 0x868b90, 0x947960, reinterpret_cast<const void*>(&MeasureShopUi) },
        { 0x9424c3, 0x93f040, reinterpret_cast<const void*>(&DrawShopNotice) },
        { 0x93f0a4, 0x91a710, reinterpret_cast<const void*>(&DrawShopNoticeLabel) },
        { 0x9b4316, 0xa3cbf0, reinterpret_cast<const void*>(&DrawShopTree) },
        { 0x8bbdae, 0x88bad0, reinterpret_cast<const void*>(&DrawAvatarTooltipFrame) },
        { 0x88bafa, 0x88b6f0, reinterpret_cast<const void*>(&DrawTooltipTiles) },
        { 0x88b901, 0xa3eb30, reinterpret_cast<const void*>(&DrawTooltipCenter) },
        { 0x94156c, 0x940200, reinterpret_cast<const void*>(&MeasureNoArguments<FrameStage::ShopPreview, 0x940200>) },
        { 0x941629, 0x97c6b0, reinterpret_cast<const void*>(&MeasureNoArguments<FrameStage::ShopPreview, 0x97c6b0>) },
        { 0x941651, 0x97b420, reinterpret_cast<const void*>(&MeasureAvatarDraw) },
        { 0x93d887, 0x46dc80, reinterpret_cast<const void*>(&MeasureShopIcon) },
        { 0x93d914, 0x46dc80, reinterpret_cast<const void*>(&MeasureShopIcon) },
        { 0x93da61, 0x46dc80, reinterpret_cast<const void*>(&MeasureShopIcon) },
        { 0x93f178, 0x46dc80, reinterpret_cast<const void*>(&MeasureShopIcon) },
        { 0x9408d2, 0x46dc80, reinterpret_cast<const void*>(&MeasureShopIcon) },
        { 0x941faf, 0x46dc80, reinterpret_cast<const void*>(&MeasureShopIcon) },
        { 0x438571, 0xa2d430, reinterpret_cast<const void*>(&MeasureNoArguments<FrameStage::TextureQueue, 0xa2d430>) },
        { 0xa269ef, 0xa2d430, reinterpret_cast<const void*>(&MeasureNoArguments<FrameStage::TextureQueue, 0xa2d430>) },
        { 0xa26c25, 0xa2d430, reinterpret_cast<const void*>(&MeasureNoArguments<FrameStage::TextureQueue, 0xa2d430>) },
    };
    const CallSite Loaders[]{
        { 0xc1dd18, 0x9cda90, reinterpret_cast<const void*>(&MeasureTwo<FrameStage::AnimationLoad, 0x9cda90>) },
        { 0xc1dd34, 0x9cdf60, reinterpret_cast<const void*>(&MeasureTwo<FrameStage::ImageLoad, 0x9cdf60>) },
        { 0xc1dd6c, 0x9ce7e0, reinterpret_cast<const void*>(&MeasureTwo<FrameStage::ScriptLoad, 0x9ce7e0>) },
    };

    std::uint32_t Read32(const unsigned char* value) noexcept
    {
        std::uint32_t result = 0;
        std::memcpy(&result, value, sizeof(result));
        return result;
    }

    void Write32(unsigned char* value, std::uint32_t data) noexcept
    {
        std::memcpy(value, &data, sizeof(data));
    }

    void Redirect(unsigned char* site, const void* hook) noexcept
    {
        site[0] = 0xe8;
        Write32(site + 1, reinterpret_cast<std::uintptr_t>(hook) - reinterpret_cast<std::uintptr_t>(site + 5));
    }
}

bool dfl::chapter_two::InstallSlowFrameTiming(unsigned char* image, std::size_t bytes) noexcept
{
    static_assert(sizeof(void*) == 4);
    if (!image || bytes != 0xe57000) return false;
    constexpr std::uintptr_t present = 0xa26994;
    constexpr std::uintptr_t endScene = 0xa26954;
    constexpr std::uintptr_t shopDrawSlot = 0xc11c34;
    constexpr std::uintptr_t queueWait = 0xa2d438;
    constexpr std::uintptr_t mainSleep = 0xa3cef3;
    constexpr std::array<unsigned char, 26> presentContext{
        0x8b, 0x86, 0x3c, 0x20, 0, 0, 0x8b, 0x08, 0x83, 0xc4, 0x1c,
        0x6a, 0, 0x6a, 0, 0x6a, 0, 0x6a, 0, 0x50, 0xff, 0x51, 0x3c, 0x6a, 1, 0x6a };
    if (std::memcmp(image + (0xa26989 - Base), presentContext.data(), presentContext.size()) != 0) return false;
    constexpr unsigned char endSceneContext[]{
        0x8b, 0x86, 0x3c, 0x20, 0, 0, 0x8b, 0x10, 0x50, 0xff, 0x92, 0x8c, 0, 0, 0, 0x6a, 1
    };
    constexpr unsigned char shopReturn[]{ 0x5e, 0xc3, 0xcc };
    constexpr unsigned char shopDrawContext[]{
        0x55, 0x8b, 0xec, 0x56, 0x8b, 0xf1, 0x8b, 0x46, 0x68, 0x57, 0x8b, 0x7d, 0x08, 0x3b, 0xf8, 0x75
    };
    constexpr unsigned char queueWaitContext[]{
        0x56, 0x57, 0x8b, 0xf1, 0x8d, 0x7e, 0x50, 0x57, 0xff, 0x15, 0x74, 0xa2, 0xae, 0x00, 0x8b, 0x86
    };
    constexpr unsigned char noticeContext[]{
        0x6a, 0x0d, 0x50, 0x6a, 0x02, 0x68, 0x00, 0x00, 0x00, 0xff, 0x6a, 0xff, 0x6a, 0x50,
        0x68, 0x6b, 0x01, 0x00, 0x00, 0xe8, 0x67, 0xb6, 0xfd, 0xff, 0x8b, 0x0d, 0xd4, 0x1f, 0xe0, 0x00
    };
    constexpr unsigned char noticeStateGate[]{0x39, 0x5e, 0x60, 0x0f, 0x85, 0xaf, 0x01, 0x00, 0x00};
    constexpr unsigned char reviveFlagGate[]{0x83, 0xe8, 0x00, 0x74, 0x30, 0x83, 0xe8, 0x02, 0x74, 0x2b};
    constexpr unsigned char shopOverlayGate[]{0x80, 0x3d, 0x95, 0xa8, 0xdf, 0x00, 0x01, 0x75, 0x5d};
    constexpr unsigned char sleepContext[]{
        0x8b, 0x57, 0x0c, 0x52, 0xff, 0x15, 0xcc, 0xa2, 0xae, 0x00, 0x8b, 0x47, 0x7c
    };
    if (std::memcmp(image + (0xa2694c - Base), endSceneContext, sizeof(endSceneContext)) != 0 ||
        std::memcmp(image + (0x947ba4 - Base), shopReturn, sizeof(shopReturn)) != 0 ||
        std::memcmp(image + (0x942480 - Base), shopDrawContext, sizeof(shopDrawContext)) != 0 ||
        std::memcmp(image + (0xa2d430 - Base), queueWaitContext, sizeof(queueWaitContext)) != 0 ||
        std::memcmp(image + (0x93f091 - Base), noticeContext, sizeof(noticeContext)) != 0 ||
        std::memcmp(image + (0x93f0b7 - Base), noticeStateGate, sizeof(noticeStateGate)) != 0 ||
        std::memcmp(image + (0x93f210 - Base), reviveFlagGate, sizeof(reviveFlagGate)) != 0 ||
        std::memcmp(image + (0x9b431b - Base), shopOverlayGate, sizeof(shopOverlayGate)) != 0 ||
        std::memcmp(image + (mainSleep - 4 - Base), sleepContext, sizeof(sleepContext)) != 0 ||
        Read32(image + (shopDrawSlot - Base)) != 0x942480) return false;
    for (const auto& call : Calls)
    {
        const auto site = image + (call.address - Base);
        if (site[0] != 0xe8 || Read32(site + 1) != call.target - call.address - 5) return false;
    }
    for (const auto& loader : Loaders)
        if (Read32(image + (loader.address - Base)) != loader.target) return false;

    // The signatures above pin the call sites. Also check the original stack-pop
    // operands for the wrapped thiscall entries before installing any write.
    struct ReturnSite
    {
        std::uintptr_t address;
        unsigned char stackBytes;
    };
    constexpr ReturnSite returns[]{
        { 0xa4a013, 8 }, { 0x625ea2, 4 }, { 0x483ba4, 20 }, { 0x4868a5, 8 },
        { 0x482837, 8 }, { 0x9cdb2b, 8 }, { 0x9ce1af, 8 }, { 0x9ce87c, 8 },
        { 0x9424a0, 4 }, { 0x942510, 4 }, { 0x97b993, 16 },
        { 0x88bb03, 4 }, { 0xa3eb68, 36 },
    };
    for (const auto& entry : returns)
    {
        const auto code = image + (entry.address - Base);
        if (code[0] != 0xc2 || code[1] != entry.stackBytes || code[2] != 0) return false;
    }

    if (image[0x88babe - Base] != 0xc3 || image[0xa3cc31 - Base] != 0xc3) return false;
    UniformPopupTile = VerifyUniformPopupTile();
    GameImage = image;
    for (const auto& call : Calls) Redirect(image + (call.address - Base), call.hook);
    for (const auto& loader : Loaders)
        Write32(image + (loader.address - Base), reinterpret_cast<std::uintptr_t>(loader.hook));
    Write32(image + (shopDrawSlot - Base), reinterpret_cast<std::uintptr_t>(&MeasureShopDraw));
    Redirect(image + (queueWait - Base), reinterpret_cast<const void*>(&MeasureTextureQueueWait));
    image[queueWait + 5 - Base] = 0x90;
    Redirect(image + (mainSleep - Base), reinterpret_cast<const void*>(&MeasureMainLoopSleep));
    image[mainSleep + 5 - Base] = 0x90;
    auto* scene = image + (endScene - Base);
    scene[0] = 0x8b;
    scene[1] = 0xc8; // mov ecx, eax
    Redirect(scene + 2, reinterpret_cast<const void*>(&MeasureEndScene));
    auto* block = image + (present - Base);
    // The native block calls device->Present(nullptr, nullptr, nullptr, nullptr).
    // EAX already carries the device and the next original instruction consumes HRESULT.
    block[0] = 0x8b;
    block[1] = 0xc8; // mov ecx, eax
    Redirect(block + 2, reinterpret_cast<const void*>(&MeasurePresent));
    std::fill_n(block + 7, 5, static_cast<unsigned char>(0x90));
    return true;
}

UINT dfl::chapter_two::performance::TakeImeGameplayKey(HWND window, LPARAM details) noexcept
{
    if (GetCurrentThreadId() != KeyboardThread) return 0;
    return PendingImeGameplayKey.Take(window, details);
}

bool dfl::chapter_two::performance::IsKeyboardTextEntryActive() noexcept
{
    return KeyboardImage && GetCurrentThreadId() == KeyboardThread && ReadChatFocus() == ChatFocus::Editor;
}

bool dfl::chapter_two::InstallKeyboardMessageTiming(unsigned char* image, std::size_t bytes) noexcept
{
    if (!image || bytes != 0xe57000) return false;
    const auto user32 = GetModuleHandleW(L"user32.dll");
    if (!user32) return false;
    struct MessageImport
    {
        const char* name;
        std::size_t offset;
        const void* hook;
    };
    const MessageImport imports[]{
        { "PeekMessageA", 0x6ea3a4, reinterpret_cast<const void*>(&MeasurePeekMessage) },
        { "TranslateMessage", 0x6ea420, reinterpret_cast<const void*>(&MeasureTranslateMessage) }
    };
    for (const auto& entry : imports)
    {
        const auto slot = dfl::pe_import::FindNamed(image, bytes, "user32.dll", entry.name);
        const auto function = GetProcAddress(user32, entry.name);
        if (reinterpret_cast<const unsigned char*>(slot) != image + entry.offset || !function ||
            Read32(image + entry.offset) != reinterpret_cast<std::uintptr_t>(function)) return false;
    }
    for (const auto& entry : imports)
        Write32(image + entry.offset, reinterpret_cast<std::uintptr_t>(entry.hook));
    constexpr unsigned char chatCloseFocus[]{
        0x8b,0x86,0x9c,0x00,0x00,0x00, 0x8b,0x40,0x0c, 0xa3,0xd4,0xdf,0xd4,0x00,
        0xa3,0xdc,0xdf,0xd4,0x00
    };
    constexpr unsigned char chatOpenFocus[]{
        0x8b,0x8e,0xa8,0x00,0x00,0x00, 0x8b,0x41,0x0c, 0xa3,0xd4,0xdf,0xd4,0x00,
        0xa3,0xdc,0xdf,0xd4,0x00
    };
    if (std::memcmp(image + (0x92384c - Base), chatCloseFocus, sizeof(chatCloseFocus)) == 0 &&
        std::memcmp(image + (0x9237f0 - Base), chatOpenFocus, sizeof(chatOpenFocus)) == 0)
    {
        KeyboardImage = image;
        KeyboardThread = GetCurrentThreadId();
        OutputDebugStringA("DFLegacy Chapter Two: inactive chat IME filter installed.\n");
    }
    return true;
}
