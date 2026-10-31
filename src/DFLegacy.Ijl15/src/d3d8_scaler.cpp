#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <psapi.h>
#include <cstdio>
#include "d3d8_scaler.h"
#include "d3d8_hooks.h"
#include "auto_input.h"
#include "ijl15_config.h"
#include "memory_patch.h"
#include "pe_import.h"
#include "window_input_scaling.h"

namespace
{
    constexpr std::uintptr_t DnfImageBase=0x00400000;
    using CreateFunction=void* (WINAPI*)(UINT);
    CreateFunction OriginalCreate=nullptr;
    bool Installed=false;
    void Pump(){if(dfl::auto_input::IsEnabled())dfl::auto_input::Pump();}
    void* WINAPI CreateProxy(UINT sdk)
    {
        OutputDebugStringA("DFLegacy: entering hooked Direct3DCreate8.\n");
        void* const factory=OriginalCreate?OriginalCreate(sdk):nullptr;
        OutputDebugStringA(factory?"DFLegacy: Direct3DCreate8 returned a factory.\n":"DFLegacy: Direct3DCreate8 returned null.\n");
        const auto& settings=dfl::ijl15_config::Get();
        if(factory&&!dfl::d3d8_hooks::AttachFactory(factory,{settings.PresentationFilter,settings.ScaleFilter,&Pump,settings.InvalidPresentationFilter}))
            OutputDebugStringA("DFLegacy: D3D8 factory hook was not installed.\n");
        return factory;
    }
}
namespace dfl::d3d8_scaler
{
    bool Install() noexcept
    {
        if(Installed)return true;
        if(!dfl::window_input_scaling::Install())return false;
        // dnf_patches performs the client image/instruction gate before this adapter runs.
        const auto module=GetModuleHandleW(nullptr);
        if(reinterpret_cast<std::uintptr_t>(module)!=DnfImageBase)return false;
        MODULEINFO info{};if(!GetModuleInformation(GetCurrentProcess(),module,&info,sizeof(info)))return false;
        auto* const iat=reinterpret_cast<CreateFunction*>(dfl::pe_import::FindNamed(module,info.SizeOfImage,"d3d8.dll","Direct3DCreate8"));
        if(!iat||!*iat)return false;
        if(*iat==&CreateProxy){Installed=true;return true;}
        OriginalCreate=*iat;const auto proxy=&CreateProxy;
        if(!dfl::memory_patch::WriteBytes(reinterpret_cast<std::uintptr_t>(iat),&proxy,sizeof(proxy))){OriginalCreate=nullptr;return false;}
        char trace[160]{};std::snprintf(trace,sizeof(trace),"DFLegacy: D3D8 import adapter at %p original %p.\n",static_cast<void*>(iat),reinterpret_cast<void*>(OriginalCreate));OutputDebugStringA(trace);
        Installed=true;return true;
    }
}
