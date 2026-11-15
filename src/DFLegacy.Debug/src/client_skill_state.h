#pragma once
#include "client_state.h"
#include "client_skill_resources.h"
#include <array>
#include <cmath>
#include <cstring>
#include <limits>

// Original ACT1 read-only replicas. Never invoke secure getters or use/allocate skills.
namespace dfl::client_skill_state
{
    using client_state::Value;
    struct ClockRead { std::array<std::uint32_t,10> binding{}; std::uint32_t value=0; };
    template<class R> bool ClockOnce(R& r,ClockRead& out)
    {
        constexpr std::uint32_t timer=0xB75978;auto& b=out.binding;
        if(!r.Read(timer,b[0])||b[0]!=0x99875C||!r.SecureAt(timer,0x10,b[1]))return false;
        if(!b[1])return true;
        if(!r.SecureAt(timer,0x18,b[2]))return false;if(!b[2])return true;
        if(!r.SecureAt(timer,0x20,b[3]))return false;
        if(b[3]){if(!r.SecureAt(timer,0x30,b[9]))return false;out.value=b[9];return true;}
        unsigned char mode=0;
        if(!r.SecureAt(timer,8,b[4])||b[4]>=16||!r.Read(0xCFD44C,mode)
            ||!r.Read(0xCFD278+b[4]*4,b[6])||!r.SecureAt(timer,0x28,b[8]))return false;
        b[5]=mode;
        if(mode){if(!r.Secure(b[6],b[7]))return false;}else b[7]=b[6];
        out.value=b[7]+b[8];return true;
    }
    template<class R> Value<std::uint32_t> Clock(R& r)
    {
        ClockRead first{},last{};
        if(!ClockOnce(r,first)||!ClockOnce(r,last)||first.binding!=last.binding||first.value!=last.value)return {};
        return {true,last.value};
    }
    template<class R> void Cooldown(R& r,std::uint32_t skill,const Value<std::uint32_t>& clock,client_state::Skill& out)
    {
        std::int32_t duration=0,check=0;std::uint32_t deadline=0,last=0;
        if(!r.SignedAt(skill,0x34C,duration))return;
        if(duration==-1)
        {if(r.SignedAt(skill,0x34C,check)&&check==duration)out.cooldownBlocked={true,true};return;}
        if(duration < -1)return;
        if(!clock.known||!r.SecureAt(skill,0x354,deadline)||!r.SignedAt(skill,0x34C,check)||check!=duration
            ||!r.SecureAt(skill,0x354,last)||last!=deadline)return;
        const auto bits=deadline-clock.value;std::int32_t remaining=0;std::memcpy(&remaining,&bits,sizeof(remaining));
        out.cooldownMs={true,remaining>0?remaining:0};out.cooldownBlocked={true,remaining>0};
    }
    template<class R> bool LevelInt(R& r,std::uint32_t skill,std::uint32_t offset,std::int32_t level,std::int32_t& out,bool clamp=false)
    {
        std::uint32_t begin=0,end=0,again=0;
        if(!r.ReadAt(skill,offset,begin)||!r.ReadAt(skill,offset+4,end)||end<begin||(end-begin)%8||(end-begin)/8>1024)return false;
        if(begin==end){out=0;return true;}if(level<=0)return false;
        const auto count=(end-begin)/8;if(!clamp&&static_cast<std::uint32_t>(level)>count)return false;
        const auto index=static_cast<std::uint32_t>(level)>count?count-1:static_cast<std::uint32_t>(level)-1;
        return r.SignedAt(begin,index*8,out)&&r.ReadAt(skill,offset,again)&&again==begin&&r.ReadAt(skill,offset+4,again)&&again==end;
    }
    template<class R> bool Factor(R& r,std::uint32_t actor,std::uint32_t id,float& out)
    {
        std::uint32_t head=0,count=0,next=0,prev=0;
        if(!r.ReadAt(actor,0x1E7C,head)||!head||!r.ReadAt(actor,0x1E80,count)||count>64
            ||!r.Read(head,next)||!r.ReadAt(head,4,prev))return false;
        if(!count){if(next!=head||prev!=head)return false;out=1;return true;}
        std::uint32_t begin=0,end=0,mask=0,buckets=0;
        if(!r.ReadAt(actor,0x1E88,begin)||!r.ReadAt(actor,0x1E8C,end)||end<begin||(end-begin)%4
            ||!r.ReadAt(actor,0x1E94,mask)||!r.ReadAt(actor,0x1E98,buckets)||!buckets)return false;
        auto index=(id^0xDEADBEEFU)&mask;if(index>=buckets)index=index-1-(mask>>1);
        if(index>=buckets||static_cast<std::uint64_t>(index)+1>=(end-begin)/4)return false;
        std::uint32_t node=0,last=0;if(!r.ReadAt(begin,index*4,node)||!r.ReadAt(begin,index*4+4,last))return false;
        std::array<std::uint32_t,64> visited{};std::size_t used=0;
        while(node!=last)
        {
            if(node==head)return false;
            if(used>=count)return false;for(std::size_t i=0;i<used;++i)if(visited[i]==node)return false;visited[used++]=node;
            std::uint32_t key=0;if(!r.ReadAt(node,8,key)||key>INT32_MAX)return false;
            if(key==id)return r.FloatAt(node,0xC,out)&&out>=0;
            if(key>id)break;if(!r.Read(node,node))return false;
        }
        out=1;return true;
    }
    template<class R> bool WeaponFactor(R& r,std::uint32_t actor,std::uint32_t index,float& out)
    {
        std::uint32_t weapon=0,begin=0,end=0;std::int32_t type=0;
        if(!r.Getter(actor,0x54C,0x498D90)||!r.ReadAt(actor,0x13E8,weapon))return false;
        if(!weapon){out=1;return true;}
        if(!r.Getter(actor,0x4DC,0x494410)||!r.Getter(weapon,0xDC,0x4A7B50)||!r.SignedAt(weapon,0xD0,type)||type<0
            ||!r.ReadAt(actor,0x2040,begin)||!r.ReadAt(actor,0x2044,end)||end<begin||(end-begin)%0x58||(end-begin)/0x58>256)return false;
        if(static_cast<std::uint32_t>(type)>=(end-begin)/0x58){out=1;return true;}
        return r.FloatAt(begin,static_cast<std::uint32_t>(type)*0x58+0x38+index*8,out)&&out>=0;
    }
    template<class R> bool NoEquipmentModifiers(R& r,std::uint32_t actor)
    {
        if(!r.Getter(actor,0x368,0x492A60))return false;
        for(std::uint32_t i=0;i<23;++i)
        {
            std::uint32_t item=0,shadow=0,begin=0,end=0,attached=0;
            if(!r.ReadAt(actor,0x13C4+i*4,item)||!r.ReadAt(actor,0x1FCC+i*4,shadow))return false;
            if(!item||item==shadow)continue;
            if(!r.Getter(item,0xE0,0x4A7B60)||!r.Getter(item,0x118,0x4A7BE0)||!r.ReadAt(item,0x390,begin)
                ||!r.ReadAt(item,0x394,end)||begin!=end||!r.ReadAt(item,0x6A0,attached)||attached)return false;
        }
        return true;
    }
    template<class R> bool MpCost(R& r,std::uint32_t actor,std::uint32_t skill,std::uint32_t id,std::int32_t level,std::int32_t& out)
    {
        std::int32_t base=0,mode=0;float factor=0,weapon=1;std::uint32_t actualId=0;
        if(!r.SecureAt(skill,8,actualId)||actualId!=id||!LevelInt(r,skill,0x184,level,base,true)||base<0||base>0x1000000
            ||!Factor(r,actor,id,factor)||!r.SignedAt(skill,0x208,mode))return false;
        if((mode==1||mode==2)&&!WeaponFactor(r,actor,mode==1?0:2,weapon))return false;
        const double factorProduct=static_cast<double>(factor)*weapon;
        if(!std::isfinite(factorProduct)||factorProduct<0||factorProduct>std::numeric_limits<float>::max())return false;
        const float rounded=static_cast<float>(factorProduct);
        // Unknown x87 precision/rounding: require the intermediate product to be exactly representable.
        if(static_cast<double>(rounded)!=factorProduct)return false;
        const double cost=static_cast<double>(base)*rounded;
        if(!std::isfinite(cost)||cost<0||cost>0x1000000||!NoEquipmentModifiers(r,actor))return false;
        if(std::floor(cost)!=cost)
        {
            const float single=static_cast<float>(cost);
            const double lower=std::nextafter(single,-std::numeric_limits<float>::infinity());
            const double upper=std::nextafter(single,std::numeric_limits<float>::infinity());
            if(std::floor(lower)!=std::floor(cost)||std::floor(upper)!=std::floor(cost))return false;
        }
        out=static_cast<std::int32_t>(cost);return true;
    }
    template<class R> void Resources(R& r,client_skill_resources::InventoryCache& cache,
                                      const client_state::Player& me,std::uint32_t skill,client_state::Skill& out)
    {
        auto& resource=out.resources;resource.present=true;
        std::int32_t cost=0,limit=0,again=0;
        if(MpCost(r,me.actorToken,skill,out.id,out.level,cost))resource.mpCost={true,cost};
        if(LevelInt(r,skill,0x1CC,out.level,limit)&&limit>=0
            &&LevelInt(r,skill,0x1CC,out.level,again)&&again==limit)
        {
            resource.useLimit={true,limit};
            if(limit)resource.useCount=client_skill_resources::Counter(r,me.actorToken,out.id);
        }
        resource.itemsSufficient=client_skill_resources::Items(r,cache,me.actorToken,skill,out.level);
    }
    template<class R> void Usability(R& r,const client_state::Player& me,std::uint32_t skill,client_state::Skill& out)
    {
        if(me.dead.known&&me.dead.value){out.usable={true,false};return;}
        std::int32_t allowed=0;std::uint32_t mode=0;
        if(r.SignedAt(skill,0x2EC,allowed)&&!allowed){out.usable={true,false};return;}
        if(r.ReadAt(me.actorToken,0x154,mode)&&mode==2){out.usable={true,false};return;}
        if(out.cooldownBlocked.known&&out.cooldownBlocked.value){out.usable={true,false};return;}
        if(!me.dead.known||me.dead.value||!r.SignedAt(skill,0x2EC,allowed)||!allowed
            ||!me.sameMap.known||!me.sameMap.value||!me.detached.known||me.detached.value
            ||!r.ReadAt(me.actorToken,0x154,mode)||mode==2||!r.Getter(me.actorToken,0x138,0x493880))return;
        const auto& resource=out.resources;
        if(resource.useLimit.known&&resource.useLimit.value&&resource.useCount.known
            &&resource.useCount.value>=resource.useLimit.value){out.usable={true,false};return;}
        if(resource.itemsSufficient.known&&!resource.itemsSufficient.value){out.usable={true,false};return;}
        if(!me.mp.known||!resource.mpCost.known)return;
        if(me.mp.value<resource.mpCost.value){out.usable={true,false};return;}
        if(!resource.useLimit.known||(resource.useLimit.value&&!resource.useCount.known)
            ||!resource.itemsSufficient.known)return;
        if(out.cooldownBlocked.known&&!out.cooldownBlocked.value)out.usable={true,true};
    }
}
