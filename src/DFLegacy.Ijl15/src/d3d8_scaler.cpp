#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include "d3d8_scaler.h"
#include "d3d8_hooks.h"
#include "auto_input.h"
#include "ijl15_config.h"
#include "memory_patch.h"

namespace
{
    constexpr std::uintptr_t DnfImageBase=0x00400000,Direct3DCreate8Iat=0x00F397D8;
    using CreateFunction=void* (WINAPI*)(UINT);
    CreateFunction OriginalCreate=nullptr;
    bool Installed=false;
    void Pump(){if(dfl::auto_input::IsEnabled())dfl::auto_input::Pump();}
    void* WINAPI CreateProxy(UINT sdk)
    {
        void* const factory=OriginalCreate?OriginalCreate(sdk):nullptr;
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
        // dnf_patches performs the client image/instruction gate before this adapter runs.
        if(reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr))!=DnfImageBase)return false;
        auto* const iat=reinterpret_cast<CreateFunction*>(Direct3DCreate8Iat);
        if(!*iat)return false;
        if(*iat==&CreateProxy){Installed=true;return true;}
        OriginalCreate=*iat;const auto proxy=&CreateProxy;
        if(!dfl::memory_patch::WriteBytes(Direct3DCreate8Iat,&proxy,sizeof(proxy))){OriginalCreate=nullptr;return false;}
        Installed=true;return true;
    }
}
