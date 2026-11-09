// Test-only injection into a separately copied original DNF.exe. No game
// binaries are linked, embedded or redistributed by this fixture.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <detours.h>
#include <cstdio>
#include "native_capture.h"
namespace {
using Entry=void(WINAPI*)();Entry OriginalEntry=nullptr;
decltype(&SetUnhandledExceptionFilter) SetFilter=SetUnhandledExceptionFilter;
FILE* Log=nullptr;volatile LONG Observed=0;
LPTOP_LEVEL_EXCEPTION_FILTER WINAPI Registration(LPTOP_LEVEL_EXCEPTION_FILTER filter)
{
    MEMORY_BASIC_INFORMATION memory{};wchar_t path[1024]{};VirtualQuery(reinterpret_cast<void*>(filter),&memory,sizeof(memory));
    if(memory.AllocationBase)GetModuleFileNameW(static_cast<HMODULE>(memory.AllocationBase),path,1024);
    const auto* leaf=wcsrchr(path,L'\\');
    if(Log){fwprintf(Log,L"registration=%s+0x%lx\n",leaf?leaf+1:L"unknown",static_cast<unsigned long>(reinterpret_cast<std::uintptr_t>(filter)-reinterpret_cast<std::uintptr_t>(memory.AllocationBase)));fflush(Log);}
    if((memory.AllocationBase==GetModuleHandleW(L"BugTrap.dll")&&reinterpret_cast<std::uintptr_t>(filter)-reinterpret_cast<std::uintptr_t>(memory.AllocationBase)==0x11ce1)||
       (memory.AllocationBase==GetModuleHandleW(L"DumpReport.dll")&&reinterpret_cast<std::uintptr_t>(filter)-reinterpret_cast<std::uintptr_t>(memory.AllocationBase)==0x10d0))InterlockedExchange(&Observed,1);
    return SetFilter(filter);
}
__declspec(noinline) void ControlledFault(){*reinterpret_cast<volatile int*>(0x123)=1;}
DWORD WINAPI Trigger(void*)
{
    const auto started=GetTickCount64();while(!Observed&&GetTickCount64()-started<15000)Sleep(10);
    if(!Observed){if(Log){fputs("no-original-registration-observed\n",Log);fflush(Log);}return 0;}
    Sleep(500);
    const auto bug=GetModuleHandleW(L"BugTrap.dll");const auto getter=bug?reinterpret_cast<DWORD(WINAPI*)()>(GetProcAddress(bug,"BT_GetActivityType")):nullptr;
    if(!getter||getter()!=5){if(Log){fputs("refused-fault-legacy-activity-unguarded\n",Log);fflush(Log);}return 0;}
    if(Log){fputs("legacy-activity=5; controlled-own-process-access-violation\n",Log);fflush(Log);}
    ControlledFault();return 0;
}
void WINAPI Gate()
{
    wchar_t path[1024]{};GetEnvironmentVariableW(L"DFL_TEST_PROBE_LOG",path,1024);_wfopen_s(&Log,path,L"w");
    const bool initialized=dfl::native_capture::Initialize(),hooks=dfl::native_capture::InstallOriginalHooks();
    if(Log){fprintf(Log,"native-initialized=%d original-hooks=%d\n",initialized,hooks);fflush(Log);}
    if(!initialized||!hooks)ExitProcess(80); // No deliberate fault without the production privacy guard.
    if(DetourTransactionBegin()!=NO_ERROR)ExitProcess(81);DetourUpdateThread(GetCurrentThread());
    if(DetourAttach(reinterpret_cast<PVOID*>(&SetFilter),Registration)!=NO_ERROR||DetourTransactionCommit()!=NO_ERROR)ExitProcess(82);
    HANDLE thread=CreateThread(nullptr,0,Trigger,nullptr,0,nullptr);if(thread)CloseHandle(thread);
    OriginalEntry();
}
}
extern "C" __declspec(dllexport) void WINAPI ProbeMarker(){}
BOOL WINAPI DllMain(HINSTANCE,DWORD reason,LPVOID)
{
    if(reason!=DLL_PROCESS_ATTACH)return TRUE;
    if(!GetEnvironmentVariableW(L"DFL_TEST_PROBE_LOG",nullptr,0)||!DetourRestoreAfterWith())return FALSE;
    OriginalEntry=reinterpret_cast<Entry>(DetourGetEntryPoint(GetModuleHandleW(nullptr)));
    if(!OriginalEntry||DetourTransactionBegin()!=NO_ERROR)return FALSE;DetourUpdateThread(GetCurrentThread());
    if(DetourAttach(reinterpret_cast<PVOID*>(&OriginalEntry),Gate)!=NO_ERROR){DetourTransactionAbort();return FALSE;}
    return DetourTransactionCommit()==NO_ERROR;
}
