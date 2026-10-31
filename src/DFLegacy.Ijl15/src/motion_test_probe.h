#pragma once
#include <cstdint>
#include <cstddef>

namespace dfl::motion_test_probe
{
    // Read-only exported diagnostic memory. A reader must compare the even
    // revision before/after its copy, then validate process identity and serials.
    struct Event
    {
        std::uint64_t serial=0,tick=0;
        std::uint32_t thread=0,actor=0,controller=0,method=0;
        std::int32_t x=0,y=0;
        std::uint32_t result=0,kind=0,map=0,nativePath=0,nativeDetail=0,axis=0;
    };
    struct Block
    {
        std::uint32_t magic=0x4D543031,version=2,headerSize=64,eventSize=64,capacity=256,pid=0;
        std::uint64_t processStarted=0;
        volatile long revision=0;std::uint32_t enabled=0;
        std::uint64_t serial=0,lockDrops=0,overflows=0;
        Event events[256]{};
    };
    static_assert(sizeof(Event)==64&&offsetof(Block,events)==64);
    struct Site
    {
        std::uint32_t at;void(*target)();std::uint32_t length,call;
        unsigned char expected[8];std::uint32_t reserved[2]{};
    };
    struct SiteTable
    {
        std::uint32_t magic=0x4D505331,version=1,count=5,entrySize=32;
        Site sites[5];
    };
    static_assert(sizeof(Site)==32&&sizeof(SiteTable)==176);
    bool Install() noexcept;
}
