#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "client_navigation.h"
#include "client_town_route_runtime.h"
#include "client_map_feedback_runtime.h"
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <iomanip>
#include <locale>
#include <sstream>

namespace dfl::client_navigation
{
    namespace
    {
        client_state::Reader Native{};
        volatile LONG Enabled=0;
        volatile LONG HookPublished=0;
        bool Installed=false, HitInstalled=false, HookAttempted=false;
        bool Diagnostics=false, Commands=false;
        DWORD GameThread=0;
        wchar_t Directory[MAX_PATH]{};
        SRWLOCK InboxLock=SRWLOCK_INIT, OutboxLock=SRWLOCK_INIT;
        bool HaveInbox=false;
        Request Inbox{}, Active{};
        client_state::Snapshot Current{};
        bool Tracking=false;
        std::uint64_t ConsumedId=0, ActiveTick=0, ActiveSequence=0;
        struct Result
        {
            Request request{};
            const char* source="debug";
            const char* status="rejected";
            const char* reason="unknown";
            std::uint64_t tick=0, serial=0;
            client_state::Position position{};
        };
        Result Outbox{};
        Result GameResult{};
        std::uint64_t NextSerial=0, ExportedSerial=0, LastPoll=0;
        BY_HANDLE_FILE_INFORMATION PreviousFile{};
        bool HaveFile=false;
        constexpr unsigned char OriginalCall[]={0xE8,0xD9,0x3B,0x0E,0x00};
        std::array<unsigned char,5> PatchedCall{};
        constexpr std::uint32_t CallAddress=0x79D242;
        constexpr std::uint32_t HitCallAddress=0x8F6FAF;
        std::array<unsigned char,5> PatchedHitCall{};
        struct Reads
        {
            template<class T> bool At(std::uint32_t base, std::uint32_t offset, T& value) const noexcept
            {
                const auto address=static_cast<std::uint64_t>(base)+offset;
                return base>=0x10000 && address+sizeof(T)<=0x100000000ULL && Native.read
                    && Native.read(Native.context,static_cast<std::uint32_t>(address),&value,sizeof(T));
            }
            bool Secure(std::uint32_t address,std::uint32_t& output) const noexcept
            {
                std::uint32_t manager=0,key=0,encoded=0,index=0,count=0,a=0,b=0,va=0,vb=0;
                if (!At(0xCFD458,0,manager) || !At(0xCFD45C,0,key) || !At(address,0,encoded) || !At(address,4,index)
                    || ((encoded^key)-address)!=index || !At(manager,4,count) || index>=count || index>0x1000000
                    || !At(manager,0x44,a) || !At(manager,0x48,b) || a<0x10000 || b<0x10000
                    || a>UINT32_MAX-index*4 || b>UINT32_MAX-index*4) return false;
                a+=index*4;b+=index*4;
                if (!At(a,0,va) || !At(b,0,vb) || (va^a^key)!=(vb^b^key)) return false;
                output=va^a^key;return true;
            }
        };
        std::uint64_t Now() noexcept { return Native.tickMs?Native.tickMs(Native.context):GetTickCount64(); }
        bool Layout() noexcept { return Native.supported && Native.supported(Native.context); }
        bool CallSignatures() noexcept
        {
            constexpr unsigned char setter[]={0x55,0x8B,0xEC,0x8B,0x81,0xC8,0x03,0,0,0x85,0xC0,0x74,0x0A,0x8A,0x90,0xA0};
            constexpr unsigned char point[]={0x55,0x8B,0xEC,0x8B,0x45,0x10,0x85,0xC0,0x74,0x06,0xC7,0,0,0,0,0};
            constexpr unsigned char activate[]={0xC6,0x05,0xA0,0xF0,0xB5,0x00,0x01};
            constexpr unsigned char update[]={0xA0,0xA0,0xF0,0xB5,0x00,0x84,0xC0,0x0F,0x84,0xB1,0,0,0};
            Reads r;std::array<unsigned char,16> a{},b{};std::array<unsigned char,7> c{};std::array<unsigned char,13> d{};
            return r.At(0x478F70,0,a) && r.At(0x479EF0,0,b)
                && r.At(0x79CD07,0,c) && r.At(0x47B24C,0,d)
                && std::memcmp(a.data(),setter,sizeof(setter))==0 && std::memcmp(b.data(),point,sizeof(point))==0
                && std::memcmp(c.data(),activate,sizeof(activate))==0 && std::memcmp(d.data(),update,sizeof(update))==0;
        }
        bool IsEnabled() noexcept { return InterlockedCompareExchange(&Enabled,0,0)!=0; }
        void FlushResult() noexcept
        {
            if (!GameResult.serial || !TryAcquireSRWLockExclusive(&OutboxLock)) return;
            Outbox=GameResult;
            ReleaseSRWLockExclusive(&OutboxLock);
        }
        void Publish(const Request& request,const char* source,const char* status,const char* reason,const client_state::Position& position={}) noexcept
        {
            // Main-thread never waits for worker serialization or disk IO.
            GameResult={request,source,status,reason,Now(),++NextSerial,position};
            FlushResult();
        }
        bool Live(const Request& request) noexcept
        {
            if (!Layout() || !CallSignatures() || request.pid!=Native.pid || request.processStartedFileTime!=Native.processStartedFileTime) return false;
            Reads r;std::uint32_t main=0,scene=0,context=0,areaObject=0,actor=0,table=0,uid=0,map=0,binding=0,transport=0;
            std::int32_t town=-1,area=-1,userState=5;std::uint16_t userUid=0;unsigned char busy=0,autoMode=0;
            std::uint32_t f=0,im=0,selectionMode=0;
            if (!r.At(0xB5D6F0,0,main) || main>UINT32_MAX-0x10 || !r.Secure(main+0x10,scene) || scene!=1
                || !r.At(main,0x1C,context) || context!=request.contextToken || !r.At(context,0x88,town) || town!=request.townId
                || !r.At(context,0x8C,areaObject) || !r.At(areaObject,0,area) || area!=request.areaId
                || !r.At(0xB75A18,0,actor) || actor!=request.actorToken || !r.At(actor,0,table) || table!=0x9988DC
                || actor>UINT32_MAX-0xD4 || !r.Secure(actor+0xD4,uid) || uid!=request.uid
                || !r.At(actor,0xE4,map) || !map || map!=request.mapToken
                || !r.At(request.userToken,0x10C,binding) || binding!=actor || !r.At(request.userToken,4,userUid) || userUid!=uid
                || !r.At(request.userToken,0x2C,userState) || userState==5
                || !r.At(0xB5DCE8,0,im) || !r.At(im,0xFC4,selectionMode) || selectionMode!=0
                || !r.At(0xB5F0A0,0,autoMode) || autoMode>1
                || !r.At(table,0x54,f) || f!=0x407400 || !r.At(table,0x94,f) || f!=0x479EF0
                || !r.At(table,0x154,f) || f!=0x47A8E0 || !r.At(table,0x178,f) || f!=0x478F70 || !r.At(table,0x1A4,f) || f!=0x4795F0
                || !r.At(actor,0x3C8,transport) || (transport && (!r.At(transport,0x1A0,busy) || busy))) return false;
            // Original point checker returns true with missing map data. Prove its entire input vector first.
            std::uint32_t mapArea=0,definition=0,begin=0,end=0;
            if (!r.At(map,0xF4,mapArea) || !r.At(mapArea,0x50,definition)
                || !r.At(definition,0x154,begin) || !begin || !r.At(definition,0x158,end)
                || end<=begin || (end-begin)%16 || (end-begin)/16>256) return false;
            std::array<std::int32_t,4> rectangle{};
            for (std::uint32_t i=0;i<(end-begin)/16;++i)
                if (!r.At(begin,i*16,rectangle) || rectangle[2]<=0 || rectangle[3]<=0 || rectangle[2]>1000000 || rectangle[3]>1000000
                    || static_cast<std::int64_t>(rectangle[0])+rectangle[2]>INT32_MAX
                    || static_cast<std::int64_t>(rectangle[1])+rectangle[3]>INT32_MAX) return false;
            std::uint32_t finalActor=0,finalMap=0,finalContext=0;
            return r.At(0xB75A18,0,finalActor) && finalActor==actor && r.At(actor,0xE4,finalMap) && finalMap==map
                && r.At(main,0x1C,finalContext) && finalContext==context;
        }
        bool NativePointAndDestination(void*,const Request& request) noexcept
        {
            // Only this leaf invokes game code. ABIs verified against original PE; bool is AL, setter returns void.
            __try
            {
                if(!Live(request))return false;
                using Point=bool (__thiscall*)(void*,std::int32_t,std::int32_t,std::int32_t*);
                using Set=void (__thiscall*)(void*,std::int32_t,std::int32_t);
                auto* actor=reinterpret_cast<void*>(request.actorToken);
                if (!reinterpret_cast<Point>(0x479EF0)(actor,request.x,request.y,nullptr) || !Live(request)) return false;
                reinterpret_cast<Set>(0x478F70)(actor,request.x,request.y);
                Reads r;std::int32_t x=0,y=0;
                return r.At(request.actorToken,0x23C,x) && r.At(request.actorToken,0x240,y) && x==request.x && y==request.y;
            }
            __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
        }
        bool NativeActivate(void*,const Request& request) noexcept
        {
            // The natural ground consumer writes this once at 79CD07. Town v+154 then
            // invokes v+1A4 every update; original keyboard/menu paths can clear it.
            __try
            {
                if (!Live(request)) return false;
                Reads reads;std::int32_t x=0,y=0;
                if (!reads.At(request.actorToken,0x23C,x) || !reads.At(request.actorToken,0x240,y)
                    || x!=request.x || y!=request.y) return false;
                auto* flag=reinterpret_cast<volatile unsigned char*>(0xB5F0A0);
                *flag=1;
                return *flag==1;
            }
            __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
        }
        const char* ActiveSource="debug";
        bool ApplyRouteStep(const Request& request) noexcept
        {
            const DestinationOperations operations{nullptr,&NativePointAndDestination,&NativeActivate};
            return SetLiveAndActivate(request,operations,&Live,Now());
        }
        void SupersedeForRoute() noexcept
        {
            if(Tracking)Publish(Active,ActiveSource,"interrupted","superseded",Current.town.position);
            Tracking=false;
        }
        void Dispatch(const Request& request,const char* source) noexcept
        {
            // The mailbox exposes the latest decision. An older request must never replace
            // this request's terminal rejection with a late arrival/context result.
            // This ends observation only; it does not invoke an unproven movement cancel.
            Tracking=false;
            const auto now=Now();const auto reason=ValidateRequest(request,Current,now,source[0]=='d'?ConsumedId:0);
            if (source[0]=='d' && request.requestId>ConsumedId) ConsumedId=request.requestId;
            if (reason) { Publish(request,source,"rejected",reason);return; }
            if (!Live(request)) { Publish(request,source,"rejected","live_context_unavailable");return; }
            const DestinationOperations operations{nullptr,&NativePointAndDestination,&NativeActivate};
            if (!SetAndActivate(request,operations)) { Publish(request,source,"rejected","point_invalid_or_native_rejected");return; }
            client_town_route::LegacyTakeover();
            Active=request;ActiveTick=now;ActiveSequence=Current.sequence;ActiveSource=source;Tracking=true;
            Publish(request,source,"requested","native_destination_set",Current.town.position);
        }
        void OriginalMinimapPoll(void* self) noexcept
        {
            using Original=void (__thiscall*)(void*);
            reinterpret_cast<Original>(0x880E20)(self);
        }
        void MinimapPollWithOriginal(void* self,void (*original)(void*) noexcept) noexcept
        {
            if (!original) return;
            original(self);
        }
        void __fastcall MinimapPoll(void* self,void*) noexcept
        { MinimapPollWithOriginal(self,&OriginalMinimapPoll); }
        std::int32_t OriginalHit(void* self) noexcept
        {
            using Original=std::int32_t (__thiscall*)(void*);
            return reinterpret_cast<Original>(0x8F6C30)(self);
        }
        void AfterCanvasHit(void*,std::uint32_t eventBefore) noexcept
        {
            if (!IsEnabled() || GetCurrentThreadId()!=GameThread || !Layout()) return;
            Reads reads;std::uint32_t im=0,minimap=0;CanvasGoal goal;
            if(reads.At(0xB5DCE8,0,im)&&reads.At(im,0xFF0,minimap)
                &&ReadRouteCanvasTarget(Native,Current,minimap,goal,eventBefore))
            {
                if(client_town_route::BeginCanvasGoal(Current,goal))client_map_feedback::AcceptGoal(Current,goal);
            }
        }
        std::int32_t HitPollWithOriginal(void* self,CanvasHitOriginal original) noexcept
        {
            // Reading the pre-event has no UI side effects. Native traversal
            // always runs first; only its consumed release can select a goal.
            std::uint32_t eventBefore=0;
            if(IsEnabled()&&GetCurrentThreadId()==GameThread&&Layout())
            {
                Reads reads;std::uint32_t right=0;
                if (reads.At(0xCFD1F8,0,right) && right==16) eventBefore=16;
            }
            return PollCanvasRelease(self,eventBefore,original,&AfterCanvasHit);
        }
        std::int32_t __fastcall CanvasHitPoll(void* self,void*) noexcept
        {return HitPollWithOriginal(self,&OriginalHit);}
        bool PatchAt(std::uint32_t address,const unsigned char* expected,const unsigned char* replacement) noexcept
        {
            Reads r;std::array<unsigned char,5> current{};
            if (!r.At(address,0,current) || std::memcmp(current.data(),expected,5)!=0) return false;
            DWORD old=0;
            auto* target=reinterpret_cast<void*>(address);
            if (!VirtualProtect(target,5,PAGE_EXECUTE_READWRITE,&old)) return false;
            std::memcpy(target,replacement,5);FlushInstructionCache(GetCurrentProcess(),target,5);
            DWORD ignored=0;
            if (!VirtualProtect(target,5,old,&ignored))
            {
                std::memcpy(target,expected,5);FlushInstructionCache(GetCurrentProcess(),target,5);
                VirtualProtect(target,5,old,&ignored);return false;
            }
            return true;
        }
        bool Patch(const unsigned char* expected,const unsigned char* replacement) noexcept
        {return PatchAt(CallAddress,expected,replacement);}
        bool PatchHit(const unsigned char* expected,const unsigned char* replacement) noexcept
        {return PatchAt(HitCallAddress,expected,replacement);}
        void Install() noexcept
        {
            if (HookAttempted || !Layout()) return;
            HookAttempted=true;
            constexpr unsigned char caller[]={0x8B,0x8E,0xF0,0x0F,0,0};
            constexpr unsigned char poll[]={0x53,0x8B,0xD9,0x8B,0x43,0x1C,0x8A,0x48,0x10,0x56,0x57,0x8D,0x7B,0x1C,0x84,0xC9};
            constexpr unsigned char hitCaller[]={0x8B,0xCE,0xE8,0x7C,0xFC,0xFF,0xFF,0x8B,0xCE,0xE8,0xC5,0xFA,0xFF,0xFF};
            constexpr unsigned char hitEntry[]={0x55,0x8B,0xEC,0x83,0xEC,0x08,0x57,0x8B,0xF9,0x8A,0x47,0x10,0x84,0xC0,0x74,0x07};
            Reads reads;std::array<unsigned char,6> callerBytes{};std::array<unsigned char,16> pollBytes{},hitEntryBytes{};
            std::array<unsigned char,14> hitCallerBytes{};
            if (!reads.At(CallAddress-6,0,callerBytes) || std::memcmp(callerBytes.data(),caller,sizeof(caller))!=0
                || !reads.At(0x880E20,0,pollBytes) || std::memcmp(pollBytes.data(),poll,sizeof(poll))!=0
                || !reads.At(HitCallAddress-2,0,hitCallerBytes) || std::memcmp(hitCallerBytes.data(),hitCaller,sizeof(hitCaller))!=0
                || !reads.At(0x8F6C30,0,hitEntryBytes) || std::memcmp(hitEntryBytes.data(),hitEntry,sizeof(hitEntry))!=0 || !CallSignatures()) return;
            const auto target=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&MinimapPoll));
            const std::uint32_t relative=target-(CallAddress+5);
            PatchedCall[0]=0xE8;std::memcpy(PatchedCall.data()+1,&relative,4);
            const auto hitTarget=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&CanvasHitPoll));
            const std::uint32_t hitRelative=hitTarget-(HitCallAddress+5);
            PatchedHitCall[0]=0xE8;std::memcpy(PatchedHitCall.data()+1,&hitRelative,4);
            InstallCanvasHooks(Installed,HitInstalled,PatchedCall,PatchedHitCall,&Patch,&PatchHit);
            InterlockedExchange(&HookPublished,Installed?1:0);
        }
        bool SameFile(const BY_HANDLE_FILE_INFORMATION& a,const BY_HANDLE_FILE_INFORMATION& b) noexcept
        {
            return a.dwVolumeSerialNumber==b.dwVolumeSerialNumber && a.nFileIndexHigh==b.nFileIndexHigh && a.nFileIndexLow==b.nFileIndexLow
                && a.ftLastWriteTime.dwHighDateTime==b.ftLastWriteTime.dwHighDateTime && a.ftLastWriteTime.dwLowDateTime==b.ftLastWriteTime.dwLowDateTime
                && a.nFileSizeHigh==b.nFileSizeHigh && a.nFileSizeLow==b.nFileSizeLow;
        }
        void ReadCommand() noexcept
        {
            wchar_t path[MAX_PATH]{};
            if (swprintf_s(path,L"%s\\dfdebug-navigation-%lu.bin",Directory,Native.pid)<0) return;
            const HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
            if (file==INVALID_HANDLE_VALUE) return;
            BY_HANDLE_FILE_INFORMATION info{};std::array<unsigned char,sizeof(Request)+1> bytes{};DWORD count=0;
            const bool okay=GetFileInformationByHandle(file,&info) && (!HaveFile || !SameFile(info,PreviousFile))
                && info.nFileSizeHigh==0 && info.nFileSizeLow==sizeof(Request)
                && ReadFile(file,bytes.data(),static_cast<DWORD>(bytes.size()),&count,nullptr) && count==sizeof(Request);
            CloseHandle(file);
            if (!okay) return;
            if (!TryAcquireSRWLockExclusive(&InboxLock)) return;
            if (!HaveInbox)
            {
                std::memcpy(&Inbox,bytes.data(),sizeof(Inbox));HaveInbox=true;PreviousFile=info;HaveFile=true;
            }
            ReleaseSRWLockExclusive(&InboxLock);
        }
        void Token(std::ostream& output,std::uint32_t value)
        { char buffer[16]{};std::snprintf(buffer,sizeof(buffer),"0x%08X",value);output<<'"'<<buffer<<'"'; }
        void ExportResult()
        {
            Result result;
            if (!TryAcquireSRWLockShared(&OutboxLock)) return;
            result=Outbox;ReleaseSRWLockShared(&OutboxLock);
            if (!result.serial || result.serial==ExportedSerial) return;
            const auto& r=result.request;
            std::ostringstream out;out.imbue(std::locale::classic());out<<std::setprecision(9);
            out<<"{\"schema\":\"dfl-client-navigation/v1\",\"pid\":"<<Native.pid<<",\"process_started_filetime\":"<<Native.processStartedFileTime
                <<",\"request_id\":"<<r.requestId<<",\"source\":\""<<result.source<<"\",\"status\":\""<<result.status<<"\",\"reason\":\""<<result.reason
                <<"\",\"tick_ms\":"<<result.tick<<",\"uid\":"<<r.uid<<",\"generation\":"<<r.generation<<",\"actor_token\":";Token(out,r.actorToken);
            out<<",\"map_token\":";Token(out,r.mapToken);
            out<<",\"town_id\":"<<r.townId<<",\"area_id\":"<<r.areaId<<",\"target\":{\"x\":"<<r.x<<",\"y\":"<<r.y<<"},\"position\":";
            if (result.position.known && std::isfinite(result.position.x) && std::isfinite(result.position.y))
                out<<"{\"x\":"<<result.position.x<<",\"y\":"<<result.position.y<<'}';
            else out<<"null";
            out<<",\"minimap_hook_installed\":"<<(InterlockedCompareExchange(&HookPublished,0,0)?"true":"false")<<'}';const auto json=out.str();wchar_t path[MAX_PATH]{},temporary[MAX_PATH]{};
            if (swprintf_s(path,L"%s\\dfdebug-navigation-%lu.json",Directory,Native.pid)<0 || swprintf_s(temporary,L"%s.tmp",path)<0) return;
            const HANDLE file=CreateFileW(temporary,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
            if (file==INVALID_HANDLE_VALUE) return;
            DWORD written=0;const bool okay=json.size()<=MAXDWORD && WriteFile(file,json.data(),static_cast<DWORD>(json.size()),&written,nullptr)
                && written==json.size() && FlushFileBuffers(file);
            CloseHandle(file);
            if (okay && MoveFileExW(temporary,path,MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH)) ExportedSerial=result.serial;
            else DeleteFileW(temporary);
        }
    }
    void Initialize(const wchar_t* settingsPath,const wchar_t* directory,client_state::Reader reader,bool allowDiagnostics,bool playerFeatures) noexcept
    {
        if(IsEnabled())return;
        InterlockedExchange(&Enabled,0);
        if (!settingsPath || !directory || wcsnlen_s(directory,MAX_PATH)>=MAX_PATH-64
            || (playerFeatures ? GetPrivateProfileIntW(L"Features",L"EnableTownNavigation",1,settingsPath)!=1
                : GetPrivateProfileIntW(L"DebugOverlay",L"EnableTownNavigation",0,settingsPath)!=1)) return;
        Diagnostics=allowDiagnostics;
        Commands=allowDiagnostics && GetPrivateProfileIntW(L"DebugOverlay",L"EnableTownNavigation",0,settingsPath)==1;
        Native=reader;wcscpy_s(Directory,directory);
        GameThread=0;Installed=HitInstalled=HookAttempted=HaveInbox=HaveFile=Tracking=false;
        InterlockedExchange(&HookPublished,0);
        Inbox={};Active={};Outbox={};GameResult={};Current={};ConsumedId=ActiveTick=ActiveSequence=NextSerial=ExportedSerial=LastPoll=0;
        InterlockedExchange(&Enabled,Native.read && Native.pid && Native.processStartedFileTime?1:0);
        if(IsEnabled())
        {
            client_town_route::Initialize(settingsPath,directory,reader,&ApplyRouteStep,&SupersedeForRoute,Diagnostics,Commands);
            client_map_feedback::Initialize(reader,Diagnostics?directory:nullptr);
        }
    }
    void OnFrame() noexcept
    {
        if (!IsEnabled()) return;
        client_state::Snapshot latest;
        if (client_state::CopyLatest(latest)) OnFrame(latest);
    }
    void OnFrame(const client_state::Snapshot& snapshot) noexcept
    {
        if (!IsEnabled()) return;
        if (!GameThread) GameThread=GetCurrentThreadId();
        if (GetCurrentThreadId()!=GameThread) return;
        if (Current.sequence!=snapshot.sequence) Current=snapshot;
        Install();FlushResult();
        if (Tracking)
        {
            const auto now=Now();const bool context=snapshot.valid && snapshot.layoutSupported && !snapshot.truncated && snapshot.scene.known && snapshot.scene.value==1
                && snapshot.haveMe && snapshot.me.uid.known && snapshot.me.uid.value==Active.uid && snapshot.generation==Active.generation
                && snapshot.me.actorToken==Active.actorToken && snapshot.me.mapToken==Active.mapToken && snapshot.town.present
                && snapshot.town.townId==Active.townId && snapshot.town.areaId==Active.areaId;
            Reads reads;std::int32_t dx=0,dy=0;unsigned char autoMode=0;
            const bool arrived=snapshot.sequence>ActiveSequence && snapshot.town.position.known
                && std::fabs(snapshot.town.position.x-Active.x)<=3 && std::fabs(snapshot.town.position.y-Active.y)<=3
                && snapshot.town.directionX.known && snapshot.town.directionY.known && snapshot.town.directionX.value==4 && snapshot.town.directionY.value==4;
            const char* interruption=nullptr;
            if (!context || !Live(Active)) interruption="context_changed";
            else if (!reads.At(Active.actorToken,0x23C,dx) || !reads.At(Active.actorToken,0x240,dy) || dx!=Active.x || dy!=Active.y) interruption="destination_changed";
            else if (now<snapshot.capturedTickMs || now-snapshot.capturedTickMs>750) interruption="capture_stale";
            else if (!reads.At(0xB5F0A0,0,autoMode) || autoMode>1) interruption="native_auto_move_unknown";
            else if (!arrived && autoMode==0) interruption="native_auto_move_stopped";
            else if (!arrived && now-ActiveTick>30000) interruption="tracking_timeout";
            if (interruption) { Tracking=false;Publish(Active,ActiveSource,"interrupted",interruption,snapshot.town.position); }
            else if (arrived)
            { Tracking=false;Publish(Active,ActiveSource,"arrived","position_observed",snapshot.town.position); }
        }
        Request pending;bool have=false;
        if (TryAcquireSRWLockExclusive(&InboxLock))
        { if (HaveInbox) {pending=Inbox;HaveInbox=false;have=true;}ReleaseSRWLockExclusive(&InboxLock); }
        if (have) Dispatch(pending,"debug");
        client_town_route::OnFrame(Current);
        client_map_feedback::ObserveFrame(Current,Installed&&HitInstalled);
    }
    void PumpWorker() noexcept
    {
        if (!IsEnabled()) return;
        try
        {
            const auto now=Now();
            if (Commands && now-LastPoll>=50) {LastPoll=now;ReadCommand();}
            if(Diagnostics)ExportResult();
            client_town_route::PumpWorker();
            client_map_feedback::PumpWorker();
        }
        catch (...) { /* IO failure never escapes into the host worker. */ }
    }
    bool Shutdown() noexcept
    {
        if(!client_map_feedback::Shutdown())return false;
        InterlockedExchange(&Enabled,0);
        if (!RestoreInstalledHitHook(HitInstalled,PatchedHitCall,&PatchHit)) return false;
        if (!RestoreInstalledHook(Installed,PatchedCall,&Patch)) return false;
        InterlockedExchange(&HookPublished,0);
        Tracking=false;
        client_town_route::Shutdown();
        return true;
    }
}
