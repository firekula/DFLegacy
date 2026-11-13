#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    // Startup-only C1 parity: the NOTI1 login convergence selects native mode1.
    // Original channel/debug setters, particle resources and fragment logic stay native.
    bool InstallAdultBlood(unsigned char* image,std::size_t bytes) noexcept;

    namespace adult_blood_detail
    {
        using PatchWriter=bool(*)(std::uintptr_t,const void*,std::size_t);
        bool InstallImage(unsigned char* image,std::size_t bytes,PatchWriter write) noexcept;
    }
}
