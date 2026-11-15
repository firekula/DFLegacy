#pragma once
#include <cstddef>

namespace dfl::chapter_two
{
    // Install before the mapped Chapter Two image enters its CRT.
    bool InstallTownMapMovement(unsigned char* image, std::size_t bytes) noexcept;
}
