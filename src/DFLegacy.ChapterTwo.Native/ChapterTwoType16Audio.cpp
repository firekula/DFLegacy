#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoType16Audio.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <cstdio>
#include <cstring>

namespace
{
    constexpr std::uintptr_t Base=0x400000,SoundCall=0x926d96;
    constexpr std::size_t ImageSize=0xe57000;
    void* const OriginalSound=reinterpret_cast<void*>(0x43c160);
    struct Counters { volatile LONG originalCalls=0,skipped=0; } Counts[257]{};

    bool __stdcall Record(int type,int suppressType16) noexcept
    {
        // Fixed storage and one atomic increment only. No strings, clocks,
        // allocation, logging, sound API calls or device work on this path.
        const auto index=static_cast<unsigned>(type)<256?static_cast<unsigned>(type):256;
        const bool skip=type==16&&suppressType16;
        if(skip)InterlockedIncrement(&Counts[index].skipped);
        else InterlockedIncrement(&Counts[index].originalCalls);
        return skip;
    }

    // 9257D0 ends with ret 14h, restoring ESP to the wrapper EBP. Four sound
    // arguments and CALL's return then put type [ebp+0Ch] at entry [esp+20h].
    // pushfd+pushad adds 24h, so the saved type is [esp+44h]. Record is stdcall.
    // Tail jump restores the original entry state and original return address;
    // the skip returns to 926D9B where the caller cleans its FOUR arguments.
    __declspec(naked) void SuppressGate()
    {
        __asm {
            pushfd
            pushad
            mov eax,[esp+44h]
            push 1
            push eax
            call Record
            test al,al
            jnz skip
            popad
            popfd
            jmp dword ptr [OriginalSound]
        skip:
            popad
            popfd
            ret
        }
    }
    __declspec(naked) void ObserveGate()
    {
        __asm {
            pushfd
            pushad
            mov eax,[esp+44h]
            push 0
            push eax
            call Record
            popad
            popfd
            jmp dword ptr [OriginalSound]
        }
    }

    template<std::size_t N> bool Matches(const unsigned char* image,std::uintptr_t address,const unsigned char(&expected)[N]) noexcept
    {return std::memcmp(image+address-Base,expected,N)==0;}
    void MakeCall(unsigned char* output,std::uintptr_t at,const void* function) noexcept
    {
        output[0]=0xe8;
        // x86 rel32 reaches the entire 32-bit address space modulo 2^32.
        const auto relative=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(function)-at-5);
        std::memcpy(output+1,&relative,4);
    }
}

bool dfl::chapter_two::type16_audio_detail::InstallImage(unsigned char* image,std::size_t bytes,WriteMemory write,bool suppressType16) noexcept
{
    static_assert(sizeof(void*)==4,"The native sound gate requires x86 MSVC.");
    if(!image||bytes!=ImageSize||!write)return false;
    constexpr unsigned char before[]{0x55,0x8b,0xec,0x8b,0x45,0x10,0x8b,0x55,0x0c,0x6a,0,0x50,
        0x8b,0x45,8,0x52,0x68,0xe6,0xc8,0x9b,0xff,0x50,0xe8,0x45,0xea,0xff,0xff,
        0x6a,0,0x6a,0,0x6a,0xff,0x68,0x94,0x84,0xc1,0};
    constexpr unsigned char after[]{0x83,0xc4,0x10,0x5d,0xc2,0x0c,0};
    constexpr unsigned char display[]{0x55,0x8b,0xec,0x6a,0xff,0x68,0x27,0x77,0xad,0,0x64,0xa1,0,0,0,0,
        0x50,0x64,0x89,0x25,0,0,0,0,0x81,0xec,0x3c,7,0,0,0x56,0x8b};
    constexpr unsigned char displayReturn[]{0x5f,0x5e,0x64,0x89,0x0d,0,0,0,0,0x8b,0xe5,0x5d,0xc2,0x14,0};
    constexpr unsigned char caller1[]{0x50,0x56,0x8d,0x8d,0,0xff,0xff,0xff,0x51,0x8b,0xcb,0xe8,0x3f,0xf9,0xff,0xff};
    constexpr unsigned char caller2[]{0x6a,0,0x56,0x8d,0x8d,0,0xff,0xff,0xff,0x51,0x8b,0xcb,0xe8,0x7f,0xf8,0xff,0xff};
    constexpr unsigned char caller3[]{0x57,0x56,0x8d,0x8d,0,0xff,0xff,0xff,0x51,0x8b,0xcb,0xe8,0xfe,0xf7,0xff,0xff};
    constexpr unsigned char sound[]{0x55,0x8b,0xec,0x8b,0x45,8,0x56,0x57,0xbf,0xbc,0xa7,0xae,0,0x8b,0xf0,
        0xb9,1,0,0,0,0x33,0xd2,0xf3,0xa6,0x5f,0x5e,0x74,0x21,0x8b,0x4d,0x0c,0x52,0x51,
        0x8b,0x4d,0x10,0x68,0xff,0xff,0,0,0x52,0x8b,0x55,0x14,0x52,0x51,0x8b,0x0d,0x24,
        0x0e,0xf5,0,0x6a,0,0x50,0xe8,0x13,0x79,0x61,0,0x5d,0xc3,0xcc};
    constexpr unsigned char originalCall[]{0xe8,0xc5,0x53,0xb1,0xff};
    if(!Matches(image,0x926d70,before)||!Matches(image,0x926d9b,after)||
        !Matches(image,0x9257d0,display)||!Matches(image,0x925bc0,displayReturn)||
        !Matches(image,0x927421,caller1)||!Matches(image,0x9274e0,caller2)||!Matches(image,0x927562,caller3)||
        !Matches(image,0x43c160,sound)||std::memcmp(image+0xc18494-Base,"CHAT_MESSAGE",13))return false;
    auto* target=image+SoundCall-Base;
    unsigned char replacement[5]{};
    MakeCall(replacement,reinterpret_cast<std::uintptr_t>(target),
        suppressType16?reinterpret_cast<const void*>(&SuppressGate):reinterpret_cast<const void*>(&ObserveGate));
    if(std::memcmp(target,replacement,5)==0)return true;
    if(std::memcmp(target,originalCall,5)!=0)return false;
    if(!write(reinterpret_cast<std::uintptr_t>(target),replacement,5)||std::memcmp(target,replacement,5)!=0)
    {
        // A writer can fail after copying (cache/protection error). Restore even
        // in that case; a failed installer must not claim that the hook is live.
        write(reinterpret_cast<std::uintptr_t>(target),originalCall,5);
        return false;
    }
    return true;
}

bool dfl::chapter_two::InstallType16Audio(unsigned char* image,std::size_t bytes,bool suppressType16) noexcept
{
    // Native absolute references and the tail destination require the real base.
    if(reinterpret_cast<std::uintptr_t>(image)!=Base)return false;
    return type16_audio_detail::InstallImage(image,bytes,&memory_patch::WriteBytes,suppressType16);
}

dfl::chapter_two::Type16AudioDiagnostics dfl::chapter_two::ReadType16AudioDiagnostics() noexcept
{
    Type16AudioDiagnostics result{};
    for(std::size_t i=0;i<result.types.size();++i)
    {
        result.types[i].originalCalls=static_cast<std::uint32_t>(InterlockedCompareExchange(&Counts[i].originalCalls,0,0));
        result.types[i].skipped=static_cast<std::uint32_t>(InterlockedCompareExchange(&Counts[i].skipped,0,0));
    }
    return result;
}

void dfl::chapter_two::LogType16AudioDiagnostics() noexcept
{
    static Type16AudioDiagnostics previous{};
    static unsigned next=0;
    const auto current=ReadType16AudioDiagnostics();
    unsigned emitted=0;
    for(unsigned visited=0;visited<current.types.size()&&emitted<16;++visited)
    {
        const auto index=next;next=(next+1)%static_cast<unsigned>(current.types.size());
        const auto& value=current.types[index];auto& old=previous.types[index];
        if(value.originalCalls==old.originalCalls&&value.skipped==old.skipped)continue;
        char line[192]{};
        sprintf_s(line,"DFLegacy Chapter Two: local notification audio event=CHAT_MESSAGE type=%d original=%lu skipped=%lu delta_original=%lu delta_skipped=%lu\n",
            index==256?-1:static_cast<int>(index),static_cast<unsigned long>(value.originalCalls),static_cast<unsigned long>(value.skipped),
            static_cast<unsigned long>(value.originalCalls-old.originalCalls),static_cast<unsigned long>(value.skipped-old.skipped));
        OutputDebugStringA(line);old=value;++emitted;
    }
}
