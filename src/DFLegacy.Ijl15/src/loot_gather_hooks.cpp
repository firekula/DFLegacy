#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdio>
#include <cstring>
#include "loot_gather.h"
#include "memory_patch.h"
#include "../../DFLegacy.Client/client_paths.h"

namespace
{
    constexpr std::uint32_t ChatEntry = 0x00849280;
    constexpr std::array<unsigned char,9> ChatPrologue{0x55,0x8B,0xEC,0x81,0xEC,0x00,0x01,0x00,0x00};
    SRWLOCK PendingLock = SRWLOCK_INIT;
    dfl::loot_gather::detail::Request Pending{};
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
            // ACT1 actor v74 -> 4715F0 -> controller 4C9D50(axis,int), ret8.
            // Its vector setter updates protected mirrors and the dirty flag.
            using Set = void(__thiscall*)(void*,int,int);
            using Get = int(__thiscall*)(void*,int);
            reinterpret_cast<Set>(0x4715F0)(reinterpret_cast<void*>(actor),0,x);
            reinterpret_cast<Set>(0x4715F0)(reinterpret_cast<void*>(actor),1,y);
            return reinterpret_cast<Get>(0x471610)(reinterpret_cast<void*>(actor),0)==x
                &&reinterpret_cast<Get>(0x471610)(reinterpret_cast<void*>(actor),1)==y;
        }
        __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
    const dfl::loot_gather::detail::Runtime Native{nullptr,&ReadNative,&SetNative};
    void Diagnostic(const char* reason,const dfl::loot_gather::detail::Request& request={},std::size_t moved=0) noexcept
    {
        // One record per command, capped for the process; no per-frame disk I/O.
        if(InterlockedIncrement(&Diagnostics)>128) return;
        wchar_t leaf[100]{},path[MAX_PATH]{};
        if(swprintf_s(leaf,L"loot-gather-%lu.log",GetCurrentProcessId())<0
            ||!dfl::client_paths::BuildLogPath(leaf,path,MAX_PATH)) return;
        const auto file=CreateFileW(path,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
            nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(file==INVALID_HANDLE_VALUE) return;
        char line[256]{};
        const auto size=_snprintf_s(line,sizeof(line),_TRUNCATE,"%s uid=%u stage=%08X map=%08X actor=%08X moved=%zu tick=%llu\r\n",
            reason,request.Uid,request.Stage,request.Map,request.Actor,moved,GetTickCount64());
        DWORD written=0; if(size>0) WriteFile(file,line,static_cast<DWORD>(size),&written,nullptr); CloseHandle(file);
    }
    int __stdcall ReceiveChat(const char* body,unsigned int type,unsigned int userId) noexcept
    {
        if(type!=16||body==nullptr) return 0;
        // Never use strlen on caller memory, and require the terminating NUL.
        constexpr char marker[]="@DFLegacy:loot-magnet:v1@";
        std::array<char,sizeof(marker)> copy{};
        if(!ReadNative(nullptr,reinterpret_cast<std::uint32_t>(body),copy.data(),copy.size())
            ||copy.back()!=0||!dfl::loot_gather::detail::IsMarker(type,copy.data(),copy.size()-1)) return 0;
        dfl::loot_gather::detail::Request request{};
        // Native callers push a word UID in EDX without clearing its high half.
        // 849342 / 849380 explicitly read only the low word too.
        if(!dfl::loot_gather::detail::Capture(Native,userId&0xFFFF,request)) return 0;
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
            push 00849289h
            ret
        consumed:
            popad
            popfd
            ret 18h
        }
    }
}

namespace dfl::loot_gather
{
    void OnFrame() noexcept
    {
        detail::Request request{}; DWORD thread=0; ULONGLONG queued=0;
        AcquireSRWLockExclusive(&PendingLock);
        const bool pending=HavePending;
        if(pending) { request=Pending; thread=PendingThread; queued=PendingAt; HavePending=false; }
        ReleaseSRWLockExclusive(&PendingLock);
        if(!pending) return;
        if(thread!=GetCurrentThreadId()||GetTickCount64()-queued>1500)
        { Diagnostic("expired-or-wrong-thread",request); return; }
        std::size_t moved=0;
        const auto reason=detail::Gather(Native,request,moved);
        Diagnostic(reason?reason:"gathered",request,moved);
    }
    bool Install() noexcept
    {
        if(reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr))!=0x400000) return false;
        std::array<unsigned char,ChatPrologue.size()> original{};
        std::array<unsigned char,ChatPrologue.size()> patch{}; patch.fill(0x90); patch[0]=0xE9;
        const auto displacement=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&ChatHook)-ChatEntry-5);
        std::memcpy(patch.data()+1,&displacement,4);
        if(!ReadNative(nullptr,ChatEntry,original.data(),original.size())) return false;
        if(original==patch) return true;
        // Bind the argument interpretation and callee cleanup to the original PE.
        constexpr std::array<unsigned char,12> typeAndUid{0x8B,0x75,0x0C,0x83,0xFE,0x12,0x75,0x2D,0x8B,0x0D,0xE8,0xDC};
        constexpr std::array<unsigned char,7> uidRead{0x0F,0xB7,0x4D,0x10,0x3B,0xC8,0x0F};
        constexpr std::array<unsigned char,3> cleanup{0xC2,0x18,0x00};
        std::array<unsigned char,typeAndUid.size()> a{}; std::array<unsigned char,uidRead.size()> b{};
        std::array<unsigned char,cleanup.size()> c{};
        if(original!=ChatPrologue||!ReadNative(nullptr,0x8492D6,a.data(),a.size())||a!=typeAndUid
            ||!ReadNative(nullptr,0x849342,b.data(),b.size())||b!=uidRead
            ||!ReadNative(nullptr,0x849308,c.data(),c.size())||c!=cleanup)
        { Diagnostic("install-signature-mismatch"); return false; }
        if(!memory_patch::WriteBytes(ChatEntry,patch.data(),patch.size())) return false;
        Diagnostic("installed-849280"); return true;
    }
}
