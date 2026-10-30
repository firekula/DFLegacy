#pragma once
#include <array>
#include <cstdint>
#include <algorithm>
#include "client_state.h"

namespace dfl::frame_profile
{
    constexpr std::size_t Parts=5,Windows=8,Events=32;
    struct Sample
    {
        std::uint64_t beginUs=0,endUs=0,tickMs=0;
        std::uint32_t scene=0;
        bool foregroundKnown=false,foregroundBegin=false,foregroundEnd=false;
        std::array<std::uint64_t,Parts> parts{};
    };
    struct Statistics
    {
        std::uint64_t frames=0,foregroundFrames=0,foregroundIntervals=0,clockErrors=0;
        std::uint64_t unknownForegroundFrames=0,backgroundFrames=0,foregroundTransitionFrames=0;
        std::uint64_t maxHookUs=0,maxForegroundHookUs=0,maxForegroundGapUs=0,over15ms=0,over50ms=0,gapsOver50ms=0;
        std::array<std::uint64_t,Parts> maximumParts{};
    };
    struct Window {std::uint64_t startTickMs=0,lastTickMs=0;Statistics stats{};};
    struct Event {Sample sample{};std::uint64_t serial=0,gapUs=0;bool intervalKnown=false;};
    struct Snapshot
    {
        std::uint64_t serial=0,lastTickMs=0,missingBuckets=0,unattributedClockErrors=0;
        Statistics total{};Window current{};
        std::array<Window,Windows> windows{};std::size_t windowCount=0;
        std::array<Event,Events> events{};std::size_t eventCount=0;
    };
    class Recorder
    {
    public:
        Snapshot value{};
        bool Add(const Sample& s) noexcept
        {
            ++value.serial;
            if(!s.tickMs||s.tickMs<value.lastTickMs)
            {++value.total.clockErrors;++value.unattributedClockErrors;previousValid=false;return false;}
            const auto bucket=s.tickMs/1000*1000;
            const bool haveWindow=value.current.stats.frames||value.current.stats.clockErrors;
            if(haveWindow&&bucket>value.current.startTickMs)
            {
                value.missingBuckets+=(bucket-value.current.startTickMs)/1000-1;
                if(value.windowCount==Windows)
                    std::move(value.windows.begin()+1,value.windows.end(),value.windows.begin());
                else ++value.windowCount;
                value.windows[value.windowCount-1]=value.current;value.current={};
            }
            if(!value.current.stats.frames&&!value.current.stats.clockErrors)value.current.startTickMs=bucket;
            value.lastTickMs=value.current.lastTickMs=s.tickMs;
            std::uint64_t sum=0;
            bool valid=s.beginUs&&s.endUs>=s.beginUs
                &&(!previousValid||s.beginUs>=previous.endUs);
            if(valid)for(auto part:s.parts)
            {if(part>s.endUs-s.beginUs-sum){valid=false;break;}sum+=part;}
            if(!valid)
            {++value.total.clockErrors;++value.current.stats.clockErrors;previousValid=false;return false;}
            const bool foreground=s.foregroundKnown&&s.foregroundBegin&&s.foregroundEnd;
            const bool interval=foreground&&previousValid&&previous.foregroundKnown
                &&previous.foregroundBegin&&previous.foregroundEnd&&previous.scene==s.scene;
            const auto gap=interval?s.beginUs-previous.beginUs:0,hook=s.endUs-s.beginUs;
            for(auto* stats:{&value.total,&value.current.stats})
            {
                ++stats->frames;stats->maxHookUs=(std::max)(stats->maxHookUs,hook);
                if(!s.foregroundKnown)++stats->unknownForegroundFrames;
                else if(!s.foregroundBegin&&!s.foregroundEnd)++stats->backgroundFrames;
                else if(s.foregroundBegin!=s.foregroundEnd)++stats->foregroundTransitionFrames;
                for(std::size_t i=0;i<Parts;++i)stats->maximumParts[i]=(std::max)(stats->maximumParts[i],s.parts[i]);
                if(foreground)
                {
                    ++stats->foregroundFrames;stats->maxForegroundHookUs=(std::max)(stats->maxForegroundHookUs,hook);
                    stats->over15ms+=hook>15000;stats->over50ms+=hook>50000;
                }
                if(interval)
                {++stats->foregroundIntervals;stats->maxForegroundGapUs=(std::max)(stats->maxForegroundGapUs,gap);stats->gapsOver50ms+=gap>50000;}
            }
            if((foreground&&hook>15000)||(interval&&gap>50000))
            {
                if(value.eventCount==Events)std::move(value.events.begin()+1,value.events.end(),value.events.begin());
                else ++value.eventCount;
                value.events[value.eventCount-1]={s,value.serial,gap,interval};
            }
            previous=s;previousValid=true;return true;
        }
    private:
        Sample previous{};bool previousValid=false;
    };
    void Initialize(const wchar_t*,const wchar_t*,void*,client_state::Reader) noexcept;
    void Export() noexcept;
    void Shutdown() noexcept;
    bool PipelineEnabled() noexcept;
    void RecordPipeline(const Sample&) noexcept;
    class Frame
    {
    public:
        explicit Frame(std::uint32_t scene) noexcept;
        void Mark() noexcept;
        ~Frame() noexcept;
    private:
        bool active=false,clockValid=true;Sample sample{};
        std::uint64_t last=0;std::size_t next=0;
    };
}
