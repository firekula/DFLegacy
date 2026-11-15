#pragma once
#include <cstddef>
#include <cstdint>
namespace dfl::chapter_two {
    bool InstallMallCreature(unsigned char* image,std::size_t bytes) noexcept;
    namespace mall_creature_detail {
        using PatchWriter=bool(*)(std::uintptr_t,const void*,std::size_t);
        bool InstallImage(unsigned char* image,std::size_t bytes,PatchWriter write) noexcept;
    }
}
