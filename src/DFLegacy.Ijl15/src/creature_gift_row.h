#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::creature_gift_row
{
    bool Install() noexcept;
    namespace detail
    {
        // Returns whether the button write completed, not whether it is enabled.
        bool __cdecl UpdateRow(void* shop, void* commodity, void* button) noexcept;
        using StubCode = std::array<unsigned char,21>;
        StubCode BuildStub(std::uintptr_t address, std::uintptr_t helper, std::uintptr_t resume) noexcept;
        using PatchWriter = bool (*)(std::uintptr_t,const void*,std::size_t);
        bool InstallImage(std::uintptr_t image, std::uintptr_t stub, PatchWriter write) noexcept;
    }
}
