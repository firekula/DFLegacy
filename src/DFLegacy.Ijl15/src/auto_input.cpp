#include "../../DFLegacy.Client/client_paths.h"
#include "../../DFLegacy.Client/bootstrap_runtime.h"
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <cstring>
#include <vector>

#include "auto_input.h"
#include "ijl15_config.h"
#include "memory_patch.h"

namespace dfl::auto_input
{
    bool IsFocusRequestForProcess(
        const char* text, unsigned size, unsigned long processId) noexcept
    {
        if (text == nullptr || size == 0 || size > 32 || processId == 0)
        {
            return false;
        }
        const auto whitespace = [](char c) noexcept
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        };
        unsigned begin = 0;
        while (begin < size && whitespace(text[begin])) ++begin;
        while (size > begin && whitespace(text[size - 1])) --size;
        if (begin == size || text[begin] == '0')
        {
            return false;
        }
        unsigned long parsed = 0;
        for (unsigned i = begin; i < size; ++i)
        {
            if (text[i] < '0' || text[i] > '9') return false;
            const unsigned long digit = static_cast<unsigned long>(text[i] - '0');
            if (parsed > (0xFFFFFFFFUL - digit) / 10UL) return false;
            parsed = parsed * 10UL + digit;
        }
        return parsed == processId;
    }

    bool IsEnabled() noexcept
    {
        return dfl::client_bootstrap::AllowTestInput(dfl::ijl15_config::Get().EnableAutoInput);
    }

    // ------------------------------------------------------------------
    // Core state. Everything lives in these translation-unit locals and is
    // only touched from the game's main thread (no new threads; spec 3.3).
    // ------------------------------------------------------------------
    namespace
    {
        constexpr int kLogCapacity = 64;
        constexpr unsigned kTextSpacingMs = 40;  // brief: text downs >= 40 ms apart
        constexpr unsigned kDragStageSpacingMs = 16;
        constexpr long long kMaxPlanBytes = 1024 * 1024;

        enum class Phase
        {
            Idle,
            Armed,
            Finished,
        };

        Phase g_phase = Phase::Idle;
        // True while Advance() is delivering edges: a nested pump (the game's
        // message handling calls back into GetAsyncKeyState, our pump anchor)
        // must not advance the same timeline from inside its own delivery.
        bool g_inTimeline = false;

        // Scope guard, not two assignments: a throw or a swallowed structured
        // exception inside Advance() must not leave the pump latched off for
        // the rest of the session.
        struct TimelineGuard
        {
            bool previous;
            TimelineGuard() noexcept : previous(g_inTimeline) { g_inTimeline = true; }
            ~TimelineGuard() noexcept { g_inTimeline = previous; }
        };
        Step g_plan[kMaxSteps];
        int g_count = 0;
        int g_version = 0;
        unsigned g_origin = 0;
        bool g_fromFile = false;
        bool g_doneMarked = false;
        bool g_renamedCleanly = true;  // a failed .done rename stops the poll
        wchar_t g_planPath[MAX_PATH] = {};
        wchar_t g_donePath[MAX_PATH] = {};

        bool g_downDone[kMaxSteps] = {};
        bool g_upDone[kMaxSteps] = {};   // key/click: up fired; move: the move fired
        bool g_dragMoveDone[kMaxSteps] = {};
        unsigned g_mouseStageTick[kMaxSteps] = {};
        bool g_mouseLeftDown = false;
        bool g_mouseRightDown = false;
        int g_mouseX = 0;
        int g_mouseY = 0;
        int g_textDowns[kMaxSteps] = {};
        int g_textUps[kMaxSteps] = {};
        unsigned char g_textVk[kMaxSteps][64] = {};  // per-char VK codes; 0 = no key
        unsigned char g_textChar[kMaxSteps][64] = {};  // raw chars for WM_CHAR delivery

        bool g_synthDown[256] = {};
        bool g_synthEver[256] = {};  // keys this module synthesized at least once

        InjectionRecord g_log[kLogCapacity] = {};
        int g_logHead = 0;
        int g_logTotal = 0;

        unsigned NativeMouseButtons() noexcept
        {
            return (g_mouseLeftDown ? 1u : 0u) | (g_mouseRightDown ? 2u : 0u);
        }

        void LogPrimitive(int kind, unsigned vk, int x, int y, unsigned buttons)
        {
            g_log[g_logHead].op = kind;
            g_log[g_logHead].vk = vk;
            g_log[g_logHead].x = x;
            g_log[g_logHead].y = y;
            g_log[g_logHead].buttons = buttons;
            g_logHead = (g_logHead + 1) % kLogCapacity;
            ++g_logTotal;
        }

        unsigned char VkFromChar(char value)
        {
            // Letters and digits map to their VK codes; space and Enter cover
            // the confirmation keys the acceptance scripts need. Everything
            // else has no shift-free single key and is skipped: its 40 ms
            // slot still elapses, so the expansion timing stays monotone.
            if (value >= 'a' && value <= 'z')
            {
                return static_cast<unsigned char>(value - 'a' + 'A');
            }
            if ((value >= 'A' && value <= 'Z')
                || (value >= '0' && value <= '9'))
            {
                return static_cast<unsigned char>(value);
            }
            if (value == ' ')
            {
                return VK_SPACE;
            }
            if (value == '\r' || value == '\n')
            {
                return VK_RETURN;
            }
            return 0;
        }

        // ------------------------------------------------------------------
        // Injection layer (Task 3). The addresses are 2008DF-specific; every
        // install-time access is readability-checked first, so a client
        // without the expected layout keeps running untouched.
        // ------------------------------------------------------------------

        using GetAsyncKeyStateFn = SHORT(WINAPI*)(int);
        constexpr std::uintptr_t kGetAsyncKeyStateIat = 0x009983DC;
        constexpr std::uintptr_t kNativeMouseInput = 0x008FCAA0;

        // DiagLine is defined further down (beside BuildExeRelativePath);
        // the hook below uses it for the one-shot VK-polling sample dump.
        void DiagLine(const char* text) noexcept;

        GetAsyncKeyStateFn g_originalGetAsyncKeyState = nullptr;
        bool g_keyboardHooked = false;
        bool g_mouseInjectEnabled = false;
        UINT_PTR g_focusMailboxTimer = 0;
        // The game's own window, cached from the focus gate: plans only
        // advance while this process owns the foreground window, and the
        // foreground window then IS the game window.
        HWND g_gameWindow = nullptr;
        // The keyboard-focus window of the game thread (GetFocus from the
        // game's own thread). Real key input lands here -- which may be a
        // CHILD window, while GetForegroundWindow only ever returns the
        // top-level one.
        HWND g_keyboardTarget = nullptr;
        // Every top-level window of this process (the game may own several,
        // e.g. a hidden input window next to the visible one). Keys are
        // posted to all of them; the one whose window proc handles input
        // acts, the rest ignore the message.
        HWND g_allWindows[8] = {};
        int g_allWindowCount = 0;
        bool g_windowsEnumerated = false;

        BOOL CALLBACK CollectTopWindow(HWND hwnd, LPARAM) noexcept
        {
            DWORD pid = 0;
            GetWindowThreadProcessId(hwnd, &pid);
            if (pid == GetCurrentProcessId() && g_allWindowCount < 8)
            {
                g_allWindows[g_allWindowCount++] = hwnd;
            }
            return TRUE;
        }

        bool SynthTableHas(unsigned vk) noexcept
        {
            return vk < 256 && g_synthEver[vk];
        }

        short SynthTableState(unsigned vk) noexcept
        {
            return g_synthDown[vk] ? static_cast<short>(0x8000) : static_cast<short>(0);
        }

        void SynthClear() noexcept
        {
            for (int i = 0; i < 256; ++i)
            {
                g_synthDown[i] = false;
                g_synthEver[i] = false;
            }
        }

        // Continuous sampling of what the game actually polls: every ~600
        // hook queries the VK histogram for that window lands in the diag
        // file and the counters reset, so event-driven keys (arrows are only
        // polled while held) show up in the dump that covers their hold
        // window.
        unsigned g_vkSample[256] = {};
        unsigned g_vkSampleTotal = 0;
        bool g_vkSampleDumped = false;

        // A key this module synthesized is answered from the synth tables
        // (pressed or released), so the real keyboard cannot pollute the
        // edge sequence while the plan runs. Finishing, re-arming or
        // restoring hands the keys back to the real keyboard. Everything
        // else forwards.
        //
        // This hook is also the reliable per-frame pump anchor: the game
        // polls GetAsyncKeyState from its input path every frame on every
        // screen (the performance call site only executes on some), the hook
        // provably installed (IAT-verified), and pumping here makes edges
        // armed this instant visible to this very poll.
        SHORT WINAPI HookedGetAsyncKeyState(int vk) noexcept
        {
            Pump();
            const unsigned code = static_cast<unsigned>(vk);
            if (!g_vkSampleDumped)
            {
                if (code < 256)
                {
                    ++g_vkSample[code];
                }
                if (++g_vkSampleTotal >= 600)
                {
                    char note[256] = {};
                    int used = 0;
                    for (unsigned v = 0; v < 256 && used < 240; ++v)
                    {
                        if (g_vkSample[v] != 0)
                        {
                            const int n = _snprintf_s(
                                note + used,
                                sizeof(note) - static_cast<size_t>(used),
                                _TRUNCATE,
                                "%s0x%02Xx%u",
                                used == 0 ? "vk polled: " : " ",
                                v,
                                g_vkSample[v]);
                            if (n <= 0)
                            {
                                break;
                            }
                            used += n;
                        }
                    }
                    DiagLine(note);
                    for (int i = 0; i < 256; ++i)
                    {
                        g_vkSample[i] = 0;
                    }
                    g_vkSampleTotal = 0;
                }
            }
            if (SynthTableHas(code))
            {
                return SynthTableState(code);
            }
            return g_originalGetAsyncKeyState != nullptr
                ? g_originalGetAsyncKeyState(vk)
                : static_cast<SHORT>(0);
        }

        bool IsReadable(const void* address, std::size_t size) noexcept
        {
            if (address == nullptr || size == 0)
            {
                return false;
            }

            MEMORY_BASIC_INFORMATION information{};
            if (VirtualQuery(
                    address,
                    &information,
                    sizeof(information)) != sizeof(information))
            {
                return false;
            }

            if (information.State != MEM_COMMIT
                || (information.Protect & PAGE_GUARD) != 0
                || (information.Protect & PAGE_NOACCESS) != 0)
            {
                return false;
            }

            const auto regionStart =
                reinterpret_cast<std::uintptr_t>(information.BaseAddress);
            const auto regionEnd = regionStart + information.RegionSize;
            const auto addressValue = reinterpret_cast<std::uintptr_t>(address);
            return addressValue >= regionStart
                && addressValue <= regionEnd
                && size <= regionEnd - addressValue;
        }

        bool ReadIatPointer(std::uintptr_t address, void*& value) noexcept
        {
            if (!IsReadable(reinterpret_cast<const void*>(address), sizeof(void*)))
            {
                return false;
            }
            value = *reinterpret_cast<void**>(address);
            return true;
        }

        using NativeMouseInputFn = char(__cdecl*)(int, int, int);

        // lParam packs the client-area coordinates: low word x, high word y
        // (the game adds its own viewport offsets internally). Unsigned math
        // keeps the packing defined for any int the JSON could carry; for
        // in-range client coordinates the bytes match the plan's layout.
        int PackMouseLParam(int x, int y) noexcept
        {
            const auto ux = static_cast<unsigned>(static_cast<short>(x)) & 0xFFFFu;
            const auto uy = static_cast<unsigned>(static_cast<short>(y)) & 0xFFFFu;
            return static_cast<int>(ux | (uy << 16));
        }

        void InjectMouse(int msg, int x, int y) noexcept
        {
            const auto fn = reinterpret_cast<NativeMouseInputFn>(kNativeMouseInput);
            // WM_MOUSEMOVE must retain MK_LBUTTON during a drag. EmitMouse
            // updates the synthetic button state before entering this native path.
            fn(msg, static_cast<int>(NativeMouseButtons()), PackMouseLParam(x, y));
        }

        // Movement and text keys reach the game through its window message
        // queue, NOT through GetAsyncKeyState: the game's WM_KEYDOWN handler
        // latches the arrow range 0x23..0x28 into its movement state, WM_CHAR
        // feeds text boxes, WM_KEYUP clears the latch (IDA: sub_8FACE0). The
        // IAT hook only serves the keys the game actually polls (Ctrl
        // modifiers 162/163, the screenshot hotkey 0x2C -- VK sampling).
        // Posting to our own thread's queue is asynchronous and safe from
        // the game thread; the tests never cache a window, so they see no
        // messages and the synth-table contract stays the unit-tested seam.
        void EmitKey(unsigned msg, WPARAM wParam) noexcept
        {
            if (!g_windowsEnumerated)
            {
                g_windowsEnumerated = true;
                g_allWindowCount = 0;
                EnumWindows(CollectTopWindow, 0);
                for (int i = 0; i < g_allWindowCount; ++i)
                {
                    char cls[64] = {};
                    GetClassNameA(g_allWindows[i], cls, sizeof(cls) - 1);
                    char note[128] = {};
                    _snprintf_s(
                        note,
                        sizeof(note),
                        _TRUNCATE,
                        "game window[%d]=0x%X class=%s",
                        i,
                        static_cast<unsigned>(
                            reinterpret_cast<std::uintptr_t>(g_allWindows[i])),
                        cls);
                    DiagLine(note);
                }
            }
            // SendMessageA calls the window proc synchronously on our (the
            // game's) own thread -- a queued PostMessageA may never be
            // dispatched if the game's loop filters or starves its queue,
            // while SendMessage reaches the proc regardless. The lParam
            // carries a realistic scan code and key-state flag bits: some
            // handlers check them before latching a key.
            const UINT scan = MapVirtualKeyA(static_cast<UINT>(wParam), 0);
            LPARAM keyLParam = static_cast<LPARAM>(1u | ((scan & 0xFFu) << 16));
            if (msg == WM_KEYUP)
            {
                keyLParam |= static_cast<LPARAM>(0xC0000000u);  // prev down, transition
            }
            if (g_keyboardTarget != nullptr)
            {
                SendMessageA(g_keyboardTarget, msg, wParam, keyLParam);
            }
            if (g_gameWindow != nullptr && g_gameWindow != g_keyboardTarget)
            {
                SendMessageA(g_gameWindow, msg, wParam, keyLParam);
            }
            for (int i = 0; i < g_allWindowCount; ++i)
            {
                if (g_allWindows[i] != g_keyboardTarget
                    && g_allWindows[i] != g_gameWindow)
                {
                    SendMessageA(g_allWindows[i], msg, wParam, keyLParam);
                }
            }
            static bool announced = false;
            if (!announced)
            {
                announced = true;
                void* const wndProc = reinterpret_cast<void*>(
                    GetWindowLongPtrA(
                        g_keyboardTarget != nullptr ? g_keyboardTarget : g_gameWindow,
                        GWLP_WNDPROC));
                char note[160] = {};
                _snprintf_s(
                    note,
                    sizeof(note),
                    _TRUNCATE,
                    "key sent msg=0x%X vk=0x%X target=0x%X top=0x%X windows=%d wndproc=0x%X",
                    msg,
                    static_cast<unsigned>(wParam),
                    static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(g_keyboardTarget)),
                    static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(g_gameWindow)),
                    g_allWindowCount,
                    static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(wndProc)));
                DiagLine(note);
            }
        }

        // ------------------------------------------------------------------
        // DirectInput keyboard state injection (movement keys). The game
        // reads its keyboard through the DirectInput keyboard device:
        // GetDeviceState(device, 256, buffer) at the device vtable index 9
        // (IDA: the input object at offset 0xD14 holds the device pointer and
        // the state buffer sits at object offset 0x10D; the game creates the
        // device with GUID_SysKeyboard through DirectInput8Create's IAT slot
        // 0x998048, CreateDevice = vtable index 3, 0x0C offset). The IAT hook
        // on GetAsyncKeyState only serves the keys the game polls there (the
        // screenshot hotkey 0x2C), and posted WM_KEYDOWN never moves the
        // character -- so this proxy is the real keyboard path: after the
        // original GetDeviceState fills the buffer, the synthetic key states
        // are OR-ed in. DirectInput key codes (DIK) are scan-code based, not
        // VK values: MapVirtualKeyA(vk, 0) translates.
        // ------------------------------------------------------------------

        using DirectInput8CreateFn = void*(WINAPI*)(void*, unsigned, const void*, void**, void*);
        using CreateDeviceFn = long(WINAPI*)(void*, const void*, void**, void*);
        using GetDeviceStateFn = long(WINAPI*)(void*, unsigned, void*);

        constexpr std::uintptr_t kDirectInput8CreateIat = 0x00998048;
        constexpr std::size_t kDirectInput8VtableEntries = 8;  // IUnknown + CreateDevice..Initialize
        constexpr std::size_t kCreateDeviceIndex = 3;
        constexpr std::size_t kKeyboardVtableEntries = 32;  // full IDirectInputDevice8
        constexpr std::size_t kGetDeviceStateIndex = 9;
        constexpr unsigned kKeyboardStateBytes = 256;

        const unsigned char kGuidSysKeyboard[16] = {
            0x61, 0x2B, 0x1D, 0x6F, 0xA0, 0xD5, 0xCF, 0x11,
            0xBF, 0xC7, 0x44, 0x45, 0x53, 0x54, 0x00, 0x00};

        DirectInput8CreateFn g_originalDirectInput8Create = nullptr;
        // Address of the patched CreateDevice slot inside the host's vtable,
        // so Restore can put the original entry back.
        void** g_di8CreateSlot = nullptr;
        CreateDeviceFn g_originalCreateDevice = nullptr;
        GetDeviceStateFn g_originalGetDeviceState = nullptr;
        // Address of the patched GetDeviceState slot in the host's device
        // table (shared by all keyboard devices of this process).
        void** g_keyboardStateSlot = nullptr;

        void** CloneVtable(void* object, std::size_t entryCount) noexcept
        {
            if (object == nullptr || entryCount == 0)
            {
                return nullptr;
            }
            void** const original = *reinterpret_cast<void***>(object);
            if (original == nullptr)
            {
                return nullptr;
            }
            const std::size_t byteCount = entryCount * sizeof(void*);
            void** const copy = static_cast<void**>(HeapAlloc(
                GetProcessHeap(),
                HEAP_ZERO_MEMORY,
                byteCount));
            if (copy == nullptr)
            {
                return nullptr;
            }
            std::memcpy(copy, original, byteCount);
            return copy;
        }

        // Patch a single entry of a vtable that belongs to the HOST's image
        // (dinput8.dll .rdata), leaving every object's vptr alone.
        //
        // Replacing the object's vptr with a heap clone -- the previous
        // approach -- made IDirectInput8::CreateDevice answer 0x80070057 for
        // the keyboard GUID, while the very same call through the original
        // vtable returned a real device (A/B in auto-input-diag.log,
        // 2026-10-23 14:01:27: result=0x0 out=0x16B728D4). The host evidently
        // does not accept its objects wearing a foreign vptr, so the hook has
        // to redirect the shared table instead.
        bool PatchVtableEntry(void** slot, void* replacement) noexcept
        {
            if (slot == nullptr || replacement == nullptr)
            {
                return false;
            }
            DWORD oldProtection = 0;
            if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &oldProtection))
            {
                return false;
            }
            *slot = replacement;
            DWORD ignoredProtection = 0;
            VirtualProtect(slot, sizeof(void*), oldProtection, &ignoredProtection);
            return true;
        }

        bool ReplaceVtable(void* object, void** replacement) noexcept
        {
            if (object == nullptr || replacement == nullptr)
            {
                return false;
            }
            void** const vtableStorage = reinterpret_cast<void**>(object);
            DWORD oldProtection = 0;
            if (!VirtualProtect(
                    vtableStorage,
                    sizeof(void*),
                    PAGE_READWRITE,
                    &oldProtection))
            {
                return false;
            }
            *vtableStorage = replacement;
            DWORD ignoredProtection = 0;
            VirtualProtect(
                vtableStorage,
                sizeof(void*),
                oldProtection,
                &ignoredProtection);
            // The pointer was already replaced: never report failure after a
            // successful write (the caller would free the live clone).
            return true;
        }

        // DirectInput hands the keyboard state back as a 256-byte array indexed
        // by DIK codes: the Set-1 scan code, plus bit 7 for the E0-prefixed
        // (extended) keys. MapVirtualKeyA only ever reports the raw scan code --
        // measured on this host 2026-10-23: MAPVK_VK_TO_VSC_EX returns 0x004B
        // for VK_LEFT, not 0xE04B -- so the extended set has to be enumerated
        // here. Without it the arrow keys land on 0x4B/0x48/0x4D/0x50 while the
        // game reads DIK_LEFT (0xCB) and nothing ever moves.
        unsigned char DikFromVk(unsigned vk) noexcept
        {
            UINT scan = 0;
            switch (vk)
            {
            case VK_LEFT:
            case VK_UP:
            case VK_RIGHT:
            case VK_DOWN:
            case VK_INSERT:
            case VK_DELETE:
            case VK_HOME:
            case VK_END:
            case VK_PRIOR:
            case VK_NEXT:
            case VK_RCONTROL:
            case VK_RMENU:
            case VK_LWIN:
            case VK_RWIN:
            case VK_APPS:
            case VK_DIVIDE:
                scan = MapVirtualKeyA(vk, 0) & 0x7Fu;
                return static_cast<unsigned char>(scan | 0x80u);
            default:
                scan = MapVirtualKeyA(vk, 0) & 0x7Fu;
                return static_cast<unsigned char>(scan);
            }
        }

        // Rate-limited proof that the proxy is live. Without it "the device was
        // never created" and "the device works but no synthetic key was held"
        // look identical in auto-input-diag.log -- which is exactly how the
        // previous session talked itself into bypassing GetDeviceState.
        unsigned g_gdsCalls = 0;
        unsigned g_gdsNonZeroPeak = 0;
        unsigned g_gdsLastLogTick = 0;
        long g_gdsLastResult = 0;

        long WINAPI GetDeviceStateProxy(void* device, unsigned size, void* data) noexcept
        {
            const long result = g_originalGetDeviceState != nullptr
                ? g_originalGetDeviceState(device, size, data)
                : 0x8007000E;  // DIERR_OUTOFMEMORY-ish failure sentinel
            int synthKeys = 0;
            if (result >= 0 && data != nullptr && size >= kKeyboardStateBytes)
            {
                auto* const keys = static_cast<unsigned char*>(data);
                for (int vk = 0; vk < 256; ++vk)
                {
                    if (g_synthDown[vk])
                    {
                        const unsigned char dik = DikFromVk(static_cast<unsigned>(vk));
                        if (dik < kKeyboardStateBytes)
                        {
                            keys[dik] |= 0x80;
                            ++synthKeys;
                        }
                    }
                }
            }
            ++g_gdsCalls;
            g_gdsLastResult = result;
            if (data != nullptr)
            {
                const unsigned char* const bytes =
                    static_cast<const unsigned char*>(data);
                unsigned nonzero = 0;
                const unsigned limit = size < kKeyboardStateBytes
                    ? size
                    : static_cast<unsigned>(kKeyboardStateBytes);
                for (unsigned i = 0; i < limit; ++i)
                {
                    if (bytes[i] != 0)
                    {
                        ++nonzero;
                    }
                }
                if (nonzero > g_gdsNonZeroPeak)
                {
                    g_gdsNonZeroPeak = nonzero;
                }
            }
            const unsigned now = GetTickCount();
            if (g_gdsLastLogTick == 0 || now - g_gdsLastLogTick >= 2000u)
            {
                g_gdsLastLogTick = now;
                char note[144] = {};
                _snprintf_s(
                    note,
                    sizeof(note),
                    _TRUNCATE,
                    "GetDeviceState calls=%u res=0x%X nonzeroPeak=%u synthKeys=%d",
                    g_gdsCalls,
                    static_cast<unsigned>(g_gdsLastResult),
                    g_gdsNonZeroPeak,
                    synthKeys);
                DiagLine(note);
                g_gdsNonZeroPeak = 0;
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Synthetic keyboard device.
        //
        // This host refuses CreateDevice(GUID_SysKeyboard) with 0x80070057 and
        // a NULL device -- observed in two independent runs
        // (auto-input-diag.log 2026-10-23 13:24:43 and 13:28:38) -- while the
        // client dereferences the device pointer with no null check
        // (sub_8E9730 at 0x8E985F: mov eax,[esi+0D14h]; mov ecx,[eax]). With a
        // NULL device the client has no keyboard at all: a physical key faults
        // in that same path, and every plan key takes the process down.
        //
        // So CreateDeviceProxy substitutes this object when the real creation
        // fails. It answers exactly the calls the client makes -- SetDataFormat
        // (0x8E9483), SetCooperativeLevel (0x8E94AA), Acquire (0x8E951C),
        // Poll (0x8E9530), GetDeviceState (0x8E9555), Unacquire (0x8E94D0),
        // Release (0x8E94E0) -- and GetDeviceState merges the physical keyboard
        // (GetAsyncKeyState, so a real keyboard keeps working through the
        // substitute) with the running plan's synthetic keys.
        //
        // Every entry is a __stdcall member: it must pop exactly the arguments
        // the caller pushed, so the stubs exist per arity and the unused slots
        // keep their own (measured from the IDirectInputDevice8 layout).
        // ------------------------------------------------------------------
        constexpr long kDiOk = 0;
        constexpr long kDiErrInvalidParam = static_cast<long>(0x80070057u);
        constexpr long kDiErrUnsupported = static_cast<long>(0x80004001u);  // DIERR_UNSUPPORTED == E_NOTIMPL (dinput.h)

        struct FakeKeyboard
        {
            void** vtable;
            unsigned refCount;
            unsigned acquired;
        };

        FakeKeyboard g_fakeKeyboard = {nullptr, 1, 0};
        void** g_fakeKeyboardVtable = nullptr;
        // The game's input object holds our device from then on, so Restore
        // must keep the physical-keyboard plumbing alive (see Restore).
        bool g_fakeKeyboardInUse = false;
        // The host probe must not run inside DllMain: DirectInput8Create from
        // under the loader lock answers E_NOINTERFACE (three runs), which says
        // nothing about whether this host can create a keyboard device.
        bool g_probePending = false;

        int CountSyntheticKeys() noexcept
        {
            int count = 0;
            for (int vk = 0; vk < 256; ++vk)
            {
                if (g_synthDown[vk])
                {
                    ++count;
                }
            }
            return count;
        }

        void MergeKeyboardState(unsigned char* keys) noexcept
        {            std::memset(keys, 0, kKeyboardStateBytes);
            for (unsigned vk = 0; vk < 256; ++vk)
            {
                bool down = g_synthDown[vk];
                if (!down && g_originalGetAsyncKeyState != nullptr)
                {
                    down = (g_originalGetAsyncKeyState(static_cast<int>(vk)) & 0x8000) != 0;
                }
                if (down)
                {
                    const unsigned char dik = DikFromVk(vk);
                    if (dik < kKeyboardStateBytes)
                    {
                        keys[dik] |= 0x80;
                    }
                }
            }
        }

        long WINAPI FakeStub0(FakeKeyboard*) noexcept { return kDiErrUnsupported; }
        long WINAPI FakeStub1(FakeKeyboard*, void*) noexcept { return kDiErrUnsupported; }
        long WINAPI FakeStub2(FakeKeyboard*, void*, void*) noexcept { return kDiErrUnsupported; }
        long WINAPI FakeStub3(FakeKeyboard*, void*, void*, void*) noexcept { return kDiErrUnsupported; }
        long WINAPI FakeStub4(FakeKeyboard*, void*, void*, void*, void*) noexcept { return kDiErrUnsupported; }

        long WINAPI FakeQueryInterface(FakeKeyboard* self, void*, void** out) noexcept
        {
            // Answer every interface request: the client only asks this object
            // for what it already holds, and refusing an IID would turn a
            // working substitute into an init failure.
            if (out != nullptr)
            {
                *out = self;
            }
            ++self->refCount;
            return kDiOk;
        }

        long WINAPI FakeAddRef(FakeKeyboard* self) noexcept
        {
            ++self->refCount;
            return static_cast<long>(self->refCount);
        }

        long WINAPI FakeRelease(FakeKeyboard* self) noexcept
        {
            if (self->refCount > 0)
            {
                --self->refCount;
            }
            if (self->refCount == 0)
            {
                // Static object: never free, and keep reporting a live
                // reference so a caller checking for zero does not conclude the
                // device vanished under it.
                self->refCount = 1;
            }
            return static_cast<long>(self->refCount);
        }

        long WINAPI FakeGetCapabilities(FakeKeyboard*, void* caps) noexcept
        {
            // DIDEVCAPS is a versioned COM struct: the caller states its own
            // size in dwSize, and DX3 callers pass a shorter one than DX9
            // callers. Zero only what the caller declared, then write the three
            // fields every version has -- never past their buffer.
            if (caps != nullptr)
            {
                auto* const fields = static_cast<unsigned*>(caps);
                const unsigned declared = fields[0];
                const unsigned limit = declared >= sizeof(unsigned) * 8u
                    ? sizeof(unsigned) * 8u
                    : declared;
                for (unsigned i = 0; i < limit; ++i)
                {
                    reinterpret_cast<unsigned char*>(caps)[i] = 0;
                }
                fields[0] = declared != 0 ? declared : sizeof(unsigned) * 8u;
                fields[1] = 0x1u | 0x4u;           // DIDC_ATTACHED | DIDC_EMULATED
                fields[2] = (3u << 8) | 3u;        // keyboard / PC enhanced
            }
            return kDiOk;
        }

        long WINAPI FakeSetProperty(FakeKeyboard*, const void*, const void*) noexcept
        {
            return kDiOk;
        }

        long WINAPI FakeAcquire(FakeKeyboard* self) noexcept
        {
            self->acquired = 1;
            return kDiOk;
        }

        long WINAPI FakeUnacquire(FakeKeyboard* self) noexcept
        {
            self->acquired = 0;
            return kDiOk;
        }

        long WINAPI FakeGetDeviceState(FakeKeyboard*, unsigned size, void* data) noexcept
        {
            if (data == nullptr || size < kKeyboardStateBytes)
            {
                return kDiErrInvalidParam;
            }
            MergeKeyboardState(static_cast<unsigned char*>(data));
            // The real-device proxy logs a rate-limited counter; without the
            // same line here the synthetic path -- the only path this host can
            // reach -- stayed invisible in auto-input-diag.log, and "did the
            // client read the keyboard this run" was unanswerable.
            ++g_gdsCalls;
            const unsigned now = GetTickCount();
            if (g_gdsLastLogTick == 0 || now - g_gdsLastLogTick >= 2000u)
            {
                g_gdsLastLogTick = now;
                char note[144] = {};
                _snprintf_s(
                    note,
                    sizeof(note),
                    _TRUNCATE,
                    "GetDeviceState(synthetic) calls=%u synthKeys=%d",
                    g_gdsCalls,
                    CountSyntheticKeys());
                DiagLine(note);
            }
            return kDiOk;
        }

        long WINAPI FakeGetDeviceData(
            FakeKeyboard*, unsigned, void*, unsigned* count, unsigned) noexcept
        {
            if (count != nullptr)
            {
                *count = 0;  // nothing buffered: the client polls the state
            }
            return kDiOk;
        }

        long WINAPI FakeSetDataFormat(FakeKeyboard*, const void*) noexcept
        {
            return kDiOk;
        }

        long WINAPI FakeSetCooperativeLevel(FakeKeyboard*, void*, unsigned) noexcept
        {
            return kDiOk;
        }

        long WINAPI FakeGetDeviceInfo(FakeKeyboard*, void* info) noexcept
        {
            // DIDEVICEINSTANCE is versioned like DIDEVCAPS, and its dwSize is an
            // input too. Zero only what the caller declared and fill the two
            // leading fields; the name buffers stay empty rather than garbage.
            if (info != nullptr)
            {
                auto* const fields = static_cast<unsigned*>(info);
                const unsigned declared = fields[0];
                const unsigned limit = declared <= 4096u ? declared : 4096u;
                for (unsigned i = 0; i < limit; ++i)
                {
                    reinterpret_cast<unsigned char*>(info)[i] = 0;
                }
                fields[0] = declared;
                fields[1] = (3u << 8) | 3u;
            }
            return kDiOk;
        }

        long WINAPI FakePoll(FakeKeyboard*) noexcept
        {
            return kDiOk;
        }

        void** BuildFakeKeyboardVtable() noexcept
        {
            static void* entries[32] = {};
            auto* const fn = reinterpret_cast<void*>(&FakeStub1);
            for (int i = 0; i < 32; ++i)
            {
                entries[i] = fn;
            }
            entries[0] = reinterpret_cast<void*>(&FakeQueryInterface);
            entries[1] = reinterpret_cast<void*>(&FakeAddRef);
            entries[2] = reinterpret_cast<void*>(&FakeRelease);
            entries[3] = reinterpret_cast<void*>(&FakeGetCapabilities);
            entries[4] = reinterpret_cast<void*>(&FakeStub3);  // EnumObjects: 3 args
            entries[5] = reinterpret_cast<void*>(&FakeStub2);  // GetProperty
            entries[6] = reinterpret_cast<void*>(&FakeSetProperty);
            entries[7] = reinterpret_cast<void*>(&FakeAcquire);
            entries[8] = reinterpret_cast<void*>(&FakeUnacquire);
            entries[9] = reinterpret_cast<void*>(&FakeGetDeviceState);
            entries[10] = reinterpret_cast<void*>(&FakeGetDeviceData);
            entries[11] = reinterpret_cast<void*>(&FakeSetDataFormat);
            entries[12] = reinterpret_cast<void*>(&FakeStub1);  // SetEventNotification
            entries[13] = reinterpret_cast<void*>(&FakeSetCooperativeLevel);
            entries[14] = reinterpret_cast<void*>(&FakeStub3);  // GetObjectInfo
            entries[15] = reinterpret_cast<void*>(&FakeGetDeviceInfo);
            entries[16] = reinterpret_cast<void*>(&FakeStub2);  // RunControlPanel
            entries[17] = reinterpret_cast<void*>(&FakeStub3);  // Initialize
            entries[18] = reinterpret_cast<void*>(&FakeStub4);  // CreateEffect
            entries[19] = reinterpret_cast<void*>(&FakeStub3);  // EnumEffects
            entries[20] = reinterpret_cast<void*>(&FakeStub2);  // GetEffectInfo
            entries[21] = reinterpret_cast<void*>(&FakeStub1);  // GetForceFeedbackState
            entries[22] = reinterpret_cast<void*>(&FakeStub1);  // SendForceFeedbackCommand
            entries[23] = reinterpret_cast<void*>(&FakeStub3);  // EnumCreatedEffectObjects
            entries[24] = reinterpret_cast<void*>(&FakeStub1);  // Escape
            entries[25] = reinterpret_cast<void*>(&FakePoll);
            entries[26] = reinterpret_cast<void*>(&FakeStub4);  // SendDeviceData
            entries[27] = reinterpret_cast<void*>(&FakeStub4);  // EnumEffectsInFile
            entries[28] = reinterpret_cast<void*>(&FakeStub4);  // WriteEffectToFile
            entries[29] = reinterpret_cast<void*>(&FakeStub3);  // BuildActionMap
            entries[30] = reinterpret_cast<void*>(&FakeStub3);  // SetActionMap
            entries[31] = reinterpret_cast<void*>(&FakeStub1);  // GetImageInfo
            g_fakeKeyboard.vtable = entries;
            g_fakeKeyboardVtable = entries;
            return entries;
        }

        long WINAPI CreateDeviceProxy(
            void* di8,
            const void* guid,
            void** device,
            void* outer) noexcept
        {
            static bool guidLogged = false;
            if (!guidLogged && guid != nullptr)
            {
                guidLogged = true;
                const auto* g = static_cast<const unsigned char*>(guid);
                char note[96] = {};
                _snprintf_s(
                    note,
                    sizeof(note),
                    _TRUNCATE,
                    "CreateDevice guid=%02X%02X%02X%02X-%02X%02X-%02X%02X",
                    g[0], g[1], g[2], g[3], g[4], g[5], g[6], g[7]);
                DiagLine(note);
            }
            long result = g_originalCreateDevice != nullptr
                ? g_originalCreateDevice(di8, guid, device, outer)
                : 0x8007000E;
            if (guid != nullptr
                && std::memcmp(guid, kGuidSysKeyboard, 16) == 0)
            {
                char note[128] = {};
                _snprintf_s(
                    note,
                    sizeof(note),
                    _TRUNCATE,
                    "CreateDevice(kbd) result=0x%X device=0x%X out=0x%X",
                    static_cast<unsigned>(result),
                    static_cast<unsigned>(
                        reinterpret_cast<std::uintptr_t>(device)),
                    static_cast<unsigned>(
                        reinterpret_cast<std::uintptr_t>(
                            device != nullptr ? *device : nullptr)));
                DiagLine(note);
            }
            // Retry the same call on the same object exactly once, before the
            // synthetic substitute is considered. If the first attempt failed
            // for a start-up ordering reason, a real device is strictly better
            // than our imitation (real DirectInput semantics for the player's
            // own keyboard). Fail again and the substitute below takes over.
            if (result < 0
                && device != nullptr
                && *device == nullptr
                && guid != nullptr
                && std::memcmp(guid, kGuidSysKeyboard, 16) == 0
                && g_originalCreateDevice != nullptr)
            {
                result = g_originalCreateDevice(di8, guid, device, outer);
                char note[144] = {};
                _snprintf_s(
                    note,
                    sizeof(note),
                    _TRUNCATE,
                    "CreateDevice(kbd) retry result=0x%X out=0x%X",
                    static_cast<unsigned>(result),
                    static_cast<unsigned>(
                        reinterpret_cast<std::uintptr_t>(
                            device != nullptr ? *device : nullptr)));
                DiagLine(note);
            }
            if (result >= 0
                && device != nullptr
                && *device != nullptr
                && guid != nullptr
                && std::memcmp(guid, kGuidSysKeyboard, 16) == 0
                && g_keyboardStateSlot == nullptr)
            {
                void** const originalVtable = *reinterpret_cast<void***>(*device);
                if (originalVtable != nullptr
                    && originalVtable[kGetDeviceStateIndex]
                        != reinterpret_cast<void*>(&GetDeviceStateProxy))
                {
                    // Same rule as the DirectInput8 table above: patch the
                    // slot inside the host's table instead of giving the device
                    // a foreign vptr. Replacing the device's vptr made the
                    // host refuse Acquire/Poll on the real device, so the
                    // client never reached GetDeviceState at all (diag
                    // 2026-10-23 14:03, zero "GetDeviceState calls" lines while
                    // the synthetic device logged thousands).
                    g_originalGetDeviceState =
                        reinterpret_cast<GetDeviceStateFn>(
                            originalVtable[kGetDeviceStateIndex]);
                    void** const slot = &originalVtable[kGetDeviceStateIndex];
                    if (PatchVtableEntry(
                            slot,
                            reinterpret_cast<void*>(&GetDeviceStateProxy)))
                    {
                        g_keyboardStateSlot = slot;
                        DiagLine("keyboard device hooked (GetDeviceState proxy in place)");
                    }
                    else
                    {
                        g_originalGetDeviceState = nullptr;
                        DiagLine("keyboard GetDeviceState slot patch FAILED");
                    }
                }
            }
            // The host has no keyboard device to give (see the synthetic
            // device note above): substitute ours so the client's input object
            // has a non-null device, its initialisation succeeds, and the
            // per-frame read gets a keyboard state instead of faulting.
            //
            // The condition is "no device came back", not "the call failed":
            // a success HRESULT with a null out-pointer would leave the same
            // null-device crash path in place.
            if (device != nullptr
                && *device == nullptr
                && guid != nullptr
                && std::memcmp(guid, kGuidSysKeyboard, 16) == 0)
            {
                if (g_fakeKeyboardVtable == nullptr)
                {
                    BuildFakeKeyboardVtable();
                }
                g_fakeKeyboardInUse = true;
                *device = &g_fakeKeyboard;
                char note[144] = {};
                _snprintf_s(
                    note,
                    sizeof(note),
                    _TRUNCATE,
                    "CreateDevice(kbd) result=0x%X with a null device; synthetic keyboard substituted",
                    static_cast<unsigned>(result));
                DiagLine(note);
                return kDiOk;
            }
            return result;
        }

        void* WINAPI DirectInput8CreateProxy(
            void* instance,
            unsigned version,
            const void* riid,
            void** out,
            void* outer) noexcept
        {
            // DirectInput8Create returns an HRESULT; the created object comes
            // back through ppvOut. Return the HRESULT as the pointer-sized
            // value (the caller tests its sign bit).
            const long hr = g_originalDirectInput8Create != nullptr
                ? static_cast<long>(reinterpret_cast<std::intptr_t>(
                    g_originalDirectInput8Create(instance, version, riid, out, outer)))
                : 0x8007000E;
            {
                // The version and the requested interface decide whether the
                // object can create a keyboard device at all: log them once.
                static bool paramsLogged = false;
                if (!paramsLogged)
                {
                    paramsLogged = true;
                    const auto* const id =
                        static_cast<const unsigned char*>(riid);
                    char note[144] = {};
                    _snprintf_s(
                        note,
                        sizeof(note),
                        _TRUNCATE,
                        "DirectInput8Create hr=0x%X version=0x%X iid=%02X%02X%02X%02X hinst=0x%X",
                        static_cast<unsigned>(hr),
                        version,
                        id != nullptr ? id[0] : 0u,
                        id != nullptr ? id[1] : 0u,
                        id != nullptr ? id[2] : 0u,
                        id != nullptr ? id[3] : 0u,
                        static_cast<unsigned>(
                            reinterpret_cast<std::uintptr_t>(instance)));
                    DiagLine(note);
                }
            }
            // Redirect CreateDevice inside the HOST's own vtable, not inside a
            // per-object copy: replacing the object's vptr made the keyboard
            // CreateDevice fail with 0x80070057 while the identical call through
            // the original table returned a real device (diag 2026-10-23
            // 14:01:27: result=0x0 out=0x16B728D4). The slot is shared by every
            // DirectInput8 object in the process, which is what this hook
            // wants: mouse and keyboard devices come from separate objects.
            if (hr >= 0 && out != nullptr && *out != nullptr)
            {
                void** const current = *reinterpret_cast<void***>(*out);
                if (current != nullptr
                    && current[kCreateDeviceIndex]
                        != reinterpret_cast<void*>(&CreateDeviceProxy))
                {
                    g_originalCreateDevice = reinterpret_cast<CreateDeviceFn>(
                        current[kCreateDeviceIndex]);
                    void** const slot = &current[kCreateDeviceIndex];
                    if (PatchVtableEntry(
                            slot,
                            reinterpret_cast<void*>(&CreateDeviceProxy)))
                    {
                        g_di8CreateSlot = slot;
                        DiagLine("DirectInput8Create proxied (CreateDevice wrapped in place)");
                    }
                    else
                    {
                        g_originalCreateDevice = nullptr;
                        DiagLine("CreateDevice slot patch FAILED: no DI proxy");
                    }
                }
            }
            return reinterpret_cast<void*>(static_cast<std::intptr_t>(hr));
        }

        // Never dereference a suspect game pointer in the exception handler:
        // the diagnostic itself must not cause the next access violation.
        bool ReadCrashDword(const char* label, std::uintptr_t address, DWORD& value) noexcept
        {
            value = 0;
            SIZE_T received = 0;
            const bool readable = address != 0
                && ReadProcessMemory(
                    GetCurrentProcess(),
                    reinterpret_cast<const void*>(address),
                    &value,
                    sizeof(value),
                    &received) != FALSE
                && received == sizeof(value);
            char note[120] = {};
            _snprintf_s(note, sizeof(note), _TRUNCATE,
                "VEH mem tid=%lu %s addr=0x%08X ok=%u value=0x%08lX",
                GetCurrentThreadId(), label, static_cast<unsigned>(address),
                readable ? 1u : 0u, value);
            DiagLine(note);
            return readable;
        }

        void DumpCrashBytes(const char* label, std::uintptr_t address, unsigned length) noexcept
        {
            constexpr char digits[] = "0123456789ABCDEF";
            for (unsigned offset = 0; offset < length && offset <= 0xFFFFFFFFu - address; offset += 16)
            {
                unsigned char bytes[16] = {};
                SIZE_T received = 0;
                const bool readable = address != 0 && ReadProcessMemory(
                    GetCurrentProcess(), reinterpret_cast<const void*>(address + offset),
                    bytes, sizeof(bytes), &received) != FALSE;
                if (received > sizeof(bytes)) received = sizeof(bytes);
                char hex[33] = {};
                for (SIZE_T index = 0; index < received; ++index)
                {
                    hex[index * 2] = digits[bytes[index] >> 4];
                    hex[index * 2 + 1] = digits[bytes[index] & 15];
                }
                char note[120] = {};
                _snprintf_s(note, sizeof(note), _TRUNCATE,
                    "VEH bytes tid=%lu %s addr=%08X ok=%u n=%u hex=%s",
                    GetCurrentThreadId(), label, static_cast<unsigned>(address + offset),
                    readable ? 1u : 0u, static_cast<unsigned>(received), hex);
                DiagLine(note);
            }
        }

        // The client died within 300 ms of the first delivered key on the run
        // above and left no WER record behind, so an unhandled exception has to
        // name itself: the module-relative fault address is what tells a null
        // device dereference (DNF.exe+0x4E9730, sub_8E9730) apart from an
        // exception inside this DLL. Log only fatal codes; never swallow.
        LONG WINAPI CrashProbe(EXCEPTION_POINTERS* info) noexcept
        {
            const unsigned code =
                info != nullptr && info->ExceptionRecord != nullptr
                ? info->ExceptionRecord->ExceptionCode
                : 0u;
            // 0xE06D7363 is a C++ throw (MSVC), routine during startup and not
            // a crash: logging it buried the fatal records.
            if (code >= 0xC0000000u && code != 0xE06D7363u)
            {
                // A logging failure must not recursively enter diagnostics. Preserve
                // LastError and leave both the exception and its CONTEXT untouched.
                static thread_local bool capturing = false;
                if (capturing)
                {
                    return EXCEPTION_CONTINUE_SEARCH;
                }
                capturing = true;
                const DWORD savedError = GetLastError();
                const std::uintptr_t where =
                    info != nullptr && info->ExceptionRecord != nullptr
                    ? reinterpret_cast<std::uintptr_t>(
                        info->ExceptionRecord->ExceptionAddress)
                    : 0u;
                char note[160] = {};
                _snprintf_s(
                    note,
                    sizeof(note),
                    _TRUNCATE,
                    "VEH code=0x%X at=0x%X modrel=0x%X",
                    code,
                    static_cast<unsigned>(where),
                    static_cast<unsigned>(where >= 0x400000u ? where - 0x400000u : 0u));
                DiagLine(note);

                const auto* const record = info->ExceptionRecord;
                _snprintf_s(note, sizeof(note), _TRUNCATE,
                    "VEH record tid=%lu flags=0x%08lX params=%lu",
                    GetCurrentThreadId(), record->ExceptionFlags, record->NumberParameters);
                DiagLine(note);
                for (DWORD index = 0;
                     index < record->NumberParameters && index < EXCEPTION_MAXIMUM_PARAMETERS;
                     ++index)
                {
                    _snprintf_s(note, sizeof(note), _TRUNCATE,
                        "VEH info tid=%lu index=%lu value=0x%08X",
                        GetCurrentThreadId(), index,
                        static_cast<unsigned>(record->ExceptionInformation[index]));
                    DiagLine(note);
                }

#if defined(_M_IX86)
                if (const auto* const context = info->ContextRecord)
                {
                    _snprintf_s(note, sizeof(note), _TRUNCATE,
                        "VEH regs tid=%lu eip=%08lX esp=%08lX ebp=%08lX eflags=%08lX",
                        GetCurrentThreadId(), context->Eip, context->Esp,
                        context->Ebp, context->EFlags);
                    DiagLine(note);
                    _snprintf_s(note, sizeof(note), _TRUNCATE,
                        "VEH regs tid=%lu eax=%08lX ebx=%08lX ecx=%08lX edx=%08lX",
                        GetCurrentThreadId(), context->Eax, context->Ebx,
                        context->Ecx, context->Edx);
                    DiagLine(note);
                    _snprintf_s(note, sizeof(note), _TRUNCATE,
                        "VEH regs tid=%lu esi=%08lX edi=%08lX contextFlags=%08lX",
                        GetCurrentThreadId(), context->Esi, context->Edi, context->ContextFlags);
                    DiagLine(note);

                    DWORD scratch = 0;
                    DWORD objectSignature = 0;
                    DWORD objectIndex = 0;
                    const bool haveSignature = ReadCrashDword(
                        "ESI.signature", context->Esi, objectSignature);
                    const bool haveObjectIndex = ReadCrashDword(
                        "ESI.index", static_cast<std::uintptr_t>(context->Esi) + 4, objectIndex);
                    ReadCrashDword("ESI+8", static_cast<std::uintptr_t>(context->Esi) + 8, scratch);
                    ReadCrashDword("ESI+12", static_cast<std::uintptr_t>(context->Esi) + 12, scratch);
                    ReadCrashDword("EDX.target", context->Edx, scratch);

                    DWORD manager = 0;
                    DWORD secureKey = 0;
                    const bool haveKey = ReadCrashDword("secure.key", 0xCFD45Cu, secureKey);
                    if (haveSignature && haveKey)
                    {
                        _snprintf_s(note, sizeof(note), _TRUNCATE,
                            "VEH derived tid=%lu cookieIndex=%08lX sweepSlot=%08lX",
                            GetCurrentThreadId(), (objectSignature ^ secureKey) - context->Esi, context->Ebx);
                        DiagLine(note);
                    }
                    if (ReadCrashDword("secure.manager", 0xCFD458u, manager) && manager != 0)
                    {
                        DWORD committed = 0;
                        ReadCrashDword("secure.reserved", manager, scratch);
                        const bool haveCommitted = ReadCrashDword(
                            "secure.committed", static_cast<std::uintptr_t>(manager) + 4, committed);
                        ReadCrashDword("secure.liveCount", static_cast<std::uintptr_t>(manager) + 8, scratch);
                        ReadCrashDword("secure.cursorXor", static_cast<std::uintptr_t>(manager) + 0x10, scratch);
                        ReadCrashDword("secure.cursorKey", 0xB55578u, scratch);
                        if (haveCommitted && haveObjectIndex && objectIndex >= committed)
                        {
                            _snprintf_s(note, sizeof(note), _TRUNCATE,
                                "VEH invalid-index tid=%lu index=%08lX committed=%08lX",
                                GetCurrentThreadId(), objectIndex, committed);
                            DiagLine(note);
                        }
                        const unsigned offsets[] = { 0x44u, 0x48u, 0x4Cu, 0x50u };
                        const char* const labels[] = { "secure.values1", "secure.values2", "secure.types", "secure.owners" };
                        for (unsigned index = 0; index < 4; ++index)
                        {
                            DWORD table = 0;
                            if (!ReadCrashDword(labels[index],
                                    static_cast<std::uintptr_t>(manager) + offsets[index], table)
                                || table == 0)
                            {
                                continue;
                            }
                            // 0x901980 enumerates secure.owners by EBX, then
                            // indexes the value/type arrays with [ESI+4]. Log
                            // both identities; reject arithmetic wrapping on x86.
                            const unsigned scale = offsets[index] == 0x4Cu ? 2u : 4u;
                            if (haveCommitted && context->Ebx < committed
                                && context->Ebx <= (0xFFFFFFFFu - table) / scale)
                            {
                                _snprintf_s(note, sizeof(note), _TRUNCATE, "table%02X[EBX]", offsets[index]);
                                const DWORD address = table + context->Ebx * scale;
                                if (ReadCrashDword(note, address, scratch)
                                    && haveKey && (offsets[index] == 0x44u || offsets[index] == 0x48u))
                                {
                                    _snprintf_s(note, sizeof(note), _TRUNCATE,
                                        "VEH decoded tid=%lu table=%02X slot=%08lX value=%08lX",
                                        GetCurrentThreadId(), offsets[index], context->Ebx,
                                        scratch ^ secureKey ^ address);
                                    DiagLine(note);
                                }
                            }
                            if (haveCommitted && haveObjectIndex && objectIndex < committed
                                && objectIndex <= (0xFFFFFFFFu - table) / scale)
                            {
                                _snprintf_s(note, sizeof(note), _TRUNCATE, "table%02X[ESI.index]", offsets[index]);
                                const DWORD address = table + objectIndex * scale;
                                if (ReadCrashDword(note, address, scratch)
                                    && haveKey && (offsets[index] == 0x44u || offsets[index] == 0x48u))
                                {
                                    _snprintf_s(note, sizeof(note), _TRUNCATE,
                                        "VEH decoded tid=%lu table=%02X index=%08lX value=%08lX",
                                        GetCurrentThreadId(), offsets[index], objectIndex,
                                        scratch ^ secureKey ^ address);
                                    DiagLine(note);
                                }
                            }
                        }
                    }
                    for (unsigned index = 0; index < 8; ++index)
                    {
                        _snprintf_s(note, sizeof(note), _TRUNCATE, "stack+%02X", index * 4);
                        ReadCrashDword(note,
                            static_cast<std::uintptr_t>(context->Esp) + index * 4, scratch);
                    }
                    ReadCrashDword("EBP.parent", context->Ebp, scratch);
                    ReadCrashDword("EBP.return", static_cast<std::uintptr_t>(context->Ebp) + 4, scratch);
                    if (context->Esi >= 0x100u)
                    {
                        DumpCrashBytes("wrapper", context->Esi - 0x100u, 512);
                    }
                    DumpCrashBytes("stack", context->Esp, 256);
                }
#endif
                SetLastError(savedError);
                capturing = false;
            }
            return EXCEPTION_CONTINUE_SEARCH;
        }

        // The game's own CreateDevice(GUID_SysKeyboard) answered 0x80070057
        // (E_INVALIDARG) with a NULL device on the first run that actually
        // carried this hook (auto-input-diag.log, 2026-10-23 13:24:43). That
        // result has two very different causes -- this hook is in the way, or
        // the host cannot create a DirectInput keyboard at all -- and they call
        // for opposite fixes. So probe the host from inside the game process
        // through the ORIGINAL entry points: our own DirectInput8 object, none
        // of our vtables, and log what the host answers.
        void ProbeHostKeyboard() noexcept
        {
            if (g_originalDirectInput8Create == nullptr)
            {
                DiagLine("host probe: no DirectInput8Create to probe with");
                return;
            }
            // IID_IDirectInput8A = {BF798030-483A-4DA2-99BD-480587061268},
            // laid out in memory (little-endian Data1..Data3). The first
            // version of this probe had Data1/Data2 swapped, so it answered
            // E_NOINTERFACE everywhere and proved nothing.
            const unsigned char iidDirectInput8A[16] = {
                0x30, 0x80, 0x79, 0xBF, 0x3A, 0x48, 0xA2, 0x4D,
                0x99, 0xBD, 0x48, 0x05, 0x87, 0x06, 0x12, 0x68};
            void* probeObject = nullptr;
            const long createHr = static_cast<long>(reinterpret_cast<std::intptr_t>(
                g_originalDirectInput8Create(
                    GetModuleHandleA(nullptr),
                    0x0800,
                    iidDirectInput8A,
                    &probeObject,
                    nullptr)));
            char note[160] = {};
            _snprintf_s(
                note,
                sizeof(note),
                _TRUNCATE,
                "host probe: DirectInput8Create hr=0x%X object=0x%X",
                static_cast<unsigned>(createHr),
                static_cast<unsigned>(
                    reinterpret_cast<std::uintptr_t>(probeObject)));
            DiagLine(note);
            if (createHr < 0 || probeObject == nullptr)
            {
                return;
            }
            void** const vtable = *reinterpret_cast<void***>(probeObject);
            void* device = nullptr;
            long deviceHr = static_cast<long>(0x80004005u);  // E_FAIL, no vtable
            void* const createSlot =
                vtable != nullptr ? vtable[kCreateDeviceIndex] : nullptr;
            if (createSlot != nullptr)
            {
                const auto create = reinterpret_cast<CreateDeviceFn>(createSlot);
                deviceHr = create(probeObject, kGuidSysKeyboard, &device, nullptr);
            }
            _snprintf_s(
                note,
                sizeof(note),
                _TRUNCATE,
                "host probe: CreateDevice(kbd) hr=0x%X device=0x%X",
                static_cast<unsigned>(deviceHr),
                static_cast<unsigned>(
                    reinterpret_cast<std::uintptr_t>(device)));
            DiagLine(note);
            // Release what came back (index 2 is IUnknown::Release on both
            // interfaces): a leaked probe device would hold a non-exclusive
            // keyboard handle for the rest of the session.
            using ReleaseFn = unsigned long(WINAPI*)(void*);
            if (device != nullptr)
            {
                void** const deviceVtable = *reinterpret_cast<void***>(device);
                if (deviceVtable != nullptr && deviceVtable[2] != nullptr)
                {
                    reinterpret_cast<ReleaseFn>(deviceVtable[2])(device);
                }
            }
            if (vtable != nullptr && vtable[2] != nullptr)
            {
                reinterpret_cast<ReleaseFn>(vtable[2])(probeObject);
            }
        }

        // Real calls replace the injection log only when Install verified the
        // NativeMouseInput address; otherwise (tests, unexpected layouts) the
        // primitives stay observable in the log.
        void EmitMouse(int kind, int x, int y) noexcept
        {
            g_mouseX = x;
            g_mouseY = y;
            if (kind == kMouseDown) g_mouseLeftDown = true;
            else if (kind == kMouseUp) g_mouseLeftDown = false;
            else if (kind == kMouseRightDown) g_mouseRightDown = true;
            else if (kind == kMouseRightUp) g_mouseRightDown = false;
            if (!g_mouseInjectEnabled)
            {
                LogPrimitive(kind, 0, x, y, NativeMouseButtons());
                return;
            }
            int msg = 0;
            if (kind == kMouseMove)
            {
                msg = 0x200;  // WM_MOUSEMOVE
            }
            else if (kind == kMouseDown)
            {
                msg = 513;    // WM_LBUTTONDOWN
            }
            else if (kind == kMouseUp)
            {
                msg = 514;    // WM_LBUTTONUP
            }
            else if (kind == kMouseRightDown)
            {
                msg = 0x204;  // WM_RBUTTONDOWN
            }
            else if (kind == kMouseRightUp)
            {
                msg = 0x205;  // WM_RBUTTONUP
            }
            else
            {
                return;
            }
            InjectMouse(msg, x, y);
        }

        void CancelPlan() noexcept
        {
            const TimelineGuard guard;
            g_phase = Phase::Idle;
            g_count = 0;
            g_fromFile = false;
            if (g_mouseLeftDown) EmitMouse(kMouseUp, g_mouseX, g_mouseY);
            if (g_mouseRightDown) EmitMouse(kMouseRightUp, g_mouseX, g_mouseY);
            for (unsigned vk = 0; vk < 256; ++vk)
            {
                if (g_synthDown[vk])
                {
                    g_synthDown[vk] = false;
                    EmitKey(WM_KEYUP, vk);
                }
            }
            SynthClear();
        }

        bool EarlierMouseBlocks(int index, unsigned nowMs) noexcept
        {
            const Step& step = g_plan[index];
            for (int i = 0; i < g_count; ++i)
            {
                if (g_plan[i].op == kKey || g_plan[i].op == kText || g_plan[i].t > step.t
                    || (g_plan[i].t == step.t && i >= index)) continue;
                if (step.op != kDrag && g_plan[i].op != kDrag) continue;
                // A drag owns the pointer through its target release sample;
                // it also waits for earlier mouse steps and their last sample.
                // Include unstarted steps so pass 1 moves cannot jump past a
                // nominally earlier drag which pass 2 has not started yet.
                // Ordinary click/move pairs retain their existing semantics.
                if (!g_upDone[i] || nowMs - g_mouseStageTick[i] < kDragStageSpacingMs)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // JSON subset parser (spec 3.1). Hand-written descent over a grammar
        // whose depth is fixed by the subset itself (root -> steps array ->
        // step -> scalar), so the descent is plain function calls and no
        // input can drive it deeper.
        // ------------------------------------------------------------------

        void SkipWs(const char*& p)
        {
            while (*p == ' ' || *p == '\t' || *p == '\r' || *p == '\n')
            {
                ++p;
            }
        }

        bool ExpectChar(const char*& p, char expected)
        {
            SkipWs(p);
            if (*p != expected)
            {
                return false;
            }
            ++p;
            return true;
        }

        bool ParseInt(const char*& p, long long& out)
        {
            SkipWs(p);
            bool negative = false;
            if (*p == '-')
            {
                negative = true;
                ++p;
            }
            if (*p < '0' || *p > '9')
            {
                return false;  // no '+' sign and no bare '-'
            }
            if (p[0] == '0' && p[1] >= '0' && p[1] <= '9')
            {
                return false;  // no leading zeros
            }
            long long value = 0;
            while (*p >= '0' && *p <= '9')
            {
                value = value * 10 + (*p - '0');
                if (value > 2147483648LL)
                {
                    return false;  // the subset stays inside the int range
                }
                ++p;
            }
            if (!negative && value > 2147483647LL)
            {
                return false;
            }
            out = negative ? -value : value;
            return true;
        }

        bool ParseString(const char*& p, char* out, int cap)
        {
            // The opening quote is already consumed; parses up to the closing
            // quote. Return escapes reach the existing text/WM_CHAR path;
            // raw control bytes, other escapes and overflow remain rejected.
            int used = 0;
            while (*p != '"')
            {
                const unsigned char c = static_cast<unsigned char>(*p);
                if (c == 0 || c < 0x20)
                {
                    return false;
                }
                char decoded = 0;
                if (c == '\\')
                {
                    ++p;
                    if (*p == 'r') decoded='\r';
                    else if (*p == 'n') decoded='\n';
                    else if (*p == '\\' || *p == '"') decoded=*p;
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    decoded = static_cast<char>(c);
                }
                if (used >= cap - 1)
                {
                    return false;
                }
                out[used] = decoded;
                ++used;
                ++p;
            }
            ++p;  // closing quote
            out[used] = '\0';
            return true;
        }

        bool ParseStepBody(const char*& p, Step& step)
        {
            // The opening '{' is already consumed. t and op are mandatory;
            // every key is allowed once; unknown keys are rejected.
            bool seen[9] = {};  // t, op, vk, x, y, dur, chars, x2, y2
            SkipWs(p);
            if (*p == '}')
            {
                ++p;
                return false;  // a step without t and op is not a step
            }
            for (;;)
            {
                if (*p != '"')
                {
                    return false;
                }
                ++p;
                char key[8];  // the longest key, "chars", is five bytes
                if (!ParseString(p, key, 8))
                {
                    return false;
                }
                if (!ExpectChar(p, ':'))
                {
                    return false;
                }

                int slot = -1;
                if (std::strcmp(key, "t") == 0) slot = 0;
                else if (std::strcmp(key, "op") == 0) slot = 1;
                else if (std::strcmp(key, "vk") == 0) slot = 2;
                else if (std::strcmp(key, "x") == 0) slot = 3;
                else if (std::strcmp(key, "y") == 0) slot = 4;
                else if (std::strcmp(key, "dur") == 0) slot = 5;
                else if (std::strcmp(key, "chars") == 0) slot = 6;
                else if (std::strcmp(key, "x2") == 0) slot = 7;
                else if (std::strcmp(key, "y2") == 0) slot = 8;
                else return false;  // unknown step key
                if (seen[slot])
                {
                    return false;  // duplicate key
                }
                seen[slot] = true;

                if (slot == 1)
                {
                    SkipWs(p);
                    if (*p != '"')
                    {
                        return false;
                    }
                    ++p;
                    char word[8];  // "rclick" is the longest word
                    if (!ParseString(p, word, 8))
                    {
                        return false;
                    }
                    if (std::strcmp(word, "key") == 0) step.op = kKey;
                    else if (std::strcmp(word, "click") == 0) step.op = kClick;
                    else if (std::strcmp(word, "rclick") == 0) step.op = kRClick;
                    else if (std::strcmp(word, "drag") == 0) step.op = kDrag;
                    else if (std::strcmp(word, "move") == 0) step.op = kMove;
                    else if (std::strcmp(word, "text") == 0) step.op = kText;
                    else return false;
                }
                else if (slot == 6)
                {
                    SkipWs(p);
                    if (*p != '"')
                    {
                        return false;
                    }
                    ++p;
                    if (!ParseString(p, step.text, 64))
                    {
                        return false;
                    }
                }
                else
                {
                    long long value = 0;
                    if (!ParseInt(p, value))
                    {
                        return false;
                    }
                    if (slot == 0 && value < 0) return false;                     // t >= 0
                    if (slot == 2 && (value < 0 || value > 0xFFFF)) return false;  // VK range
                    if (slot == 5 && value < 0) return false;                     // dur >= 0
                    if (slot == 0) step.t = static_cast<int>(value);
                    else if (slot == 2) step.vk = static_cast<unsigned>(value);
                    else if (slot == 3) step.x = static_cast<int>(value);
                    else if (slot == 4) step.y = static_cast<int>(value);
                    else if (slot == 5) step.dur = static_cast<int>(value);
                    else if (slot == 7) step.x2 = static_cast<int>(value);
                    else if (slot == 8) step.y2 = static_cast<int>(value);
                }

                SkipWs(p);
                if (*p == ',')
                {
                    ++p;
                    SkipWs(p);
                    continue;
                }
                if (*p == '}')
                {
                    ++p;
                    break;
                }
                return false;
            }
            if (!seen[0] || !seen[1]) return false;
            if (step.op == kDrag)
                return seen[3] && seen[4] && seen[7] && seen[8] && !seen[2] && !seen[6];
            return !seen[7] && !seen[8];
        }

        bool ParseStepsArray(const char*& p, Step* out, int cap, int& count)
        {
            if (!ExpectChar(p, '['))
            {
                return false;
            }
            count = 0;
            SkipWs(p);
            if (*p == ']')
            {
                ++p;
                return true;
            }
            for (;;)
            {
                if (count >= cap)
                {
                    return false;  // more steps than the caller takes
                }
                if (!ExpectChar(p, '{'))
                {
                    return false;  // only objects are steps
                }
                if (!ParseStepBody(p, out[count]))
                {
                    return false;
                }
                ++count;
                SkipWs(p);
                if (*p == ',')
                {
                    ++p;
                    SkipWs(p);
                    continue;
                }
                if (*p == ']')
                {
                    ++p;
                    return true;
                }
                return false;
            }
        }

        bool BuildExeRelativePath(const wchar_t* fileName,wchar_t* out,int cap)
        {return dfl::client_paths::BuildClientPath(fileName,out,static_cast<std::size_t>(cap));}

        // Real-machine diagnostic: OutputDebugStringA is invisible without a
        // debugger-style listener, so the runbook evidence lives in a file
        // in the client Logs/DFLegacy directory. One short line per call, append mode.
        void DiagLine(const char* text) noexcept
        {
            wchar_t path[MAX_PATH] = {};
            if (!dfl::client_paths::BuildLogPath(L"auto-input-diag.log",path,MAX_PATH))
            {
                return;
            }
            const HANDLE handle = CreateFileW(
                path,
                FILE_APPEND_DATA,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                nullptr,
                OPEN_ALWAYS,
                FILE_ATTRIBUTE_NORMAL,
                nullptr);
            if (handle == INVALID_HANDLE_VALUE)
            {
                return;
            }
            SYSTEMTIME st{};
            GetLocalTime(&st);
            char line[160] = {};
            const int n = _snprintf_s(
                line,
                sizeof(line),
                _TRUNCATE,
                "%04u-%02u-%02u %02u:%02u:%02u.%03u [%lu] %s\r\n",
                st.wYear,
                st.wMonth,
                st.wDay,
                st.wHour,
                st.wMinute,
                st.wSecond,
                st.wMilliseconds,
                static_cast<unsigned long>(GetCurrentProcessId()),
                text);
            if (n > 0)
            {
                DWORD written = 0;
                WriteFile(handle, line, static_cast<DWORD>(n), &written, nullptr);
            }
            CloseHandle(handle);
        }

        struct FocusWindowCandidate
        {
            HWND window = nullptr;
            ULONGLONG area = 0;
        };

        BOOL CALLBACK FindOwnVisibleWindow(HWND window, LPARAM context) noexcept
        {
            DWORD ownerPid = 0;
            GetWindowThreadProcessId(window, &ownerPid);
            if (ownerPid != GetCurrentProcessId() || !IsWindowVisible(window)
                || GetWindow(window, GW_OWNER) != nullptr)
            {
                return TRUE;
            }
            RECT rect{};
            if (!GetClientRect(window, &rect) || rect.right <= rect.left
                || rect.bottom <= rect.top)
            {
                return TRUE;
            }
            auto& candidate = *reinterpret_cast<FocusWindowCandidate*>(context);
            const auto area = static_cast<ULONGLONG>(rect.right - rect.left)
                * static_cast<ULONGLONG>(rect.bottom - rect.top);
            if (candidate.window == nullptr || area > candidate.area)
            {
                candidate.window = window;
                candidate.area = area;
            }
            return TRUE;
        }

        bool OwnsForegroundWindow() noexcept
        {
            DWORD ownerPid = 0;
            GetWindowThreadProcessId(GetForegroundWindow(), &ownerPid);
            return ownerPid == GetCurrentProcessId();
        }

        // Activation belongs to the built-in input interface. The driver only
        // writes a PID request; it never synthesizes OS mouse/keyboard input.
        void PollFocusRequest() noexcept
        {
            static unsigned lastPollMs = 0;
            const unsigned now = GetTickCount();
            if (now - lastPollMs < 100) return;
            lastPollMs = now;

            wchar_t path[MAX_PATH]{};
            wchar_t donePath[MAX_PATH]{};
            if (!BuildExeRelativePath(L"auto-input.focus", path, MAX_PATH)
                || !BuildExeRelativePath(L"auto-input.focus.done", donePath, MAX_PATH))
            {
                return;
            }
            const HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ,
                nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (file == INVALID_HANDLE_VALUE) return;
            LARGE_INTEGER size{};
            char text[32]{};
            DWORD read = 0;
            const bool valid = GetFileSizeEx(file, &size) != FALSE
                && size.QuadPart > 0 && size.QuadPart <= sizeof(text)
                && ReadFile(file, text, static_cast<DWORD>(size.QuadPart), &read, nullptr) != FALSE
                && read == static_cast<DWORD>(size.QuadPart)
                && IsFocusRequestForProcess(text, read, GetCurrentProcessId());
            CloseHandle(file);
            if (!valid) return;

            if (!OwnsForegroundWindow())
            {
                FocusWindowCandidate candidate;
                EnumWindows(FindOwnVisibleWindow, reinterpret_cast<LPARAM>(&candidate));
                if (candidate.window == nullptr) return;
                ShowWindow(candidate.window, SW_RESTORE);
                SetForegroundWindow(candidate.window);
                if (!OwnsForegroundWindow())
                {
                    const DWORD currentThread = GetCurrentThreadId();
                    const DWORD foregroundThread = GetWindowThreadProcessId(
                        GetForegroundWindow(), nullptr);
                    const bool attached = foregroundThread != 0
                        && foregroundThread != currentThread
                        && AttachThreadInput(currentThread, foregroundThread, TRUE) != FALSE;
                    BringWindowToTop(candidate.window);
                    SetActiveWindow(candidate.window);
                    SetForegroundWindow(candidate.window);
                    if (attached) AttachThreadInput(currentThread, foregroundThread, FALSE);
                }
            }
            if (OwnsForegroundWindow()
                && MoveFileExW(path, donePath, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
            {
                DiagLine("focus request completed");
            }
        }

        // The game stops polling/rendering while unfocused. Its UI message
        // loop still dispatches timers, so activation cannot depend on Pump.
        // This callback runs on the installing game thread and never advances
        // a timeline or reads a plan file.
        void CALLBACK FocusMailboxTimerProc(HWND, UINT, UINT_PTR, DWORD) noexcept
        {
            if (g_inTimeline) return;
            const TimelineGuard guard;
            PollFocusRequest();
        }
    }

    bool Install() noexcept
    {
        // dnf_patches gates this call on EnableAutoInput; the check here
        // keeps the module inert even if that gate ever changes.
        if (!IsEnabled())
        {
            return true;
        }
        // A second pass would capture the hook itself as the "original" and
        // make a later Restore permanentize the hook.
        if (g_keyboardHooked)
        {
            return true;
        }

        void* current = nullptr;
        if (!ReadIatPointer(kGetAsyncKeyStateIat, current) || current == nullptr)
        {
            OutputDebugStringA(
                "auto_input: GetAsyncKeyState IAT slot is not readable or null; "
                "keyboard hook not installed\n");
            return false;
        }
        g_originalGetAsyncKeyState = reinterpret_cast<GetAsyncKeyStateFn>(current);

        const auto hook = reinterpret_cast<void*>(&HookedGetAsyncKeyState);
        if (!dfl::memory_patch::WriteBytes(
                kGetAsyncKeyStateIat,
                &hook,
                sizeof(hook)))
        {
            // memory_patch can report failure after the bytes actually
            // landed (its protection-restore step can fail). Re-read the
            // slot: a live hook must never be left behind unowned, because
            // it answers 0 for everything the synth tables do not own and
            // Restore would then skip the write-back.
            void* after = nullptr;
            if (!(ReadIatPointer(kGetAsyncKeyStateIat, after) && after == hook))
            {
                g_originalGetAsyncKeyState = nullptr;
                OutputDebugStringA(
                    "auto_input: swapping the GetAsyncKeyState IAT entry failed\n");
                return false;
            }
            OutputDebugStringA(
                "auto_input: IAT write reported failure but the hook is in "
                "place; treating the install as done\n");
        }
        g_keyboardHooked = true;

        // Mouse injection is best-effort: an unreadable NativeMouseInput only
        // switches the click primitives back to the injection log.
        g_mouseInjectEnabled =
            IsReadable(reinterpret_cast<const void*>(kNativeMouseInput), 1);
        if (!g_mouseInjectEnabled)
        {
            OutputDebugStringA(
                "auto_input: NativeMouseInput is not readable; click primitives "
                "stay in the injection log\n");
        }

        // DirectInput keyboard: the game reads key state via the keyboard
        // device's GetDeviceState. Hook the DirectInput8Create IAT so device
        // creation is wrapped; failure degrades (movement keys stay unmapped,
        // everything else keeps working).
        void* di8Current = nullptr;
        if (ReadIatPointer(kDirectInput8CreateIat, di8Current)
            && di8Current != nullptr)
        {
            g_originalDirectInput8Create =
                reinterpret_cast<DirectInput8CreateFn>(di8Current);
            const auto di8Hook =
                reinterpret_cast<void*>(&DirectInput8CreateProxy);
            if (dfl::memory_patch::WriteBytes(
                    kDirectInput8CreateIat,
                    &di8Hook,
                    sizeof(di8Hook)))
            {
                DiagLine("DirectInput8Create IAT hooked");
            }
            else
            {
                // Same failure semantics as the GetAsyncKeyState slot: re-read
                // before giving up, because a live hook must never be left
                // behind unowned (Restore would then skip the write-back and
                // the proxy would answer for the rest of the session).
                void* after = nullptr;
                if (ReadIatPointer(kDirectInput8CreateIat, after)
                    && after == di8Hook)
                {
                    DiagLine("DirectInput8Create IAT hooked (write reported failure)");
                }
                else
                {
                    g_originalDirectInput8Create = nullptr;
                    DiagLine("DirectInput8Create IAT write FAILED: no DI proxy");
                }
            }
        }
        else
        {
            DiagLine("DirectInput8Create IAT unreadable: no DI proxy");
        }

        // A missing or invalid plan file is not an install failure: the
        // client simply runs without a scripted timeline.
        LoadPlanFile();
        g_probePending = true;  // deferred to the first Pump, outside DllMain
        {
            static bool crashProbeInstalled = false;
            if (!crashProbeInstalled)
            {
                crashProbeInstalled = true;
                AddVectoredExceptionHandler(1, &CrashProbe);
            }
        }
        OutputDebugStringA("auto_input: keyboard hook installed\n");
        DiagLine("keyboard hook installed");
        g_focusMailboxTimer = SetTimer(nullptr, 0, 100, &FocusMailboxTimerProc);
        DiagLine(g_focusMailboxTimer != 0
            ? "focus mailbox timer installed"
            : "focus mailbox timer install FAILED");
        return true;
    }

    void Restore() noexcept
    {
        // Release through NativeMouseInput while it is still enabled, before
        // unhooking/disabling the input bridge. Stop the old timeline first.
        CancelPlan();
        if (g_focusMailboxTimer != 0)
        {
            KillTimer(nullptr, g_focusMailboxTimer);
            g_focusMailboxTimer = 0;
        }
        if (g_keyboardHooked)
        {
            const auto original =
                reinterpret_cast<void*>(g_originalGetAsyncKeyState);
            if (!dfl::memory_patch::WriteBytes(
                    kGetAsyncKeyStateIat,
                    &original,
                    sizeof(original)))
            {
                OutputDebugStringA(
                    "auto_input: restoring the GetAsyncKeyState IAT failed\n");
            }
            g_keyboardHooked = false;
        }
        if (!g_fakeKeyboardInUse)
        {
            // Only drop the physical-keyboard entry point when nothing else
            // still needs it. Once the synthetic device has been handed to the
            // game it keeps calling GetAsyncKeyState through this pointer for
            // the rest of the session, so nulling it here would silently turn
            // the player's own keyboard into "no keys down".
            g_originalGetAsyncKeyState = nullptr;
        }
        g_mouseInjectEnabled = false;
        if (g_originalDirectInput8Create != nullptr)
        {
            const auto original =
                reinterpret_cast<void*>(g_originalDirectInput8Create);
            if (!dfl::memory_patch::WriteBytes(
                    kDirectInput8CreateIat,
                    &original,
                    sizeof(original)))
            {
                OutputDebugStringA(
                    "auto_input: restoring the DirectInput8Create IAT failed\n");
            }
            g_originalDirectInput8Create = nullptr;
        }
        if (g_di8CreateSlot != nullptr && g_originalCreateDevice != nullptr)
        {
            // Put the host's own CreateDevice entry back: the slot lives in
            // dinput8's image and is shared by every DirectInput8 object, so a
            // leftover redirect would outlive Restore for the whole process.
            if (!PatchVtableEntry(
                    g_di8CreateSlot,
                    reinterpret_cast<void*>(g_originalCreateDevice)))
            {
                OutputDebugStringA(
                    "auto_input: restoring the CreateDevice vtable slot failed\n");
            }
            g_di8CreateSlot = nullptr;
            g_originalCreateDevice = nullptr;
        }
        if (g_keyboardStateSlot != nullptr && g_originalGetDeviceState != nullptr)
        {
            // Put the host's own entry back: the slot lives in dinput8's image
            // and is shared, so a leftover redirect would outlive Restore.
            if (!PatchVtableEntry(
                    g_keyboardStateSlot,
                    reinterpret_cast<void*>(g_originalGetDeviceState)))
            {
                OutputDebugStringA(
                    "auto_input: restoring the GetDeviceState vtable slot failed\n");
            }
        }
        g_keyboardStateSlot = nullptr;
        g_originalGetDeviceState = nullptr;
        SynthClear();
    }

    void Pump() noexcept
    {
        // Delivering a key is a synchronous SendMessage, so the game's own
        // message handling runs inside Advance() -- and that handling can call
        // GetAsyncKeyState, which is this module's pump anchor. Without this
        // latch the nested pump would advance the same timeline from inside its
        // own edge delivery (observed as an unbounded recursion and a stack
        // overflow, 2026-10-23 13:34:34).
        if (g_inTimeline)
        {
            return;
        }
        // This mailbox is checked even by an unfocused process. Keep the
        // re-entry latch while window activation dispatches game messages.
        {
            const TimelineGuard guard;
            PollFocusRequest();
        }
        // The game polls GetAsyncKeyState only while its window is
        // foreground, so the module mirrors that: an unfocused client must
        // neither consume a shared plan file (dual-client acceptance runs
        // share one exe directory) nor fire timeline edges. This also keeps
        // the texture-proxy anchor from pumping a background client.
        HWND foreground = GetForegroundWindow();
        DWORD foregroundPid = 0;
        GetWindowThreadProcessId(foreground, &foregroundPid);
        if (foregroundPid != GetCurrentProcessId())
        {
            return;
        }
        g_gameWindow = foreground;
        g_keyboardTarget = GetFocus();
        if (g_keyboardTarget == nullptr)
        {
            g_keyboardTarget = foreground;
        }

        if (g_probePending)
        {
            g_probePending = false;
            ProbeHostKeyboard();
        }

        // One-shot liveness marker: the real-machine runbook uses it to prove
        // this frame path executes (file diagnostic; see DiagLine).
        static bool announced = false;
        if (!announced)
        {
            announced = true;
            OutputDebugStringA("auto_input: pump first call\n");
            DiagLine("pump first call");
        }

        // The platform writes the instruction file after the client is
        // already up, so an idle (or finished) module polls for it every
        // frame -- spec 3.2 mandates the per-frame instruction-file polling.
        // This Pump can be called many times per frame (it rides the texture
        // state proxy as well as the performance call site), so the file
        // probe is throttled to one attempt every 50 ms.
        //
        // A finished plan whose .done rename failed would otherwise be
        // re-loaded and re-executed every 50 ms (the file is still at
        // auto-input.json) -- spec 3.1 forbids repeating a plan, so a
        // latched failure stops the polling until Restore or a successful
        // later rename.
        if (g_phase == Phase::Finished && g_doneMarked && !g_renamedCleanly)
        {
            return;
        }
        if (g_phase != Phase::Armed)
        {
            static unsigned lastPollMs = 0;
            const unsigned now = GetTickCount();
            if (now - lastPollMs >= 50)
            {
                lastPollMs = now;
                LoadPlanFile();
            }
        }
        // The call site in performance_overlay.cpp gates on IsEnabled(); a
        // disarmed module returns from Advance immediately.
        const TimelineGuard guard;
        Advance(GetTickCount());
    }

    bool ParsePlan(const char* text, Step* steps, int maxSteps, int* count, int* version)
    {
        if (text == nullptr || steps == nullptr || maxSteps <= 0
            || count == nullptr || version == nullptr)
        {
            return false;
        }

        // Parse into local storage first: a rejected plan must not touch the
        // caller's buffers. The capacity is the smaller of the caller's limit
        // and the module's own maximum.
        Step local[kMaxSteps];
        const int capacity = maxSteps < kMaxSteps ? maxSteps : kMaxSteps;
        int parsed = 0;
        long long parsedVersion = 0;
        bool hasVersion = false;
        bool hasSteps = false;

        const char* p = text;
        if (!ExpectChar(p, '{'))
        {
            return false;
        }
        SkipWs(p);
        if (*p == '"')
        {
            for (;;)
            {
                if (*p != '"')
                {
                    return false;
                }
                ++p;
                char key[10];  // "version" is the longest root key
                if (!ParseString(p, key, 10))
                {
                    return false;
                }
                if (!ExpectChar(p, ':'))
                {
                    return false;
                }
                if (std::strcmp(key, "version") == 0)
                {
                    if (hasVersion)
                    {
                        return false;
                    }
                    hasVersion = true;
                    if (!ParseInt(p, parsedVersion))
                    {
                        return false;
                    }
                }
                else if (std::strcmp(key, "steps") == 0)
                {
                    if (hasSteps)
                    {
                        return false;
                    }
                    hasSteps = true;
                    if (!ParseStepsArray(p, local, capacity, parsed))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;  // unknown root key
                }
                SkipWs(p);
                if (*p == ',')
                {
                    ++p;
                    SkipWs(p);
                    continue;
                }
                if (*p == '}')
                {
                    ++p;
                    break;
                }
                return false;
            }
        }
        else if (!ExpectChar(p, '}'))
        {
            return false;  // empty root: both mandatory keys are missing
        }

        if (!hasVersion || !hasSteps)
        {
            return false;
        }
        if (parsedVersion != 1)
        {
            return false;  // exactly one plan version exists
        }
        SkipWs(p);
        if (*p != '\0')
        {
            return false;  // trailing garbage
        }

        for (int i = 0; i < parsed; ++i)
        {
            steps[i] = local[i];
        }
        *count = parsed;
        *version = static_cast<int>(parsedVersion);
        return true;
    }

    int Advance(unsigned nowMs)
    {
        if (g_phase != Phase::Armed)
        {
            return 0;
        }

        const unsigned elapsed = nowMs - g_origin;  // GetTickCount wrap-safe
        int edges = 0;

        // Pass 1 -- ups (and the move primitive): a repeated key's release
        // must land before the next press of the same key, so everything due
        // to be released this frame is processed before any press.
        for (int i = 0; i < g_count; ++i)
        {
            const Step& step = g_plan[i];
            if (step.op != kKey && step.op != kText && EarlierMouseBlocks(i, nowMs))
                continue;
            if (step.op == kKey)
            {
                if (!g_upDone[i] && g_downDone[i]
                    && elapsed >= static_cast<unsigned>(step.t) + static_cast<unsigned>(step.dur))
                {
                    g_upDone[i] = true;
                    if (step.vk < 256)
                    {
                        g_synthDown[step.vk] = false;
                    }
                    EmitKey(WM_KEYUP, step.vk);
                    ++edges;
                }
            }
            else if (step.op == kText)
            {
                const int chars = static_cast<int>(std::strlen(step.text));
                int due = 0;
                if (elapsed >= static_cast<unsigned>(step.t) + kTextSpacingMs)
                {
                    due = static_cast<int>(
                        (elapsed - static_cast<unsigned>(step.t) - kTextSpacingMs)
                        / kTextSpacingMs) + 1;
                }
                if (due > chars)
                {
                    due = chars;
                }
                while (g_textUps[i] < due && g_textUps[i] < g_textDowns[i])
                {
                    // The down bound keeps a catch-up pump (a loading screen
                    // can stall the frame hook for seconds) from releasing a
                    // character that was never pressed; the release lands on
                    // the next pump instead.
                    //
                    // The index advances BEFORE the emit: delivering a message
                    // is a synchronous call back into the game, and the game's
                    // key path can call GetAsyncKeyState -- which is this
                    // module's pump anchor. With the counter still pointing at
                    // this character, that re-entry re-emitted it and recursed
                    // until the stack ran out (VEH 0xC00000FD at the game's
                    // 512-byte frame, 2026-10-23 13:34:34).
                    const int index = g_textUps[i]++;
                    const unsigned char vk = g_textVk[i][index];
                    if (vk != 0)
                    {
                        g_synthDown[vk] = false;
                        EmitKey(WM_KEYUP, vk);
                        ++edges;
                    }
                }
            }
            else if (step.op == kClick || step.op == kRClick)
            {
                if (!g_upDone[i] && g_downDone[i]
                    && elapsed >= static_cast<unsigned>(step.t) + static_cast<unsigned>(step.dur))
                {
                    g_upDone[i] = true;
                    g_mouseStageTick[i] = nowMs;
                    EmitMouse(step.op == kRClick ? kMouseRightUp : kMouseUp, step.x, step.y);
                }
            }
            else if (step.op == kDrag)
            {
                if (!g_downDone[i] || g_upDone[i]
                    || nowMs - g_mouseStageTick[i] < kDragStageSpacingMs) continue;
                if (!g_dragMoveDone[i]
                    && elapsed >= static_cast<unsigned>(step.t) + static_cast<unsigned>(step.dur) / 2u)
                {
                    g_dragMoveDone[i] = true;
                    g_mouseStageTick[i] = nowMs;
                    EmitMouse(kMouseMove, step.x2, step.y2);
                }
                else if (g_dragMoveDone[i]
                    && elapsed >= static_cast<unsigned>(step.t) + static_cast<unsigned>(step.dur))
                {
                    g_upDone[i] = true;
                    g_mouseStageTick[i] = nowMs;
                    EmitMouse(kMouseUp, step.x2, step.y2);
                }
            }
            else  // kMove: the move is this step's single primitive
            {
                if (!g_upDone[i] && elapsed >= static_cast<unsigned>(step.t))
                {
                    g_upDone[i] = true;
                    g_mouseStageTick[i] = nowMs;
                    EmitMouse(kMouseMove, step.x, step.y);
                }
            }
        }

        // Pass 2 -- downs. A zero-duration step (its up due the moment it
        // fires) reads as pressed for this frame and releases on the next
        // pump, which also keeps the click's down and up one frame apart.
        for (int i = 0; i < g_count; ++i)
        {
            const Step& step = g_plan[i];
            if (step.op != kKey && step.op != kText && EarlierMouseBlocks(i, nowMs))
                continue;
            if (step.op == kKey)
            {
                if (!g_downDone[i] && elapsed >= static_cast<unsigned>(step.t))
                {
                    g_downDone[i] = true;
                    if (step.vk < 256)
                    {
                        g_synthDown[step.vk] = true;
                        g_synthEver[step.vk] = true;
                    }
                    EmitKey(WM_KEYDOWN, step.vk);
                    ++edges;
                }
            }
            else if (step.op == kText)
            {
                const int chars = static_cast<int>(std::strlen(step.text));
                int due = 0;
                if (elapsed >= static_cast<unsigned>(step.t))
                {
                    due = static_cast<int>(
                        (elapsed - static_cast<unsigned>(step.t)) / kTextSpacingMs) + 1;
                }
                if (due > chars)
                {
                    due = chars;
                }
                while (g_textDowns[i] < due)
                {
                    // Same rule as the release loop above: advance the index
                    // before the synchronous send, or a re-entrant pump repeats
                    // this character forever.
                    const int index = g_textDowns[i]++;
                    const unsigned char vk = g_textVk[i][index];
                    if (vk != 0)
                    {
                        g_synthDown[vk] = true;
                        g_synthEver[vk] = true;
                        EmitKey(WM_KEYDOWN, vk);
                        EmitKey(WM_CHAR, g_textChar[i][index]);
                        ++edges;
                    }
                }
            }
            else if (step.op == kClick || step.op == kRClick || step.op == kDrag)
            {
                if (!g_downDone[i] && elapsed >= static_cast<unsigned>(step.t))
                {
                    g_downDone[i] = true;
                    if (step.op == kDrag) g_mouseStageTick[i] = nowMs;
                    EmitMouse(kMouseMove, step.x, step.y);
                    EmitMouse(step.op == kRClick ? kMouseRightDown : kMouseDown, step.x, step.y);
                }
            }
            // kMove was handled in pass 1.
        }

        // Pass 3 -- Finished once every step completed and no up is pending.
        // The .done rename fires exactly once and only for plans loaded from
        // the file (tests arm without the file system).
        bool complete = true;
        for (int i = 0; i < g_count && complete; ++i)
        {
            if (g_plan[i].op == kText)
            {
                complete = g_textUps[i] == static_cast<int>(std::strlen(g_plan[i].text));
            }
            else
            {
                complete = g_upDone[i];
            }
        }
        if (complete)
        {
            g_phase = Phase::Finished;
            // The plan no longer owns any key: release the ever-set so the
            // hook forwards the real keyboard again until the next arm.
            SynthClear();
            // Restart the VK-polling sample so the next dump reflects the
            // post-plan screen (town idle polling, movement keys included).
            for (int i = 0; i < 256; ++i)
            {
                g_vkSample[i] = 0;
            }
            g_vkSampleTotal = 0;
            g_vkSampleDumped = false;
            if (g_fromFile && !g_doneMarked)
            {
                g_doneMarked = true;  // fire once, whatever the rename does
                if (GetFileAttributesW(g_donePath) != INVALID_FILE_ATTRIBUTES)
                {
                    DeleteFileW(g_donePath);
                }
                g_renamedCleanly = MoveFileW(g_planPath, g_donePath) != FALSE;
                if (!g_renamedCleanly)
                {
                    // Two clients can share one exe directory (Tier 2/3 dual
                    // client runs): whoever finishes first wins the rename and
                    // the loser finds its source file already gone. That is a
                    // benign race -- this client executed the very same plan --
                    // so only a failure with the source still present means a
                    // stuck plan worth suppressing the poll for.
                    if (GetFileAttributesW(g_planPath) == INVALID_FILE_ATTRIBUTES)
                    {
                        g_renamedCleanly = true;
                        OutputDebugStringA("auto_input: another client consumed the shared plan file");
                    }
                    else
                    {
                        OutputDebugStringA("auto_input: renaming auto-input.json to .done failed");
                        DiagLine("plan finished but the .done rename FAILED; polling suppressed");
                    }
                }
            }
        }
        return edges;
    }

    short QueryKey(unsigned vk, short original)
    {
        if (vk < 256 && g_synthDown[vk])
        {
            return static_cast<short>(0x8000);
        }
        return original;
    }

    bool LoadPlanFile()
    {
        wchar_t planPath[MAX_PATH] = {};
        wchar_t donePath[MAX_PATH] = {};
        if (!BuildExeRelativePath(L"auto-input.json", planPath, MAX_PATH)
            || !BuildExeRelativePath(L"auto-input.done.json", donePath, MAX_PATH))
        {
            return false;
        }

        const HANDLE handle = CreateFileW(
            planPath,
            GENERIC_READ,
            FILE_SHARE_READ,
            nullptr,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (handle == INVALID_HANDLE_VALUE)
        {
            const DWORD error = GetLastError();
            if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND)
                CancelPlan();
            return false;
        }

        LARGE_INTEGER size{};
        bool ok = GetFileSizeEx(handle, &size) != FALSE
            && size.QuadPart > 0
            && size.QuadPart <= kMaxPlanBytes;
        std::vector<char> text;
        if (ok)
        {
            text.resize(static_cast<size_t>(size.QuadPart) + 1);
            DWORD read = 0;
            ok = ReadFile(
                handle,
                text.data(),
                static_cast<DWORD>(size.QuadPart),
                &read,
                nullptr) != FALSE
                && read == static_cast<DWORD>(size.QuadPart);
        }
        CloseHandle(handle);
        if (!ok)
        {
            CancelPlan();
            return false;
        }
        text[static_cast<size_t>(size.QuadPart)] = '\0';

        Step local[kMaxSteps];
        int count = 0;
        int version = 0;
        if (!ParsePlan(text.data(), local, kMaxSteps, &count, &version))
        {
            CancelPlan();
            return false;  // only a valid plan reaches the armed state
        }
        if (!ArmPlan(local, count, version, GetTickCount()))
        {
            return false;
        }
        g_fromFile = true;  // ArmPlan cleared it; this plan came from the file
        std::memcpy(g_planPath, planPath, sizeof(g_planPath));
        std::memcpy(g_donePath, donePath, sizeof(g_donePath));
        // Taking a new plan consumes the previous plan's completion marker:
        // the platform's wait_ready() (marker gone = taken) is defined on
        // this clear, and the marker of a finished previous plan is stale
        // evidence at this point.
        DeleteFileW(donePath);
        OutputDebugStringA("auto_input: plan armed\n");
        {
            char note[32] = {};
            if (_snprintf_s(note, sizeof(note), _TRUNCATE, "plan armed (%d steps)", count) > 0)
            {
                DiagLine(note);
            }
        }
        return true;
    }

    bool ArmPlan(const Step* steps, int count, int version, unsigned t0Ms)
    {
        if (steps == nullptr || count < 0 || count > kMaxSteps || version != 1)
        {
            CancelPlan();
            return false;
        }

        for (int i = 0; i < count; ++i)
        {
            if (steps[i].op < kKey || steps[i].op > kDrag || steps[i].t < 0 || steps[i].dur < 0
                || (steps[i].op == kKey && steps[i].vk > 0xFFFFu))
            {
                CancelPlan();
                return false;
            }
        }
        CancelPlan();

        for (int i = 0; i < count; ++i)
        {
            g_plan[i] = steps[i];
            g_downDone[i] = false;
            g_upDone[i] = false;
            g_dragMoveDone[i] = false;
            g_mouseStageTick[i] = 0;
            g_textDowns[i] = 0;
            g_textUps[i] = 0;
            for (int j = 0; j < 64; ++j)
            {
                g_textVk[i][j] = 0;
                g_textChar[i][j] = 0;
            }
            if (g_plan[i].op == kText)
            {
                g_plan[i].text[63] = '\0';  // defensive: caller arrays may be unterminated
                for (int j = 0; j < 64; ++j)
                {
                    const char c = g_plan[i].text[j];
                    if (c == '\0')
                    {
                        break;
                    }
                    g_textVk[i][j] = VkFromChar(c);
                    g_textChar[i][j] = static_cast<unsigned char>(c);
                }
            }
        }
        for (int i = count; i < kMaxSteps; ++i)
        {
            g_downDone[i] = false;
            g_upDone[i] = false;
            g_dragMoveDone[i] = false;
            g_mouseStageTick[i] = 0;
            g_textDowns[i] = 0;
            g_textUps[i] = 0;
        }
        for (int i = 0; i < 256; ++i)
        {
            g_synthDown[i] = false;  // a fresh plan starts with a clear table
            g_synthEver[i] = false;
        }

        g_count = count;
        g_version = version;
        g_origin = t0Ms;
        g_fromFile = false;
        g_doneMarked = false;
        g_renamedCleanly = true;
        g_phase = Phase::Armed;
        return true;
    }

    bool IsFinished()
    {
        return g_phase == Phase::Finished;
    }

    int GetInjectionLog(InjectionRecord* out, int maxRecords)
    {
        if (out == nullptr || maxRecords <= 0)
        {
            return 0;
        }
        const int available = g_logTotal < kLogCapacity ? g_logTotal : kLogCapacity;
        const int copied = available < maxRecords ? available : maxRecords;
        const int start = g_logTotal <= kLogCapacity ? 0 : g_logHead;
        for (int i = 0; i < copied; ++i)
        {
            out[i] = g_log[(start + i) % kLogCapacity];
        }
        return copied;
    }

    void ClearInjectionLog()
    {
        g_logHead = 0;
        g_logTotal = 0;
    }

    unsigned char MapVkToDik(unsigned vk) noexcept
    {
        // Thin public face on the translation the injection path uses, so the
        // DIK rule is pinned by tests instead of by "the character moved".
        return DikFromVk(vk);
    }

    void BuildKeyboardState(unsigned char* keys) noexcept
    {
        if (keys == nullptr)
        {
            return;
        }
        MergeKeyboardState(keys);
    }
}
