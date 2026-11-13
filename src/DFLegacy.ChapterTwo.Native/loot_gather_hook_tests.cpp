// Execute the real x86 naked shim against fixed-address synthetic client memory.
// Only the original continuation is replaced; parser, capture and queue are real.
#include "ChapterTwoLootGatherHooks.cpp"
#include "loot_gather_fixture.h"

// Reserve the complete synthetic address range as part of the fixed-base test
// image. Runtime low-address allocations can otherwise collide with a CRT heap
// reservation before main runs. This data section contains no executable code.
#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char lootGatherFixtureReserve[0x2300000]{1};

namespace
{
    unsigned int OriginalCalls=0,ReceivedThis=0;
    std::array<unsigned int,6> Received{};
    __declspec(naked) void OriginalContinuation()
    {
        __asm
        {
            inc OriginalCalls
            mov ReceivedThis,ecx
            mov eax,[ebp+8]
            mov dword ptr [Received],eax
            mov eax,[ebp+0Ch]
            mov dword ptr [Received+4],eax
            mov eax,[ebp+10h]
            mov dword ptr [Received+8],eax
            mov eax,[ebp+14h]
            mov dword ptr [Received+12],eax
            mov eax,[ebp+18h]
            mov dword ptr [Received+16],eax
            mov eax,[ebp+1Ch]
            mov dword ptr [Received+20],eax
            mov esp,ebp
            pop ebp
            ret 18h
        }
    }
    struct FixedMemory
    {
        const std::uintptr_t Begin = reinterpret_cast<std::uintptr_t>(lootGatherFixtureReserve);
        static constexpr std::size_t Size = sizeof(lootGatherFixtureReserve);
        FixedMemory()
        {
            Check(Begin <= 0xA00000 && Begin + Size >= 0x2200014,
                "the fixed-base test image reserves every synthetic fixture address");
        }
        void Copy(const Fixture& f)
        {
            for(const auto& byte:f.memory)
            {
                if(byte.first<0xA00000) continue;
                Check(byte.first >= Begin && byte.first-Begin < Size,
                    "fixture writes stay within the statically reserved data section");
                *reinterpret_cast<unsigned char*>(byte.first)=byte.second;
            }
        }
    };
    void RunHook()
    {
        FixedMemory memory;
        Fixture f; memory.Copy(f);
        // The real shim's continuation is an absolute client address. Redirect
        // only that immediate in this test image so OS stack reservations at
        // 0x840000 cannot make ABI coverage depend on ASLR placement.
        auto* code=reinterpret_cast<unsigned char*>(&ChatHook);
        bool patched=false;
        for(std::size_t i=0;i<120;++i)
        {
            constexpr unsigned char continuation[]{0x68,0xD9,0x72,0x92,0x00,0xC3};
            if(std::memcmp(code+i,continuation,sizeof(continuation))!=0) continue;
            const auto target=reinterpret_cast<std::uintptr_t>(&OriginalContinuation);
            Check(dfl::memory_patch::WriteBytes(reinterpret_cast<std::uintptr_t>(code+i+1),&target,4),"redirect original continuation");
            patched=true; break;
        }
        Check(patched,"real shim contains expected continuation");
        using Chat=void(__thiscall*)(void*,const char*,int,unsigned int,void*,int,int);
        const auto call=reinterpret_cast<Chat>(&ChatHook);
        constexpr char ordinary[]="ordinary Type16 chat message";
        call(reinterpret_cast<void*>(0x12345678),ordinary,16,0xABCD0007,reinterpret_cast<void*>(0x87654321),1,-1);
        Check(OriginalCalls==1&&ReceivedThis==0x12345678,"ordinary chat retains original this pointer");
        Check(Received[0]==reinterpret_cast<unsigned int>(ordinary)&&Received[1]==16&&Received[2]==0xABCD0007
            &&Received[3]==0x87654321&&Received[4]==1&&Received[5]==UINT32_MAX,"all original stack arguments preserved");
        constexpr char marker[]="@DFLegacy:loot-magnet:v1@";
        call(nullptr,marker,0,7,nullptr,0,-1);
        Check(OriginalCalls==2&&!HavePending,"ordinary user message containing marker is untouched");
        call(nullptr,marker,16,8,nullptr,0,-1);
        Check(OriginalCalls==3&&!HavePending,"other sender's marker is untouched");
        call(nullptr,marker,16,0xFFFF0007,nullptr,0,-1);
        Check(OriginalCalls==3&&HavePending&&Pending.Uid==7&&Pending.Map==Fixture::Map,"correct low-word UID queues and consumes exact marker");
        dfl::chapter_two::OnLootGatherFrame(); Check(!HavePending,"one frame consumes the request");
        dfl::chapter_two::OnLootGatherFrame(); Check(!HavePending,"idle frames do not replay requests");
        call(nullptr,marker,16,7,nullptr,0,-1); Check(HavePending,"repeat command queues again");
        PendingAt=GetTickCount64()-2000; dfl::chapter_two::OnLootGatherFrame(); Check(!HavePending,"expired command discarded");
    }
}
int main()
{
    try { RunHook(); std::puts("loot_gather_hook_tests PASS"); return 0; }
    catch(const std::exception& error) { std::fprintf(stderr,"FAIL: %s\n",error.what()); return 1; }
}
