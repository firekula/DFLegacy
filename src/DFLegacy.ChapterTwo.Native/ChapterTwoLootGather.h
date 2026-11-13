#pragma once
#include <cstddef>
#include <cstdint>
namespace dfl::chapter_two {
    bool InstallLootGather(unsigned char* image, std::size_t bytes) noexcept;
    void OnLootGatherFrame() noexcept;
}
namespace dfl::chapter_two::loot_gather_detail {
    struct Runtime {
        void* State;
        bool (*Read)(void*, std::uint32_t, void*, std::size_t);
        bool (*SetPosition)(void*, std::uint32_t, int, int);
    };
    struct Request { std::uint32_t Game=0, Stage=0, Map=0, Actor=0, User=0, Uid=0; };
    bool IsMarker(unsigned int, const char*, std::size_t) noexcept;
    bool Capture(const Runtime&, unsigned int, Request&) noexcept;
    const char* Gather(const Runtime&, const Request&, std::size_t&) noexcept;
    using PatchWriter = bool(*)(std::uintptr_t, const void*, std::size_t) noexcept;
    bool InstallImage(unsigned char*,std::size_t,PatchWriter) noexcept;
}
