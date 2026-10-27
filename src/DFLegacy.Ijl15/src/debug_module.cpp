#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "client_debug_state.h"
#include "debug_module.h"
#include "ijl15_config.h"
#include "performance_overlay.h"
#include "../../DFLegacy.Debug/include/debug_api.h"

namespace
{
    HMODULE Module = nullptr;
    dfl::debug_api::Render Draw = nullptr;
    bool WasDungeon = false;
    bool Enabled = false;
    bool Attempted = false;
    std::uint64_t Entered = 0;
    HANDLE Diagnostic = INVALID_HANDLE_VALUE;
    bool Disabled = false;

    BOOL CALLBACK FindGameWindow(HWND window, LPARAM result) noexcept
    {
        DWORD process = 0;
        GetWindowThreadProcessId(window, &process);
        if (process == GetCurrentProcessId() && IsWindowVisible(window)
            && GetWindow(window, GW_OWNER) == nullptr)
        {
            *reinterpret_cast<HWND*>(result) = window;
            return FALSE;
        }
        return TRUE;
    }

    void Note(const char* text) noexcept
    {
        if (Diagnostic == INVALID_HANDLE_VALUE) return;
        DWORD written = 0;
        WriteFile(Diagnostic, text, static_cast<DWORD>(std::strlen(text)), &written, nullptr);
    }

    LONG WINAPI ObserveException(EXCEPTION_POINTERS* pointers) noexcept
    {
        if (pointers == nullptr || pointers->ExceptionRecord == nullptr || pointers->ContextRecord == nullptr)
            return EXCEPTION_CONTINUE_SEARCH;
        const auto code = pointers->ExceptionRecord->ExceptionCode;
        if (code < 0xC0000000 || code == 0xE06D7363) return EXCEPTION_CONTINUE_SEARCH;
        static volatile LONG recorded = 0;
        if (InterlockedIncrement(&recorded) > 16) return EXCEPTION_CONTINUE_SEARCH;
        const auto& c = *pointers->ContextRecord;
        char line[320]{};
        _snprintf_s(line, _TRUNCATE,
            "exception code=%08lX eip=%08lX eax=%08lX ecx=%08lX edx=%08lX esi=%08lX edi=%08lX esp=%08lX ebp=%08lX\r\n",
            code, c.Eip, c.Eax, c.Ecx, c.Edx, c.Esi, c.Edi, c.Esp, c.Ebp);
        Note(line);
        FlushFileBuffers(Diagnostic);
        return EXCEPTION_CONTINUE_SEARCH;
    }

    void StartDiagnostics() noexcept
    {
        wchar_t path[MAX_PATH]{};
        const DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
        if (length == 0 || length >= MAX_PATH) return;
        wchar_t* separator = wcsrchr(path, L'\\');
        if (separator == nullptr) return;
        const auto remaining = static_cast<std::size_t>(MAX_PATH - (separator + 1 - path));
        if (swprintf_s(separator + 1, remaining, L"debug-overlay-%lu.log", GetCurrentProcessId()) < 0) return;
        Diagnostic = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (Diagnostic != INVALID_HANDLE_VALUE)
        {
            Note("debug overlay: passive exception observer; no debug input injection\r\n");
            AddVectoredExceptionHandler(1, &ObserveException);
        }
    }

    void __stdcall DrawText(int x, int y, std::uint32_t color, const char* text) noexcept
    {
        dfl::performance_overlay::DrawOverlayText(x, y, color, text);
    }

    bool Capture(dfl::debug_api::Frame& frame) noexcept
    {
        __try
        {
            const auto game = *reinterpret_cast<const std::uintptr_t*>(0x00B5D6F0);
            const auto ui = *reinterpret_cast<const std::uintptr_t*>(0x00B5DCE0);
            if (game == 0 || ui == 0) return false;
            const auto manager = *reinterpret_cast<const std::uintptr_t*>(0x00CFD458);
            const auto key = *reinterpret_cast<const std::uint32_t*>(0x00CFD45C);
            if (!dfl::debug_module::detail::ReadProtected(game + 16, manager, key, frame.scene)) return false;
            if (frame.scene != 3) return true;
            const auto stage = *reinterpret_cast<const std::uintptr_t*>(ui + 0x200010);
            const auto actor = *reinterpret_cast<void**>(0x00B75A14);
            if (stage == 0 || actor == nullptr) return false;
            const auto bytes = reinterpret_cast<std::uintptr_t>(actor);
            // +D0 is object kind (0x111), +D4 is the protected user ID.
            // 4073D0 reads +D0 and must never be used as the ID getter.
            if (*reinterpret_cast<const std::uint32_t*>(bytes + 0xD0) != 0x111
                || !dfl::debug_module::detail::ReadProtected(bytes + 0xD4, manager, key, frame.userId)
                || *reinterpret_cast<void* const*>(stage + 0xB0) == nullptr
                || *reinterpret_cast<void* const*>(bytes + 0xE4)
                    != *reinterpret_cast<void* const*>(stage + 0xB0)) return false;
            frame.bossDead = *reinterpret_cast<const unsigned char*>(stage + 0x110) != 0;
            return frame.userId > 0 && frame.userId <= 65535;
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
}

namespace dfl::debug_module
{
    bool LoadModule() noexcept
    {
        if (Module != nullptr) return Draw != nullptr;
        wchar_t executable[MAX_PATH]{};
        const DWORD length = GetModuleFileNameW(nullptr, executable, MAX_PATH);
        if (length == 0 || length >= MAX_PATH) return false;
        wchar_t* slash = wcsrchr(executable, L'\\');
        if (slash == nullptr) return false;
        *slash = L'\0';
        wchar_t config[MAX_PATH]{}, dll[MAX_PATH]{}, configured[MAX_PATH]{}, directory[MAX_PATH]{};
        if (swprintf_s(config, L"%s\\Config.ini", executable) < 0
            || swprintf_s(dll, L"%s\\debug.dll", executable) < 0) return false;
        GetPrivateProfileStringW(L"DebugOverlay", L"SnapshotDirectory", L"debug-overlay", configured, MAX_PATH, config);
        wchar_t combined[MAX_PATH]{};
        if ((configured[0] != L'\0' && configured[1] == L':') || configured[0] == L'\\')
            wcscpy_s(combined, configured);
        else if (swprintf_s(combined, L"%s\\%s", executable, configured) < 0) return false;
        const DWORD fullLength = GetFullPathNameW(combined, MAX_PATH, directory, nullptr);
        if (fullLength == 0 || fullLength >= MAX_PATH) return false;
        const HMODULE candidate = LoadLibraryExW(dll, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (candidate == nullptr) return false;
        const auto initialize = reinterpret_cast<debug_api::Initialize>(GetProcAddress(candidate, "DebugInitialize"));
        const auto render = reinterpret_cast<debug_api::Render>(GetProcAddress(candidate, "DebugRender"));
        HWND gameWindow = nullptr;
        EnumWindows(&FindGameWindow, reinterpret_cast<LPARAM>(&gameWindow));
        if (gameWindow == nullptr) { FreeLibrary(candidate); return false; }
        const debug_api::Host host{sizeof(debug_api::Host), debug_api::Version, &DrawText, gameWindow, config};
        if (initialize == nullptr || render == nullptr || !initialize(&host, directory))
        {
            FreeLibrary(candidate);
            return false;
        }
        Module = candidate;
        Draw = render;
        Note("debug.dll loaded; ABI version=2; owned floating window\r\n");
        char bases[120]{};
        _snprintf_s(bases, _TRUNCATE, "module bases debug=%p ijl15=%p\r\n",
            reinterpret_cast<void*>(Module), reinterpret_cast<void*>(GetModuleHandleW(L"ijl15.dll")));
        Note(bases);
        OutputDebugStringA("DFLegacy.Ijl15: independent debug.dll overlay loaded.\n");
        return true;
    }

    bool Install() noexcept
    {
        // Apply runs in IJL15 DllMain. Load debug.dll on the first render,
        // outside the loader lock; never start or wait on workers in DllMain.
        Enabled = ijl15_config::Get().EnableDebugOverlay;
        return true;
    }

    void Render() noexcept
    {
        if (!Enabled || Disabled) return;
        if (!Attempted)
        {
            Attempted = true;
            StartDiagnostics();
            if (!LoadModule()) OutputDebugStringA("DFLegacy.Ijl15: debug.dll could not be loaded.\n");
        }
        if (Draw == nullptr) return;
        debug_api::Frame frame{};
        frame.size = sizeof(frame);
        if (!Capture(frame)) frame.scene = 0;
        if (frame.scene == 3 && !WasDungeon)
        {
            FILETIME now{};
            GetSystemTimeAsFileTime(&now);
            Entered = (static_cast<std::uint64_t>(now.dwHighDateTime) << 32) | now.dwLowDateTime;
        }
        WasDungeon = frame.scene == 3;
        frame.enteredFileTime = Entered;
        static unsigned traces = 0;
        if (frame.scene == 3 && traces < 3)
        {
            char line[100]{};
            _snprintf_s(line, _TRUNCATE, "dungeon render begin uid=%lu\r\n", static_cast<unsigned long>(frame.userId));
            Note(line);
        }
        __try { Draw(&frame); }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            Disabled = true;
            Note("overlay disabled after render exception; gameplay continues\r\n");
        }
        if (frame.scene == 3 && traces++ < 3) Note("dungeon render complete\r\n");
    }
}
