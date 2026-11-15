#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <atomic>
#include <cstring>
#include "native_loop_probe.h"
#include "memory_patch.h"
#include "ijl15_config.h"

namespace dfl::native_loop_probe
{
    namespace
    {
        std::atomic<debug_api::PipelineEnabled> Enabled{nullptr};
        debug_api::RecordNativeLoop Record=nullptr;HWND Window=nullptr;const std::uint32_t* Scene=nullptr;
        bool Installed=false;std::uint64_t Frequency=0;
        thread_local detail::ReturnStack Returns{};
        std::uint64_t Clock() noexcept
        {
            LARGE_INTEGER n{};if(!Frequency||!QueryPerformanceCounter(&n)||n.QuadPart<=0)return 0;
            const auto q=static_cast<std::uint64_t>(n.QuadPart);return q/Frequency*1000000+q%Frequency*1000000/Frequency;
        }
        bool Cpu(std::uint64_t& value) noexcept
        {
            FILETIME created{},exited{},kernel{},user{};
            if(!GetThreadTimes(GetCurrentThread(),&created,&exited,&kernel,&user))return false;
            value=(((static_cast<std::uint64_t>(kernel.dwHighDateTime)<<32)|kernel.dwLowDateTime)
                +((static_cast<std::uint64_t>(user.dwHighDateTime)<<32)|user.dwLowDateTime))/10;return true;
        }
    }
    void LoopReturnProbe();
    void __stdcall ObserveLoopMarker(unsigned point,std::uint32_t* returnSlot) noexcept
    {
        // Once redirected, a return must be restored even if profiling is
        // subsequently disabled. Never clear the return stack at a scene edge.
        if(point==7)
        {
            if(!Returns.Depth())return;
            std::uint64_t cpu=0;const auto wall=Clock();const bool known=Cpu(cpu);
            const bool fgKnown=Window&&IsWindow(Window);
            Returns.Mark(7,wall,cpu,known,Scene?*Scene:0,fgKnown,fgKnown&&GetForegroundWindow()==Window,GetTickCount64());
            std::uint32_t target=0;debug_api::NativeLoopSample sample{};
            if(Returns.Leave(target,sample))
            {
                *returnSlot=target;
                if(Record)Record(&sample);
            }
            return;
        }
        if(!Installed)return;
        if(point==0)
        {
            const auto enabled=Enabled.load(std::memory_order_acquire);
            if(!enabled||!Record||!enabled())return;
            debug_api::NativeLoopSample s{};s.size=sizeof(s);s.version=1;s.thread=GetCurrentThreadId();
            s.wallUs[0]=Clock();s.cpuMask=Cpu(s.cpuUs[0])?1:0;s.pointMask=1;s.tickMs=GetTickCount64();
            s.sceneBegin=Scene?*Scene:0;s.foregroundKnown=Window&&IsWindow(Window)?1:0;
            s.foregroundBegin=s.foregroundKnown&&GetForegroundWindow()==Window?1:0;
            if(Returns.Enter(*returnSlot,s))*returnSlot=reinterpret_cast<std::uint32_t>(&LoopReturnProbe);
        }
        else if(Returns.Depth())
        {
            std::uint64_t cpu=0;const auto wall=Clock();const bool known=Cpu(cpu);
            Returns.Mark(point,wall,cpu,known,0,false,false,0);
        }
    }
    // A marker sits between original instructions, so preserve FP/SIMD state
    // as well as GPRs and flags. The aligned512-byte save area stays above the
    // helper's stack. 580 =544 bytes plus pushad/pushfd's36 bytes.
#define LOOP_MARK(point,slot) \
    __asm pushfd \
    __asm pushad \
    __asm sub esp,220h \
    __asm lea eax,[esp+0Fh] \
    __asm and eax,0FFFFFFF0h \
    __asm fxsave [eax] \
    __asm lea edx,[esp+slot] \
    __asm push eax \
    __asm push edx \
    __asm push point \
    __asm call ObserveLoopMarker \
    __asm pop eax \
    __asm fxrstor [eax] \
    __asm add esp,220h \
    __asm popad \
    __asm popfd

    __declspec(naked) void LoopBeginProbe(){LOOP_MARK(0,580) __asm push 00B5D9E8h __asm push 00433B05h __asm ret}
    __declspec(naked) void NetworkBeginProbe(){LOOP_MARK(1,0) __asm mov ecx,dword ptr ds:[00B5DCE0h] __asm push 00433B3Ch __asm ret}
    __declspec(naked) void UiBeginProbe(){LOOP_MARK(2,0) __asm mov ecx,dword ptr ds:[00B5DCE4h] __asm push 00433B47h __asm ret}
    __declspec(naked) void SceneBeginProbe(){LOOP_MARK(3,0) __asm mov ecx,dword ptr ds:[00B5DCE8h] __asm push 00433B52h __asm ret}
    __declspec(naked) void SceneEndProbe(){LOOP_MARK(4,0) __asm mov ecx,dword ptr ds:[00BF1638h] __asm push 00433B5Dh __asm ret}
    __declspec(naked) void PacingBeginProbe(){LOOP_MARK(5,0) __asm push 00433C12h __asm push 00433660h __asm ret}
    __declspec(naked) void TailBeginProbe(){LOOP_MARK(6,0) __asm push 00792A70h __asm ret}
    // Reserve the outgoing return separately, above the saved flags. Writing
    // [original ESP-4] without this reservation would overwrite pushfd's value.
    __declspec(naked) void LoopReturnProbe(){__asm push 0 LOOP_MARK(7,580) __asm ret}
#undef LOOP_MARK

    void Configure(debug_api::PipelineEnabled enabled,debug_api::RecordNativeLoop record,void* window,const std::uint32_t* scene) noexcept
    {
        Record=record;Window=static_cast<HWND>(window);Scene=scene;Enabled.store(record?enabled:nullptr,std::memory_order_release);
    }
    bool Install() noexcept
    {
        if(!ijl15_config::Get().EnableDebugOverlay||!ijl15_config::Get().ExportNativeLoopProfile)return true;
        if(Installed)return true;
        if(reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr))!=0x400000)return false;
        struct Site {std::uintptr_t address,target;unsigned length;unsigned char original[6];};
        const Site sites[]={
            {0x433B00,reinterpret_cast<std::uintptr_t>(&LoopBeginProbe),5,{0x68,0xE8,0xD9,0xB5,0}},
            {0x433B36,reinterpret_cast<std::uintptr_t>(&NetworkBeginProbe),6,{0x8B,0x0D,0xE0,0xDC,0xB5,0}},
            {0x433B41,reinterpret_cast<std::uintptr_t>(&UiBeginProbe),6,{0x8B,0x0D,0xE4,0xDC,0xB5,0}},
            {0x433B4C,reinterpret_cast<std::uintptr_t>(&SceneBeginProbe),6,{0x8B,0x0D,0xE8,0xDC,0xB5,0}},
            {0x433B57,reinterpret_cast<std::uintptr_t>(&SceneEndProbe),6,{0x8B,0x0D,0x38,0x16,0xBF,0}},
            {0x433C0D,reinterpret_cast<std::uintptr_t>(&PacingBeginProbe),5,{0xE8,0x4E,0xFA,0xFF,0xFF}},
            {0x433C12,reinterpret_cast<std::uintptr_t>(&TailBeginProbe),5,{0xE9,0x59,0xEE,0x35,0}}};
        __try
        {
            for(const auto& s:sites)if(std::memcmp(reinterpret_cast<const void*>(s.address),s.original,s.length)!=0)return false;
            LARGE_INTEGER f{};if(!QueryPerformanceFrequency(&f)||f.QuadPart<=0)return false;Frequency=f.QuadPart;
            for(const auto& s:sites)if(!memory_patch::WriteRelativeJump(s.address,s.target))
            {
                bool restored=true;for(const auto& old:sites)restored=memory_patch::WriteBytes(old.address,old.original,old.length)&&restored;
                if(!restored)OutputDebugStringA("DFLegacy.Ijl15: native loop rollback incomplete; observation stays disabled.\n");
                return false;
            }
            Installed=true;return true;
        }
        __except(EXCEPTION_EXECUTE_HANDLER){return false;}
    }
}
