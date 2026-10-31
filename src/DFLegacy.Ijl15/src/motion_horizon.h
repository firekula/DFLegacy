#pragma once
#include <cstdint>

namespace dfl::motion_horizon
{
    bool Install() noexcept;
    namespace detail { bool ApplyAt(std::uintptr_t address) noexcept; }
}
