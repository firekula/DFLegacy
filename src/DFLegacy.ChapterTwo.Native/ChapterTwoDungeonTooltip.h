#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    // Restore selection(2) -> town(1) tooltip cleanup through the native queue.
    // Call only at the same stopped startup point as the other local patches.
    bool InstallDungeonTooltip(unsigned char* image,std::size_t bytes) noexcept;
    namespace dungeon_tooltip_detail
    {
        using PatchWriter=bool(*)(std::uintptr_t,const void*,std::size_t);
        bool InstallImage(unsigned char*,std::size_t,PatchWriter) noexcept;
        struct Reader
        {
            void* context=nullptr;
            bool (*read)(void*,std::uintptr_t,void*,std::size_t) noexcept=nullptr;
        };
        enum class Result { NotApplicable,Unavailable,NoWindows,AlreadyQueued,CloseRequested,CloseFailed };
        using Close=bool(*)(void*,std::uintptr_t) noexcept;
        Result Cleanup(int oldScene,int newScene,std::uintptr_t manager,const Reader&,void*,Close) noexcept;
    }
}
