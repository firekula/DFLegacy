#pragma once

#include <cstddef>
#include <cstdint>

namespace dfl::warehouse_upgrade_button
{
    bool Install() noexcept;

    namespace detail
    {
        using PatchWriter = bool (*)(std::uintptr_t, const void*, std::size_t);
        bool InstallImage(std::uintptr_t image, PatchWriter write) noexcept;
    }
}
