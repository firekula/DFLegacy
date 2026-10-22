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
    bool IsEnabled() noexcept
    {
        return dfl::ijl15_config::Get().EnableAutoInput;
    }

    // ------------------------------------------------------------------
    // Core state. Everything lives in these translation-unit locals and is
    // only touched from the game's main thread (no new threads; spec 3.3).
    // ------------------------------------------------------------------
    namespace
    {
        constexpr int kLogCapacity = 64;
        constexpr unsigned kTextSpacingMs = 40;  // brief: text downs >= 40 ms apart
        constexpr long long kMaxPlanBytes = 1024 * 1024;

        enum class Phase
        {
            Idle,
            Armed,
            Finished,
        };

        Phase g_phase = Phase::Idle;
        Step g_plan[kMaxSteps];
        int g_count = 0;
        int g_version = 0;
        unsigned g_origin = 0;
        bool g_fromFile = false;
        bool g_doneMarked = false;
        bool g_renamedCleanly = true;  // a failed .done rename stops the poll
        char g_planPath[MAX_PATH] = {};
        char g_donePath[MAX_PATH] = {};

        bool g_downDone[kMaxSteps] = {};
        bool g_upDone[kMaxSteps] = {};   // key/click: up fired; move: the move fired
        int g_textDowns[kMaxSteps] = {};
        int g_textUps[kMaxSteps] = {};
        unsigned char g_textVk[kMaxSteps][64] = {};  // per-char VK codes; 0 = no key

        bool g_synthDown[256] = {};
        bool g_synthEver[256] = {};  // keys this module synthesized at least once

        InjectionRecord g_log[kLogCapacity] = {};
        int g_logHead = 0;
        int g_logTotal = 0;

        void LogPrimitive(int kind, unsigned vk, int x, int y)
        {
            g_log[g_logHead].op = kind;
            g_log[g_logHead].vk = vk;
            g_log[g_logHead].x = x;
            g_log[g_logHead].y = y;
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

        GetAsyncKeyStateFn g_originalGetAsyncKeyState = nullptr;
        bool g_keyboardHooked = false;
        bool g_mouseInjectEnabled = false;

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
            fn(msg, msg == 513 ? 1 : 0, PackMouseLParam(x, y));  // wParam: MK_LBUTTON only for down
        }

        // Real calls replace the injection log only when Install verified the
        // NativeMouseInput address; otherwise (tests, unexpected layouts) the
        // primitives stay observable in the log.
        void EmitMouse(int kind, int x, int y) noexcept
        {
            if (!g_mouseInjectEnabled)
            {
                LogPrimitive(kind, 0, x, y);
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
            else
            {
                return;
            }
            InjectMouse(msg, x, y);
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
            // quote. Only \\ and \" exist as escapes; raw control bytes and
            // anything past the capacity are rejected.
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
                    if (*p != '\\' && *p != '"')
                    {
                        return false;
                    }
                    decoded = *p;
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
            bool seen[7] = {};  // t, op, vk, x, y, dur, chars
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
                    char word[8];  // "click" is the longest word
                    if (!ParseString(p, word, 8))
                    {
                        return false;
                    }
                    if (std::strcmp(word, "key") == 0) step.op = kKey;
                    else if (std::strcmp(word, "click") == 0) step.op = kClick;
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
                    else step.dur = static_cast<int>(value);
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
            return seen[0] && seen[1];  // t and op are mandatory
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

        bool BuildExeRelativePath(const char* fileName, char* out, int cap)
        {
            char exe[MAX_PATH] = {};
            const DWORD written = GetModuleFileNameA(nullptr, exe, MAX_PATH);
            if (written == 0 || written >= MAX_PATH)
            {
                return false;
            }
            int cut = -1;
            for (DWORD i = 0; i < written; ++i)
            {
                if (exe[i] == '\\' || exe[i] == '/')
                {
                    cut = static_cast<int>(i);
                }
            }
            if (cut < 0)
            {
                return false;
            }
            const int dirLen = cut + 1;  // keep the separator itself
            const int nameLen = static_cast<int>(std::strlen(fileName));
            if (dirLen + nameLen + 1 > cap)
            {
                return false;
            }
            std::memcpy(out, exe, static_cast<size_t>(dirLen));
            std::memcpy(out + dirLen, fileName, static_cast<size_t>(nameLen) + 1);
            return true;
        }

        // Real-machine diagnostic: OutputDebugStringA is invisible without a
        // debugger-style listener, so the runbook evidence lives in a file
        // beside DNF.exe instead. One short line per call, append mode.
        void DiagLine(const char* text) noexcept
        {
            char path[MAX_PATH] = {};
            if (!BuildExeRelativePath("auto-input-diag.log", path, MAX_PATH))
            {
                return;
            }
            const HANDLE handle = CreateFileA(
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
                "%04u-%02u-%02u %02u:%02u:%02u.%03u %s\r\n",
                st.wYear,
                st.wMonth,
                st.wDay,
                st.wHour,
                st.wMinute,
                st.wSecond,
                st.wMilliseconds,
                text);
            if (n > 0)
            {
                DWORD written = 0;
                WriteFile(handle, line, static_cast<DWORD>(n), &written, nullptr);
            }
            CloseHandle(handle);
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

        // A missing or invalid plan file is not an install failure: the
        // client simply runs without a scripted timeline.
        LoadPlanFile();
        OutputDebugStringA("auto_input: keyboard hook installed\n");
        DiagLine("keyboard hook installed");
        return true;
    }

    void Restore() noexcept
    {
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
        g_originalGetAsyncKeyState = nullptr;
        g_mouseInjectEnabled = false;
        SynthClear();
    }

    void Pump() noexcept
    {
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
                    const unsigned char vk = g_textVk[i][g_textUps[i]];
                    if (vk != 0)
                    {
                        g_synthDown[vk] = false;
                        ++edges;
                    }
                    ++g_textUps[i];
                }
            }
            else if (step.op == kClick)
            {
                if (!g_upDone[i] && g_downDone[i]
                    && elapsed >= static_cast<unsigned>(step.t) + static_cast<unsigned>(step.dur))
                {
                    g_upDone[i] = true;
                    EmitMouse(kMouseUp, step.x, step.y);
                }
            }
            else  // kMove: the move is this step's single primitive
            {
                if (!g_upDone[i] && elapsed >= static_cast<unsigned>(step.t))
                {
                    g_upDone[i] = true;
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
                    const unsigned char vk = g_textVk[i][g_textDowns[i]];
                    if (vk != 0)
                    {
                        g_synthDown[vk] = true;
                        g_synthEver[vk] = true;
                        ++edges;
                    }
                    ++g_textDowns[i];
                }
            }
            else if (step.op == kClick)
            {
                if (!g_downDone[i] && elapsed >= static_cast<unsigned>(step.t))
                {
                    g_downDone[i] = true;
                    EmitMouse(kMouseMove, step.x, step.y);
                    EmitMouse(kMouseDown, step.x, step.y);
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
            if (g_fromFile && !g_doneMarked)
            {
                g_doneMarked = true;  // fire once, whatever the rename does
                if (GetFileAttributesA(g_donePath) != INVALID_FILE_ATTRIBUTES)
                {
                    DeleteFileA(g_donePath);
                }
                g_renamedCleanly = MoveFileA(g_planPath, g_donePath) != FALSE;
                if (!g_renamedCleanly)
                {
                    OutputDebugStringA("auto_input: renaming auto-input.json to .done failed");
                    DiagLine("plan finished but the .done rename FAILED; polling suppressed");
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
        char planPath[MAX_PATH] = {};
        char donePath[MAX_PATH] = {};
        if (!BuildExeRelativePath("auto-input.json", planPath, MAX_PATH)
            || !BuildExeRelativePath("auto-input.done.json", donePath, MAX_PATH))
        {
            return false;
        }

        const HANDLE handle = CreateFileA(
            planPath,
            GENERIC_READ,
            FILE_SHARE_READ,
            nullptr,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (handle == INVALID_HANDLE_VALUE)
        {
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
            return false;
        }
        text[static_cast<size_t>(size.QuadPart)] = '\0';

        Step local[kMaxSteps];
        int count = 0;
        int version = 0;
        if (!ParsePlan(text.data(), local, kMaxSteps, &count, &version))
        {
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
        DeleteFileA(donePath);
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
            return false;
        }

        for (int i = 0; i < count; ++i)
        {
            g_plan[i] = steps[i];
            g_downDone[i] = false;
            g_upDone[i] = false;
            g_textDowns[i] = 0;
            g_textUps[i] = 0;
            for (int j = 0; j < 64; ++j)
            {
                g_textVk[i][j] = 0;
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
                }
            }
        }
        for (int i = count; i < kMaxSteps; ++i)
        {
            g_downDone[i] = false;
            g_upDone[i] = false;
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
}
