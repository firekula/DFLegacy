#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "client_party.h"
#include <array>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <locale>
#include <sstream>

namespace dfl::client_party
{
    namespace
    {
        client_state::Reader Native{};
        volatile LONG Enabled=0;
        DWORD GameThread=0;
        wchar_t Directory[MAX_PATH]{};
        SRWLOCK InboxLock=SRWLOCK_INIT,PublicationLock=SRWLOCK_INIT;
        bool HaveInbox=false,HaveFile=false;
        Request Inbox{};
        BY_HANDLE_FILE_INFORMATION PreviousFile{};
        std::uint64_t ConsumedId=0,ConsumedPrompt=0,NextPrompt=0,LastPoll=0,PublishedSerial=0,ExportedSerial=0;
        std::uint64_t SessionFloor=0;
        // Worker-only successful export cache. Never compare main-thread
        // publication bytes/padding or retimestamp a prior decision.
        std::string ExportedStateKey,ExportedResultJson;
        std::uint64_t LastStateExportTick=0;
        Pending PreviousPrompt{};
        const char* NativeRequestFailure=nullptr;
        struct Result { Request request{};const char* status="rejected";const char* reason="unknown";std::uint64_t tick=0; };
        struct Publication
        {
            Request binding{};Pending pending{};bool available=false;const char* reason="unavailable";
            std::uint64_t tick=0,serial=0,consumedRequest=0;Result result{};bool haveResult=false;
        };
        Publication Game{},Published{};
        bool IsEnabled() noexcept {return InterlockedCompareExchange(&Enabled,0,0)!=0;}
        std::uint64_t Now() noexcept {return Native.tickMs?Native.tickMs(Native.context):GetTickCount64();}
        template<class T> bool Read(std::uint32_t base,std::uint32_t offset,T& value) noexcept
        {
            const auto at=static_cast<std::uint64_t>(base)+offset;
            return base>=0x10000 && at+sizeof(T)<=0x100000000ULL && Native.read
                && Native.read(Native.context,static_cast<std::uint32_t>(at),&value,sizeof(T));
        }
        template<std::size_t N> bool Signature(std::uint32_t at,const unsigned char (&expected)[N]) noexcept
        {std::array<unsigned char,N> actual{};return Read(at,0,actual)&&std::memcmp(actual.data(),expected,N)==0;}
        bool Layout() noexcept
        {
            if(!Native.supported||!Native.supported(Native.context))return false;
            constexpr unsigned char request[]={0x55,0x8B,0xEC,0x83,0xEC,0x08,0x8B,0x45,0x08,0x8B,0x0D,0xB8,0xDC,0xB5,0x00};
            constexpr unsigned char accept[]={0x55,0x8B,0xEC,0x83,0xEC,0x08,0x56,0x8B,0xF1,0x8B,0x0D,0xB8,0xDC,0xB5,0x00};
            constexpr unsigned char begin[]={0x55,0x8B,0xEC,0x83,0xEC,0x10,0x89,0x4D,0xF0};
            constexpr unsigned char write[]={0x55,0x8B,0xEC,0x8A,0x41,0x08,0x84,0xC0,0x75,0x09};
            constexpr unsigned char send[]={0x55,0x8B,0xEC,0x83,0xEC,0x58,0x89,0x4D,0xA8};
            constexpr unsigned char close[]={0x51,0x8B,0x49,0x10,0xE8};
            std::uint32_t yes=0,dispose=0;
            return Signature(0x4FE9B0,request)&&Signature(0x821DA0,accept)&&Signature(0x8B2650,begin)
                &&Signature(0x8B2450,write)&&Signature(0x8B24C0,send)&&Signature(0x7BDC50,close)
                &&Read(0xA7E2F4,0x48,yes)&&yes==0x821DA0&&Read(0xA7E2F4,0x28,dispose)&&dispose==0x7BDC50;
        }
        Request Bind(const client_state::Snapshot& s) noexcept
        {
            Request r;r.pid=s.pid;r.uid=s.me.uid.known?s.me.uid.value:0;r.processStartedFileTime=s.processStartedFileTime;
            r.snapshotTickMs=s.capturedTickMs;r.generation=s.generation;r.snapshotSequence=s.sequence;
            r.actorToken=s.me.actorToken;r.mapToken=s.me.mapToken;r.userToken=s.me.userToken;r.contextToken=s.town.contextToken;
            r.townId=s.town.townId;r.areaId=s.town.areaId;return r;
        }
        bool Socket(std::uint32_t& socket) noexcept
        {
            unsigned char connected=0,initialized=0,blocked=0;std::uint32_t size=0,buffer=0,write=0,read=0;
            return Read(0xBF0A9C,0,socket)&&Read(socket,4,initialized)&&initialized==1&&Read(socket,8,connected)&&connected==1
                &&Read(socket,0x15E1E8,blocked)&&blocked==0
                &&Read(socket,0x15E1C8,size)&&size==0&&Read(socket,0x15E1CC,buffer)&&buffer==0
                // Native begin stores the ring's read pointer, while write appends at its write index.
                // Require drained ring and contiguous room: header12 + largest party body9.
                &&Read(socket,0x15E1C0,write)&&Read(socket,0x15E1C4,read)&&write==read&&write<=0xAF000-21;
        }
        bool Live(void*,const Request& request) noexcept
        {
            std::uint32_t manager=0,socket=0;
            if(!Layout()||!ReadLiveContext(Native,request,manager,true)||!Socket(socket))return false;
            Pending pending;
            if(!ReadPending(Native,manager,pending))return false;
            if(request.operation==1)return !pending.present;
            return pending.present&&pending.canReply&&pending.peerType==0&&pending.windowToken==request.windowToken
                &&pending.requesterUid==request.targetUid&&pending.timerOffset==Game.pending.timerOffset
                &&request.invitationSerial==Game.pending.serial&&pending.remainingMs>0;
        }
        bool NativeRequest(void*,const Request& request) noexcept
        {
            NativeRequestFailure=ReadRequestFailure(Native,request);
            __try
            {
                using Function=bool (__cdecl*)(std::uint32_t);
                return reinterpret_cast<Function>(0x4FE9B0)(request.targetUid);
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        bool NativeAccept(void*,const Request& request) noexcept
        {
            __try
            {
                // Original yes callback calls 4FEBC0, then queues this exact window for normal deletion.
                using Function=void (__thiscall*)(void*);
                reinterpret_cast<Function>(0x821DA0)(reinterpret_cast<void*>(request.windowToken));return true;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        std::int32_t OriginalBegin(void* socket,std::uint32_t command) noexcept
        {using F=std::int32_t (__thiscall*)(void*,std::uint32_t);return reinterpret_cast<F>(0x8B2650)(socket,command);}
        std::int32_t OriginalWrite(void* socket,const void* bytes,std::uint32_t size) noexcept
        {using F=std::int32_t (__thiscall*)(void*,const void*,std::uint32_t);return reinterpret_cast<F>(0x8B2450)(socket,bytes,size);}
        std::int32_t OriginalSend(void* socket) noexcept
        {using F=std::int32_t (__thiscall*)(void*);return reinterpret_cast<F>(0x8B24C0)(socket);}
        bool NativeReject(void*,const Request& request) noexcept
        {
            __try
            {
                // The original no branch (821FB2..82202F) is inline, not a callable function.
                // Reproduce its party-only body through the SAME original client builder/send/close.
                // No external packet, fake button capture ID, or mid-function ABI jump is used.
                std::uint32_t socket=0;if(!Socket(socket))return false;
                using Close=void (__thiscall*)(void*);
                const RejectionWriter writer{reinterpret_cast<void*>(socket),&OriginalBegin,&OriginalWrite,&OriginalSend};
                if(!SendRejection(request.targetUid,writer))return false;
                reinterpret_cast<Close>(0x7BDC50)(reinterpret_cast<void*>(request.windowToken));
                return true;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        void Flush() noexcept
        {
            if(!TryAcquireSRWLockExclusive(&PublicationLock))return;
            Published=Game;ReleaseSRWLockExclusive(&PublicationLock);
        }
        bool SameFile(const BY_HANDLE_FILE_INFORMATION& a,const BY_HANDLE_FILE_INFORMATION& b) noexcept
        {
            return a.dwVolumeSerialNumber==b.dwVolumeSerialNumber&&a.nFileIndexHigh==b.nFileIndexHigh&&a.nFileIndexLow==b.nFileIndexLow
                &&a.ftLastWriteTime.dwHighDateTime==b.ftLastWriteTime.dwHighDateTime&&a.ftLastWriteTime.dwLowDateTime==b.ftLastWriteTime.dwLowDateTime
                &&a.nFileSizeHigh==b.nFileSizeHigh&&a.nFileSizeLow==b.nFileSizeLow;
        }
        void ReadCommand() noexcept
        {
            wchar_t path[MAX_PATH]{};if(swprintf_s(path,L"%s\\dfdebug-party-%lu.bin",Directory,Native.pid)<0)return;
            const auto file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE)return;
            BY_HANDLE_FILE_INFORMATION info{};std::array<unsigned char,sizeof(Request)+1> bytes{};DWORD count=0;
            const bool okay=GetFileInformationByHandle(file,&info)&&(!HaveFile||!SameFile(info,PreviousFile))&&info.nFileSizeHigh==0
                &&info.nFileSizeLow==sizeof(Request)&&ReadFile(file,bytes.data(),static_cast<DWORD>(bytes.size()),&count,nullptr)&&count==sizeof(Request);
            CloseHandle(file);if(!okay||!TryAcquireSRWLockExclusive(&InboxLock))return;
            if(!HaveInbox){std::memcpy(&Inbox,bytes.data(),sizeof(Inbox));HaveInbox=true;PreviousFile=info;HaveFile=true;}
            ReleaseSRWLockExclusive(&InboxLock);
        }
        void Token(std::ostream& out,std::uint32_t value)
        {char text[16]{};std::snprintf(text,sizeof(text),"0x%08X",value);out<<'"'<<text<<'"';}
        void Identity(std::ostream& out,const Request& r,bool transient=true)
        {
            out<<"\"pid\":"<<r.pid<<",\"uid\":"<<r.uid<<",\"process_started_filetime\":"<<r.processStartedFileTime
                <<",\"generation\":"<<r.generation;
            if(transient)out<<",\"snapshot_sequence\":"<<r.snapshotSequence<<",\"snapshot_tick_ms\":"<<r.snapshotTickMs;
            out<<",\"actor_token\":";Token(out,r.actorToken);
            out<<",\"map_token\":";Token(out,r.mapToken);out<<",\"user_token\":";Token(out,r.userToken);
            out<<",\"context_token\":";Token(out,r.contextToken);out<<",\"town_id\":"<<r.townId<<",\"area_id\":"<<r.areaId;
        }
        bool WriteJson(const wchar_t* suffix,const std::string& json)
        {
            wchar_t path[MAX_PATH]{},temporary[MAX_PATH]{};
            if(swprintf_s(path,L"%s\\dfdebug-party-%lu%s.json",Directory,Native.pid,suffix)<0||swprintf_s(temporary,L"%s.tmp",path)<0)return false;
            const auto file=CreateFileW(temporary,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE)return false;
            DWORD written=0;const bool okay=json.size()<=MAXDWORD&&WriteFile(file,json.data(),static_cast<DWORD>(json.size()),&written,nullptr)
                &&written==json.size();
            CloseHandle(file);
            if(okay&&MoveFileExW(temporary,path,MOVEFILE_REPLACE_EXISTING))return true;
            DeleteFileW(temporary);return false;
        }
        std::string StateJson(const Publication& p,bool transient)
        {
            std::ostringstream out;out.imbue(std::locale::classic());out<<"{\"schema\":\"dfl-client-party/v1\",";Identity(out,p.binding,transient);
            if(transient)out<<",\"tick_ms\":"<<p.tick;
            out<<",\"consumed_request_id\":"<<p.consumedRequest<<",\"available\":"<<(p.available?"true":"false")<<",\"reason\":\""<<p.reason<<"\",\"pending\":";
            if(!p.pending.present)out<<"null";
            else
            {
                const auto& q=p.pending;out<<"{\"window_token\":";Token(out,q.windowToken);
                out<<",\"serial\":"<<q.serial<<",\"requester_uid\":"<<q.requesterUid<<",\"peer_type\":"<<q.peerType;
                if(transient)out<<",\"remaining_ms\":"<<q.remainingMs;
                out<<",\"can_reply\":"<<(q.canReply?"true":"false")<<",\"reason\":\""<<q.reason<<"\"}";
            }
            out<<'}';return out.str();
        }
        std::string ResultJson(const Result& result)
        {
            std::ostringstream out;out.imbue(std::locale::classic());const auto& r=result.request;
            out<<"{\"schema\":\"dfl-client-party-result/v1\",";Identity(out,r);
            out<<",\"request_id\":"<<r.requestId<<",\"operation\":"<<r.operation<<",\"target_uid\":"<<r.targetUid<<",\"window_token\":";Token(out,r.windowToken);
            out<<",\"invitation_serial\":"<<r.invitationSerial<<",\"tick_ms\":"<<result.tick<<",\"status\":\""<<result.status
                <<"\",\"reason\":\""<<result.reason<<"\"}";
            return out.str();
        }
        void Export(const Publication& p)
        {
            const auto resultJson=p.haveResult?ResultJson(p.result):std::string{};
            // A new decision also publishes state promptly. Only continuously
            // changing clock/sequence/countdown fields wait for the heartbeat.
            const auto stateKey=StateJson(p,false)+'\n'+resultJson;
            const auto now=Now();
            const bool stateDue=ExportedStateKey.empty()||stateKey!=ExportedStateKey
                ||now<LastStateExportTick||now-LastStateExportTick>=200;
            bool stateWritten=true,resultWritten=true;
            if(stateDue)
            {
                stateWritten=WriteJson(L"",StateJson(p,true));
                if(stateWritten){ExportedStateKey=stateKey;LastStateExportTick=now;}
            }
            if(p.haveResult&&resultJson!=ExportedResultJson)
            {
                resultWritten=WriteJson(L".result",resultJson);
                if(resultWritten)ExportedResultJson=resultJson;
            }
            if(stateWritten&&resultWritten)ExportedSerial=p.serial;
        }
    }
    void Initialize(const wchar_t* settings,const wchar_t* directory,client_state::Reader reader) noexcept
    {
        InterlockedExchange(&Enabled,0);
        LastStateExportTick=0;ExportedStateKey.clear();ExportedResultJson.clear();
        if(!settings||!directory||wcsnlen_s(directory,MAX_PATH)>=MAX_PATH-64
            ||GetPrivateProfileIntW(L"DebugOverlay",L"EnablePartyCommands",0,settings)!=1)return;
        const bool sameProcess=Native.pid&&Native.pid==reader.pid&&Native.processStartedFileTime==reader.processStartedFileTime;
        if(!sameProcess){ConsumedId=ConsumedPrompt=NextPrompt=0;PreviousPrompt={};}
        Native=reader;SessionFloor=Now();wcscpy_s(Directory,directory);GameThread=0;HaveInbox=HaveFile=false;Inbox={};Game={};Published={};
        LastPoll=PublishedSerial=ExportedSerial=0;
        InterlockedExchange(&Enabled,Native.read&&Native.pid&&Native.processStartedFileTime?1:0);
    }
    void OnFrame() noexcept
    {
        if(!IsEnabled())return;
        if(!GameThread)GameThread=GetCurrentThreadId();if(GameThread!=GetCurrentThreadId())return;
        client_state::Snapshot snapshot;if(!client_state::CopyLatest(snapshot))return;
        Game.binding=Bind(snapshot);Game.tick=Now();Game.pending={};Game.available=false;Game.reason="town_unavailable";
        std::uint32_t manager=0;
        if(snapshot.valid&&snapshot.layoutSupported&&!snapshot.truncated&&snapshot.haveMe&&snapshot.scene.known&&snapshot.scene.value==1
            &&snapshot.town.present&&snapshot.capturedTickMs<=Game.tick&&Game.tick-snapshot.capturedTickMs<=750)
        {
            Game.reason="native_context_unavailable";
            if(Layout()&&ReadLiveContext(Native,Game.binding,manager,false))
            {
                Game.reason="pending_unknown";
                if(ReadPending(Native,manager,Game.pending))
                {
                    Game.available=true;Game.reason="ready";
                    if(Game.pending.present)
                    {
                        auto& p=Game.pending;AssignInvitationSerial(p,PreviousPrompt,NextPrompt);
                        if(p.serial<=ConsumedPrompt){p.canReply=false;p.reason="invitation_consumed";}
                    }
                    std::uint32_t socket=0;
                    if(!Socket(socket)){Game.available=false;Game.reason="native_transport_busy";}
                }
            }
        }
        Request request;bool have=false;
        if(TryAcquireSRWLockExclusive(&InboxLock))
        {if(HaveInbox){request=Inbox;HaveInbox=false;have=true;}ReleaseSRWLockExclusive(&InboxLock);}
        if(have)
        {
            const Operations ops{nullptr,&Live,&NativeRequest,&NativeAccept,&NativeReject};
            NativeRequestFailure=nullptr;
            const char* error=Perform(request,snapshot,Game.pending,Now(),ConsumedId,ConsumedPrompt,ops,SessionFloor);
            if(error&&std::strcmp(error,"native_action_failed")==0&&NativeRequestFailure)error=NativeRequestFailure;
            Game.result={request,error?"rejected":"dispatched",error?error:(request.operation==1?"native_request_sent":
                (request.operation==2?"native_accept_callback":"native_reject_sent")),Now()};Game.haveResult=true;
        }
        Game.consumedRequest=ConsumedId;Game.serial=++PublishedSerial;Flush();
    }
    void PumpWorker() noexcept
    {
        if(!IsEnabled())return;
        try
        {
            const auto now=Now();if(now-LastPoll<50)return;LastPoll=now;ReadCommand();
            Publication copy;if(!TryAcquireSRWLockShared(&PublicationLock))return;
            copy=Published;ReleaseSRWLockShared(&PublicationLock);
            if(copy.serial&&copy.serial!=ExportedSerial)Export(copy);
        }
        catch(...){/* Disk errors never escape the existing debug worker. */}
    }
    void Shutdown() noexcept {InterlockedExchange(&Enabled,0);}
}
