#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "client_debug_state.h"
#include "debug_module.h"
#include "../../DFLegacy.Client/player_runtime.h"
#include "../../DFLegacy.Client/bootstrap_runtime.h"
#include "../../DFLegacy.Client/client_paths.h"
#include "ijl15_config.h"
#include "performance_overlay.h"
#include "memory_patch.h"
#include "native_loop_probe.h"
#include "../../DFLegacy.Debug/include/debug_api.h"

namespace
{
    HMODULE Module = nullptr;
    dfl::debug_api::Render Draw = nullptr;
    dfl::debug_api::PipelineEnabled ProfileEnabled=nullptr;
    dfl::debug_api::RecordPipeline ProfileRecord=nullptr;
    dfl::debug_api::PipelineEnabled TraceEnabled=nullptr;
    dfl::debug_api::ObserveGameplayV2 TraceObserve=nullptr;
    dfl::debug_api::ObserveMovementGate TraceGate=nullptr;
    bool GameplayProbesInstalled=false;
    HWND ProfileWindow=nullptr;
    std::uint32_t ProfileScene=0;
    std::uint64_t ProfileClock() noexcept
    {
        static const auto frequency=[]() noexcept {LARGE_INTEGER f{};return QueryPerformanceFrequency(&f)?f.QuadPart:0LL;}();
        LARGE_INTEGER now{};
        if(frequency<=0||!QueryPerformanceCounter(&now)||now.QuadPart<=0)return 0;
        return static_cast<std::uint64_t>(now.QuadPart/frequency)*1000000
            +static_cast<std::uint64_t>(now.QuadPart%frequency)*1000000/frequency;
    }
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
        wchar_t path[MAX_PATH]{},leaf[100]{};
        if(swprintf_s(leaf,L"debug-overlay-%lu.log",GetCurrentProcessId())<0
            ||!dfl::client_paths::BuildLogPath(leaf,path,MAX_PATH))return;
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
    void __stdcall ObserveAcceptedGameplay(std::uint32_t context,std::uint32_t length,const void* bytes,std::uint32_t frame) noexcept
    {
        if(!GameplayProbesInstalled||!TraceEnabled||!TraceObserve||Disabled||!TraceEnabled())return;
        const auto pointer=reinterpret_cast<std::uintptr_t>(bytes);
        const bool provider=context<=UINT32_MAX-0x10000C&&pointer==context+0x10000C;
        TraceObserve(context,bytes,length,provider?1:0,frame);
    }
    __declspec(naked) void AcceptedGameplayProbe()
    {
        __asm
        {
            pushfd
            pushad
            mov eax,[ebp+10h]
            add eax,7
            push ebp
            push edi
            push eax
            push ebx
            call ObserveAcceptedGameplay
            popad
            popfd
            // Preserve the original cdecl arguments and return address.
            push 007927A0h
            ret
        }
    }
    void __stdcall ObserveMovementEntry(std::uint32_t actor,std::uint32_t controller,std::uint32_t flags,std::uint32_t frame) noexcept
    {
        if(!GameplayProbesInstalled||!TraceEnabled||!TraceGate||Disabled||!TraceEnabled())return;
        __try
        {
            debug_api::MovementGate gate{};gate.size=sizeof(gate);gate.version=1;
            gate.context=*reinterpret_cast<const std::uint32_t*>(0xB5DCE0);
            gate.frame=frame;gate.actor=actor;gate.controller=controller;gate.flags=flags;
            gate.objectKind=*reinterpret_cast<const std::uint16_t*>(frame-0x24);
            gate.objectUid=*reinterpret_cast<const std::uint16_t*>(frame-0x14);
            gate.playerKind=*reinterpret_cast<const std::uint16_t*>(frame-0x1C);
            gate.playerUid=*reinterpret_cast<const std::uint16_t*>(frame-0x20);
            gate.roomX=*reinterpret_cast<const unsigned char*>(frame+0xF);
            gate.roomY=*reinterpret_cast<const unsigned char*>(frame+0x13);
            TraceGate(&gate);
        }
        __except(EXCEPTION_EXECUTE_HANDLER){/* An unavailable observation never changes dispatch. */}
    }
    __declspec(naked) void MovementEntryProbe()
    {
        __asm
        {
            pushfd
            pushad
            mov eax,[esp+28h]
            push ebp
            push eax
            push ecx
            push edi
            call ObserveMovementEntry
            popad
            popfd
            push 004C9270h
            ret
        }
    }
    bool InstallGameplayProbe() noexcept
    {
        constexpr unsigned char expected[]={0xE8,0x70,0x38,0,0};
        constexpr unsigned char movement[]={0xE8,0x3F,0xA0,0xD3,0xFF};
        __try
        {
            if(std::memcmp(reinterpret_cast<const void*>(0x78EF2B),expected,sizeof(expected))!=0
                ||std::memcmp(reinterpret_cast<const void*>(0x78F22C),movement,sizeof(movement))!=0)return false;
            if(!memory_patch::WriteRelativeCall(0x78EF2B,reinterpret_cast<std::uintptr_t>(&AcceptedGameplayProbe))
                ||!memory_patch::WriteRelativeCall(0x78F22C,reinterpret_cast<std::uintptr_t>(&MovementEntryProbe)))
            {
                const bool restoredAccepted=memory_patch::WriteBytes(0x78EF2B,expected,sizeof(expected));
                const bool restoredMovement=memory_patch::WriteBytes(0x78F22C,movement,sizeof(movement));
                if(!restoredAccepted||!restoredMovement)
                    OutputDebugStringA("DFLegacy.Ijl15: gameplay probe rollback failed; both observers remain disabled.\n");
                return false;
            }
            GameplayProbesInstalled=true;return true;
        }
        __except(EXCEPTION_EXECUTE_HANDLER){return false;}
    }
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
        const debug_api::Host host{sizeof(debug_api::Host), debug_api::Version, &DrawText, gameWindow, config, player_runtime::GetSharedState()};
        if (initialize == nullptr || render == nullptr || !initialize(&host, directory))
        {
            FreeLibrary(candidate);
            return false;
        }
        Module = candidate;
        Draw = render;
        ProfileEnabled=reinterpret_cast<debug_api::PipelineEnabled>(GetProcAddress(candidate,"DebugPipelineEnabled"));
        ProfileRecord=reinterpret_cast<debug_api::RecordPipeline>(GetProcAddress(candidate,"DebugRecordPipeline"));
        ProfileWindow=gameWindow;
        native_loop_probe::Configure(
            reinterpret_cast<debug_api::PipelineEnabled>(GetProcAddress(candidate,"DebugNativeLoopEnabled")),
            reinterpret_cast<debug_api::RecordNativeLoop>(GetProcAddress(candidate,"DebugRecordNativeLoop")),gameWindow,&ProfileScene);
        TraceEnabled=reinterpret_cast<debug_api::PipelineEnabled>(GetProcAddress(candidate,"DebugGameplayTraceEnabled"));
        TraceObserve=reinterpret_cast<debug_api::ObserveGameplayV2>(GetProcAddress(candidate,"DebugObserveGameplayV2"));
        TraceGate=reinterpret_cast<debug_api::ObserveMovementGate>(GetProcAddress(candidate,"DebugObserveMovementGate"));
        // Intermediate/older debug modules lack the gate export. Keep their
        // observation path disabled while both native calls remain untouched.
        if(!GameplayProbesInstalled||!TraceObserve||!TraceGate){TraceObserve=nullptr;TraceGate=nullptr;}
        Note("debug.dll loaded; ABI version=3; owned floating window\r\n");
        HMODULE hostModule=nullptr;GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&LoadModule),&hostModule);
        char bases[120]{};
        _snprintf_s(bases, _TRUNCATE, "module bases debug=%p host=%p\r\n",
            reinterpret_cast<void*>(Module), reinterpret_cast<void*>(hostModule));
        Note(bases);
        OutputDebugStringA("DFLegacy.Ijl15: independent debug.dll overlay loaded.\n");
        return true;
    }

    bool Install() noexcept
    {
        // Apply runs in IJL15 DllMain. Load debug.dll on the first render,
        // outside the loader lock; never start or wait on workers in DllMain.
        // Standalone flavor is authenticated by bootstrap and never rewrites
        // player configuration. The old IJL path retains its explicit opt-in.
        Enabled = player_runtime::DiagnosticsAllowed()
            && (client_bootstrap::IsStandalone() || ijl15_config::Get().EnableDebugOverlay);
        // All gameplay dispatch hooks are installed with the other IJL patches,
        // before the game starts. Never rewrite an executing dispatch call.
        if(Enabled&&ijl15_config::Get().ExportGameplayTrace&&!InstallGameplayProbe())
            OutputDebugStringA("DFLegacy.Ijl15: gameplay trace signature mismatch; probe not installed.\n");
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
        ProfileScene=frame.scene;
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
    Pipeline::Pipeline() noexcept
    {
        if(!ProfileEnabled||!ProfileRecord||Disabled||!ProfileEnabled())return;
        active=true;sample.size=sizeof(sample);sample.version=1;
        sample.beginUs=last=ProfileClock();sample.tickMs=GetTickCount64();
        sample.foregroundKnown=ProfileWindow&&IsWindow(ProfileWindow)?1:0;
        sample.foregroundBegin=ProfileWindow&&GetForegroundWindow()==ProfileWindow?1:0;
    }
    void Pipeline::Mark() noexcept
    {
        if(!active||next>=4)return;
        const auto now=ProfileClock();
        if(!last||now<last)clockValid=false;else sample.partsUs[next]=now-last;
        last=now;++next;
    }
    Pipeline::~Pipeline() noexcept
    {
        if(!active||!ProfileRecord||next!=4)return;
        sample.endUs=ProfileClock();sample.scene=ProfileScene;
        sample.foregroundEnd=ProfileWindow&&GetForegroundWindow()==ProfileWindow?1:0;
        sample.foregroundKnown=sample.foregroundKnown&&ProfileWindow&&IsWindow(ProfileWindow)?1:0;
        if(!last||sample.endUs<last)clockValid=false;else sample.partsUs[4]=sample.endUs-last;
        if(!clockValid)sample.endUs=0;
        ProfileRecord(&sample);
    }
}
