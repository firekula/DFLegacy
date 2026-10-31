#include "client_party.h"
#include "client_navigation.h"
#include <array>
#include <cstring>
#include <cstddef>
#include <algorithm>
namespace dfl::client_party
{
    namespace
    {
        struct Reads
        {
            const client_state::Reader& reader;
            template<class T> bool At(std::uint32_t base,std::uint32_t offset,T& value) const noexcept
            {
                const auto at=static_cast<std::uint64_t>(base)+offset;
                return base>=0x10000 && at+sizeof(T)<=0x100000000ULL && reader.read
                    && reader.read(reader.context,static_cast<std::uint32_t>(at),&value,sizeof(T));
            }
            bool Secure(std::uint32_t at,std::uint32_t& value) const noexcept
            {
                std::uint32_t manager=0,key=0,encoded=0,index=0,count=0,a=0,b=0,va=0,vb=0;
                if(!At(0xCFD458,0,manager)||!At(0xCFD45C,0,key)||!At(at,0,encoded)||!At(at,4,index)
                    ||((encoded^key)-at)!=index||!At(manager,4,count)||index>=count||index>0x1000000
                    ||!At(manager,0x44,a)||!At(manager,0x48,b)||a<0x10000||b<0x10000
                    ||a>UINT32_MAX-index*4||b>UINT32_MAX-index*4)return false;
                a+=index*4;b+=index*4;
                if(!At(a,0,va)||!At(b,0,vb)||(va^a^key)!=(vb^b^key))return false;
                value=va^a^key;return true;
            }
        };
        bool Vector(const Reads& r,std::uint32_t owner,std::uint32_t offset,std::uint32_t& begin,std::uint32_t& end,unsigned maximum) noexcept
        {
            return r.At(owner,offset,begin)&&r.At(owner,offset+4,end)&&end>=begin&&(end-begin)%4==0
                &&(end-begin)/4<=maximum&&(!end||begin>=0x10000);
        }
    }
    bool ReadPending(const client_state::Reader& reader,std::uint32_t manager,Pending& output) noexcept
    {
        output={};Reads r{reader};std::uint32_t begin=0,end=0,cap=0,closedBegin=0,closedEnd=0;
        // CNRDResponsePeerWindow constructor 7C00F0 passes class 0x25 to 7BDB30.
        // Active vector: D8 + 0x25*16 = 328; deferred deletion: EF8/EFC.
        if(!Vector(r,manager,0x328,begin,end,1)||!r.At(manager,0x330,cap)||cap<end
            ||!Vector(r,manager,0xEF8,closedBegin,closedEnd,64))return false;
        output.known=true;output.reason="none";
        if(begin==end)return true;
        std::uint32_t window=0;
        if(!r.At(begin,0,window))return false;
        for(std::uint32_t at=closedBegin;at<closedEnd;at+=4)
        {std::uint32_t closed=0;if(!r.At(at,0,closed))return false;if(closed==window)return true;}
        std::uint32_t vt=0,cls=0,owner=0,root=0,uid=0,type=0;unsigned char visible=0;
        if(!r.At(window,0,vt)||vt!=0xA7E2F4||!r.At(window,4,cls)||cls!=0x25
            ||!r.At(window,0x10,owner)||owner!=manager||!r.At(window,8,root)||!r.At(root,0x10,visible)||visible>1
            ||!r.At(window,0x38,uid)||!uid||uid>65535||!r.At(window,0x3C,type)||type>3)return false;
        if(!visible)return true;
        // Read-only equivalent of 8F3F60 / 40DF60. All timer fields use mirrored secure ints.
        if(window>UINT32_MAX-0x80)return false;
        const auto timer=window+0x48;
        std::uint32_t enabled=0,running=0,paused=0,index=0,offset=0,duration=0,clock=0;unsigned char protectedClock=0;
        if(!r.Secure(timer+0x10,enabled)||enabled!=1||!r.Secure(timer+0x18,running)||running!=1
            ||!r.Secure(timer+0x20,paused)||paused!=0||!r.Secure(timer+8,index)||index>=64
            ||!r.Secure(timer+0x28,offset)||!r.Secure(timer+0x38,duration)||duration!=10000
            ||!r.At(0xCFD44C,0,protectedClock)||protectedClock>1||!r.At(0xCFD278,index*4,clock))return false;
        if(protectedClock && !r.Secure(clock,clock))return false;
        const auto elapsed=static_cast<std::int32_t>(clock+offset);
        if(elapsed<0)return false;
        output.present=true;output.windowToken=window;output.requesterUid=uid;output.peerType=type;output.timerOffset=offset;
        output.remainingMs=static_cast<std::uint32_t>(elapsed)>=duration?0:duration-static_cast<std::uint32_t>(elapsed);
        output.canReply=type==0 && output.remainingMs>0;
        output.reason=type!=0?"non_party_prompt":(output.remainingMs?"ready":"expired_invitation");
        return true;
    }
    bool ReadLiveContext(const client_state::Reader& reader,const Request& request,std::uint32_t& im,bool requirePeer) noexcept
    {
        Reads r{reader};std::uint32_t main=0,scene=0,context=0,area=0,actor=0,vt=0,uid=0,map=0,binding=0;
        std::int32_t town=-1,areaId=-1,rawState=-1,mode=-1;std::uint16_t userUid=0;
        if(!r.At(0xB5D6F0,0,main)||main>UINT32_MAX-0x10||!r.Secure(main+0x10,scene)||scene!=1
            ||!r.At(main,0x1C,context)||context!=request.contextToken||!r.At(context,0x88,town)||town!=request.townId
            ||!r.At(context,0x8C,area)||!r.At(area,0,areaId)||areaId!=request.areaId
            ||!r.At(0xB75A18,0,actor)||actor!=request.actorToken||!r.At(actor,0,vt)||vt!=0x9988DC
            ||actor>UINT32_MAX-0xD4||!r.Secure(actor+0xD4,uid)||uid!=request.uid
            ||!r.At(actor,0xE4,map)||map!=request.mapToken||!map
            ||!r.At(request.userToken,4,userUid)||userUid!=uid||!r.At(request.userToken,0x10C,binding)||binding!=actor
            ||!r.At(request.userToken,0x2C,rawState)||rawState!=0
            ||!r.At(0xB5DCE8,0,im)||!r.At(im,0xFC4,mode)||mode!=0)return false;
        if(!requirePeer)return true;
        std::uint32_t manager=0,head=0,node=0,peer=0;std::array<std::uint32_t,256> visited{};std::size_t count=0;
        if(!r.At(0xB5DCB8,0,manager)||!r.At(manager,0xC,head)||!r.At(head,0,node))return false;
        while(node!=head)
        {
            if(count==visited.size()||std::find(visited.begin(),visited.begin()+count,node)!=visited.begin()+count)return false;
            visited[count++]=node;std::uint32_t user=0;std::uint16_t candidate=0;
            if(!r.At(node,0xC,user)||!r.At(user,4,candidate))return false;
            if(candidate==request.targetUid){if(peer)return false;peer=user;}
            if(!r.At(node,0,node))return false;
        }
        if(!peer||!r.At(peer,0x2C,rawState)||rawState!=0||!r.At(peer,0x10C,actor)||!r.At(actor,0,vt)||vt!=0x9988DC
            ||actor>UINT32_MAX-0xD4||!r.Secure(actor+0xD4,uid)||uid!=request.targetUid
            ||!r.At(actor,0xE4,map)||map!=request.mapToken)return false;
        return true;
    }
    const char* ReadRequestFailure(const client_state::Reader& reader,const Request& request) noexcept
    {
        std::uint32_t im=0;
        if(!ReadLiveContext(reader,request,im,true))return nullptr;
        Reads r{reader};std::uint32_t ownParty=0,manager=0,head=0,node=0;
        if(!r.At(request.userToken,0xFC,ownParty)||!ownParty
            ||!r.At(0xB5DCB8,0,manager)||!r.At(manager,0xC,head)||!r.At(head,0,node))return nullptr;
        // Original 4FEA65..4FEA97 rejects ANY two non-null party links, including
        // the one-member party retained by the last dungeon give-up. Never
        // disband it implicitly or treat the resolved HUD row count as membership.
        std::array<std::uint32_t,256> visited{};std::size_t count=0;
        while(node!=head)
        {
            if(count==visited.size()||std::find(visited.begin(),visited.begin()+count,node)!=visited.begin()+count)return nullptr;
            visited[count++]=node;std::uint32_t user=0;std::uint16_t uid=0;
            if(!r.At(node,0xC,user)||!r.At(user,4,uid))return nullptr;
            if(uid==request.targetUid)
            {
                std::uint32_t peerParty=0;
                return r.At(user,0xFC,peerParty)&&peerParty?"both_already_in_parties":nullptr;
            }
            if(!r.At(node,0,node))return nullptr;
        }
        return nullptr;
    }
    void AssignInvitationSerial(Pending& pending,Pending& last,std::uint64_t& next) noexcept
    {
        // Unknown/empty frames are not proof of a different episode. In particular an
        // exception after a native send must not make the same prompt actionable again.
        if(!pending.known||!pending.present)return;
        const bool same=last.present&&last.windowToken==pending.windowToken&&last.requesterUid==pending.requesterUid
            &&last.peerType==pending.peerType&&last.timerOffset==pending.timerOffset;
        pending.serial=same?last.serial:++next;
        last=pending;
    }
    const char* ValidateRequest(const Request& r,const client_state::Snapshot& snapshot,const Pending& pending,
                               std::uint64_t now,std::uint64_t consumed,std::uint64_t consumedPrompt,std::uint64_t sessionFloor) noexcept
    {
        if(std::memcmp(r.magic,"DLPT",4)!=0||r.version!=1||r.size!=sizeof(Request))return "invalid_request";
        if(sessionFloor&&r.issuedTickMs<=sessionFloor)return "previous_session";
        client_navigation::Request binding;
        static_assert(offsetof(Request,operation)==offsetof(client_navigation::Request,x),"shared identity prefix");
        std::memcpy(&binding,&r,offsetof(Request,operation));std::memcpy(binding.magic,"DLNV",4);binding.size=sizeof(binding);
        if(const auto* error=client_navigation::ValidateRequest(binding,snapshot,now,consumed))return error;
        if(r.operation<1||r.operation>3)return "invalid_operation";
        if(!r.targetUid||r.targetUid>65535||r.targetUid==r.uid)return "invalid_target";
        if(!pending.known)return "pending_unknown";
        if(r.operation==1)
        {
            if(r.windowToken||r.invitationSerial)return "invalid_request_invitation";
            if(pending.present)return "pending_invitation";
        }
        else
        {
            if(!pending.present||!pending.canReply||pending.peerType!=0||!pending.remainingMs)return "invitation_unavailable";
            if(!r.invitationSerial||r.invitationSerial<=consumedPrompt||r.invitationSerial!=pending.serial
                ||r.windowToken!=pending.windowToken||r.targetUid!=pending.requesterUid)return "invitation_mismatch";
        }
        return nullptr;
    }
    const char* Perform(const Request& request,const client_state::Snapshot& snapshot,const Pending& pending,std::uint64_t now,
                        std::uint64_t& consumed,std::uint64_t& consumedPrompt,const Operations& ops,std::uint64_t sessionFloor) noexcept
    {
        if(const auto* error=ValidateRequest(request,snapshot,pending,now,consumed,consumedPrompt,sessionFloor))return error;
        consumed=request.requestId; // A failed native precondition is terminal for this command too.
        if(!ops.live||!ops.live(ops.context,request))return "live_context_changed";
        const auto action=request.operation==1?ops.request:(request.operation==2?ops.accept:ops.reject);
        if(!action)return "native_action_unavailable";
        // Consume before invocation: a send/exception cannot cause a second attempt on the same prompt.
        if(request.operation!=1)consumedPrompt=request.invitationSerial;
        return action(ops.context,request)?nullptr:"native_action_failed";
    }
    bool SendRejection(std::uint32_t requesterUid,const RejectionWriter& writer) noexcept
    {
        if(!requesterUid||requesterUid>65535||!writer.begin||!writer.write||!writer.send)return false;
        const std::uint16_t uid=static_cast<std::uint16_t>(requesterUid),error=100;
        const unsigned char peerType=0;const std::uint32_t unused=0;
        return writer.begin(writer.context,11)>=0&&writer.write(writer.context,&uid,2)>=0
            &&writer.write(writer.context,&peerType,1)>=0&&writer.write(writer.context,&unused,4)>=0
            &&writer.write(writer.context,&error,2)>=0&&writer.send(writer.context)>=0;
    }
}
