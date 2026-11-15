#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "gameplay_trace.h"
#include <array>
#include <algorithm>
#include <cstring>
#include <cwchar>
#include <sstream>
#include <locale>
namespace dfl::gameplay_trace
{
    namespace
    {
        constexpr std::size_t Capacity=64,Payload=2048;
        struct Event
        {
            std::uint64_t serial=0,tick=0,counter=0;
            std::uint32_t thread=0,context=0,buffer=0,length=0,captured=0;
            bool provider=false;
            std::uint32_t frame=0;bool movement=false;debug_api::MovementGate gate{};
            std::array<unsigned char,Payload> bytes{};
        };
        std::array<Event,Capacity> Ring{};
        std::size_t Count=0;
        std::uint64_t Serial=0,Overwritten=0,Exported=0,LastExport=0,Invalid=0,ExportedInvalid=0;
        LONG ExportedLockDrops=0;
        volatile LONG Active=0,LockDrops=0;
        SRWLOCK Lock=SRWLOCK_INIT;
        client_state::Reader Native{};
        wchar_t Directory[MAX_PATH]{};
        bool Copy(Event& e,const void* input) noexcept
        {
            __try{std::memcpy(e.bytes.data(),input,e.captured);return true;}
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
    }
    bool Enabled() noexcept{return InterlockedCompareExchange(&Active,0,0)==1;}
    void Initialize(const wchar_t* settings,const wchar_t* directory,client_state::Reader reader) noexcept
    {
        InterlockedExchange(&Active,0);
        if(!settings||!directory||wcsnlen_s(directory,MAX_PATH)>=MAX_PATH-64
            ||GetPrivateProfileIntW(L"DebugOverlay",L"ExportGameplayTrace",0,settings)!=1
            ||!reader.pid||!reader.counterUs)return;
        if(!reader.processStartedFileTime)
        {
            FILETIME created{},exited{},kernel{},user{};
            if(reader.pid!=GetCurrentProcessId()||!GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user))return;
            reader.processStartedFileTime=(static_cast<std::uint64_t>(created.dwHighDateTime)<<32)|created.dwLowDateTime;
        }
        Native=reader;wcscpy_s(Directory,directory);Ring={};Count=0;
        Serial=Overwritten=Exported=LastExport=Invalid=ExportedInvalid=0;LockDrops=ExportedLockDrops=0;
        InterlockedExchange(&Active,1);
    }
    void Observe(std::uint32_t context,const void* input,std::uint32_t length,bool provider,std::uint32_t frame) noexcept
    {
        if(!Enabled())return;
        if(!TryAcquireSRWLockExclusive(&Lock)){InterlockedIncrement(&LockDrops);return;}
        if(!input||length<7||length>0x100000){++Invalid;ReleaseSRWLockExclusive(&Lock);return;}
        Event e;e.length=length;e.captured=(std::min)(length,static_cast<std::uint32_t>(Payload));
        if(!Copy(e,input)||((e.bytes[1]|(static_cast<unsigned>(e.bytes[2])<<8))>41))
        {++Invalid;ReleaseSRWLockExclusive(&Lock);return;}
        e.serial=++Serial;e.tick=GetTickCount64();e.counter=Native.counterUs(Native.context);
        e.thread=GetCurrentThreadId();e.context=context;e.provider=provider;e.frame=frame;
        e.buffer=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(input));
        Ring[(Serial-1)%Capacity]=e;if(Count<Capacity)++Count;else ++Overwritten;
        ReleaseSRWLockExclusive(&Lock);
    }
    void ObserveGate(const debug_api::MovementGate& gate) noexcept
    {
        if(!Enabled())return;
        if(!TryAcquireSRWLockExclusive(&Lock)){InterlockedIncrement(&LockDrops);return;}
        if(gate.size!=sizeof(gate)||gate.version!=1||gate.context<0x10000||gate.frame<0x10000
            ||gate.actor<0x10000||gate.controller<0x10000||gate.objectKind>65535||gate.objectUid>65535
            ||gate.playerKind>65535||gate.playerUid>65535||gate.roomX>255||gate.roomY>255)
        {++Invalid;ReleaseSRWLockExclusive(&Lock);return;}
        Event e;e.serial=++Serial;e.tick=GetTickCount64();e.counter=Native.counterUs(Native.context);
        e.thread=GetCurrentThreadId();e.context=gate.context;e.frame=gate.frame;e.movement=true;e.gate=gate;
        Ring[(Serial-1)%Capacity]=e;if(Count<Capacity)++Count;else ++Overwritten;
        ReleaseSRWLockExclusive(&Lock);
    }
    void Export() noexcept
    {
        if(!Enabled())return;
        try
        {
            const auto tick=GetTickCount64();if(LastExport&&tick>=LastExport&&tick-LastExport<200)return;LastExport=tick;
            std::array<Event,Capacity> rows{};std::size_t count=0;std::uint64_t serial=0,overwritten=0,invalid=0;
            if(!TryAcquireSRWLockShared(&Lock))return;
            count=Count;serial=Serial;overwritten=Overwritten;invalid=Invalid;
            for(std::size_t i=0;i<count;++i)rows[i]=Ring[(serial-count+i)%Capacity];
            ReleaseSRWLockShared(&Lock);
            // The throttle clock may predate an event produced before we
            // acquired the lock. Date the copied snapshot after its last event.
            const auto exportedTick=GetTickCount64();
            const auto drops=InterlockedCompareExchange(&LockDrops,0,0);
            if(serial==Exported&&invalid==ExportedInvalid&&drops==ExportedLockDrops)return;
            if(!CreateDirectoryW(Directory,nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return;
            std::ostringstream o;o.imbue(std::locale::classic());
            o<<"{\"schema\":\"dfl-native-gameplay-trace/v2\",\"pid\":"<<Native.pid
                <<",\"process_started_filetime\":"<<Native.processStartedFileTime<<",\"serial\":"<<serial
                <<",\"exported_tick_ms\":"<<exportedTick<<",\"capacity\":64,\"capacity_overwrites\":"<<overwritten
                <<",\"lock_drops\":"<<drops<<",\"invalid_records\":"<<invalid<<",\"events\":[";
            constexpr char hex[]="0123456789abcdef";
            for(std::size_t i=0;i<count;++i)
            {
                if(i)o<<',';const auto& e=rows[i];
                o<<"{\"serial\":"<<e.serial<<",\"tick_ms\":"<<e.tick<<",\"counter_us\":"<<e.counter
                    <<",\"thread\":"<<e.thread<<",\"context_token\":"<<e.context<<",\"buffer_token\":"<<e.buffer
                    <<",\"event_kind\":\""<<(e.movement?"gp4_movement_entered":"accepted")<<"\",\"frame_token\":"<<e.frame
                    <<",\"native_gate\":";
                if(!e.movement)o<<"null";
                else
                {
                    const auto& g=e.gate;
                    o<<"{\"actor_token\":"<<g.actor<<",\"controller_token\":"<<g.controller<<",\"movement_flags\":"<<g.flags
                        <<",\"object_kind\":"<<g.objectKind<<",\"object_uid\":"<<g.objectUid<<",\"player_kind\":"<<g.playerKind
                        <<",\"player_uid\":"<<g.playerUid<<",\"room_x\":"<<g.roomX<<",\"room_y\":"<<g.roomY<<'}';
                }
                o
                    <<",\"provider_buffer_match\":"<<(e.provider?"true":"false")<<",\"length\":"<<e.length
                    <<",\"captured_bytes\":"<<e.captured<<",\"truncated\":"<<(e.length>e.captured?"true":"false")<<",\"hex\":\"";
                for(std::size_t j=0;j<e.captured;++j)o<<hex[e.bytes[j]>>4]<<hex[e.bytes[j]&15];o<<"\"}";
            }
            o<<"],\"source\":\"Accepted: ACT1 78EF2B after CRC gate, before opcode dispatch. Movement: 78F22C after native object/room/association gates, before original motion call. Entry is not completion or final position proof. Provider buffer match is a pointer comparison, not a provider branch event.\"}";
            const auto json=o.str();wchar_t path[MAX_PATH]{},temporary[MAX_PATH]{};
            if(swprintf_s(path,L"%s\\dfdebug-gameplay-trace-%lu.json",Directory,Native.pid)<0||swprintf_s(temporary,L"%s.tmp",path)<0)return;
            const auto file=CreateFileW(temporary,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);if(file==INVALID_HANDLE_VALUE)return;
            DWORD written=0;const bool good=WriteFile(file,json.data(),static_cast<DWORD>(json.size()),&written,nullptr)&&written==json.size();CloseHandle(file);
            if(good&&MoveFileExW(temporary,path,MOVEFILE_REPLACE_EXISTING))
            {Exported=serial;ExportedInvalid=invalid;ExportedLockDrops=drops;}else DeleteFileW(temporary);
        }
        catch(...){/* Only the existing worker performs diagnostic IO. */}
    }
    void Shutdown() noexcept{InterlockedExchange(&Active,0);}
}
