#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "creature_gift_row.h"
#include "memory_patch.h"
#include <cstring>

namespace
{
    constexpr std::uintptr_t HookOffset=0x4636BE;
    constexpr std::uintptr_t ResumeOffset=0x4636DD;
    constexpr unsigned char Original[]={0xC6,0x42,0x11,0,0xEB,0x19};
    constexpr unsigned char Prefix[]={0x8B,0x8E,0xE0,0x01,0,0,0x8B,0x45,0xF8,0x8B,0x14,0x01,
        0xC6,0x42,0x10,0x01,0x8B,0x8E,0xE0,0x01,0,0,0x8B,0x54,0x01,0x04};
    constexpr unsigned char Suffix[]={0x8B,0x45,0xFC,0x8B,0x7D,0x08,0x83,0xC0,0x04};
    template<class T> T Read(const unsigned char* at) noexcept
    {
        T result;std::memcpy(&result,at,sizeof(result));return result;
    }
    bool Matches(std::uintptr_t at,const void* bytes,std::size_t count) noexcept
    {
        __try { return std::memcmp(reinterpret_cast<const void*>(at),bytes,count)==0; }
        __except(EXCEPTION_EXECUTE_HANDLER){return false;}
    }
    bool KnownContext(std::uintptr_t image) noexcept
    {
        return image!=0&&image<=UINT32_MAX-ResumeOffset-sizeof(Suffix)
            &&Matches(image+0x4636A4,Prefix,sizeof(Prefix))
            &&Matches(image+ResumeOffset,Suffix,sizeof(Suffix));
    }
    bool Eligible(void* shop,void* commodity,void* button) noexcept
    {
        if(!shop||!commodity||!button)return false;
        __try
        {
            const auto* s=static_cast<const unsigned char*>(shop);
            const auto* r=static_cast<const unsigned char*>(commodity);
            if(Read<int>(s+84)!=3||Read<int>(s+13064)!=0)return false;
            // Constructor865C30 creates six fixed12-byte controls, not a vector.
            const auto* controls=Read<const unsigned char*>(s+480);
            if(!controls)return false;
            bool associated=false;
            for(unsigned i=0;i<6;++i)
                if(Read<void*>(s+456+4*i)==commodity&&Read<void*>(controls+12*i+4)==button)
                    associated=true;
            if(!associated||Read<unsigned>(r+4)!=3||Read<unsigned>(r+8)!=0
                ||r[16]!=0xFF||Read<unsigned>(r+20)!=1||r[24]==99
                ||Read<unsigned short>(r+26)!=0||Read<unsigned>(r+32)!=0
                ||Read<unsigned>(r+36)!=0||r[76]!=0)return false;
            const auto* item=Read<const volatile unsigned char*>(r+80);
            if(!item)return false;
            // Only prove a readable materialized object; its subtype/lifetime
            // layout is not established. Server catalog validation is authoritative.
            const auto readable=*item;(void)readable;
            // Parser72E6F0 and price consumer729F30 place cash at+40;
            // +32/+36 are the other price columns and must remain zero.
            constexpr unsigned tuples[][3]={
                {105008,63006,9900},{105009,63007,6600},{105010,63013,9900},
                {105011,63014,9900},{105012,63015,9900},{105013,63016,9900},
                {105014,63017,9900},{105015,63018,9900},{105016,63022,6600},
                {105017,63024,6600},{105018,63028,9900},{105019,63031,9900},
                {105020,63035,13200},{105028,63134,9900},{105030,63136,13200}};
            for(const auto& t:tuples)
                if(Read<unsigned>(r)==t[0]&&Read<unsigned>(r+12)==t[1]&&Read<unsigned>(r+40)==t[2])return true;
            return false;
        }
        __except(EXCEPTION_EXECUTE_HANDLER){return false;}
    }
}
namespace dfl::creature_gift_row::detail
{
    bool __cdecl UpdateRow(void* shop,void* commodity,void* button) noexcept
    {
        const bool enabled=Eligible(shop,commodity,button);
        // Metadata faults must not bypass clearing a previously enabled button.
        __try
        {
            if(!button)return false;
            static_cast<unsigned char*>(button)[17]=enabled?1:0;return true;
        }
        __except(EXCEPTION_EXECUTE_HANDLER){return false;}
    }
    StubCode BuildStub(std::uintptr_t address,std::uintptr_t helper,std::uintptr_t resume) noexcept
    {
        StubCode code{0x9C,0x60,0xFC,0x52,0x57,0x56,0xE8,0,0,0,0,0x83,0xC4,0x0C,0x61,0x9D,0xE9,0,0,0,0};
        const auto call=static_cast<std::uint32_t>(helper-address-11);
        const auto jump=static_cast<std::uint32_t>(resume-address-21);
        std::memcpy(code.data()+7,&call,4);std::memcpy(code.data()+17,&jump,4);return code;
    }
    bool InstallImage(std::uintptr_t image,std::uintptr_t stub,PatchWriter write) noexcept
    {
        if(!stub||!write||!KnownContext(image))return false;
        unsigned char patch[]={0xE9,0,0,0,0,0x90};
        const auto delta=static_cast<std::uint32_t>(stub-image-HookOffset-5);
        std::memcpy(patch+1,&delta,4);
        if(Matches(image+HookOffset,patch,sizeof(patch)))return true;
        if(!Matches(image+HookOffset,Original,sizeof(Original)))return false;
        if(!write(image+HookOffset,patch,sizeof(patch)))
        {
            // The writer can report failure after writing (protection restore).
            write(image+HookOffset,Original,sizeof(Original));return false;
        }
        return true;
    }
}
namespace dfl::creature_gift_row
{
    bool Install() noexcept
    {
        const auto image=reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
        if(image!=0x00400000||!KnownContext(image))return false;
        // DLL initialization owns installation before the native event loop.
        // Retain a live trampoline even after an uncertain rollback, never expose
        // a jump into freed memory. A later call reuses this single allocation.
        static unsigned char* stub=nullptr;
        if(!stub)
        {
            if(!Matches(image+HookOffset,Original,sizeof(Original)))return false;
            auto* allocated=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
            if(!allocated)return false;
            const auto code=detail::BuildStub(reinterpret_cast<std::uintptr_t>(allocated),
                reinterpret_cast<std::uintptr_t>(&detail::UpdateRow),image+ResumeOffset);
            std::memcpy(allocated,code.data(),code.size());
            DWORD old{};
            if(!VirtualProtect(allocated,4096,PAGE_EXECUTE_READ,&old)
                ||!FlushInstructionCache(GetCurrentProcess(),allocated,code.size()))
            {VirtualFree(allocated,0,MEM_RELEASE);return false;}
            stub=allocated;
        }
        return detail::InstallImage(image,reinterpret_cast<std::uintptr_t>(stub),memory_patch::WriteBytes);
    }
}
