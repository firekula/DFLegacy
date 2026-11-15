#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstring>
#include <algorithm>
#include "provider_probe.h"
#include "ijl15_config.h"
#include "memory_patch.h"

extern "C" __declspec(dllexport) dfl::provider_probe::Block DflProviderTrace{};
namespace dfl::provider_probe
{
    namespace
    {
        struct Saved {std::uint32_t edi,esi,ebp,esp,ebx,edx,ecx,eax;};
        volatile LONG Busy=0;bool Installed=false;std::uint64_t Frequency=0;
        bool Capture(std::uint32_t pointer,std::uint32_t length,unsigned char* target,std::uint32_t& count) noexcept
        {
            if(pointer<0x10000||length<7||length>1048576||pointer>UINT32_MAX-length)return false;
            __try{count=std::min<std::uint32_t>(length,2048);std::memcpy(target,reinterpret_cast<const void*>(pointer),count);return true;}
            __except(EXCEPTION_EXECUTE_HANDLER){count=0;return false;}
        }
    }
    void __stdcall ObserveProviderMarker(unsigned point,std::uint32_t*,const Saved* regs) noexcept
    {
        if(!Installed||point!=200)return;
        Event e{};e.thread=GetCurrentThreadId();e.context=regs->ebx;e.frame=regs->ebp;e.result=regs->eax;
        __try
        {
            const auto* frame=reinterpret_cast<const unsigned char*>(regs->ebp);
            const auto length=*reinterpret_cast<const std::uint32_t*>(frame+0x10);
            if(regs->edi<0x10007||length>1048569)return;
            e.source=regs->edi-7;e.sourceLength=length+7;
            if(*reinterpret_cast<const std::uint16_t*>(e.source+1)!=42)return;
            e.inner=*reinterpret_cast<const std::uint32_t*>(frame-0x14);
            e.innerLength=*reinterpret_cast<const std::uint32_t*>(frame+0xC);
        }
        __except(EXCEPTION_EXECUTE_HANDLER){InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(&DflProviderTrace.invalid));return;}
        if(Capture(e.source,e.sourceLength,e.sourceBytes,e.sourceCaptured))e.flags|=1;
        if(Capture(e.inner,e.innerLength,e.innerBytes,e.innerCaptured))e.flags|=2;
        if(InterlockedCompareExchange(&Busy,1,0))
        {InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(&DflProviderTrace.lockDrops));return;}
        e.tick=GetTickCount64();LARGE_INTEGER q{};
        if(Frequency&&QueryPerformanceCounter(&q)&&q.QuadPart>0)
        {const auto v=static_cast<std::uint64_t>(q.QuadPart);e.counterUs=v/Frequency*1000000+v%Frequency*1000000/Frequency;}
        InterlockedIncrement(&DflProviderTrace.revision);
        e.serial=++DflProviderTrace.serial;DflProviderTrace.events[(e.serial-1)%32]=e;
        InterlockedIncrement(&DflProviderTrace.revision);InterlockedExchange(&Busy,0);
    }
    __declspec(naked) void ProviderResultProbe()
    {
        __asm pushfd
        __asm pushad
        __asm sub esp,220h
        __asm lea eax,[esp+0Fh]
        __asm and eax,0FFFFFFF0h
        __asm fxsave [eax]
        __asm lea edx,[esp+580]
        __asm lea ecx,[esp+544]
        __asm push eax
        __asm push ecx
        __asm push edx
        __asm push 200
        __asm call ObserveProviderMarker
        __asm pop eax
        __asm fxrstor [eax]
        __asm add esp,220h
        __asm popad
        __asm popfd
        // Exact original BOOL conversion; upper EAX bytes and TEST flags stay
        // as in the original code, rather than assigning a new integer BOOL.
        __asm test eax,eax
        __asm setne al
        __asm push 0078EE7Bh
        __asm ret
    }
    bool Install() noexcept
    {
        if(Installed)return true;const auto& c=ijl15_config::Get();
        if(!c.EnableDebugOverlay||!c.ExportGameplayTrace)return true;
        constexpr std::uint32_t at=0x78EE76;const unsigned char expected[]{0x85,0xC0,0x0F,0x95,0xC0};
        if(std::memcmp(reinterpret_cast<const void*>(at),expected,5))return false;
        FILETIME created{},exited{},kernel{},user{};LARGE_INTEGER f{};
        if(!GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user)||!QueryPerformanceFrequency(&f)||f.QuadPart<=0)return false;
        Frequency=f.QuadPart;DflProviderTrace.pid=GetCurrentProcessId();
        DflProviderTrace.processStarted=(static_cast<std::uint64_t>(created.dwHighDateTime)<<32)|created.dwLowDateTime;
        if(!memory_patch::WriteRelativeJump(at,reinterpret_cast<std::uint32_t>(&ProviderResultProbe)))
        {
            if(!memory_patch::WriteBytes(at,expected,5))OutputDebugStringA("DFLegacy.Ijl15: provider observation rollback incomplete; observation stays disabled.\n");
            return false;
        }
        Installed=true;DflProviderTrace.enabled=1;return true;
    }
}
