#include "client_test_control.h"
#include <cstring>
namespace dfl::client_test_control
{
    const char* PlanGroundMoves(const Request& r,const Ground* ground,std::size_t count,MovePlan& plan) noexcept
    {
        plan.count=0;
        if(!ground||!count||count>MaximumGround||(r.operation!=3&&r.operation!=4))return "invalid_ground_vector";
        std::size_t target=count;
        for(std::size_t i=0;i<count;++i)
        {
            const auto& g=ground[i];
            if(!g.actor||!g.id||g.id>65535)return "invalid_ground_identity";
            for(std::size_t j=0;j<i;++j)if(g.id==ground[j].id||g.actor==ground[j].actor)return "duplicate_ground_identity";
            if(g.id==static_cast<std::uint32_t>(r.targetId))target=i;
            const bool selected=g.id==static_cast<std::uint32_t>(r.targetId);
            if((selected&&!g.active)||((selected||(r.operation==4&&g.active))&&g.pending))return "ground_inactive_or_pending";
        }
        if(target==count)return "target_not_found";
        plan.moves[plan.count++]={ground[target],r.x,r.y};
        if(r.operation==4)
        {
            const auto bits=static_cast<std::uint32_t>(r.reserved);
            const auto parkX=static_cast<std::int16_t>(bits&0xFFFF),parkY=static_cast<std::int16_t>(bits>>16);
            for(std::size_t i=0;i<count;++i)if(i!=target&&ground[i].active)plan.moves[plan.count++]={ground[i],parkX,parkY};
        }
        return nullptr;
    }
    const char* ValidateRequest(const Request& r,const client_state::Snapshot& s,const Lease& l,std::uint64_t now,std::uint64_t ft,std::uint64_t consumed) noexcept
    {
        if(std::memcmp(r.magic,"DLTC",4)||r.version!=1||r.size!=112||r.operation<1||r.operation>6||((r.operation!=4&&r.operation!=5)&&r.reserved))return "invalid_request";
        if(!r.requestId||r.requestId<=consumed)return "replayed_request";
        if(l.magic!=0x4C545444||l.version!=1||l.flags!=1||!l.nonce||!l.runId||!l.issuedFileTime
            ||l.issuedFileTime>ft||ft-l.issuedFileTime>15000000||l.uid!=r.uid||l.nonce!=r.sessionNonce||l.runId!=r.runId)return "server_lease_unavailable";
        if(!s.valid||!s.layoutSupported||s.truncated||!s.haveMe||!s.scene.known||s.scene.value!=3||!s.me.uid.known
            ||!s.me.dead.known||s.me.dead.value||!s.me.sameMap.known||!s.me.sameMap.value||!s.me.position.known)return "actor_unavailable";
        if(!r.issuedTickMs||r.issuedTickMs>now||now-r.issuedTickMs>1000||!r.snapshotTickMs||r.snapshotTickMs>now
            ||now-r.snapshotTickMs>750||s.capturedTickMs>now||now-s.capturedTickMs>750)return "expired_request";
        if(r.snapshotTickMs>s.capturedTickMs||!r.snapshotSequence||r.snapshotSequence>s.sequence||s.sequence-r.snapshotSequence>5)return "snapshot_mismatch";
        if(r.pid!=s.pid||r.uid!=s.me.uid.value||r.processStartedFileTime!=s.processStartedFileTime||r.generation!=s.generation
            ||r.actorToken!=s.me.actorToken||r.mapToken!=s.me.mapToken||r.userToken!=s.me.userToken||!r.actorToken||!r.mapToken||!r.userToken)return "identity_mismatch";
        if(!r.uid||r.uid>65535)return "invalid_target";
        if(r.operation==6)
            return r.targetId!=static_cast<std::int32_t>(r.uid)||r.x||r.y||r.reserved ? "invalid_player_death_target" : nullptr;
        if(r.operation==5)
        {
            if(r.targetId<0||r.targetId>=512||(r.targetId>0&&r.targetId<9)||r.x<1||r.y<0||r.y>65535
                ||((r.targetId==0)!=(r.y==0))||static_cast<std::uint32_t>(r.reserved)<0x10000)return "invalid_inventory_target";
            return nullptr;
        }
        if(r.x < -32768||r.x>32767||r.y < -32768||r.y>32767||!r.uid||r.uid>65535
            ||(r.operation==1&&r.targetId!=static_cast<std::int32_t>(r.uid))
            ||(r.operation==2&&(r.targetId<0||r.targetId>65535))
            ||(r.operation>=3&&(r.targetId<=0||r.targetId>65535)))return "invalid_target";
        return nullptr;
    }
    const char* CompletePlayerTeleport(const PlayerTeleportOperations& ops,std::uint32_t actor,
        std::int32_t x,std::int32_t y,bool& touched,std::size_t& applied) noexcept
    {
        if(!ops.set||!ops.publish)return "native_position_publication_unavailable";
        if(!ops.set(ops.context,actor,x,y,touched))return "native_position_failed";
        applied=1;
        return ops.publish(ops.context,actor)?nullptr:"native_position_publication_failed";
    }
    bool ReadPositionBinding(const client_state::Reader& r,std::uint32_t actor,PositionBinding& out) noexcept
    {
        auto read=[&](std::uint32_t base,std::uint32_t offset,void* to,std::size_t n) noexcept
        {return base>=0x10000&&static_cast<std::uint64_t>(base)+offset+n<=0x100000000ULL&&r.read&&r.read(r.context,base+offset,to,n);};
        auto u=[&](std::uint32_t base,std::uint32_t offset,std::uint32_t& to) noexcept{return read(base,offset,&to,4);};
        PositionBinding p{};p.actor=actor;std::uint32_t table=0,f=0,provider=0,vectorTable=0,get=0,set=0;unsigned char active=0;
        if(!u(actor,0,table)||!u(table,0x80,f)||f!=0x471610||!u(table,0x74,f)||f!=0x4715F0
            ||!u(actor,0xE0,p.controller)||!u(p.controller,0x78,provider)||!read(p.controller,0x7C,&active,1)
            ||(provider&&active)||!u(p.controller,0,p.vector))return false;
        if(!p.vector){if(p.controller>UINT32_MAX-8)return false;p.vector=p.controller+8;}
        if(!u(p.vector,0,vectorTable)||!u(vectorTable,0,get)||!u(vectorTable,4,set)
            ||!((get==0x4736A0&&set==0x4736B0)||(get==0x4CAC70&&set==0x4CAC90)))return false;
        out=p;return true;
    }
}
