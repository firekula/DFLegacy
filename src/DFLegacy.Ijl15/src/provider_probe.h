#pragma once
#include <cstdint>
#include <cstddef>
namespace dfl::provider_probe
{
    struct Event
    {
        std::uint64_t serial=0,tick=0,counterUs=0;
        std::uint32_t thread=0,context=0,frame=0,result=0;
        std::uint32_t source=0,sourceLength=0,sourceCaptured=0,inner=0,innerLength=0,innerCaptured=0,flags=0,reserved=0;
        unsigned char sourceBytes[2048]{},innerBytes[2048]{};
    };
    struct Block
    {
        std::uint32_t magic=0x50543031,version=1,headerSize=64,eventSize=4168,capacity=32,pid=0;
        std::uint64_t processStarted=0;
        volatile long revision=0;std::uint32_t enabled=0;
        std::uint64_t serial=0,lockDrops=0,invalid=0;
        Event events[32]{};
    };
    static_assert(sizeof(Event)==4168&&offsetof(Block,events)==64&&sizeof(Block)==133440);
    bool Install() noexcept;
}
