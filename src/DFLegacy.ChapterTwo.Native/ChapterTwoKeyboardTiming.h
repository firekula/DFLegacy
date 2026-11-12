#pragma once
#include <windows.h>
#include <cstdint>
#include "ChapterTwoFrameStages.h"

namespace dfl::chapter_two::performance
{
    constexpr bool IsChineseImeLetterDown(UINT message, WPARAM key, LPARAM details,
        LANGID language, UINT originalKey) noexcept
    {
        return message == WM_KEYDOWN && key == VK_PROCESSKEY && PRIMARYLANGID(language) == LANG_CHINESE &&
            (details & 0x20000000) == 0 && originalKey >= 'A' && originalKey <= 'Z';
    }

    constexpr bool ChatFallbackHasFocus(unsigned char chatOpen, std::uint32_t focus,
        std::uint32_t fallbackId, std::uint32_t editorId) noexcept
    {
        return chatOpen == 0 && fallbackId != 0 && editorId != 0 &&
            fallbackId != editorId && focus == fallbackId;
    }

    struct ImeGameplayKey
    {
        HWND window = nullptr;
        LPARAM details = 0;
        UINT virtualKey = 0;

        UINT Take(HWND target, LPARAM messageDetails) noexcept
        {
            if (target != window || messageDetails != details) return 0;
            const auto result = virtualKey;
            *this = {};
            return result;
        }
    };

    UINT TakeImeGameplayKey(HWND window, LPARAM details) noexcept;

    constexpr FrameStage ClassifyKeyMessage(UINT message, WPARAM key, LPARAM details) noexcept
    {
        if (message == WM_KEYDOWN && (details & 0x40000000) == 0)
        {
            if (key >= 'A' && key <= 'Z') return FrameStage::LetterDown;
            if (key >= VK_LEFT && key <= VK_DOWN) return FrameStage::ArrowDown;
        }
        if (message == WM_KEYDOWN || message == WM_KEYUP || message == WM_SYSKEYDOWN || message == WM_SYSKEYUP)
            return FrameStage::KeyDispatch;
        if (message == WM_CHAR || message == WM_SYSCHAR || message == WM_DEADCHAR ||
            message == WM_SYSDEADCHAR || message == WM_UNICHAR || message == WM_INPUTLANGCHANGE ||
            (message >= WM_IME_STARTCOMPOSITION && message <= WM_IME_COMPOSITION) ||
            (message >= WM_IME_SETCONTEXT && message <= WM_IME_KEYUP))
            return FrameStage::TextDispatch;
        return FrameStage::Count;
    }

    using KeyboardPoll = HRESULT (WINAPI*)(void*, DWORD, void*);

    inline HRESULT MeasureKeyboardPoll(KeyboardPoll poll, void* device, DWORD size, void* state)
    {
        FrameStageScope scope(FrameStage::KeyboardPoll);
        return poll(device, size, state);
    }
}
