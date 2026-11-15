#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstring>
#include "motion_horizon.h"
#include "memory_patch.h"

namespace dfl::motion_horizon
{
    namespace
    {
        constexpr std::array<unsigned char,23> Original{
            0x8D,0x57,0x1C,0x85,0xD2,0xC7,0x45,0xF8,0xF4,0x01,0,0,
            0x76,0x09,0x8B,0x03,0x85,0xC0,0x7E,0x03,0x89,0x45,0xF8};
        constexpr auto Repaired=[]() constexpr
        {
            auto bytes=Original;
            // ACT1 4C9270: EDI is the target vector; EBX advances from
            // target+28 by four for each axis. Test that axis's acceleration,
            // not the always non-null address of its acceleration array.
            // An inactive acceleration leaves its old positive duration intact
            // in 4C8C00; using it here can quantize all correction to zero.
            // Keep the native 500ms default, arithmetic and active-axis horizon.
            bytes[0]=0x8B;bytes[1]=0x53;bytes[2]=0xF4;return bytes;
        }();
    }
    bool detail::ApplyAt(std::uintptr_t address) noexcept
    {
        __try
        {
            if(!address)return false;
            if(std::memcmp(reinterpret_cast<const void*>(address),Repaired.data(),Repaired.size())==0)return true;
            if(std::memcmp(reinterpret_cast<const void*>(address),Original.data(),Original.size())!=0)return false;
            return memory_patch::WriteBytes(address,Repaired.data(),3);
        }
        __except(EXCEPTION_EXECUTE_HANDLER){return false;}
    }
    bool Install() noexcept
    {
        return reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr))==0x400000&&detail::ApplyAt(0x4C99EF);
    }
}
