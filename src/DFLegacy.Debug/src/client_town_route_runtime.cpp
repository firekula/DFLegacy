#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "client_town_route_runtime.h"
#include <array>
#include <cstring>
#include <cwchar>
#include <iomanip>
#include <locale>
#include <memory>
#include <sstream>

namespace dfl::client_town_route
{
    namespace
    {
        client_state::Reader Native{};
        ApplyDestination Apply=nullptr;
        SupersedeLegacy Supersede=nullptr;
        volatile LONG Enabled=0;
        DWORD GameThread=0;
        wchar_t Directory[MAX_PATH]{};
        SRWLOCK CatalogLock=SRWLOCK_INIT, InboxLock=SRWLOCK_INIT;
        std::shared_ptr<const town_route::Catalog> PublishedCatalog;
        bool CatalogAttempted=false,HaveInbox=false,HaveCommandFile=false;
        client_navigation::Request Inbox{},Origin{};
        BY_HANDLE_FILE_INFORMATION PreviousFile{};
        town_route::Controller Controller;
        client_state::History CaptureHistory{};
        std::uint64_t Consumed=0,UiId=0,Frame=0,NotBefore=0,LastPoll=0,LastProgress=0;
        const char* Source="minimap";
        struct Result
        {
            client_navigation::Request origin{};
            town_route::Observation observed{};
            std::uint64_t tick=0;
            std::size_t steps=0;
            char source[16]{},status[24]{},reason[96]{},pvf[65]{};
        };
        // One game-thread producer, one worker consumer. No game-thread waits.
        std::array<Result,64> Results{};
        volatile LONG Written=0,Read=0;
        Result Pending{},Deferred{};
        bool HavePending=false,HaveDeferred=false;
        bool Terminal(const Result& result) noexcept
        {return std::strcmp(result.status,"arrived")==0||std::strcmp(result.status,"rejected")==0||std::strcmp(result.status,"interrupted")==0;}
        std::uint64_t Now() noexcept {return Native.tickMs?Native.tickMs(Native.context):GetTickCount64();}
        bool IsEnabled() noexcept {return InterlockedCompareExchange(&Enabled,0,0)!=0;}
        bool Supported() noexcept {return Native.supported&&Native.supported(Native.context);}
        template<class T> bool At(std::uint32_t base,std::uint32_t offset,T& output) noexcept
        {
            const auto address=static_cast<std::uint64_t>(base)+offset;
            return base>=0x10000&&address+sizeof(T)<=0x100000000ULL&&Native.read
                &&Native.read(Native.context,static_cast<std::uint32_t>(address),&output,sizeof(T));
        }
        std::shared_ptr<const town_route::Catalog> Catalog() noexcept
        {
            std::shared_ptr<const town_route::Catalog> result;
            if(TryAcquireSRWLockShared(&CatalogLock))
            {result=PublishedCatalog;ReleaseSRWLockShared(&CatalogLock);}
            return result;
        }
        bool Push(const Result& result) noexcept
        {
            const LONG write=InterlockedCompareExchange(&Written,0,0),read=InterlockedCompareExchange(&Read,0,0);
            if(static_cast<unsigned long>(write-read)>=Results.size())return false;
            Results[static_cast<unsigned long>(write)%Results.size()]=result;
            InterlockedExchange(&Written,write+1);return true;
        }
        void Publish(const client_navigation::Request& origin,const char* source,const char* status,
                     const char* reason,const town_route::Observation& observation,
                     const town_route::Catalog* catalog=nullptr) noexcept
        {
            Result result{};result.origin=origin;result.observed=observation;result.tick=Now();result.steps=Controller.Steps();
            strncpy_s(result.source,source,_TRUNCATE);strncpy_s(result.status,status,_TRUNCATE);strncpy_s(result.reason,reason,_TRUNCATE);
            if(catalog)strncpy_s(result.pvf,catalog->pvfSha256.c_str(),_TRUNCATE);
            if(HavePending)
            {
                // Progress is expendable; an acknowledged decision is not.
                // Input is backpressured while these fixed terminal slots wait.
                if(Terminal(result))
                {
                    const bool same=result.origin.requestId==Pending.origin.requestId&&std::strcmp(result.source,Pending.source)==0;
                    if(!Terminal(Pending)||same)Pending=result;
                    else if(!HaveDeferred){Deferred=result;HaveDeferred=true;}
                }
                return;
            }
            if(!Push(result)){Pending=result;HavePending=true;}
        }
        bool Fresh(client_state::Snapshot& result) noexcept
        {return Supported()&&client_state::Capture(Native,CaptureHistory,result);}
        bool SameOrigin(const client_state::Snapshot& a,const client_state::Snapshot& b) noexcept
        {
            const auto x=Observe(a),y=Observe(b);
            return x.valid&&y.valid&&x.pid==y.pid&&x.started==y.started&&x.uid==y.uid
                &&x.user==y.user&&x.actor==y.actor&&x.context==y.context&&x.map==y.map&&x.area==y.area;
        }
        client_navigation::Request RequestAt(const client_state::Snapshot& s,town_route::Goal goal,std::uint64_t id) noexcept
        {
            client_navigation::Request r{};std::memcpy(r.magic,"DLRT",4);r.pid=s.pid;r.uid=s.me.uid.value;
            r.processStartedFileTime=s.processStartedFileTime;r.requestId=id;r.issuedTickMs=Now();
            r.snapshotTickMs=s.capturedTickMs;r.generation=s.generation;r.snapshotSequence=s.sequence;
            r.actorToken=s.me.actorToken;r.mapToken=s.me.mapToken;r.userToken=s.me.userToken;r.contextToken=s.town.contextToken;
            r.townId=goal.area.town;r.areaId=goal.area.area;r.x=goal.point.x;r.y=goal.point.y;return r;
        }
        bool FloorsMatch(const client_state::Snapshot& snapshot,const town_route::Area& area) noexcept
        {
            std::uint32_t mapArea=0,definition=0,begin=0,end=0,againBegin=0,againEnd=0,actor=0,map=0;
            if(!At(snapshot.town.mapToken,0xF4,mapArea)||!At(mapArea,0x50,definition)
                ||!At(definition,0x154,begin)||!At(definition,0x158,end)||!begin||end<begin
                ||(end-begin)%16||(end-begin)/16!=area.walkable.size()||area.walkable.size()>256)return false;
            for(std::size_t i=0;i<area.walkable.size();++i)
            {
                client_navigation::Rect actual{};const auto& expected=area.walkable[i];
                if(!At(begin,static_cast<std::uint32_t>(i*16),actual)||actual.x!=expected.x||actual.y!=expected.y
                    ||actual.width!=expected.width||actual.height!=expected.height)return false;
            }
            return At(definition,0x154,againBegin)&&againBegin==begin&&At(definition,0x158,againEnd)&&againEnd==end
                &&At(0xB75A18,0,actor)&&actor==snapshot.me.actorToken&&At(actor,0xE4,map)&&map==snapshot.town.mapToken;
        }
        bool Begin(const client_state::Snapshot& source,const client_state::Snapshot& fresh,
                   client_navigation::Request origin,const char* from)
        {
            const auto catalog=Catalog();const auto observation=Observe(fresh);
            const char* rejected=nullptr;
            if(HavePending||HaveDeferred)return false;
            const auto queued=static_cast<unsigned long>(InterlockedCompareExchange(&Written,0,0)-InterlockedCompareExchange(&Read,0,0));
            if(queued>Results.size()-2){Publish(origin,from,"rejected","result_queue_busy",observation,catalog.get());return false;}
            else if(!catalog)rejected="catalog_unavailable";
            else if(!SameOrigin(source,fresh))rejected="live_context_changed";
            if(rejected){Publish(origin,from,"rejected",rejected,observation,catalog.get());return false;}
            std::string error;
            const bool previousActive=Controller.Active();
            const auto previousOrigin=Origin;const auto* previousSource=Source;
            if(!Controller.Start(*catalog,observation,{{origin.townId,origin.areaId},{origin.x,origin.y}},Now(),error))
            {Publish(origin,from,"rejected",error.c_str(),observation,catalog.get());return false;}
            if(previousActive)Publish(previousOrigin,previousSource,"interrupted","superseded",observation,catalog.get());
            // Start is transactional. Only a validated replacement supersedes
            // legacy tracking; original UI/keyboard flags are never forged.
            if(Supersede)Supersede();
            Origin=origin;Source=from;NotBefore=Frame+1;LastProgress=Now();
            Publish(Origin,Source,"requested","goal_accepted",observation,catalog.get());return true;
        }
        void Token(std::ostream& output,std::uint32_t value)
        {char buffer[16]{};sprintf_s(buffer,"0x%08X",value);output<<'"'<<buffer<<'"';}
        std::string Json(const Result& r)
        {
            std::ostringstream out;out.imbue(std::locale::classic());out<<std::setprecision(10);
            out<<"{\"schema\":\"dfl-client-route/v1\",\"pid\":"<<r.origin.pid<<",\"process_started_filetime\":"<<r.origin.processStartedFileTime
               <<",\"request_id\":"<<r.origin.requestId<<",\"source\":\""<<r.source<<"\",\"status\":\""<<r.status
               <<"\",\"reason\":\""<<r.reason<<"\",\"tick_ms\":"<<r.tick<<",\"uid\":"<<r.origin.uid
               <<",\"goal\":{\"town_id\":"<<r.origin.townId<<",\"area_id\":"<<r.origin.areaId<<",\"x\":"<<r.origin.x<<",\"y\":"<<r.origin.y<<"}"
               <<",\"origin\":{\"generation\":"<<r.origin.generation<<",\"actor_token\":";Token(out,r.origin.actorToken);
            out<<",\"map_token\":";Token(out,r.origin.mapToken);out<<",\"user_token\":";Token(out,r.origin.userToken);
            out<<",\"context_token\":";Token(out,r.origin.contextToken);
            out<<"},\"steps\":"<<r.steps<<",\"pvf_sha256\":\""<<r.pvf<<"\",\"observed\":";
            if(!r.observed.valid)out<<"null";
            else
            {
                const auto& o=r.observed;
                out<<"{\"town_id\":"<<o.area.town<<",\"area_id\":"<<o.area.area<<",\"map_token\":";Token(out,o.map);
                out<<",\"observation_generation\":"<<o.generation<<",\"position\":{\"x\":"<<o.position.x<<",\"y\":"<<o.position.y
                   <<"},\"destination\":{\"x\":"<<o.destination.x<<",\"y\":"<<o.destination.y
                   <<"},\"direction_raw\":{\"x\":"<<o.directionX<<",\"y\":"<<o.directionY<<"},\"auto_move_active\":";
                if(o.autoKnown)out<<(o.autoActive?"true":"false");else out<<"null";out<<'}';
            }
            out<<'}';return out.str();
        }
        bool Save(const wchar_t* path,const std::string& json)
        {
            wchar_t temporary[MAX_PATH]{};if(swprintf_s(temporary,L"%s.tmp",path)<0)return false;
            const HANDLE file=CreateFileW(temporary,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE)return false;
            DWORD written=0;const bool okay=WriteFile(file,json.data(),static_cast<DWORD>(json.size()),&written,nullptr)&&written==json.size();
            CloseHandle(file);
            if(okay&&MoveFileExW(temporary,path,MOVEFILE_REPLACE_EXISTING))return true;
            DeleteFileW(temporary);return false;
        }
        void ReadCommand()
        {
            wchar_t path[MAX_PATH]{};if(swprintf_s(path,L"%s\\dfdebug-route-%u.bin",Directory,Native.pid)<0)return;
            const HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE)return;
            BY_HANDLE_FILE_INFORMATION info{};std::array<unsigned char,sizeof(client_navigation::Request)+1> raw{};DWORD count=0;
            const bool changed=GetFileInformationByHandle(file,&info)&&(!HaveCommandFile||std::memcmp(&info.ftLastWriteTime,&PreviousFile.ftLastWriteTime,sizeof(FILETIME))!=0
                ||info.nFileIndexHigh!=PreviousFile.nFileIndexHigh||info.nFileIndexLow!=PreviousFile.nFileIndexLow);
            const bool okay=changed&&info.nFileSizeHigh==0&&info.nFileSizeLow==sizeof(client_navigation::Request)
                &&ReadFile(file,raw.data(),static_cast<DWORD>(raw.size()),&count,nullptr)&&count==sizeof(client_navigation::Request);
            CloseHandle(file);if(!okay||!TryAcquireSRWLockExclusive(&InboxLock))return;
            if(!HaveInbox){std::memcpy(&Inbox,raw.data(),sizeof(Inbox));HaveInbox=true;PreviousFile=info;HaveCommandFile=true;}
            ReleaseSRWLockExclusive(&InboxLock);
        }
    }
    void Initialize(const wchar_t* settings,const wchar_t* directory,client_state::Reader reader,
                    ApplyDestination apply,SupersedeLegacy supersede) noexcept
    {
        (void)settings;InterlockedExchange(&Enabled,0);
        if(!directory||wcsnlen_s(directory,MAX_PATH)>=MAX_PATH-100||!reader.pid||!reader.processStartedFileTime||!apply)return;
        Native=reader;Apply=apply;Supersede=supersede;wcscpy_s(Directory,directory);
        Controller={};CaptureHistory={};PublishedCatalog.reset();CatalogAttempted=HaveInbox=HaveCommandFile=HavePending=HaveDeferred=false;
        Written=Read=0;Origin={};GameThread=0;Consumed=UiId=Frame=NotBefore=LastPoll=LastProgress=0;
        InterlockedExchange(&Enabled,1);
    }
    bool BeginCanvasGoal(const client_state::Snapshot& source,const client_navigation::CanvasGoal& goal) noexcept
    {
        if(!IsEnabled()||!GameThread||GetCurrentThreadId()!=GameThread)return false;
        try
        {
            client_state::Snapshot fresh;if(!Fresh(fresh))return false;
            auto origin=RequestAt(fresh,{{goal.town,goal.area},{goal.x,goal.y}},++UiId);
            return Begin(source,fresh,origin,"minimap");
        }
        catch(...){InterlockedExchange(&Enabled,0);return false;}
    }
    void LegacyTakeover() noexcept
    {
        if(!IsEnabled()||!Controller.Active())return;
        try{Controller.Cancel("legacy_destination");Publish(Origin,Source,"interrupted","legacy_destination",{},Catalog().get());}
        catch(...){InterlockedExchange(&Enabled,0);}
    }
    void OnFrame(const client_state::Snapshot& source) noexcept
    {
        if(!IsEnabled())return;
        if(!GameThread)GameThread=GetCurrentThreadId();if(GameThread!=GetCurrentThreadId())return;
        try
        {
            ++Frame;
            if(HavePending&&Push(Pending))HavePending=false;
            if(!HavePending&&HaveDeferred)
            {if(Push(Deferred))HaveDeferred=false;else{Pending=Deferred;HavePending=true;HaveDeferred=false;}}
            const auto stopForBackpressure=[&]()
            {
                const auto queued=static_cast<unsigned long>(InterlockedCompareExchange(&Written,0,0)-InterlockedCompareExchange(&Read,0,0));
                if(!HavePending&&!HaveDeferred&&queued<Results.size())return false;
                if(Controller.Active())
                {Controller.Cancel("result_queue_busy");Publish(Origin,Source,"interrupted","result_queue_busy",{},Catalog().get());}
                return true;
            };
            if(stopForBackpressure())return;
            client_navigation::Request command{};bool have=false;
            if(!HavePending&&TryAcquireSRWLockExclusive(&InboxLock))
            {if(HaveInbox){command=Inbox;HaveInbox=false;have=true;}ReleaseSRWLockExclusive(&InboxLock);}
            client_state::Snapshot fresh{};bool captured=false;
            if(have)
            {
                const char* error=ValidateGoalRequest(command,source,Now(),Consumed);
                if(command.requestId>Consumed)Consumed=command.requestId;
                if(error)Publish(command,"debug","rejected",error,Observe(source),Catalog().get());
                else
                {
                    captured=Fresh(fresh);
                    if(captured)Begin(source,fresh,command,"debug");
                    else Publish(command,"debug","rejected","live_context_unavailable",{},Catalog().get());
                }
            }
            // Handling this frame's inbox can itself fill the last result
            // slot. Recheck before Tick can issue any native destination.
            if(stopForBackpressure())return;
            if(!Controller.Active()||Frame<=NotBefore)return;
            const auto catalog=Catalog();if(!catalog)return;
            if(!captured)Fresh(fresh);
            const auto observation=Observe(fresh);
            const auto action=Controller.Tick(*catalog,observation,Now());
            if(action.kind==town_route::Action::Kind::SetDestination)
            {
                const auto* area=catalog->Find(observation.area);
                const auto step=RequestAt(fresh,{observation.area,action.point},Origin.requestId);
                const bool accepted=area&&FloorsMatch(fresh,*area)&&Apply&&Apply(step);
                Controller.ConfirmDestination(observation,action.point,Now(),accepted);
            }
            if(action.kind!=town_route::Action::Kind::None||Now()-LastProgress>=200)
            {LastProgress=Now();Publish(Origin,Source,Controller.Status().c_str(),Controller.Reason().c_str(),observation,catalog.get());}
        }
        catch(...){InterlockedExchange(&Enabled,0);}
    }
    void PumpWorker() noexcept
    {
        if(!IsEnabled()||!Supported())return;
        try
        {
            if(!CatalogAttempted)
            {
                CatalogAttempted=true;wchar_t file[MAX_PATH]{};
                const auto length=GetModuleFileNameW(nullptr,file,MAX_PATH);
                wchar_t* slash=length&&length<MAX_PATH?wcsrchr(file,L'\\'):nullptr;
                if(slash)
                {
                    *(slash+1)=0;
                    if(wcscat_s(file,L"Script.pvf")==0)
                    {
                        auto catalog=std::make_shared<town_route::Catalog>();std::string error;
                        if(town_route::LoadCatalog(file,*catalog,error))
                        {AcquireSRWLockExclusive(&CatalogLock);PublishedCatalog=std::move(catalog);ReleaseSRWLockExclusive(&CatalogLock);}
                    }
                }
            }
            if(Now()-LastPoll>=50){LastPoll=Now();ReadCommand();}
            for(unsigned i=0;i<8;++i)
            {
                const LONG read=InterlockedCompareExchange(&Read,0,0);
                if(read==InterlockedCompareExchange(&Written,0,0))break;
                const auto result=Results[static_cast<unsigned long>(read)%Results.size()];
                const auto json=Json(result);wchar_t path[MAX_PATH]{};
                bool saved=true;
                if(std::strcmp(result.source,"debug")==0)
                    saved=swprintf_s(path,L"%s\\dfdebug-route-%u-%llu.result.json",Directory,Native.pid,result.origin.requestId)>0&&Save(path,json);
                if(saved)saved=swprintf_s(path,L"%s\\dfdebug-route-%u.json",Directory,Native.pid)>0&&Save(path,json);
                if(!saved)break; // Keep this exact record until both writes succeed.
                InterlockedExchange(&Read,read+1);
            }
        }
        catch(...){/* Worker failures never escape into game rendering. */}
    }
    void Shutdown() noexcept
    {
        InterlockedExchange(&Enabled,0);Apply=nullptr;Supersede=nullptr;
        // Host joins the IO worker before restoring native hooks/unloading.
        PublishedCatalog.reset();HaveInbox=HavePending=HaveDeferred=false;
    }
}
