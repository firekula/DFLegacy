#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::loot_gather
{
    bool Install() noexcept;
    void OnFrame() noexcept;
}

namespace dfl::loot_gather::detail
{
    struct Runtime
    {
        void* State;
        bool (*Read)(void*, std::uint32_t, void*, std::size_t);
        bool (*SetPosition)(void*, std::uint32_t, int, int);
    };
    struct Request
    {
        std::uint32_t Game = 0, Stage = 0, Map = 0, Actor = 0, User = 0, Uid = 0;
    };
    bool IsMarker(unsigned int type, const char* text, std::size_t bytes) noexcept;
    bool Capture(const Runtime&, unsigned int uid, Request&) noexcept;
    const char* Gather(const Runtime&, const Request&, std::size_t& moved) noexcept;
}
