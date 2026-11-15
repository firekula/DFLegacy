#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstring>
#include "motion_test_probe.h"
#include "ijl15_config.h"
#include "memory_patch.h"

extern "C" __declspec(dllexport) dfl::motion_test_probe::Block DflMotionTestTrace{};

namespace dfl::motion_test_probe
{
    namespace
    {
        struct Saved {std::uint32_t edi,esi,ebp,esp,ebx,edx,ecx,eax;};
        struct Pending {std::uint32_t target=0,nativeFrame=0,gridReturn=0;Event event{};};
        thread_local Pending Returns[16]{};
        thread_local unsigned Depth=0;
        volatile LONG Busy=0;
        bool Installed=false;
        void Store(Event e) noexcept
        {
            if(InterlockedCompareExchange(&Busy,1,0))
            {InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(&DflMotionTestTrace.lockDrops));return;}
            // Date publication while holding the producer lock, so concurrent
            // native callers cannot publish a later serial with an older tick.
            e.tick=GetTickCount64();
            InterlockedIncrement(&DflMotionTestTrace.revision);
            e.serial=++DflMotionTestTrace.serial;
            DflMotionTestTrace.events[(e.serial-1)%256]=e;
            InterlockedIncrement(&DflMotionTestTrace.revision);
            InterlockedExchange(&Busy,0);
        }
    }
    void MotionReturnProbe();
    void SecondGridReturnProbe();
    void __stdcall ObserveMotionMarker(unsigned point,std::uint32_t* slot,const Saved* regs) noexcept
    {
        // The second grid CALL has its own original ret16. Always restore its
        // native return before deciding whether this observation is usable.
        if(point==112)
        {
            if(!Depth||!Returns[Depth-1].gridReturn)return;
            auto& f=Returns[Depth-1];*slot=f.gridReturn;f.gridReturn=0;
        }
        if(point>=110)
        {
            if(!Depth)return;auto& f=Returns[Depth-1];
            if(f.event.method!=0x4B3C30||f.nativeFrame!=regs->ebp||f.event.actor!=regs->esi)return;
            if(point==115)
            {
                if(!f.gridReturn&&*slot==0x4B3DFF)
                {f.gridReturn=*slot;*slot=reinterpret_cast<std::uint32_t>(&SecondGridReturnProbe);}
                return;
            }
            if(point==111&&!regs->eax)f.event.nativePath=2;
            if(point==112&&!regs->eax)f.event.nativePath=3;
            if(point==113&&regs->eax!=5){f.event.nativePath=4;f.event.nativeDetail=regs->eax;}
            if(point==114)f.event.nativePath=1;
            return;
        }
        if(point==101)
        {
            if(!Depth)return;
            auto frame=Returns[--Depth];*slot=frame.target;
            // Record only the original v94 refusal, for XY monster calls.
            // Z uses v90 and is deliberately absent from this stream.
            if((regs->eax&255)||frame.event.axis>1)return;
            auto& e=frame.event;e.result=regs->eax&255;
            __try
            {
                auto* actor=reinterpret_cast<const unsigned char*>(e.actor);
                auto* controller=reinterpret_cast<const unsigned char*>(e.controller);
                if(e.method!=0x4B3C30||*reinterpret_cast<const std::uint32_t*>(controller+0x74)!=e.actor
                    ||*reinterpret_cast<const std::uint32_t*>(actor+0xE0)!=e.controller)return;
                e.kind=*reinterpret_cast<const std::uint32_t*>(actor+0xD0);if(e.kind!=529)return;
                e.map=*reinterpret_cast<const std::uint32_t*>(actor+0xE4);
                if(e.map<0x10000)return;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return;}
            // A missed/unrecognized marker remains unknown. Never infer the
            // bounds gate merely from the absence of a grid/state marker.
            Store(e);return;
        }
        if(!Installed||!DflMotionTestTrace.enabled)return;
        if(Depth==16)
        {InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(&DflMotionTestTrace.overflows));return;}
        Pending p{};p.target=*slot;p.nativeFrame=reinterpret_cast<std::uint32_t>(slot)-4;
        p.event.thread=GetCurrentThreadId();p.event.actor=regs->ecx;
        p.event.controller=regs->esi;p.event.axis=regs->edi;
        __try{p.event.method=*reinterpret_cast<const std::uint32_t*>(regs->eax+0x94);}
        __except(EXCEPTION_EXECUTE_HANDLER){return;}
        p.event.x=static_cast<std::int32_t>(slot[1]);p.event.y=static_cast<std::int32_t>(slot[2]);
        Returns[Depth++]=p;*slot=reinterpret_cast<std::uint32_t>(&MotionReturnProbe);
    }
    // Called at an original call boundary. Preserve all original argument and
    // return state, including flags/x87/SSE. The return reservation is separate
    // from saved flags because the original thiscall has already popped12.
#define MOTION_MARK(point) \
    __asm pushfd \
    __asm pushad \
    __asm sub esp,220h \
    __asm lea eax,[esp+0Fh] \
    __asm and eax,0FFFFFFF0h \
    __asm fxsave [eax] \
    __asm lea edx,[esp+580] \
    __asm lea ecx,[esp+544] \
    __asm push eax \
    __asm push ecx \
    __asm push edx \
    __asm push point \
    __asm call ObserveMotionMarker \
    __asm pop eax \
    __asm fxrstor [eax] \
    __asm add esp,220h \
    __asm popad \
    __asm popfd
    __declspec(naked) void MotionCallProbe(){MOTION_MARK(100) __asm jmp dword ptr [eax+94h]}
    __declspec(naked) void MotionReturnProbe(){__asm push 0 MOTION_MARK(101) __asm ret}
    __declspec(naked) void FirstGridProbe()
    {
        MOTION_MARK(111)
        __asm test eax,eax
        __asm jz rejected
        __asm mov ecx,[ebp+0Ch]
        __asm push 004B3DCCh
        __asm ret
    rejected:
        __asm push 004B3D8Dh
        __asm ret
    }
    // Do not overwrite DFF..E06: the same-row shortcut jumps to E03 inside
    // that range. Intercept the complete preceding CALL and keep the shared
    // native test/jump/mov block intact for both incoming control-flow paths.
    __declspec(naked) void SecondGridCallProbe(){MOTION_MARK(115) __asm push 008A8FE0h __asm ret}
    __declspec(naked) void SecondGridReturnProbe(){__asm push 0 MOTION_MARK(112) __asm ret}
    __declspec(naked) void StateGateProbe()
    {
        MOTION_MARK(113)
        __asm pop edi
        __asm cmp eax,5
        __asm pop esi
        __asm push 004B3D27h
        __asm ret
    }
    __declspec(naked) void BoundsGateProbe()
    {
        MOTION_MARK(114)
        __asm test ebx,ebx
        __asm jl rejected
        __asm test edi,edi
        __asm push 004B3D77h
        __asm ret
    rejected:
        __asm push 004B3D8Dh
        __asm ret
    }
#undef MOTION_MARK
    extern "C" __declspec(dllexport) const SiteTable DflMotionProbeSites{
        0x4D505331,1,5,32,{
            {0x4C8EBC,&MotionCallProbe,6,1,{0xFF,0x90,0x94,0,0,0}},
            {0x4B3DC5,&FirstGridProbe,7,0,{0x85,0xC0,0x74,0xC4,0x8B,0x4D,0x0C}},
            {0x4B3DFA,&SecondGridCallProbe,5,1,{0xE8,0xE1,0x51,0x3F,0}},
            {0x4B3D22,&StateGateProbe,5,0,{0x5F,0x83,0xF8,0x05,0x5E}},
            {0x4B3D71,&BoundsGateProbe,6,0,{0x85,0xDB,0x7C,0x18,0x85,0xFF}}}};
    bool Install() noexcept
    {
        if(Installed)return true;
        const auto& c=ijl15_config::Get();if(!c.EnableDebugOverlay||!c.ExportGameplayTrace)return true;
        const auto& sites=DflMotionProbeSites.sites;
        for(const auto& s:sites)if(std::memcmp(reinterpret_cast<const void*>(s.at),s.expected,s.length))return false;
        FILETIME created{},exited{},kernel{},user{};
        if(!GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user))return false;
        DflMotionTestTrace.pid=GetCurrentProcessId();
        DflMotionTestTrace.processStarted=(static_cast<std::uint64_t>(created.dwHighDateTime)<<32)|created.dwLowDateTime;
        for(const auto& s:sites)
        {
            unsigned char replacement[]{0x90,0x90,0x90,0x90,0x90,0x90,0x90};replacement[0]=s.call?0xE8:0xE9;
            const auto displacement=reinterpret_cast<std::uint32_t>(s.target)-(s.at+5);std::memcpy(replacement+1,&displacement,4);
            if(!memory_patch::WriteBytes(s.at,replacement,s.length))
            {
                bool restored=true;for(const auto& old:sites)restored=memory_patch::WriteBytes(old.at,old.expected,old.length)&&restored;
                if(!restored)OutputDebugStringA("DFLegacy.Ijl15: motion observation rollback incomplete; observation stays disabled.\n");
                return false;
            }
        }
        Installed=true;DflMotionTestTrace.enabled=1;return true;
    }
}
