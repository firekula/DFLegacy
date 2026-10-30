#pragma once
#include "client_state.h"
#include <array>
#include <limits>

// Read-only replicas of ACT1 4A4B10/4D5C80 and 45D9A0/4A3CF0.
namespace dfl::client_skill_resources
{
    using client_state::Value;
    struct CounterRead
    {
        std::array<std::uint32_t,10+64*4> trace{};
        std::int32_t value=0;
    };
    template<class R> bool CounterOnce(R& r,std::uint32_t actor,std::uint32_t id,CounterRead& out)
    {
        auto& t=out.trace;
        constexpr std::array<std::uint32_t,6> offsets={0x1E54,0x1E58,0x1E60,0x1E64,0x1E6C,0x1E70};
        for(std::size_t i=0;i<offsets.size();++i)if(!r.ReadAt(actor,offsets[i],t[i]))return false;
        const auto head=t[0],count=t[1],begin=t[2],end=t[3],mask=t[4],buckets=t[5];
        if(!head||count>64||!r.Read(head,t[6])||!r.ReadAt(head,4,t[7]))return false;
        if(!count)return t[6]==head&&t[7]==head;
        if(end<begin||(end-begin)%4||!buckets)return false;
        auto index=(id^0xDEADBEEFU)&mask;if(index>=buckets)index=index-1-(mask>>1);
        if(index>=buckets||static_cast<std::uint64_t>(index)+1>=(end-begin)/4)return false;
        if(!r.ReadAt(begin,index*4,t[8])||!r.ReadAt(begin,index*4+4,t[9])||!t[8]||!t[9])return false;
        // Validate bucket boundaries against the complete bounded native list.
        // Equal endpoints alone do not prove an empty bucket is well-formed.
        std::uint32_t node=t[6],previous=head;
        for(std::uint32_t i=0;i<count;++i)
        {
            if(!node||node==head)return false;
            for(std::uint32_t n=0;n<i;++n)if(t[10+n*4]==node)return false;
            auto* row=t.data()+10+i*4;row[0]=node;
            if(!r.Read(node,row[1])||!r.ReadAt(node,4,row[2])||!r.ReadAt(node,8,row[3])||row[3]>INT32_MAX
                ||row[2]!=previous||row[1]==node)return false;
            previous=node;node=row[1];
        }
        if(node!=head||previous!=t[7])return false;
        auto member=[&](std::uint32_t value) noexcept
        {
            if(value==head)return true;
            for(std::uint32_t i=0;i<count;++i)if(t[10+i*4]==value)return true;
            return false;
        };
        if(!member(t[8])||!member(t[9]))return false;
        node=t[8];
        for(std::uint32_t i=0;node!=t[9]&&i<count;++i)
        {
            const std::uint32_t* row=nullptr;
            for(std::uint32_t n=0;n<count;++n)if(t[10+n*4]==node){row=t.data()+10+n*4;break;}
            if(!row)return false;
            if(row[3]==id)return r.SignedAt(node,0xC,out.value)&&out.value>=0;
            if(row[3]>id)return true;
            node=row[1];
        }
        return node==t[9];
    }
    template<class R> Value<std::int32_t> Counter(R& r,std::uint32_t actor,std::uint32_t id)
    {
        CounterRead first{},last{};
        if(!CounterOnce(r,actor,id,first)||!CounterOnce(r,actor,id,last)
            ||first.trace!=last.trace||first.value!=last.value)return {};
        return {true,last.value};
    }
    struct Item
    {
        std::uint32_t token=0,id=0,quantity=0;bool quantityKnown=false;
        bool operator==(const Item& b) const noexcept
        {return token==b.token&&id==b.id&&quantity==b.quantity&&quantityKnown==b.quantityKnown;}
    };
    struct Inventory
    {
        std::uint32_t root=0,slots=0,count=0;
        std::array<Item,512> items{};
        bool operator==(const Inventory& b) const noexcept
        {return root==b.root&&slots==b.slots&&count==b.count&&items==b.items;}
    };
    template<class R> bool InventoryOnce(R& r,std::uint32_t actor,Inventory& out)
    {
        std::uint32_t binding=0;
        if(!r.Read(0xB5DCEC,out.root)||!out.root||!r.ReadAt(actor,0x242C,binding)||binding!=out.root
            ||!r.SecureAt(out.root,0x10,out.count)||out.count>out.items.size()
            ||!r.ReadAt(out.root,0x18,out.slots)||(!out.slots&&out.count))return false;
        for(std::uint32_t i=0;i<out.count;++i)
        {
            auto& item=out.items[i];
            if(!r.ReadAt(out.slots,i*4,item.token))return false;
            if(!item.token)continue;
            if(!r.Getter(item.token,0x18,0x4619A0)||!r.ReadAt(item.token,0xC,item.id))return false;
            item.quantityKnown=r.Getter(item.token,0x34,0x461AB0)&&r.SecureAt(item.token,0xA0,item.quantity);
        }
        return true;
    }
    struct InventoryCache
    {
        bool attempted=false,valid=false;
        Inventory snapshot{};
        template<class R> bool Ensure(R& r,std::uint32_t actor)
        {
            if(attempted)return valid;
            attempted=true;Inventory again{};
            valid=InventoryOnce(r,actor,snapshot)&&InventoryOnce(r,actor,again)&&snapshot==again;
            return valid;
        }
        Value<bool> Has(std::uint32_t id,std::uint32_t required) const noexcept
        {
            if(!valid)return {};
            for(std::uint32_t i=0;i<snapshot.count;++i)
            {
                const auto& item=snapshot.items[i];
                if(item.token&&item.id==id)return item.quantityKnown?Value<bool>{true,item.quantity>=required}:Value<bool>{};
            }
            return {true,false};
        }
    };
    struct ItemRead
    {
        std::uint32_t begin=0,end=0;std::int32_t maximumLevel=0;
        std::array<std::int32_t,32*3> rows{};
        Value<bool> sufficient{true,true};
    };
    template<class R> bool ItemsOnce(R& r,InventoryCache& cache,std::uint32_t actor,std::uint32_t skill,
                                      std::int32_t level,ItemRead& out)
    {
        if(!r.ReadAt(skill,0x1BC,out.begin)||!r.ReadAt(skill,0x1C0,out.end)
            ||out.end<out.begin||(out.end-out.begin)%0x18||(out.end-out.begin)/0x18>32)return false;
        if(out.begin==out.end)return true;
        if(!out.begin||!r.SignedAt(skill,0x20,out.maximumLevel)||out.maximumLevel<=1
            ||level<1||level>out.maximumLevel)return false;
        bool unknown=false,shortage=false;
        for(std::uint32_t i=0;i<(out.end-out.begin)/0x18;++i)
        {
            auto* row=out.rows.data()+i*3;
            if(!r.SignedAt(out.begin,i*0x18,row[0])||row[0]<=0
                ||!r.SignedAt(out.begin,i*0x18+8,row[1])||row[1]<0
                ||!r.SignedAt(out.begin,i*0x18+0x10,row[2])||row[2]<0)return false;
            const auto delta=static_cast<std::int64_t>(row[2])-row[1];
            const auto numerator=delta*(static_cast<std::int64_t>(level)-1);
            if(delta<INT32_MIN||delta>INT32_MAX||numerator<INT32_MIN||numerator>INT32_MAX)return false;
            const auto required=static_cast<std::int64_t>(row[1])+numerator/(out.maximumLevel-1);
            if(required<0||required>INT32_MAX)return false;
            if(!required)continue;
            Value<bool> has{};
            if(cache.Ensure(r,actor))has=cache.Has(static_cast<std::uint32_t>(row[0]),static_cast<std::uint32_t>(required));
            if(!has.known)unknown=true;else if(!has.value)shortage=true;
        }
        out.sufficient=shortage?Value<bool>{true,false}:unknown?Value<bool>{}:Value<bool>{true,true};
        return true;
    }
    template<class R> Value<bool> Items(R& r,InventoryCache& cache,std::uint32_t actor,std::uint32_t skill,std::int32_t level)
    {
        ItemRead first{},last{};
        if(!ItemsOnce(r,cache,actor,skill,level,first)||!ItemsOnce(r,cache,actor,skill,level,last)
            ||first.begin!=last.begin||first.end!=last.end||first.maximumLevel!=last.maximumLevel
            ||first.rows!=last.rows||first.sufficient.known!=last.sufficient.known
            ||first.sufficient.value!=last.sufficient.value)return {};
        return last.sufficient;
    }
}
