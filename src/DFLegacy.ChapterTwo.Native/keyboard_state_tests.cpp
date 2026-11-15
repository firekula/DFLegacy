// Exercise the generated production host without entering the game or creating
// a window. Only the operating-system input boundary is supplied by the fixture.
#define WIN32_LEAN_AND_MEAN
#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include <winsock2.h>
#include <windows.h>
#include <array>
#include <cstdio>
#include <cstring>
#include "ChapterTwoKeyboardTiming.h"

namespace
{
    HWND Foreground = reinterpret_cast<HWND>(0x1234);
    DWORD WindowThread = GetCurrentThreadId();
    UINT UnmappedKey = 0;
    ULONGLONG Now = 1000;
    std::array<SHORT, 256> PhysicalKeys{};
    dfl::chapter_two::performance::ImeGameplayKey ImeKey{};
    unsigned Checks = 0, Failures = 0, PollCalls = 0, WindowCalls = 0;
    HRESULT PollStatus = S_OK;
    bool TextEntryActive = false;
    std::array<unsigned char, 256> DeviceKeys{};

    ULONGLONG WINAPI FixtureTickCount() { return Now; }
    SHORT WINAPI FixtureAsyncKeyState(int key) { return PhysicalKeys.at(key); }
    HWND WINAPI FixtureForeground() { return Foreground; }
    DWORD WINAPI FixtureWindowThread(HWND, LPDWORD) { return WindowThread; }
    UINT WINAPI FixtureMapVirtualKey(UINT key, UINT type)
    {
        return key == UnmappedKey ? 0 : MapVirtualKeyW(key, type);
    }
    LRESULT WINAPI FixtureWindowProc(WNDPROC, HWND, UINT, WPARAM, LPARAM)
    {
        ++WindowCalls;
        return 123;
    }
}

namespace dfl::chapter_two::performance
{
    UINT FixtureTakeImeGameplayKey(HWND window, LPARAM details) noexcept
    {
        return ImeKey.Take(window, details);
    }
    bool FixtureIsKeyboardTextEntryActive() noexcept { return TextEntryActive; }
}

#define GetTickCount64 FixtureTickCount
#define GetAsyncKeyState FixtureAsyncKeyState
#define GetForegroundWindow FixtureForeground
#define GetWindowThreadProcessId FixtureWindowThread
#define MapVirtualKeyW FixtureMapVirtualKey
#define CallWindowProcW FixtureWindowProc
#define TakeImeGameplayKey FixtureTakeImeGameplayKey
#define IsKeyboardTextEntryActive FixtureIsKeyboardTextEntryActive
#define wmain UnusedGameEntry
#include CHAPTER_TWO_HOST_SOURCE
#undef wmain
#undef TakeImeGameplayKey
#undef IsKeyboardTextEntryActive
#undef CallWindowProcW
#undef GetWindowThreadProcessId
#undef MapVirtualKeyW
#undef GetForegroundWindow
#undef GetAsyncKeyState
#undef GetTickCount64

namespace
{
    void Check(bool condition, const char* message)
    {
        ++Checks;
        if (!condition)
        {
            ++Failures;
            std::fprintf(stderr, "FAIL C2 keyboard: %s\n", message);
        }
    }

    HRESULT WINAPI DevicePoll(void*, DWORD size, void* state)
    {
        ++PollCalls;
        if (SUCCEEDED(PollStatus) && state)
            std::memcpy(state, DeviceKeys.data(), (std::min)(size, DWORD(DeviceKeys.size())));
        return PollStatus;
    }

    void* DeviceMethods[10]{};
    void** Device = DeviceMethods;

    void Reset()
    {
        std::memset(windowKeys, 0, sizeof(windowKeys));
        DeviceKeys.fill(0);
        PhysicalKeys.fill(0);
        ImeKey = {};
        Now = 1000;
        Foreground = reinterpret_cast<HWND>(0x1234);
        keyboardWindow = Foreground;
        gameWindowProc = reinterpret_cast<WNDPROC>(1);
        WindowThread = GetCurrentThreadId();
        UnmappedKey = 0;
        PollStatus = S_OK;
        TextEntryActive = false;
        DeviceMethods[9] = reinterpret_cast<void*>(&DevicePoll);
    }

    std::array<unsigned char, 256> Poll()
    {
        std::array<unsigned char, 256> keys{};
        Check(GameKeyboardState(&Device, DWORD(keys.size()), keys.data()) == PollStatus,
            "the original DirectInput status is returned");
        return keys;
    }

    void SendKey(UINT message, WPARAM key, UINT scan)
    {
        const LPARAM details = LPARAM(scan << 16) | 1 |
            (message == WM_KEYUP ? LPARAM(0xc0000000u) : 0);
        Check(GameWindowKeys(keyboardWindow, message, key, details) == 123,
            "the original window procedure result is returned");
    }

    void CheckRapidTaps()
    {
        Reset();
        constexpr UINT xScan = 0x2d;
        for (unsigned tap = 0; tap < 12; ++tap)
        {
            PhysicalKeys['X'] = -32768;
            SendKey(WM_KEYDOWN, 'X', xScan);
            Check((Poll()[xScan] & 0x80) != 0, "every X keydown reaches the next poll");
            Now += 8;
            PhysicalKeys['X'] = 0;
            SendKey(WM_KEYUP, 'X', xScan);
            Check((Poll()[xScan] & 0x80) == 0, "every X release separates rapid taps immediately");
            Now += 8;
        }
    }

    void CheckPhysicalFallback()
    {
        for (const auto key : {int('X'), int(VK_LEFT)})
        {
            Reset();
            const UINT scan = key == 'X' ? 0x2d : 0xcb;
            PhysicalKeys[key] = -32768;
            Check((Poll()[scan] & 0x80) != 0, "a physical key is read when its window message is unavailable");
            Now += 1;
            PhysicalKeys[key] = 0;
            Check((Poll()[scan] & 0x80) == 0, "physical fallback stops at release without a grace period");
            PhysicalKeys[key] = 1;
            Check((Poll()[scan] & 0x80) == 0, "the asynchronous transition bit does not count as a held key");
        }
        Reset();
        UnmappedKey = 'X';
        PhysicalKeys['X'] = -32768;
        Check(Poll()[0] == 0, "an unmapped virtual key does not alter the reserved scan-code byte");
    }

    void CheckUnobservedTaps()
    {
        for (const bool ime : {false, true})
        {
            Reset();
            const auto press = [&]
            {
                if (ime) ImeKey = {keyboardWindow, 1, 'X'};
                SendKey(WM_KEYDOWN, ime ? VK_PROCESSKEY : 'X', ime ? 0 : 0x2d);
                Now += 20;
                SendKey(WM_KEYUP, ime ? VK_PROCESSKEY : 'X', ime ? 0 : 0x2d);
            };
            press();
            Check((Poll()[0x2d] & 0x80) != 0, "a released tap dispatched between polls is sampled once");
            Check((Poll()[0x2d] & 0x80) == 0, "a sampled released tap is clear on the next poll");
            press();
            PollStatus = E_FAIL;
            Poll();
            PollStatus = S_OK;
            Check((Poll()[0x2d] & 0x80) != 0, "a failed native poll does not consume a pending tap");
            Check((Poll()[0x2d] & 0x80) == 0, "a recovered tap is delivered only once");
            press();
            GameWindowKeys(keyboardWindow, WM_KILLFOCUS, 0, 0);
            Check((Poll()[0x2d] & 0x80) == 0, "focus loss discards an unobserved tap");
            press();
            TextEntryActive = true;
            Poll();
            TextEntryActive = false;
            Check((Poll()[0x2d] & 0x80) == 0, "opening text input clears a pending gameplay tap");
            TextEntryActive = true;
            press();
            TextEntryActive = false;
            Check((Poll()[0x2d] & 0x80) == 0, "text entry never accumulates a gameplay tap");
            Foreground = nullptr;
            GameWindowKeys(keyboardWindow, WM_KILLFOCUS, 0, 0);
            press();
            Poll();
            Foreground = keyboardWindow;
            Check((Poll()[0x2d] & 0x80) == 0, "messages dispatched after focus loss never accumulate a tap");
            press();
            Foreground = nullptr;
            Poll();
            Foreground = keyboardWindow;
            Check((Poll()[0x2d] & 0x80) == 0, "background polling clears pending input before focus returns");
        }
    }

    void CheckImeHoldAndTap()
    {
        Reset();
        constexpr UINT xScan = 0x2d;
        ImeKey = {keyboardWindow, 1, 'X'};
        PhysicalKeys['X'] = -32768;
        Check(GameWindowKeys(keyboardWindow, WM_KEYDOWN, VK_PROCESSKEY, 1) == 123,
            "a recovered IME key keeps the native window dispatch");
        Check((Poll()[xScan] & 0x80) != 0, "the recovered IME key reaches the first poll");
        Now += 200;
        Check((Poll()[xScan] & 0x80) != 0, "a physically held IME key remains down beyond 60 milliseconds");
        PhysicalKeys['X'] = 0;
        Now += 1;
        Check((Poll()[xScan] & 0x80) == 0, "an IME key releases even when its keyup has no scan code");
        ImeKey = {keyboardWindow, 1, 'X'};
        PhysicalKeys['X'] = -32768;
        GameWindowKeys(keyboardWindow, WM_KEYDOWN, VK_PROCESSKEY, 1);
        Check((Poll()[xScan] & 0x80) != 0, "a second short IME press is accepted immediately");
        Now += 1;
        PhysicalKeys['X'] = 0;
        Check((Poll()[xScan] & 0x80) == 0, "a short IME press ends at the physical release");
        Reset();
        SendKey(WM_KEYDOWN, VK_PROCESSKEY, xScan);
        SendKey(WM_KEYUP, VK_PROCESSKEY, 0);
        Check((Poll()[xScan] & 0x80) == 0 && (Poll()[xScan] & 0x80) == 0,
            "an unrecovered IME message cannot latch a scan code after a scanless release");
        PhysicalKeys['X'] = -32768;
        Check((Poll()[xScan] & 0x80) != 0, "unrecovered IME messages retain physical held-key fallback");
        PhysicalKeys['X'] = 0;
        Check((Poll()[xScan] & 0x80) == 0, "unrecovered IME physical fallback releases immediately");
    }

    void CheckNativeAndFocusContracts()
    {
        Reset();
        DeviceKeys[0x2d] = 0x81;
        Check(Poll()[0x2d] == 0x81, "DirectInput bytes are preserved without a fallback key");
        DeviceKeys.fill(0);
        SendKey(WM_KEYDOWN, VK_SPACE, 0x39);
        Check((Poll()[0x39] & 0x80) != 0, "ordinary window keys retain their existing down state");
        SendKey(WM_KEYUP, VK_SPACE, 0x39);
        Check((Poll()[0x39] & 0x80) == 0, "ordinary window keys release immediately");
        SendKey(WM_KEYDOWN, 'X', 0x2d);
        PhysicalKeys['X'] = -32768;
        Foreground = nullptr;
        Check(Poll()[0x2d] == 0, "background polling receives only the native state");
        GameWindowKeys(keyboardWindow, WM_KILLFOCUS, 0, 0);
        Foreground = keyboardWindow;
        PhysicalKeys['X'] = 0;
        Check(Poll()[0x2d] == 0, "focus loss clears message-held keys before focus returns");
        PhysicalKeys['X'] = -32768;
        WindowThread = 0;
        Check(Poll()[0x2d] == 0, "another thread receives only the native state");
        WindowThread = GetCurrentThreadId();
        for (const auto status : {E_FAIL, HRESULT(0x8007001e)})
        {
            PollStatus = status;
            std::array<unsigned char, 256> keys{};
            keys.fill(0x23);
            const auto before = keys;
            const auto calls = PollCalls;
            Check(GameKeyboardState(&Device, DWORD(keys.size()), keys.data()) == status &&
                keys == before && PollCalls == calls + 1,
                "a failed DirectInput poll preserves its status and caller buffer");
        }
        PollStatus = S_FALSE;
        Check((Poll()[0x2d] & 0x80) != 0, "successful nonzero DirectInput results still merge the current key");
        unsigned char shortState = 0x23;
        DeviceKeys[0] = 0x42;
        Check(GameKeyboardState(&Device, 1, &shortState) == S_FALSE && shortState == 0x42,
            "a non-keyboard-sized state receives only the native poll output");
    }
}

int main()
{
    CheckRapidTaps();
    CheckUnobservedTaps();
    CheckPhysicalFallback();
    CheckImeHoldAndTap();
    CheckNativeAndFocusContracts();
    std::printf("C2 keyboard: %u checks, %u failures, %u native polls, %u window dispatches\n",
        Checks, Failures, PollCalls, WindowCalls);
    return Failures ? 1 : 0;
}
