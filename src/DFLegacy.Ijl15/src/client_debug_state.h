#pragma once
#include <windows.h>
#include <cstdint>

namespace dfl::debug_module::detail
{
    // Read the client's mirrored secure integer without invoking its assertion/
    // repair path. Transitional or invalid wrappers simply have no debug value.
    inline bool ReadProtected(std::uintptr_t address, std::uintptr_t manager,
        std::uint32_t key, std::uint32_t& value) noexcept
    {
        __try
        {
            if (address == 0 || manager == 0) return false;
            const auto encoded = *reinterpret_cast<const std::uint32_t*>(address);
            const auto index = *reinterpret_cast<const std::uint32_t*>(address + 4);
            if (static_cast<std::uint32_t>((encoded ^ key) - address) != index
                || index >= *reinterpret_cast<const std::uint32_t*>(manager + 4)) return false;
            const auto first = *reinterpret_cast<const std::uintptr_t*>(manager + 0x44) + index * 4;
            const auto second = *reinterpret_cast<const std::uintptr_t*>(manager + 0x48) + index * 4;
            const auto a = *reinterpret_cast<const std::uint32_t*>(first) ^ static_cast<std::uint32_t>(first) ^ key;
            const auto b = *reinterpret_cast<const std::uint32_t*>(second) ^ static_cast<std::uint32_t>(second) ^ key;
            if (a != b) return false;
            value = a;
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }
}
