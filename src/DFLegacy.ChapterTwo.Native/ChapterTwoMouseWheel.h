#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    bool InstallMouseWheel(unsigned char* image, std::size_t bytes) noexcept;
    namespace mouse_wheel_detail
    {
        using WriteMemory = bool (*)(std::uintptr_t, const void*, std::size_t);
        bool InstallImage(unsigned char* image, std::size_t bytes, WriteMemory write) noexcept;
    }
}
