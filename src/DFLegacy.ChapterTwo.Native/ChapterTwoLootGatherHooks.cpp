#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdio>
#include <cstring>
#include "ChapterTwoLootGather.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"

namespace
{
    constexpr std::uint32_t ChatEntry = 0x009272D0;
    constexpr std::array<unsigned char,9> ChatPrologue{0x55,0x8B,0xEC,0x81,0xEC,0x00,0x01,0x00,0x00};
    SRWLOCK PendingLock = SRWLOCK_INIT;
    dfl::chapter_two::loot_gather_detail::Request Pending{};
    bool HavePending = false;
    DWORD PendingThread = 0;
    ULONGLONG PendingAt = 0;
    volatile LONG Diagnostics = 0;

    bool ReadNative(void*,std::uint32_t address,void* output,std::size_t size)
    {
        __try { std::memcpy(output,reinterpret_cast<const void*>(address),size); return true; }
        __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
    bool SetNative(void*,std::uint32_t actor,int x,int y)
    {
        __try
        {
            // C2 actor v84 -> 487050 -> controller 4F2FD0(axis,int), ret8.
            // Its vector setter updates protected mirrors and the dirty flag.
            using Set = void(__thiscall*)(void*,int,int);
            using Get = int(__thiscall*)(void*,int);
            reinterpret_cast<Set>(0x487050)(reinterpret_cast<void*>(actor),0,x);
            reinterpret_cast<Set>(0x487050)(reinterpret_cast<void*>(actor),1,y);
            return reinterpret_cast<Get>(0x487070)(reinterpret_cast<void*>(actor),0)==x
                &&reinterpret_cast<Get>(0x487070)(reinterpret_cast<void*>(actor),1)==y;
        }
        __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
    const dfl::chapter_two::loot_gather_detail::Runtime Native{nullptr,&ReadNative,&SetNative};
    void Diagnostic(const char* reason,const dfl::chapter_two::loot_gather_detail::Request& request={},std::size_t moved=0) noexcept
    {
        // The isolated C2 process has read-only client files. The broker captures
        // this prefix in its native log without granting game-side file writes.
        // One record per command, capped for the process; no per-frame logging.
        if(InterlockedIncrement(&Diagnostics)>128) return;
        char line[256]{};
        _snprintf_s(line,sizeof(line),_TRUNCATE,"DFLegacy Chapter Two: solo loot %s uid=%u stage=%08X map=%08X actor=%08X moved=%zu tick=%llu\n",
            reason,request.Uid,request.Stage,request.Map,request.Actor,moved,GetTickCount64());
        OutputDebugStringA(line);
    }
    int __stdcall ReceiveChat(const char* body,unsigned int type,unsigned int userId) noexcept
    {
        if(type!=16||body==nullptr) return 0;
        // Never use strlen on caller memory, and require the terminating NUL.
        constexpr char marker[]="@DFLegacy:loot-magnet:v1@";
        std::array<char,sizeof(marker)> copy{};
        if(!ReadNative(nullptr,reinterpret_cast<std::uint32_t>(body),copy.data(),copy.size())
            ||copy.back()!=0||!dfl::chapter_two::loot_gather_detail::IsMarker(type,copy.data(),copy.size()-1)) return 0;
        dfl::chapter_two::loot_gather_detail::Request request{};
        // Native callers push a word UID in EDX without clearing its high half.
        // 927381 / 9273D0 explicitly read only the low word too.
        if(!dfl::chapter_two::loot_gather_detail::Capture(Native,userId&0xFFFF,request)) return 0;
        AcquireSRWLockExclusive(&PendingLock);
        Pending=request; PendingAt=GetTickCount64(); PendingThread=GetCurrentThreadId(); HavePending=true;
        ReleaseSRWLockExclusive(&PendingLock);
        Diagnostic("queued",request);
        return 1;
    }
    __declspec(naked) void ChatHook()
    {
        __asm
        {
            // Entry: return, body, type, word UID, name, mode, extra.
            // pushfd+pushad add 36 bytes; original arg1 starts at +40.
            pushfd
            pushad
            mov eax, dword ptr [esp + 28h]
            mov ecx, dword ptr [esp + 2Ch]
            mov edx, dword ptr [esp + 30h]
            push edx
            push ecx
            push eax
            call ReceiveChat
            test eax, eax
            jnz consumed
            popad
            popfd
            push ebp
            mov ebp, esp
            sub esp, 100h
            push 009272D9h
            ret
        consumed:
            popad
            popfd
            ret 18h
        }
    }
}

namespace dfl::chapter_two
{
    void OnLootGatherFrame() noexcept
    {
        loot_gather_detail::Request request{}; DWORD thread=0; ULONGLONG queued=0;
        AcquireSRWLockExclusive(&PendingLock);
        const bool pending=HavePending;
        if(pending) { request=Pending; thread=PendingThread; queued=PendingAt; HavePending=false; }
        ReleaseSRWLockExclusive(&PendingLock);
        if(!pending) return;
        if(thread!=GetCurrentThreadId()||GetTickCount64()-queued>1500)
        { Diagnostic("expired-or-wrong-thread",request); return; }
        std::size_t moved=0;
        const auto reason=loot_gather_detail::Gather(Native,request,moved);
        Diagnostic(reason?reason:"gathered",request,moved);
    }
    bool InstallLootGather(unsigned char* image,std::size_t bytes) noexcept
    {
        if (reinterpret_cast<std::uintptr_t>(image)!=0x400000) return false;
        const auto installed=loot_gather_detail::InstallImage(image,bytes,memory_patch::WriteBytes);
        Diagnostic(installed?"installed-9272D0":"install-signature-mismatch");
        return installed;
    }
}

bool dfl::chapter_two::loot_gather_detail::InstallImage(unsigned char* image,std::size_t bytes,PatchWriter write) noexcept
{
    if (!image||bytes!=0xe57000||!write) return false;
    constexpr std::array<unsigned char,12> typeAndUid{0x8B,0x75,0x0C,0x83,0xFE,0x21,0x75,0x2D,0x8B,0x0D,0x24,0x6D};
    constexpr std::array<unsigned char,7> uidRead{0x0F,0xB7,0x4D,0x10,0x3B,0xC8,0x0F};
    constexpr std::array<unsigned char,3> cleanup{0xC2,0x18,0x00};
    constexpr std::array<unsigned char,10> groundKind{0xC7,0x81,0xE0,0,0,0,0x21,0x01,0,0};
    constexpr std::array<unsigned char,7> active{0x8A,0x81,0x60,0x01,0,0,0xC3};
    constexpr std::array<unsigned char,15> dead{0x8B,0x91,0x24,0x15,0,0,0x33,0xC0,0x85,0xD2,0x0F,0x95,0xC0,0xC3,0xCC};
    if (std::memcmp(image+0x527326,typeAndUid.data(),typeAndUid.size())||
        std::memcmp(image+0x527381,uidRead.data(),uidRead.size())||
        std::memcmp(image+0x527358,cleanup.data(),cleanup.size())||
        std::memcmp(image+0x83F30,groundKind.data(),groundKind.size())||
        std::memcmp(image+0x6AE0,active.data(),active.size())||
        std::memcmp(image+0x2F4500,dead.data(),dead.size())) return false;
    std::array<unsigned char,ChatPrologue.size()> patch{};patch.fill(0x90);patch[0]=0xE9;
    const auto site=reinterpret_cast<std::uintptr_t>(image+ChatEntry-0x400000);
    const auto relative=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&ChatHook)-site-5);
    std::memcpy(patch.data()+1,&relative,4);
    const auto* current=reinterpret_cast<const unsigned char*>(site);
    if (!std::memcmp(current,patch.data(),patch.size())) return true;
    if (std::memcmp(current,ChatPrologue.data(),ChatPrologue.size())) return false;
    if (!write(site,patch.data(),patch.size()))
    {
        write(site,ChatPrologue.data(),ChatPrologue.size());
        return false;
    }
    return true;
}
