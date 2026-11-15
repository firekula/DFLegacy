#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <cstddef>
#include <cstdint>

// Minimal public Direct3D8 ABI. No dependency on the retired D3DX SDK or a proxy runtime.
namespace dfl::d3d8
{
    inline constexpr std::size_t DeviceMethods = 97, FactoryMethods = 16;
    inline constexpr HRESULT DeviceLost = static_cast<HRESULT>(0x88760868U);
    inline constexpr HRESULT DeviceNotReset = static_cast<HRESULT>(0x88760869U);
    inline constexpr HRESULT InvalidCall = static_cast<HRESULT>(0x8876086cU);
    inline constexpr HRESULT NotFound = static_cast<HRESULT>(0x88760866U);
    struct PresentationParameters
    {
        UINT BackBufferWidth = 0, BackBufferHeight = 0;
        DWORD BackBufferFormat = 0;
        UINT BackBufferCount = 0;
        DWORD MultiSampleType = 0, SwapEffect = 0;
        HWND DeviceWindow = nullptr;
        BOOL Windowed = FALSE, EnableAutoDepthStencil = FALSE;
        DWORD AutoDepthStencilFormat = 0, Flags = 0;
        UINT FullScreenRefreshRate = 0, FullScreenPresentationInterval = 0;
    };
    struct Caps
    {
        DWORD Prefix[15]{};
        DWORD TextureCaps = 0;
        DWORD FilteringAndAddress[6]{};
        DWORD MaxTextureWidth = 0, MaxTextureHeight = 0;
        DWORD Remaining[29]{};
    };
    struct DisplayMode {UINT Width, Height, RefreshRate; DWORD Format;};
    struct SurfaceDescription {DWORD Format, Type, Usage, Pool; UINT Size; DWORD MultiSampleType; UINT Width, Height;};
    struct Viewport {DWORD X,Y,Width,Height; float MinZ,MaxZ;};
    struct LockedRect {INT Pitch; void* Bits;};
    static_assert(sizeof(void*) == 4 && sizeof(PresentationParameters) == 52 && sizeof(Caps) == 212);
    static_assert(offsetof(Caps,TextureCaps) == 60 && offsetof(Caps,MaxTextureWidth) == 88);
    static_assert(sizeof(SurfaceDescription) == 32 && sizeof(Viewport) == 24 && sizeof(LockedRect) == 8);

    template<class Result, class... Args>
    Result CallTable(void* object, void** table, std::size_t index, Args... args)
    {
        return reinterpret_cast<Result (WINAPI*)(void*,Args...)>(table[index])(object,args...);
    }
    template<class Result, class... Args>
    Result Call(void* object, std::size_t index, Args... args)
    {
        return CallTable<Result>(object,*reinterpret_cast<void***>(object),index,args...);
    }
}
