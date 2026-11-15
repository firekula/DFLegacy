#pragma once
#include <array>
#include "../../DFLegacy.Debug/include/debug_api.h"

namespace dfl::native_loop_probe
{
    bool Install() noexcept;
    void Configure(debug_api::PipelineEnabled,debug_api::RecordNativeLoop,void*,const std::uint32_t*) noexcept;
    namespace detail
    {
        class ReturnStack
        {
            struct Frame {debug_api::NativeLoopSample sample{};std::uint32_t target=0;unsigned next=1;};
            std::array<Frame,16> frames{};unsigned depth=0;std::uint64_t sequence=0;
        public:
            constexpr ReturnStack() noexcept=default;
            bool Enter(std::uint32_t target,const debug_api::NativeLoopSample& input) noexcept
            {
                if(!target)return false;
                if(depth)for(unsigned i=0;i<depth;++i)frames[i].sample.flags|=1;
                if(depth==frames.size())return false;
                auto s=input;s.sequence=++sequence;if(depth)s.flags|=1;
                frames[depth++]={s,target,1};return true;
            }
            void Mark(unsigned point,std::uint64_t wall,std::uint64_t cpu,bool cpuKnown,std::uint32_t scene,bool fgKnown,bool fg,std::uint64_t tick) noexcept
            {
                if(!depth)return;auto& f=frames[depth-1];if(f.sample.flags)return;
                if(point!=f.next||point>7){f.sample.flags|=2;return;}
                f.sample.wallUs[point]=wall;f.sample.cpuUs[point]=cpu;f.sample.pointMask|=1u<<point;
                if(cpuKnown)f.sample.cpuMask|=1u<<point;
                if(point==7){f.sample.endTickMs=tick;f.sample.sceneEnd=scene;f.sample.foregroundKnown=f.sample.foregroundKnown&&fgKnown;f.sample.foregroundEnd=fg?1:0;}
                ++f.next;
            }
            bool Leave(std::uint32_t& target,debug_api::NativeLoopSample& sample) noexcept
            {
                if(!depth)return false;const auto& f=frames[--depth];target=f.target;sample=f.sample;
                if(f.next!=8)sample.flags|=2;return true;
            }
            unsigned Depth() const noexcept{return depth;}
        };
    }
}
