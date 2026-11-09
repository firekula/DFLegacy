#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>
#include <cstdio>
#include "player_runtime.h"
#include "bootstrap_runtime.h"
#include "client_paths.h"
#include "../DFLegacy.Debug/src/client_navigation.h"
namespace dfl::player_runtime
{
    namespace
    {
        HANDLE Stop=nullptr,Worker=nullptr;
        bool Started=false;
        FrameTiming Timing{};
        SharedState Shared{};
        std::uint64_t Counter() noexcept
        {
            LARGE_INTEGER now{},frequency{};
            if(!QueryPerformanceCounter(&now)||!QueryPerformanceFrequency(&frequency)||frequency.QuadPart<=0)return 0;
            return static_cast<std::uint64_t>(now.QuadPart/frequency.QuadPart)*1000000
                +static_cast<std::uint64_t>(now.QuadPart%frequency.QuadPart)*1000000/frequency.QuadPart;
        }
        bool __stdcall Copy(client_state::Snapshot* value) noexcept{return value&&client_state::CopyLatest(*value);}
        void __stdcall Times(FrameTiming* value) noexcept{if(value)*value=Timing;}
        DWORD WINAPI Pump(void*) noexcept
        {
            do { client_state::ExportLatest();client_navigation::PumpWorker(); }
            while(WaitForSingleObject(Stop,33)==WAIT_TIMEOUT);
            return 0;
        }
    }
    bool DiagnosticsAllowed() noexcept
    {return !client_bootstrap::IsStandalone()||client_bootstrap::GetMode()==client_bootstrap::Mode::Test;}
    void Initialize() noexcept
    {
        // First frame, outside the loader lock, on the bound game thread.
        if(Started)return;
        wchar_t settings[MAX_PATH]{},directory[MAX_PATH]{},configured[MAX_PATH]{};
        if(!client_paths::BuildClientPath(L"Config.ini",settings,MAX_PATH))return;
        wcscpy_s(configured,L"debug-overlay");
        if(DiagnosticsAllowed())
            GetPrivateProfileStringW(L"DebugOverlay",L"SnapshotDirectory",L"debug-overlay",configured,MAX_PATH,settings);
        if((configured[0]&&configured[1]==L':')||configured[0]==L'\\')
        {if(wcslen(configured)>=MAX_PATH-1)return;wcscpy_s(directory,configured);}
        else
        {
            wchar_t base[MAX_PATH]{};if(!client_paths::BuildClientPath(L"debug-overlay",base,MAX_PATH))return;
            auto* slash=wcsrchr(base,L'\\');if(!slash)return;slash[1]=0;
            if(swprintf_s(directory,L"%s%s",base,configured)<0)return;
        }
        Stop=CreateEventW(nullptr,TRUE,FALSE,nullptr);if(!Stop)return;
        const bool diagnostics=DiagnosticsAllowed();
        client_state::Initialize(settings,directory,diagnostics);
        Shared={sizeof(SharedState),SharedStateVersion,sizeof(client_state::Snapshot),sizeof(client_state::Reader),client_state::NativeReader(),&Copy,&Times};
        const bool exportState=diagnostics && GetPrivateProfileIntW(L"DebugOverlay",L"ExportState",0,settings)==1;
        client_navigation::Initialize(settings,directory,Shared.reader,exportState,client_bootstrap::IsStandalone());
        Worker=CreateThread(nullptr,0,&Pump,nullptr,0,nullptr);
        if(!Worker){client_navigation::Shutdown();client_state::Shutdown();CloseHandle(Stop);Stop=nullptr;return;}
        Started=true;
    }
    void OnFrame() noexcept
    {
        Initialize();if(!Started)return;
        const auto begin=Counter();client_state::CaptureFrame();const auto captured=Counter();
        client_navigation::OnFrame();const auto navigated=Counter();
        Timing={captured>=begin?captured-begin:0,navigated>=captured?navigated-captured:0};
    }
    const SharedState* GetSharedState() noexcept{return Started?&Shared:nullptr;}
    void Shutdown() noexcept
    {
        if(!Started)return;
        if(Worker){SetEvent(Stop);WaitForSingleObject(Worker,INFINITE);CloseHandle(Worker);Worker=nullptr;}
        // Retain runtime code/resources if game-thread texture cleanup is pending.
        // Diagnostics unload never calls this function. Process-lifetime core is pinned.
        if(!client_navigation::Shutdown())return;
        client_state::Shutdown();CloseHandle(Stop);Stop=nullptr;Started=false;Shared={};
    }
}
