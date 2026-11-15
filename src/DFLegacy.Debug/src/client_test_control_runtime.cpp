#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "client_test_control.h"
#include <algorithm>
#include <array>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <locale>
#include <sstream>
#include <cmath>
#include <iomanip>
namespace dfl::client_test_control
{
    namespace
    {
        client_state::Reader Native{};
        volatile LONG Enabled=0;
        DWORD GameThread=0;
        wchar_t Directory[MAX_PATH]{};
        SRWLOCK InboxLock=SRWLOCK_INIT,OutboxLock=SRWLOCK_INIT;
        struct Command {Request request{};Lease lease{};};
        Command Inbox{};bool HaveInbox=false,HaveFile=false;
        BY_HANDLE_FILE_INFORMATION PreviousFile{};
        std::uint64_t Consumed=0,NextSerial=0,ExportedSerial=0;
        struct Result
        {
            Request request{};const char* status="rejected";const char* reason="unknown";
            std::uint64_t tick=0,serial=0;std::size_t appliedCount=0;
        } GameResult{},Outbox{};
        struct GroundRow {Ground ground{};client_state::Position position{};};
        struct InventoryRow {std::uint32_t slot=0,item=0,id=0,count=0;};
        struct InventoryState
        {
            bool valid=false,pending=false;const char* reason="inventory_unavailable";
            std::uint32_t container=0,array=0,capacity=0;std::size_t count=0;
            std::array<InventoryRow,512> rows{};
        };
        struct TestState
        {
            Request identity{};std::uint64_t tick=0,serial=0;
            std::uint32_t observerMap=0,stage=0;bool observerDead=false;
            bool valid=false,truncated=false;const char* reason="ground_state_unavailable";
            std::array<GroundRow,256> ground{};std::size_t count=0;
            InventoryState inventory{};
        } GameState{},PublishedState{};
        SRWLOCK StateLock=SRWLOCK_INIT,LeaseLock=SRWLOCK_INIT;
        Lease LiveLease{};volatile LONG LeaseUid=0;
        std::uint64_t StateSerial=0,ExportedStateSerial=0,LastStateCapture=0;
        bool IsEnabled() noexcept{return InterlockedCompareExchange(&Enabled,0,0)!=0;}
        std::uint64_t Now() noexcept{return Native.tickMs?Native.tickMs(Native.context):GetTickCount64();}
        std::uint64_t FileTime() noexcept
        {if(Native.fileTime)return Native.fileTime(Native.context);FILETIME ft{};GetSystemTimeAsFileTime(&ft);return(static_cast<std::uint64_t>(ft.dwHighDateTime)<<32)|ft.dwLowDateTime;}
        struct Reads
        {
            template<class T> bool At(std::uint32_t base,std::uint32_t offset,T& value) const noexcept
            {return base>=0x10000&&static_cast<std::uint64_t>(base)+offset+sizeof(T)<=0x100000000ULL&&Native.read&&Native.read(Native.context,base+offset,&value,sizeof(T));}
            bool Secure(std::uint32_t base,std::uint32_t offset,std::uint32_t& out) const noexcept
            {
                if(base<0x10000||base>UINT32_MAX-offset)return false;const auto address=base+offset;
                std::uint32_t manager=0,key=0,encoded=0,index=0,count=0,a=0,b=0,va=0,vb=0;
                if(!At(0xCFD458,0,manager)||!At(0xCFD45C,0,key)||!At(address,0,encoded)||!At(address,4,index)
                    ||((encoded^key)-address)!=index||!At(manager,4,count)||index>=count||index>0x1000000
                    ||!At(manager,0x44,a)||!At(manager,0x48,b)||a>UINT32_MAX-index*4||b>UINT32_MAX-index*4)return false;
                a+=index*4;b+=index*4;
                if(!At(a,0,va)||!At(b,0,vb)||(va^a^key)!=(vb^b^key))return false;out=va^a^key;return true;
            }
            template<std::size_t N> bool Matches(std::uint32_t address,const unsigned char(&expected)[N]) const noexcept
            {std::array<unsigned char,N> actual{};return At(address,0,actual)&&!std::memcmp(actual.data(),expected,N);}
            bool Getter(std::uint32_t actor,std::uint32_t offset,std::uint32_t expected) const noexcept
            {std::uint32_t table=0,fn=0;return At(actor,0,table)&&At(table,offset,fn)&&fn==expected;}
        };
        bool PositionSignatures() noexcept
        {
            constexpr unsigned char setter[]={0x55,0x8B,0xEC,0x8B,0x89,0xE0,0,0,0,0x5D,0xE9,0x51,0x87,0x05,0};
            // 4715F0 is a tail thunk into the original controller setter.
            constexpr unsigned char controller[]={0x55,0x8B,0xEC,0x8B,0x01,0x85,0xC0,0xC6,0x81,0x8C,0,0,0,0x01,0x74,0x16};
            constexpr unsigned char getter[]={0x55,0x8B,0xEC,0x8B,0x89,0xE0,0,0,0,0x5D,0xE9,0xD1,0x85,0x05,0};
            constexpr unsigned char plain[]={0x55,0x8B,0xEC,0xD9,0x45,0x0C,0x8B,0x45,0x08,0xD9,0x5C,0x81,0x04};
            constexpr unsigned char secure[]={0x55,0x8B,0xEC,0x53,0x56,0x8B,0x75,0x08,0x8B,0x54,0xF1,0x69,0x57,0x8B,0x3D,0x58};
            Reads r;return r.Matches(0x4715F0,setter)&&r.Matches(0x4C9D50,controller)&&r.Matches(0x471610,getter)
                &&r.Matches(0x4736B0,plain)&&r.Matches(0x4CAC90,secure);
        }
        bool LivePointers(const Request& q,const client_state::Snapshot& s) noexcept
        {
            Reads r;std::uint32_t game=0,scene=0,actor=0,uid=0,map=0,binding=0,ui=0,stage=0,kind=0,dead=1;
            std::uint16_t userUid=0;
            return r.At(0xB5D6F0,0,game)&&r.Secure(game,0x10,scene)&&scene==3&&r.At(0xB75A14,0,actor)&&actor==q.actorToken
                &&r.At(actor,0xD0,kind)&&kind==0x111&&r.Secure(actor,0xD4,uid)&&uid==q.uid
                &&r.At(actor,0xE4,map)&&map==q.mapToken&&r.At(q.userToken,0x110,binding)&&binding==actor
                &&r.At(q.userToken,4,userUid)&&userUid==uid&&r.At(0xB5DCE0,0,ui)&&r.At(ui,0x200010,stage)
                &&stage==s.room.stageToken&&r.At(stage,0xB0,map)&&map==q.mapToken
                &&r.Getter(actor,0x318,0x652A00)&&r.Secure(actor,0x142C,dead)&&dead==0;
        }
        bool LiveContext(const Command& c,const client_state::Snapshot& s) noexcept
        {
            return Native.supported&&Native.supported(Native.context)
                &&!ValidateRequest(c.request,s,c.lease,Now(),FileTime(),0)&&LivePointers(c.request,s);
        }
        bool ObservationContext(const Request& q,const Lease& lease,const client_state::Snapshot& s) noexcept
        {
            const auto now=Now(),ft=FileTime();
            if(!Native.supported||!Native.supported(Native.context)||!s.valid||!s.layoutSupported||!s.haveMe
                ||!s.scene.known||s.scene.value!=3||!s.me.uid.known||!s.me.dead.known||!s.me.sameMap.known
                ||!s.room.present||!s.room.stageToken||!s.room.mapToken||!q.actorToken||!q.userToken
                ||q.pid!=Native.pid||q.processStartedFileTime!=Native.processStartedFileTime
                ||q.pid!=s.pid||q.processStartedFileTime!=s.processStartedFileTime||q.uid!=s.me.uid.value||!q.uid||q.uid>65535
                ||q.actorToken!=s.me.actorToken||q.userToken!=s.me.userToken||q.mapToken!=s.room.mapToken
                ||q.generation!=s.generation||q.snapshotSequence!=s.sequence||q.snapshotTickMs!=s.capturedTickMs
                ||!s.capturedTickMs||s.capturedTickMs>now||now-s.capturedTickMs>750
                ||lease.magic!=0x4C545444||lease.version!=1||lease.flags!=1||lease.uid!=q.uid||!lease.nonce||!lease.runId
                ||q.sessionNonce!=lease.nonce||q.runId!=lease.runId||!lease.issuedFileTime||lease.issuedFileTime>ft
                ||ft-lease.issuedFileTime>15000000)return false;
            if(!s.me.dead.value&&(!s.me.sameMap.value||s.me.mapToken!=q.mapToken))return false;
            // A dead actor can be detached or retained on the old map. Observe only
            // the current stage's map; never turn this context into an action lease.
            Reads r;std::uint32_t game=0,scene=0,actor=0,uid=0,map=0,binding=0,ui=0,stage=0,kind=0,dead=0;
            std::uint32_t manager=0,key=0,final=0;std::uint16_t userUid=0;
            return r.At(0xCFD458,0,manager)&&manager&&r.At(0xCFD45C,0,key)
                &&r.At(0xB5D6F0,0,game)&&r.Secure(game,0x10,scene)&&scene==3
                &&r.At(0xB75A14,0,actor)&&actor==q.actorToken&&r.At(actor,0xD0,kind)&&kind==0x111
                &&r.Secure(actor,0xD4,uid)&&uid==q.uid&&r.At(actor,0xE4,map)&&map==s.me.mapToken
                &&r.At(q.userToken,0x110,binding)&&binding==actor&&r.At(q.userToken,4,userUid)&&userUid==uid
                &&r.At(0xB5DCE0,0,ui)&&r.At(ui,0x200010,stage)&&stage==s.room.stageToken
                &&r.At(stage,0xB0,map)&&map==q.mapToken
                &&r.Getter(actor,0x318,0x652A00)&&r.Secure(actor,0x142C,dead)&&(dead!=0)==s.me.dead.value
                &&r.At(0xB5D6F0,0,final)&&final==game&&r.Secure(game,0x10,final)&&final==3
                &&r.At(0xCFD458,0,final)&&final==manager&&r.At(0xCFD45C,0,final)&&final==key;
        }
        bool PositionReady(std::uint32_t actor) noexcept
        {
            PositionBinding binding{};if(!ReadPositionBinding(Native,actor,binding))return false;
            Reads r;std::uint32_t table=0,getter=0,value=0;if(!r.At(binding.vector,0,table)||!r.At(table,0,getter))return false;
            if(getter==0x4CAC70)
                for(std::uint32_t i=0;i<3;++i)if(!r.Secure(binding.vector,0x35+i*8,value)||!r.Secure(binding.vector,0x65+i*8,value))return false;
            return true;
        }
        bool SetPosition(std::uint32_t actor,std::int32_t x,std::int32_t y,bool& touched) noexcept
        {
            // Original PE: actor v+74 -> 4715F0 -> 4C9D50(index,int), ret8.
            // Native vector setters update both protected mirrors and dirty state.
            __try
            {
                if(!PositionSignatures()||!PositionReady(actor))return false;
                using Set=void(__thiscall*)(void*,std::int32_t,std::int32_t);
                using Get=std::int32_t(__thiscall*)(void*,std::int32_t);
                touched=true;reinterpret_cast<Set>(0x4715F0)(reinterpret_cast<void*>(actor),0,x);
                reinterpret_cast<Set>(0x4715F0)(reinterpret_cast<void*>(actor),1,y);
                return reinterpret_cast<Get>(0x471610)(reinterpret_cast<void*>(actor),0)==x
                    &&reinterpret_cast<Get>(0x471610)(reinterpret_cast<void*>(actor),1)==y;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        bool TeleportPublicationReady(std::uint32_t actor,PositionBinding& binding) noexcept
        {
            Reads r;std::uint32_t local=0,owner=0,map=0,stage=0,network=0,currentStage=0;
            constexpr unsigned char publish[]={0x55,0x8B,0xEC,0x56,0x8B,0xF1,0x8B,0x4E,0x74,0x85,0xC9,
                0x0F,0x84,0x6B,0x02,0,0,0xE8,0x2A,0xA6,0xFA,0xFF,0x84,0xC0};
            return r.Matches(0x4C8FE0,publish)&&ReadPositionBinding(Native,actor,binding)
                &&r.At(0xB75A14,0,local)&&local==actor&&r.At(binding.controller,0x74,owner)&&owner==actor
                &&r.Getter(actor,0x40,0x4073E0)&&r.Getter(actor,0x54,0x407400)&&r.Getter(actor,0x138,0x493880)
                &&r.At(actor,0xE4,map)&&map&&r.At(map,0xF4,stage)&&stage
                &&r.At(0xB5DCE0,0,network)&&r.At(network,0x200010,currentStage)&&currentStage==stage;
        }
        struct TeleportBinding {PositionBinding before{};};
        bool SetPlayerTeleport(void* context,std::uint32_t actor,std::int32_t x,std::int32_t y,bool& touched) noexcept
        {
            auto& state=*static_cast<TeleportBinding*>(context);
            // Validate the new publication path before either local axis changes.
            return TeleportPublicationReady(actor,state.before)&&SetPosition(actor,x,y,touched);
        }
        bool PublishPlayerTeleport(void* context,std::uint32_t actor) noexcept
        {
            const auto& before=static_cast<TeleportBinding*>(context)->before;PositionBinding after{};
            if(!TeleportPublicationReady(actor,after)||before.actor!=after.actor
                ||before.controller!=after.controller||before.vector!=after.vector)return false;
            __try
            {
                // Original player reposition callers 4929E1 / 8A537F use (1,1).
                // This updates the native compression baseline as well as requesting
                // the full 0x10 publication. The void return is NOT delivery proof.
                using Publish=void(__thiscall*)(void*,bool,std::int32_t);
                reinterpret_cast<Publish>(0x4C8FE0)(reinterpret_cast<void*>(after.controller),true,1);
                return true;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
        }
        struct ActorVector
        {std::uint32_t begin=0,end=0;std::array<std::uint32_t,MaximumGround> actors{};std::size_t count=0;};
        bool ReadActors(std::uint32_t map,ActorVector& v) noexcept
        {
            Reads r;if(!r.At(map,0x10,v.begin)||!r.At(map,0x14,v.end)||v.end<v.begin||(v.end-v.begin)%4||(v.end-v.begin)/4>MaximumGround)return false;
            v.count=(v.end-v.begin)/4;
            for(std::size_t i=0;i<v.count;++i)if(!r.At(v.begin,static_cast<std::uint32_t>(i*4),v.actors[i]))return false;
            std::uint32_t a=0,b=0;return r.At(map,0x10,a)&&a==v.begin&&r.At(map,0x14,b)&&b==v.end;
        }
        bool SameActors(std::uint32_t map,const ActorVector& v) noexcept
        {ActorVector now;return ReadActors(map,now)&&now.begin==v.begin&&now.end==v.end&&now.count==v.count&&now.actors==v.actors;}
        bool ReadGround(std::uint32_t actor,std::uint32_t map,Ground& g) noexcept
        {
            Reads r;std::uint32_t table=0,kind=0,actorMap=0;unsigned char active=0,pending=0;
            if(!r.At(actor,0,table)||table!=0x999574||!r.At(actor,0xD0,kind)||kind!=0x121||!r.At(actor,0xE4,actorMap)||actorMap!=map
                ||!r.Secure(actor,0xD4,g.id)||!r.At(actor,0x7A0,g.descriptor)
                ||!r.At(actor,0x128,active)||active>1||!r.At(actor,0x7A5,pending)||pending>1)return false;
            g.actor=actor;g.active=active!=0;g.pending=pending!=0;return true;
        }
        bool ReadCoordinates(std::uint32_t actor,client_state::Position& p) noexcept
        {
            p={};PositionBinding binding;if(!ReadPositionBinding(Native,actor,binding))return false;
            Reads r;std::uint32_t table=0,getter=0;if(!r.At(binding.vector,0,table)||!r.At(table,0,getter))return false;
            float values[3]{};
            for(std::uint32_t i=0;i<3;++i)
            {
                if(getter==0x4CAC70){std::uint32_t bits=0;if(!r.Secure(binding.vector,0x35+i*8,bits))return false;std::memcpy(&values[i],&bits,4);}
                else if(!r.At(binding.vector,4+i*4,values[i]))return false;
                if(!std::isfinite(values[i])||std::fabs(values[i])>=1000000)return false;
            }
            p={true,values[0],values[1],values[2]};return true;
        }
        bool ReadGroundState(std::uint32_t map,TestState& state) noexcept
        {
            state.count=0;state.valid=false;state.truncated=false;state.reason="ground_state_unavailable";ActorVector actors;if(!ReadActors(map,actors))return false;
            if(actors.count>state.ground.size()){state.truncated=true;state.reason="map_object_limit";return false;}Reads r;
            std::array<GroundRow,256> rows{};std::size_t count=0;
            for(std::size_t i=0;i<actors.count;++i)
            {
                const auto actor=actors.actors[i];if(!actor)continue;std::uint32_t kind=0;if(!r.At(actor,0xD0,kind))return false;if(kind!=0x121)continue;
                auto& row=rows[count];if(!ReadGround(actor,map,row.ground)||!ReadCoordinates(actor,row.position))return false;
                for(std::size_t j=0;j<count;++j)if(rows[j].ground.id==row.ground.id||rows[j].ground.actor==actor)return false;
                ++count;
            }
            if(!SameActors(map,actors))return false;
            for(std::size_t i=0;i<count;++i)
            {
                Ground verify;const auto& g=rows[i].ground;
                if(!ReadGround(g.actor,map,verify)||verify.id!=g.id||verify.descriptor!=g.descriptor||verify.active!=g.active||verify.pending!=g.pending)return false;
            }
            state.ground=rows;state.count=count;state.valid=true;state.reason="ready";return true;
        }
        bool ReadInventoryState(InventoryState& output) noexcept
        {
            output={};InventoryState next;Reads r;std::uint32_t space=0,begin=0,end=0,cap=0;
            if(!r.At(0xB5DCEC,0,next.container)||!r.Secure(next.container,0,space)||space!=0
                ||!r.Secure(next.container,0x10,next.capacity)||next.capacity<9||next.capacity>next.rows.size()
                ||!r.At(next.container,0x18,next.array)||next.array<0x10000
                ||static_cast<std::uint64_t>(next.array)+next.capacity*4>0x100000000ULL
                ||!r.At(next.container,0x20,begin)||!r.At(next.container,0x24,end)||!r.At(next.container,0x28,cap)
                ||end<begin||cap<end||(end-begin)%4||(end-begin)/4>64||(!begin&&(end||cap)))return false;
            next.pending=begin!=end;
            for(std::uint32_t slot=0;slot<next.capacity;++slot)
            {
                if(slot>0&&slot<9)continue;InventoryRow row;row.slot=slot;
                if(!r.At(next.array,slot*4,row.item))return false;if(!row.item)continue;
                if(!r.Getter(row.item,0x18,0x4619A0)||!r.Getter(row.item,0x34,0x461AB0)
                    ||!r.At(row.item,0xC,row.id)||row.id>65535||((slot==0)!=(row.id==0))
                    ||!r.Secure(row.item,0xA0,row.count))return false;
                for(std::size_t i=0;i<next.count;++i)if(next.rows[i].item==row.item)return false;
                next.rows[next.count++]=row;
            }
            // No partial inventory escapes when a slot or container changed.
            std::uint32_t value=0;
            if(!r.At(0xB5DCEC,0,value)||value!=next.container||!r.Secure(next.container,0,space)||space!=0
                ||!r.At(next.container,0x18,value)||value!=next.array||!r.Secure(next.container,0x10,value)||value!=next.capacity
                ||!r.At(next.container,0x20,value)||value!=begin||!r.At(next.container,0x24,value)||value!=end
                ||!r.At(next.container,0x28,value)||value!=cap)return false;
            std::size_t index=0;
            for(std::uint32_t slot=0;slot<next.capacity;++slot)
            {
                if(slot>0&&slot<9)continue;if(!r.At(next.array,slot*4,value))return false;
                const auto* row=index<next.count&&next.rows[index].slot==slot?&next.rows[index++]:nullptr;
                if(value!=(row?row->item:0))return false;if(!row)continue;
                if(!r.At(row->item,0xC,value)||value!=row->id||!r.Secure(row->item,0xA0,value)||value!=row->count)return false;
            }
            next.valid=true;next.reason="ready";output=next;return true;
        }
        const char* ValidateInventoryTarget(const Request& q,const InventoryState& inv,InventoryRow& row) noexcept
        {
            if(!inv.valid)return "inventory_unavailable";if(inv.pending)return "inventory_pending";
            if(q.operation!=5||q.targetId<0||static_cast<std::uint32_t>(q.targetId)>=inv.capacity
                ||(q.targetId>0&&q.targetId<9)||q.x<1||q.y<0||q.y>65535||((q.targetId==0)!=(q.y==0)))return "invalid_inventory_target";
            for(std::size_t i=0;i<inv.count;++i)
            {
                const auto& item=inv.rows[i];if(item.slot!=static_cast<std::uint32_t>(q.targetId))continue;
                if(item.item!=static_cast<std::uint32_t>(q.reserved)||item.id!=static_cast<std::uint32_t>(q.y))return "inventory_item_changed";
                if(item.count<static_cast<std::uint32_t>(q.x))return "inventory_quantity_changed";row=item;return nullptr;
            }
            return "inventory_slot_empty";
        }
        void CaptureTestState(const client_state::Snapshot& snapshot) noexcept
        {
            InterlockedExchange(&LeaseUid,snapshot.haveMe&&snapshot.me.uid.known?static_cast<LONG>(snapshot.me.uid.value):0);
            const auto now=Now();if(LastStateCapture&&now>=LastStateCapture&&now-LastStateCapture<200)return;
            Lease lease;if(!TryAcquireSRWLockShared(&LeaseLock))return;lease=LiveLease;ReleaseSRWLockShared(&LeaseLock);
            Request q{};q.pid=snapshot.pid;q.uid=snapshot.me.uid.value;q.processStartedFileTime=snapshot.processStartedFileTime;
            q.requestId=1;q.issuedTickMs=now;q.snapshotTickMs=snapshot.capturedTickMs;q.generation=snapshot.generation;q.snapshotSequence=snapshot.sequence;
            q.actorToken=snapshot.me.actorToken;q.mapToken=snapshot.room.mapToken;q.userToken=snapshot.me.userToken;
            q.sessionNonce=lease.nonce;q.runId=lease.runId;
            if(!ObservationContext(q,lease,snapshot))return;LastStateCapture=now;
            TestState state;state.identity=q;state.tick=now;state.stage=snapshot.room.stageToken;
            state.observerMap=snapshot.me.mapToken;state.observerDead=snapshot.me.dead.value;
            if(snapshot.truncated){state.truncated=true;state.reason="client_snapshot_truncated";}
            else ReadGroundState(q.mapToken,state);
            ReadInventoryState(state.inventory);
            Lease finalLease;if(!TryAcquireSRWLockShared(&LeaseLock))return;
            finalLease=LiveLease;ReleaseSRWLockShared(&LeaseLock);
            if(!ObservationContext(q,finalLease,snapshot))return;
            state.serial=++StateSerial;GameState=state;
            if(!TryAcquireSRWLockExclusive(&StateLock))return;PublishedState=GameState;ReleaseSRWLockExclusive(&StateLock);
        }
        const char* ApplyGround(const Command& command,const client_state::Snapshot& snapshot,std::size_t& applied,bool& touched) noexcept
        {
            const auto& q=command.request;ActorVector actors;std::array<Ground,MaximumGround> ground{};std::size_t count=0;Reads r;
            if(!ReadActors(q.mapToken,actors))return "invalid_map_vector";
            for(std::size_t i=0;i<actors.count;++i)
            {
                const auto actor=actors.actors[i];if(!actor)continue;std::uint32_t kind=0;if(!r.At(actor,0xD0,kind))return "map_actor_unreadable";
                if(kind!=0x121)continue;if(!ReadGround(actor,q.mapToken,ground[count]))return "unsupported_ground_layout";++count;
            }
            MovePlan plan;const auto error=PlanGroundMoves(q,ground.data(),count,plan);if(error)return error;
            // Complete preflight prevents a missing/duplicate/pending late actor from causing an avoidable partial batch.
            for(std::size_t i=0;i<plan.count;++i)if(!PositionReady(plan.moves[i].ground.actor))return "unsupported_position_binding";
            if(!PositionSignatures()||!LiveContext(command,snapshot)||!SameActors(q.mapToken,actors))return "live_context_changed";
            for(std::size_t i=0;i<plan.count;++i)
            {
                const auto& move=plan.moves[i];Ground current;
                if(!LiveContext(command,snapshot)||!SameActors(q.mapToken,actors)||!ReadGround(move.ground.actor,q.mapToken,current)
                    ||current.id!=move.ground.id||current.descriptor!=move.ground.descriptor||!current.active||current.pending)return "ground_identity_changed";
                if(!SetPosition(current.actor,move.x,move.y,touched))return "native_position_failed";
                ++applied;
            }
            return nullptr;
        }
        bool FindKillTarget(std::uint32_t map,std::uint32_t target,std::uint32_t local,std::uint32_t& output) noexcept
        {
            ActorVector actors;if(!ReadActors(map,actors))return false;Reads r;std::uint32_t found=0;
            for(std::size_t i=0;i<actors.count;++i)
            {
                const auto actor=actors.actors[i];if(!actor)continue;std::uint32_t kind=0,uid=0;
                if(!r.At(actor,0xD0,kind))return false;if(kind!=529)continue;
                if(!r.Secure(actor,0xD4,uid))return false;if(uid!=target)continue;if(found)return false;found=actor;
            }
            std::uint32_t actorMap=0,owner=0,hp=0,dead=1;std::int32_t team=0;unsigned char active=0;
            if(!found||!r.At(found,0xE4,actorMap)||actorMap!=map||!r.At(found,0x1864,owner)||owner!=local
                ||!r.At(found,0x128,active)||active!=1||!r.At(found,0x424,team)||team!=100
                ||!r.Secure(found,0x1168,hp)||!hp||hp>INT32_MAX||!r.Secure(found,0x142C,dead)||dead)return false;
            constexpr std::uint32_t offsets[]={0x34,0x138,0x18C,0x314,0x318,0x328,0x32C,0x330};
            constexpr std::uint32_t functions[]={0x4073D0,0x47F480,0x42C990,0x47FFB0,0x652A00,0x481000,0x4BCA70,0x652A20};
            for(std::size_t i=0;i<8;++i)if(!r.Getter(found,offsets[i],functions[i]))return false;
            if(!SameActors(map,actors))return false;output=found;return true;
        }
        bool DeathSignatures() noexcept
        {
            constexpr unsigned char setter[]={0x55,0x8B,0xEC,0x83,0xEC,0x0C,0x56,0x8B,0xF1,0xE8,0x12,0x26,0xFF,0xFF,0x84,0xC0,
                0x0F,0x84,0xE2,0x01,0,0,0x8B,0x06,0x8B,0xCE,0xFF,0x90,0x18,0x03,0,0};
            constexpr unsigned char authority[]={0x8B,0x01,0xFF,0x90,0x38,0x01,0,0,0x8B,0x0D,0x14,0x5A,0xB7,0,0x3B,0xC1,0x75,0x0A,0x85,0xC9,0x74,0x06,0xB8,1,0,0,0,0xC3,0x33,0xC0,0xC3};
            constexpr unsigned char owner[]={0x8B,0x81,0x64,0x18,0,0,0xC3};
            constexpr unsigned char apply[]={0x55,0x8B,0xEC,0x56,0x8B,0xF1,0x8B,0x06,0x57,0xFF,0x90,0xEC,0x02,0,0,0x8B,0xC8};
            constexpr unsigned char cleanup[]={0xC2,0x0C,0};
            Reads r;return r.Matches(0x481000,setter)&&r.Matches(0x473620,authority)&&r.Matches(0x47F480,owner)
                &&r.Matches(0x4BCA70,apply)&&r.Matches(0x4811FC,cleanup);
        }
        bool ReadPlayerDeathTarget(const Request& q,std::uint32_t& output) noexcept
        {
            output=0;Reads r;std::uint32_t actor=0,kind=0,uid=0,map=0,binding=0,hp=0,dead=1,table=0;
            std::uint16_t userUid=0;unsigned char active=0;
            if(q.operation!=6||q.targetId!=static_cast<std::int32_t>(q.uid)||q.x||q.y||q.reserved
                ||!r.At(0xB75A14,0,actor)||actor!=q.actorToken||!r.At(actor,0,table)
                ||!r.At(actor,0xD0,kind)||kind!=0x111||!r.Secure(actor,0xD4,uid)||uid!=q.uid
                ||!r.At(actor,0xE4,map)||map!=q.mapToken||!r.At(q.userToken,0x110,binding)||binding!=actor
                ||!r.At(q.userToken,4,userUid)||userUid!=uid||!r.At(actor,0x128,active)||active!=1
                ||!r.Secure(actor,0x1168,hp)||!hp||hp>INT32_MAX||!r.Secure(actor,0x142C,dead)||dead)return false;
            bool playerTable=false;for(auto expected:{0xA032BCU,0xA038DCU,0xA03EFCU,0xA0451CU,0xA04B3CU,0xA0515CU})playerTable|=table==expected;
            if(!playerTable)return false;
            constexpr std::uint32_t offsets[]={0x138,0x318,0x324,0x328,0x32C,0x330,0x334};
            constexpr std::uint32_t functions[]={0x493880,0x652A00,0x47FD90,0x481000,0x4A1E40,0x652A20,0x652A30};
            for(std::size_t i=0;i<7;++i)if(!r.Getter(actor,offsets[i],functions[i]))return false;
            output=actor;return true;
        }
        const char* ApplyPlayerKill(const Command& command,const client_state::Snapshot& snapshot,std::size_t& applied,bool& touched) noexcept
        {
            __try
            {
                constexpr unsigned char self[]={0x8B,0xC1,0xC3};
                constexpr unsigned char playerApply[]={0x55,0x8B,0xEC,0x64,0xA1,0,0,0,0,0x6A,0xFF,0x68,0x67,0x09,0x96,0,0x50,0x64,0x89,0x25,0,0,0,0,0x53,0x56,0x8B,0xF1,0x8B,0x06,0x57};
                Reads r;std::uint32_t actor=0;
                if(!DeathSignatures()||!r.Matches(0x493880,self)||!r.Matches(0x4A1E40,playerApply)
                    ||!LiveContext(command,snapshot)||!ReadPlayerDeathTarget(command.request,actor))return "player_not_alive_self_or_layout";
                using Authority=bool(__thiscall*)(void*);using Hp=void(__thiscall*)(void*,int,bool,void*);
                if(!reinterpret_cast<Authority>(0x473620)(reinterpret_cast<void*>(actor)))return "player_not_local_authority";
                // Same original HP setter ABI, player-specific v32C apply policy.
                touched=true;reinterpret_cast<Hp>(0x481000)(reinterpret_cast<void*>(actor),0,true,nullptr);applied=1;return nullptr;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return "native_player_death_request_failed";}
        }
        const char* ApplyKill(const Command& command,const client_state::Snapshot& snapshot,std::size_t& applied,bool& touched) noexcept
        {
            __try
            {
                std::uint32_t actor=0;const auto& q=command.request;
                if(!DeathSignatures()||!LiveContext(command,snapshot)||!FindKillTarget(q.mapToken,static_cast<std::uint32_t>(q.targetId),q.actorToken,actor))return "monster_not_alive_local_authority_or_layout";
                using Authority=bool(__thiscall*)(void*);using Hp=void(__thiscall*)(void*,int,bool,void*);
                if(!reinterpret_cast<Authority>(0x473620)(reinterpret_cast<void*>(actor)))return "monster_not_local_authority";
                // Proven original Map::allDie caller: (HP=0, force=true, source=null).
                // Original revive/status logic and subsequent game frame/reporting are preserved.
                touched=true;reinterpret_cast<Hp>(0x481000)(reinterpret_cast<void*>(actor),0,true,nullptr);applied=1;return nullptr;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return "native_death_request_failed";}
        }
        // Exact original PE ranges, independently frozen in native-contract/native-policy.
        bool InventorySignatures() noexcept
        {
            constexpr unsigned char drop_full[]={
                0x55,0x8B,0xEC,0x83,0xEC,0x08,0x57,0x8B,0xF9,0x8B,0x4F,0x20,0x85,0xC9,0x74,0x11,
                0x8B,0x47,0x24,0x2B,0xC1,0xC1,0xF8,0x02,0x83,0xF8,0x01,0x0F,0x8D,0x4C,0x01,0x00,
                0x00,0x8D,0x4F,0x10,0xE8,0x17,0x2F,0xFA,0xFF,0x8B,0x4D,0x08,0x3B,0xC8,0x0F,0x8D,
                0x39,0x01,0x00,0x00,0x8B,0x47,0x18,0x83,0x3C,0x88,0x00,0x0F,0x84,0x2C,0x01,0x00,
                0x00,0x8B,0xD0,0x8B,0x0C,0x8A,0x8B,0x01,0xFF,0x50,0x34,0x3B,0x45,0x0C,0x0F,0x82,
                0x19,0x01,0x00,0x00,0x56,0x8B,0x35,0x14,0x5A,0xB7,0x00,0x85,0xF6,0x74,0x4C,0x8B,
                0x16,0x8B,0xCE,0xFF,0x52,0x54,0x85,0xC0,0x74,0x41,0x8B,0xCF,0xE8,0xCF,0x2E,0xFA,
                0xFF,0x85,0xC0,0x74,0x0C,0x8B,0xCF,0xE8,0xC4,0x2E,0xFA,0xFF,0x83,0xF8,0x07,0x75,
                0x2A,0xA1,0xFC,0xDC,0xB5,0x00,0x66,0x81,0x78,0x04,0xFF,0xFF,0x74,0x27,0x6A,0x10,
                0x68,0x6D,0x75,0x00,0x00,0xE8,0x16,0xB4,0x44,0x00,0x8B,0x0D,0xE8,0xDC,0xB5,0x00,
                0x83,0xC4,0x04,0x50,0x6A,0x00,0xE8,0x55,0xBE,0x33,0x00,0x5E,0x32,0xC0,0x5F,0x8B,
                0xE5,0x5D,0xC2,0x08,0x00,0x8B,0x0D,0x9C,0x0A,0xBF,0x00,0x53,0x6A,0x32,0xE8,0x9D,
                0x34,0x45,0x00,0x8B,0x16,0x8B,0x1D,0x9C,0x0A,0xBF,0x00,0x8B,0xCE,0xFF,0x92,0x84,
                0x00,0x00,0x00,0x89,0x45,0xF8,0x6A,0x02,0x8D,0x45,0xF8,0x50,0x8B,0xCB,0xE8,0x7D,
                0x32,0x45,0x00,0x8B,0x16,0x8B,0x1D,0x9C,0x0A,0xBF,0x00,0x8B,0xCE,0xFF,0x92,0x88,
                0x00,0x00,0x00,0x89,0x45,0xF8,0x6A,0x02,0x8D,0x45,0xF8,0x50,0x8B,0xCB,0xE8,0x5D,
                0x32,0x45,0x00,0x8B,0x35,0x9C,0x0A,0xBF,0x00,0x8B,0xCF,0xE8,0x30,0x2E,0xFA,0xFF,
                0x6A,0x01,0x8D,0x4D,0xFC,0x51,0x8B,0xCE,0x88,0x45,0xFC,0xE8,0x40,0x32,0x45,0x00,
                0x8B,0x55,0x08,0x8B,0x0D,0x9C,0x0A,0xBF,0x00,0x6A,0x02,0x8D,0x45,0x08,0x50,0x89,
                0x55,0x08,0xE8,0x29,0x32,0x45,0x00,0x8B,0x4D,0x0C,0x6A,0x04,0x8D,0x55,0x08,0x89,
                0x4D,0x08,0x8B,0x0D,0x9C,0x0A,0xBF,0x00,0x52,0xE8,0x12,0x32,0x45,0x00,0x8B,0x0D,
                0x9C,0x0A,0xBF,0x00,0xE8,0x77,0x32,0x45,0x00,0x6A,0x01,0x8B,0xCF,0xE8,0xEE,0xFB,
                0xFA,0xFF,0x5B,0x5E,0xB0,0x01,0x5F,0x8B,0xE5,0x5D,0xC2,0x08,0x00,0x32,0xC0,0x5F,
                0x8B,0xE5,0x5D,0xC2,0x08,0x00,
            };
            constexpr unsigned char allow_full[]={
                0x56,0x57,0x8B,0xF9,0x8B,0x07,0x6A,0x02,0xFF,0x50,0x1C,0x84,0xC0,0x74,0x45,0x6A,
                0x00,0x68,0xC0,0xD6,0xAF,0x00,0x68,0xA8,0xD6,0xAF,0x00,0x6A,0x00,0x57,0xE8,0x5E,
                0xE2,0x4B,0x00,0x8B,0xF0,0x83,0xC4,0x14,0x85,0xF6,0x74,0x28,0x8B,0x16,0x8B,0xCE,
                0xFF,0x92,0xDC,0x00,0x00,0x00,0x83,0x38,0x13,0x75,0x19,0x8B,0x06,0x8B,0xCE,0xFF,
                0x90,0xDC,0x00,0x00,0x00,0x8B,0xC8,0x83,0xC1,0x04,0xE8,0xC1,0x55,0xFA,0xFF,0x83,
                0xF8,0x01,0x74,0x13,0x8B,0x17,0x8B,0xCF,0xFF,0x52,0x58,0x84,0xC0,0x74,0x08,0x5F,
                0xB8,0x01,0x00,0x00,0x00,0x5E,0xC3,0x5F,0x33,0xC0,0x5E,0xC3,
            };
            constexpr unsigned char fallback_full[]={
                0x8B,0x41,0x24,0x85,0xC0,0x74,0x0F,0x83,0xF8,0x03,0x75,0x07,0x8A,0x41,0x28,0x84,
                0xC0,0x75,0x03,0x33,0xC0,0xC3,0xB8,0x01,0x00,0x00,0x00,0xC3,
            };
            constexpr unsigned char descriptor_getter[]={
                0x8D,0x41,0x08,0xC3,
            };
            constexpr unsigned char count_getter[]={
                0x81,0xC1,0xA0,0x00,0x00,0x00,0xE9,0x75,0x05,0xFA,0xFF,
            };
            constexpr unsigned char typeflag_getter[]={
                0x55,0x8B,0xEC,0x8B,0x45,0x08,0x8B,0x49,0x04,0x23,0xC8,0x3B,0xC8,0x0F,0x94,0xC0,
                0x5D,0xC2,0x04,0x00,
            };
            Reads r;return r.Matches(0x45f0f0,drop_full)
                &&r.Matches(0x45ca20,allow_full)
                &&r.Matches(0x461a10,fallback_full)
                &&r.Matches(0x4619a0,descriptor_getter)
                &&r.Matches(0x461ab0,count_getter)
                &&r.Matches(0x45c040,typeflag_getter);
        }
        const char* ApplyInventoryDrop(const Command& command,const client_state::Snapshot& snapshot,std::size_t& applied,bool& touched) noexcept
        {
            __try
            {
                const auto& q=command.request;InventoryState before;InventoryRow item;Reads r;
                if(!InventorySignatures()||!LiveContext(command,snapshot))return "inventory_context_or_signatures";
                if(!ReadInventoryState(before))return "inventory_unavailable";
                if(const auto error=ValidateInventoryTarget(q,before,item))return error;
                std::uint32_t table=0,trade=0,socket=0,readable=0;std::uint16_t target=0;
                if(!r.At(item.item,0,table)||(table!=0x99EE84&&table!=0x9A99DC&&table!=0x99C5EC&&table!=0x99C68C)
                    ||!r.Getter(item.item,0x54,0x45CA20)||!r.Getter(item.item,0x58,0x461A10)
                    ||!r.Getter(item.item,0x1C,0x45C040))return "unsupported_discard_item";
                if(!r.At(0xB5DCFC,0,trade)||!r.At(trade,4,target)||target!=0xFFFF
                    ||!r.At(0xBF0A9C,0,socket)||!r.At(socket,0,readable))return "trade_or_socket_unavailable";
                using Allow=bool(__thiscall*)(void*);using Drop=bool(__thiscall*)(void*,std::int32_t,std::uint32_t);
                // Keep the exact original UI predicate before its complete sender.
                if(!reinterpret_cast<Allow>(0x45CA20)(reinterpret_cast<void*>(item.item)))return "native_discard_forbidden";
                InventoryState after;InventoryRow current;
                if(!LiveContext(command,snapshot)||!ReadInventoryState(after))return "inventory_context_changed";
                if(const auto error=ValidateInventoryTarget(q,after,current))return error;
                if(after.container!=before.container||after.array!=before.array||after.capacity!=before.capacity
                    ||current.count!=item.count)return "inventory_context_changed";
                touched=true;
                if(!reinterpret_cast<Drop>(0x45F0F0)(reinterpret_cast<void*>(after.container),q.targetId,static_cast<std::uint32_t>(q.x)))
                {touched=false;return "native_drop_refused";}
                applied=1;return nullptr;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){return "native_drop_request_failed";}
        }
        void FlushResult() noexcept
        {if(!GameResult.serial||!TryAcquireSRWLockExclusive(&OutboxLock))return;Outbox=GameResult;ReleaseSRWLockExclusive(&OutboxLock);}
        void Dispatch(const Command& command,const client_state::Snapshot& snapshot) noexcept
        {
            const auto& q=command.request;const auto reason=ValidateRequest(q,snapshot,command.lease,Now(),FileTime(),Consumed);
            if(q.requestId>Consumed)Consumed=q.requestId;
            std::size_t applied=0;bool touched=false;const char* error=reason;
            if(!error&&(!Native.supported||!Native.supported(Native.context)))error="unsupported_layout";
            if(!error&&!LiveContext(command,snapshot))error="live_context_unavailable";
            if(!error)
            {
                if(q.operation==1)
                {
                    TeleportBinding binding;const PlayerTeleportOperations ops{&binding,&SetPlayerTeleport,&PublishPlayerTeleport};
                    error=CompletePlayerTeleport(ops,q.actorToken,q.x,q.y,touched,applied);
                }
                else if(q.operation==2)error=ApplyKill(command,snapshot,applied,touched);
                else if(q.operation==5)error=ApplyInventoryDrop(command,snapshot,applied,touched);
                else if(q.operation==6)error=ApplyPlayerKill(command,snapshot,applied,touched);
                else error=ApplyGround(command,snapshot,applied,touched);
            }
            GameResult={q,error?(touched?"partial":"rejected"):"applied",error?error:(q.operation==1?"native_position_publication_requested":q.operation==2?"native_death_requested":q.operation==6?"native_player_death_requested":q.operation==5?"native_drop_requested":"native_position_observed"),Now(),++NextSerial,applied};FlushResult();
        }
        bool SameFile(const BY_HANDLE_FILE_INFORMATION& a,const BY_HANDLE_FILE_INFORMATION& b) noexcept
        {return a.dwVolumeSerialNumber==b.dwVolumeSerialNumber&&a.nFileIndexHigh==b.nFileIndexHigh&&a.nFileIndexLow==b.nFileIndexLow
            &&a.ftLastWriteTime.dwHighDateTime==b.ftLastWriteTime.dwHighDateTime&&a.ftLastWriteTime.dwLowDateTime==b.ftLastWriteTime.dwLowDateTime
            &&a.nFileSizeHigh==b.nFileSizeHigh&&a.nFileSizeLow==b.nFileSizeLow;}
        template<class T> bool ReadExact(const wchar_t* path,T& output,BY_HANDLE_FILE_INFORMATION* info=nullptr) noexcept
        {
            auto file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE)return false;BY_HANDLE_FILE_INFORMATION details{};std::array<unsigned char,sizeof(T)+1> bytes{};DWORD n=0;
            const bool okay=GetFileInformationByHandle(file,&details)&&details.nFileSizeHigh==0&&details.nFileSizeLow==sizeof(T)
                &&ReadFile(file,bytes.data(),static_cast<DWORD>(bytes.size()),&n,nullptr)&&n==sizeof(T);CloseHandle(file);
            if(!okay)return false;std::memcpy(&output,bytes.data(),sizeof(T));if(info)*info=details;return true;
        }
        void ReadCommand() noexcept
        {
            wchar_t path[MAX_PATH]{};Command command;BY_HANDLE_FILE_INFORMATION info{};
            if(swprintf_s(path,L"%s\\dfdebug-test-control-%lu.bin",Directory,Native.pid)<0||!ReadExact(path,command.request,&info)
                ||(HaveFile&&SameFile(info,PreviousFile)))return;
            if(swprintf_s(path,L"%s\\dfdebug-test-lease-%u.bin",Directory,command.request.uid)<0)return;
            if(!ReadExact(path,command.lease))command.lease={0,0,0,0,0,0,0};
            if(!TryAcquireSRWLockExclusive(&InboxLock))return;
            if(!HaveInbox){Inbox=command;HaveInbox=true;PreviousFile=info;HaveFile=true;}ReleaseSRWLockExclusive(&InboxLock);
        }
        void ReadLiveLease() noexcept
        {
            const auto uid=InterlockedCompareExchange(&LeaseUid,0,0);Lease lease{0,0,0,0,0,0,0};wchar_t path[MAX_PATH]{};
            if(uid>0&&swprintf_s(path,L"%s\\dfdebug-test-lease-%ld.bin",Directory,uid)>0)
                if(!ReadExact(path,lease))lease={0,0,0,0,0,0,0};
            if(!TryAcquireSRWLockExclusive(&LeaseLock))return;LiveLease=lease;ReleaseSRWLockExclusive(&LeaseLock);
        }
        void Token(std::ostream& out,std::uint32_t value)
        {char text[16]{};std::snprintf(text,sizeof(text),"0x%08X",value);out<<'"'<<text<<'"';}
        void ExportResult()
        {
            Result result;if(!TryAcquireSRWLockShared(&OutboxLock))return;result=Outbox;ReleaseSRWLockShared(&OutboxLock);
            if(!result.serial||result.serial==ExportedSerial)return;const auto& q=result.request;std::ostringstream out;out.imbue(std::locale::classic());
            out<<"{\"schema\":\"dfl-client-test-control/v1\",\"pid\":"<<Native.pid<<",\"process_started_filetime\":"<<Native.processStartedFileTime
                <<",\"request_id\":"<<q.requestId<<",\"uid\":"<<q.uid<<",\"generation\":"<<q.generation<<",\"actor_token\":";Token(out,q.actorToken);
            out<<",\"map_token\":";Token(out,q.mapToken);out<<",\"user_token\":";Token(out,q.userToken);
            out<<",\"operation\":"<<q.operation<<",\"target_id\":"<<q.targetId<<",\"x\":"<<q.x<<",\"y\":"<<q.y<<",\"reserved\":"<<q.reserved<<",\"session_nonce\":"<<q.sessionNonce
                <<",\"run_id\":"<<q.runId<<",\"tick_ms\":"<<result.tick<<",\"status\":\""<<result.status<<"\",\"reason\":\""<<result.reason
                <<"\",\"applied_count\":"<<result.appliedCount<<",\"server_committed\":false,\"persistence_verified\":false}";
            const auto json=out.str();wchar_t path[MAX_PATH]{},temporary[MAX_PATH]{};
            if(swprintf_s(path,L"%s\\dfdebug-test-control-%lu.json",Directory,Native.pid)<0||swprintf_s(temporary,L"%s.tmp",path)<0)return;
            auto file=CreateFileW(temporary,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);if(file==INVALID_HANDLE_VALUE)return;
            DWORD n=0;const bool okay=WriteFile(file,json.data(),static_cast<DWORD>(json.size()),&n,nullptr)&&n==json.size()&&FlushFileBuffers(file);CloseHandle(file);
            if(okay&&MoveFileExW(temporary,path,MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH))ExportedSerial=result.serial;else DeleteFileW(temporary);
        }
        void ExportState()
        {
            TestState state;if(!TryAcquireSRWLockShared(&StateLock))return;state=PublishedState;ReleaseSRWLockShared(&StateLock);
            if(!state.serial||state.serial==ExportedStateSerial)return;const auto& q=state.identity;std::ostringstream out;out.imbue(std::locale::classic());out<<std::setprecision(9);
            out<<"{\"schema\":\"dfl-client-test-state/v1\",\"pid\":"<<Native.pid<<",\"uid\":"<<q.uid<<",\"process_started_filetime\":"<<Native.processStartedFileTime
                <<",\"generation\":"<<q.generation<<",\"map_token\":";Token(out,q.mapToken);
            out<<",\"captured_tick_ms\":"<<state.tick<<",\"snapshot_sequence\":"<<q.snapshotSequence<<",\"session_nonce\":"<<q.sessionNonce<<",\"run_id\":"<<q.runId
                <<",\"observer\":{\"actor_token\":";Token(out,q.actorToken);out<<",\"user_token\":";Token(out,q.userToken);
            out<<",\"actor_map_token\":";if(state.observerMap)Token(out,state.observerMap);else out<<"null";
            out<<",\"stage_token\":";Token(out,state.stage);
            out<<",\"dead\":"<<(state.observerDead?"true":"false")<<'}'
                <<",\"valid\":"<<(state.valid?"true":"false")<<",\"truncated\":"<<(state.truncated?"true":"false")<<",\"reason\":\""<<state.reason<<"\",\"ground_items\":[";
            for(std::size_t i=0;i<state.count;++i)
            {
                if(i)out<<',';const auto& row=state.ground[i];out<<"{\"ground_id\":"<<row.ground.id<<",\"actor_token\":";Token(out,row.ground.actor);
                out<<",\"descriptor_token\":";Token(out,row.ground.descriptor);
                out<<",\"x\":"<<row.position.x<<",\"y\":"<<row.position.y<<",\"z\":"<<row.position.z<<",\"active\":"<<(row.ground.active?"true":"false")<<",\"pending\":"<<(row.ground.pending?"true":"false")<<'}';
            }
            out<<"],\"supported_operations\":[1,2,3,4,5,6],\"inventory\":{\"valid\":"<<(state.inventory.valid?"true":"false")
                <<",\"reason\":\""<<state.inventory.reason<<"\",\"space\":0,\"container_token\":";Token(out,state.inventory.container);
            out<<",\"array_token\":";Token(out,state.inventory.array);
            out<<",\"capacity\":"<<state.inventory.capacity<<",\"pending\":"<<(state.inventory.pending?"true":"false")<<",\"items\":[";
            for(std::size_t i=0;i<state.inventory.count;++i)
            {
                if(i)out<<',';const auto& row=state.inventory.rows[i];out<<"{\"slot\":"<<row.slot<<",\"item_token\":";Token(out,row.item);
                out<<",\"item_id\":"<<row.id<<",\"count\":"<<row.count<<'}';
            }
            out<<"]}}";const auto json=out.str();wchar_t path[MAX_PATH]{},temporary[MAX_PATH]{};
            if(swprintf_s(path,L"%s\\dfdebug-test-state-%lu.json",Directory,Native.pid)<0||swprintf_s(temporary,L"%s.tmp",path)<0)return;
            auto file=CreateFileW(temporary,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);if(file==INVALID_HANDLE_VALUE)return;
            DWORD n=0;const bool okay=WriteFile(file,json.data(),static_cast<DWORD>(json.size()),&n,nullptr)&&n==json.size();CloseHandle(file);
            if(okay&&MoveFileExW(temporary,path,MOVEFILE_REPLACE_EXISTING))ExportedStateSerial=state.serial;else DeleteFileW(temporary);
        }
    }
    void Initialize(const wchar_t* settings,const wchar_t* directory,client_state::Reader reader) noexcept
    {
        InterlockedExchange(&Enabled,0);
        if(!settings||!directory||wcsnlen_s(directory,MAX_PATH)>=MAX_PATH-64||GetPrivateProfileIntW(L"DebugOverlay",L"EnableTestControl",0,settings)!=1)return;
        Native=reader;wcscpy_s(Directory,directory);GameThread=0;HaveInbox=HaveFile=false;Inbox={};GameResult={};Outbox={};Consumed=NextSerial=ExportedSerial=0;
        GameState={};PublishedState={};LiveLease={};InterlockedExchange(&LeaseUid,0);StateSerial=ExportedStateSerial=LastStateCapture=0;
        InterlockedExchange(&Enabled,Native.read&&Native.pid&&Native.processStartedFileTime?1:0);
    }
    void OnFrame() noexcept
    {if(!IsEnabled())return;client_state::Snapshot snapshot;if(client_state::CopyLatest(snapshot))OnFrame(snapshot);}
    void OnFrame(const client_state::Snapshot& snapshot) noexcept
    {
        if(!IsEnabled())return;if(!GameThread)GameThread=GetCurrentThreadId();if(GameThread!=GetCurrentThreadId())return;FlushResult();
        CaptureTestState(snapshot);
        Command command;bool have=false;if(TryAcquireSRWLockExclusive(&InboxLock)){if(HaveInbox){command=Inbox;HaveInbox=false;have=true;}ReleaseSRWLockExclusive(&InboxLock);}
        if(have)Dispatch(command,snapshot);
    }
    void PumpWorker() noexcept
    {if(!IsEnabled())return;try{ReadLiveLease();ReadCommand();ExportResult();ExportState();}catch(...){/* worker IO must not escape into host */}}
    void Shutdown() noexcept{InterlockedExchange(&Enabled,0);}
}
