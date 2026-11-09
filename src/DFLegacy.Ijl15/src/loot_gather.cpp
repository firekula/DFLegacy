#include "loot_gather.h"
#include <array>
#include <cmath>
#include <cstring>

namespace dfl::loot_gather::detail
{
    namespace
    {
        constexpr std::size_t MaximumActors = 4096;
        struct Reads
        {
            const Runtime& Runtime;
            std::uint32_t Manager = 0, Key = 0;
            template<class T> bool At(std::uint32_t base, std::uint32_t offset, T& value) const noexcept
            {
                return base >= 0x10000 && static_cast<std::uint64_t>(base) + offset + sizeof(T) <= 0x100000000ULL
                    && Runtime.Read && Runtime.Read(Runtime.State, base + offset, &value, sizeof(T));
            }
            bool Init() noexcept { return At(0xCFD458, 0, Manager) && At(0xCFD45C, 0, Key); }
            bool Secure(std::uint32_t base, std::uint32_t offset, std::uint32_t& value) const noexcept
            {
                if (base > UINT32_MAX - offset) return false;
                const auto address = base + offset;
                std::uint32_t encoded=0,index=0,count=0,a=0,b=0,va=0,vb=0;
                if (!At(address,0,encoded) || !At(address,4,index) || (encoded^Key)-address != index
                    || !At(Manager,4,count) || index>=count || index>0x1000000
                    || !At(Manager,0x44,a) || !At(Manager,0x48,b) || a<0x10000 || b<0x10000
                    || a>UINT32_MAX-index*4 || b>UINT32_MAX-index*4) return false;
                a+=index*4; b+=index*4;
                if (!At(a,0,va) || !At(b,0,vb) || (va^a^Key)!=(vb^b^Key)) return false;
                value=va^a^Key; return true;
            }
            bool Method(std::uint32_t actor,std::uint32_t slot,std::uint32_t expected) const noexcept
            { std::uint32_t table=0,fn=0; return At(actor,0,table)&&At(table,slot,fn)&&fn==expected; }
            template<std::size_t N> bool Matches(std::uint32_t address,const unsigned char(&bytes)[N]) const noexcept
            { std::array<unsigned char,N> actual{}; return At(address,0,actual)&&std::memcmp(actual.data(),bytes,N)==0; }
        };
        bool Signatures(const Reads& r) noexcept
        {
            constexpr unsigned char setter[]={0x55,0x8B,0xEC,0x8B,0x89,0xE0,0,0,0,0x5D,0xE9,0x51,0x87,0x05,0};
            constexpr unsigned char controller[]={0x55,0x8B,0xEC,0x8B,0x01,0x85,0xC0,0xC6,0x81,0x8C,0,0,0,0x01,0x74,0x16};
            constexpr unsigned char getter[]={0x55,0x8B,0xEC,0x8B,0x89,0xE0,0,0,0,0x5D,0xE9,0xD1,0x85,0x05,0};
            constexpr unsigned char plain[]={0x55,0x8B,0xEC,0xD9,0x45,0x0C,0x8B,0x45,0x08,0xD9,0x5C,0x81,0x04};
            constexpr unsigned char secure[]={0x55,0x8B,0xEC,0x53,0x56,0x8B,0x75,0x08,0x8B,0x54,0xF1,0x69,0x57,0x8B,0x3D,0x58};
            return r.Matches(0x4715F0,setter)&&r.Matches(0x4C9D50,controller)&&r.Matches(0x471610,getter)
                &&r.Matches(0x4736B0,plain)&&r.Matches(0x4CAC90,secure);
        }
        bool Position(const Reads& r,std::uint32_t actor,int& x,int& y) noexcept
        {
            std::uint32_t controller=0,provider=0,vector=0,table=0,get=0,set=0; unsigned char active=0;
            if (!r.Method(actor,0x74,0x4715F0)||!r.Method(actor,0x80,0x471610)
                ||!r.At(actor,0xE0,controller)||!r.At(controller,0x78,provider)||!r.At(controller,0x7C,active)
                ||active>1||(provider&&active)||!r.At(controller,0,vector)) return false;
            if (!vector) { if(controller>UINT32_MAX-8) return false; vector=controller+8; }
            if (!r.At(vector,0,table)||!r.At(table,0,get)||!r.At(table,4,set)
                ||!((get==0x4736A0&&set==0x4736B0)||(get==0x4CAC70&&set==0x4CAC90))) return false;
            float values[3]{};
            for(std::uint32_t i=0;i<3;++i)
            {
                if(get==0x4CAC70)
                {
                    std::uint32_t bits=0,mirror=0;
                    if(!r.Secure(vector,0x35+i*8,bits)||!r.Secure(vector,0x65+i*8,mirror)) return false;
                    std::memcpy(&values[i],&bits,4);
                }
                else if(!r.At(vector,4+i*4,values[i])) return false;
                if(!std::isfinite(values[i])||std::fabs(values[i])>=1000000) return false;
            }
            x=static_cast<int>(std::round(values[0])); y=static_cast<int>(std::round(values[1])); return true;
        }
        bool Same(const Request& a,const Request& b) noexcept
        { return a.Game==b.Game&&a.Stage==b.Stage&&a.Map==b.Map&&a.Actor==b.Actor&&a.User==b.User&&a.Uid==b.Uid; }
        struct Ground { std::uint32_t Actor=0,Id=0,Descriptor=0; unsigned char Active=0,Pending=0; };
        bool ReadGround(const Reads& r,std::uint32_t actor,std::uint32_t map,Ground& ground) noexcept
        {
            std::uint32_t table=0,kind=0,owner=0;
            if(!r.At(actor,0,table)||table!=0x999574||!r.At(actor,0xD0,kind)||kind!=0x121
                ||!r.At(actor,0xE4,owner)||owner!=map||!r.Secure(actor,0xD4,ground.Id)||ground.Id>65535
                ||!r.At(actor,0x7A0,ground.Descriptor)||!r.At(actor,0x128,ground.Active)||ground.Active>1
                ||!r.At(actor,0x7A5,ground.Pending)||ground.Pending>1) return false;
            ground.Actor=actor; return true;
        }
    }
    bool IsMarker(unsigned int type,const char* text,std::size_t bytes) noexcept
    {
        constexpr char Marker[]="@DFLegacy:loot-magnet:v1@";
        return type==16&&text&&bytes==sizeof(Marker)-1&&std::memcmp(text,Marker,sizeof(Marker)-1)==0;
    }
    bool Capture(const Runtime& runtime,unsigned int uid,Request& result) noexcept
    {
        result={}; if(!uid||uid>65535) return false;
        Reads r{runtime}; Request q{}; q.Uid=uid;
        std::uint32_t scene=0,ui=0,kind=0,actorMap=0,id=0,dead=1,party=0;
        if(!r.Init()||!r.At(0xB5D6F0,0,q.Game)||!r.Secure(q.Game,0x10,scene)||scene!=3
            ||!r.At(0xB5DCE0,0,ui)||!r.At(ui,0x200010,q.Stage)||!r.At(q.Stage,0xB0,q.Map)||!q.Map
            ||!r.At(0xB75A14,0,q.Actor)||!r.At(q.Actor,0xD0,kind)||kind!=0x111
            ||!r.At(q.Actor,0xE4,actorMap)||actorMap!=q.Map||!r.Secure(q.Actor,0xD4,id)||id!=uid
            ||!r.Method(q.Actor,0x318,0x652A00)||!r.Secure(q.Actor,0x142C,dead)||dead
            ||!r.At(0xB5DCC8,0,party)) return false;
        for(std::uint32_t slot=0;slot<4;++slot)
        {
            std::uint32_t user=0,actor=0; std::uint16_t userId=0;
            if(!r.At(party,0x10+slot*4,user)) return false;
            if(!user) continue;
            if(q.User||!r.At(user,0x110,actor)||actor!=q.Actor||!r.At(user,4,userId)||userId!=uid) return false;
            q.User=user;
        }
        if(!q.User) return false;
        std::uint32_t final=0;
        if(!r.At(0xCFD458,0,final)||final!=r.Manager||!r.At(0xCFD45C,0,final)||final!=r.Key
            ||!r.At(0xB5D6F0,0,final)||final!=q.Game||!r.Secure(q.Game,0x10,final)||final!=3
            ||!r.At(0xB5DCE0,0,final)||final!=ui||!r.At(ui,0x200010,final)||final!=q.Stage
            ||!r.At(q.Stage,0xB0,final)||final!=q.Map||!r.At(0xB75A14,0,final)||final!=q.Actor) return false;
        result=q; return true;
    }
    const char* Gather(const Runtime& runtime,const Request& request,std::size_t& moved) noexcept
    {
        moved=0; Request now{}; Reads r{runtime};
        if(!runtime.SetPosition||!Capture(runtime,request.Uid,now)||!Same(now,request)||!r.Init()) return "context-changed";
        if(!Signatures(r)) return "signature-mismatch";
        int x=0,y=0; if(!Position(r,request.Actor,x,y)) return "player-position-unavailable";
        std::uint32_t begin=0,end=0;
        if(!r.At(request.Map,0x10,begin)||!r.At(request.Map,0x14,end)||end<begin||(end-begin)%4
            ||(end-begin)/4>MaximumActors) return "actor-list-invalid";
        std::array<std::uint32_t,MaximumActors> actors{};
        std::array<Ground,MaximumActors> ground{}; std::size_t count=0; bool ownMember=false;
        const auto actorCount=(end-begin)/4;
        for(std::uint32_t i=0;i<actorCount;++i)
        {
            auto& actor=actors[i]; std::uint32_t kind=0;
            if(!r.At(begin,i*4,actor)) return "actor-list-unreadable";
            if(!actor) continue;
            if(!r.At(actor,0xD0,kind)) return "actor-unreadable";
            if(actor==request.Actor) ownMember=true;
            if(kind==0x111&&actor!=request.Actor) return "other-player-present";
            if(kind!=0x121) continue;
            Ground item{}; if(!ReadGround(r,actor,request.Map,item)) return "ground-layout-unsupported";
            if(!item.Active||item.Pending) continue;
            int ignoredX=0,ignoredY=0;
            if(!Position(r,actor,ignoredX,ignoredY)) return "ground-position-unsupported";
            for(std::size_t j=0;j<count;++j) if(ground[j].Actor==actor||ground[j].Id==item.Id) return "duplicate-ground-identity";
            ground[count++]=item;
        }
        if(!ownMember) return "player-not-in-map";
        for(std::size_t i=0;i<count;++i)
        {
            if(!Capture(runtime,request.Uid,now)||!Same(now,request)) return "context-changed";
            std::uint32_t current=0;
            if(!r.At(request.Map,0x10,current)||current!=begin||!r.At(request.Map,0x14,current)||current!=end) return "actor-list-changed";
            // No actor pointer crosses a frame. Recheck actual membership and identity
            // after each setter, which may invoke native controller code.
            for(std::uint32_t j=0;j<actorCount;++j)
                if(!r.At(begin,j*4,current)||current!=actors[j]) return "actor-list-changed";
            Ground item{}; const auto& before=ground[i]; int ignoredX=0,ignoredY=0;
            if(!ReadGround(r,before.Actor,request.Map,item)||item.Id!=before.Id||item.Descriptor!=before.Descriptor
                ||!item.Active||item.Pending||!Position(r,item.Actor,ignoredX,ignoredY)) return "ground-identity-changed";
            if(!runtime.SetPosition(runtime.State,item.Actor,x,y)) return "native-position-failed";
            ++moved;
        }
        return nullptr;
    }
}
