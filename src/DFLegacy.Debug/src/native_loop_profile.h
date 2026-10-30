#pragma once
#include <array>
#include <algorithm>
#include "../include/debug_api.h"
#include "client_state.h"

namespace dfl::native_loop_profile
{
    constexpr std::size_t Parts=7,Windows=8,Events=32;
    struct Statistics
    {
        std::uint64_t frames=0,intervals=0,cpuFrames=0,cpuIntervals=0,foregroundFrames=0,backgroundFrames=0,unknownForegroundFrames=0,transitionFrames=0;
        std::uint64_t maxLoop=0,maxInterval=0,maxBetween=0,maxCpuLoop=0,maxCpuBetween=0,over15ms=0,betweenOver50ms=0;
        std::array<std::uint64_t,Parts> parts{},cpuParts{};
    };
    struct Window {std::uint64_t start=0,last=0;Statistics stats{};};
    struct Event
    {
        debug_api::NativeLoopSample sample{},previous{};
        std::uint64_t id=0;bool interval=false,cpu=false,cpuInterval=false;
    };
    struct Snapshot
    {
        std::uint64_t received=0,invalid=0,nested=0,discontinuities=0,slowTotal=0,lastTick=0;
        Statistics total{};Window current{};std::array<Window,Windows> windows{};std::size_t windowCount=0;
        Event last{};bool have=false;std::array<Event,Events> events{};std::size_t eventCount=0;
    };
    inline bool CpuKnown(const debug_api::NativeLoopSample& s) noexcept
    {
        if(s.cpuMask!=255)return false;
        for(unsigned i=1;i<8;++i)if(s.cpuUs[i]<s.cpuUs[i-1])return false;
        return true;
    }
    class Model
    {
    public:
        Snapshot value{};
        bool Add(const debug_api::NativeLoopSample& s) noexcept
        {
            ++value.received;
            bool valid=s.size==sizeof(s)&&s.version==1&&s.thread&&s.sequence&&s.tickMs&&s.endTickMs>=s.tickMs
                &&s.endTickMs>=value.lastTick&&s.pointMask==255&&s.cpuMask<=255&&!s.reserved&&!s.flags
                &&s.foregroundKnown<=1&&s.foregroundBegin<=1&&s.foregroundEnd<=1&&s.wallUs[0];
            for(unsigned i=1;i<8&&valid;++i)valid=s.wallUs[i]>=s.wallUs[i-1];
            if(thread&&s.thread!=thread)valid=false;
            if(previousValid&&(s.sequence<=previous.sequence||s.wallUs[0]<previous.wallUs[7]))valid=false;
            if(!valid){++value.invalid;if(s.flags&1)++value.nested;previousValid=false;return false;}
            thread=s.thread;
            const bool interval=previousValid&&s.sequence==previous.sequence+1;
            if(previousValid&&!interval)++value.discontinuities;
            const bool cpu=CpuKnown(s),cpuInterval=interval&&cpu&&CpuKnown(previous)&&s.cpuUs[0]>=previous.cpuUs[7];
            const auto loop=s.wallUs[7]-s.wallUs[0],between=interval?s.wallUs[0]-previous.wallUs[7]:0;
            const auto bucket=s.endTickMs/1000*1000;
            if(value.current.stats.frames&&bucket>value.current.start)
            {
                if(value.windowCount==Windows)std::move(value.windows.begin()+1,value.windows.end(),value.windows.begin());
                else ++value.windowCount;
                value.windows[value.windowCount-1]=value.current;value.current={};
            }
            if(!value.current.stats.frames)value.current.start=bucket;
            value.current.last=value.lastTick=s.endTickMs;
            for(auto* st:{&value.total,&value.current.stats})
            {
                ++st->frames;st->maxLoop=(std::max)(st->maxLoop,loop);st->over15ms+=loop>15000;
                if(!s.foregroundKnown)++st->unknownForegroundFrames;
                else if(s.foregroundBegin&&s.foregroundEnd)++st->foregroundFrames;
                else if(!s.foregroundBegin&&!s.foregroundEnd)++st->backgroundFrames;
                else ++st->transitionFrames;
                for(unsigned i=0;i<Parts;++i)st->parts[i]=(std::max)(st->parts[i],s.wallUs[i+1]-s.wallUs[i]);
                if(cpu)
                {
                    ++st->cpuFrames;st->maxCpuLoop=(std::max)(st->maxCpuLoop,s.cpuUs[7]-s.cpuUs[0]);
                    for(unsigned i=0;i<Parts;++i)st->cpuParts[i]=(std::max)(st->cpuParts[i],s.cpuUs[i+1]-s.cpuUs[i]);
                }
                if(interval)
                {
                    ++st->intervals;st->maxBetween=(std::max)(st->maxBetween,between);
                    st->maxInterval=(std::max)(st->maxInterval,s.wallUs[0]-previous.wallUs[0]);st->betweenOver50ms+=between>50000;
                }
                if(cpuInterval){++st->cpuIntervals;st->maxCpuBetween=(std::max)(st->maxCpuBetween,s.cpuUs[0]-previous.cpuUs[7]);}
            }
            value.last={s,interval?previous:debug_api::NativeLoopSample{},0,interval,cpu,cpuInterval};value.have=true;
            if(loop>15000||(interval&&s.wallUs[0]-previous.wallUs[0]>50000))
            {
                if(value.eventCount==Events)std::move(value.events.begin()+1,value.events.end(),value.events.begin());
                else ++value.eventCount;
                auto event=value.last;event.id=++value.slowTotal;value.events[value.eventCount-1]=event;
            }
            previous=s;previousValid=true;return true;
        }
    private:
        debug_api::NativeLoopSample previous{};bool previousValid=false;
        std::uint32_t thread=0;
    };
    void Initialize(const wchar_t*,const wchar_t*,client_state::Reader) noexcept;
    bool Enabled() noexcept;
    void Record(const debug_api::NativeLoopSample&) noexcept;
    void Export() noexcept;
    void Shutdown() noexcept;
}
