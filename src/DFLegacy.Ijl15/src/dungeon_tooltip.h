#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::dungeon_tooltip
{
    namespace detail
    {
        struct Reader
        {
            void* context=nullptr;
            bool (*read)(void*,std::uintptr_t,void*,std::size_t) noexcept=nullptr;
        };
        constexpr std::size_t MaximumWindows=1; // Original A7DE28[class3F] limit.
        struct Windows
        {
            std::array<std::uintptr_t,MaximumWindows> objects{};
            std::array<bool,MaximumWindows> alreadyQueued{};
            std::size_t count=0;
        };
        enum class Result { NotApplicable,Unavailable,NoWindows,AlreadyQueued,CloseRequested,CloseFailed };
        bool ReadWindows(const Reader&,std::uintptr_t manager,Windows&) noexcept;
        using Close=bool (*)(void*,std::uintptr_t window) noexcept;
        Result Cleanup(int oldScene,int newScene,std::uintptr_t manager,const Reader&,void* context,Close) noexcept;
    }
    bool Install() noexcept;
}
