#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    // Install before SlowFrameTiming, which redirects the original Sleep call.
    // These policies are separate so isolated acceptance can exercise each one.
    bool InstallBackgroundPolicy(unsigned char* image, std::size_t bytes) noexcept;
    bool InstallStartupWindowPolicy(unsigned char* image, std::size_t bytes) noexcept;

    namespace background_policy_detail
    {
        using PatchWriter = bool (*)(std::uintptr_t address, const void* bytes, std::size_t size);
        bool InstallBackground(unsigned char* image, std::size_t bytes, PatchWriter write) noexcept;
        bool InstallStartup(unsigned char* image, std::size_t bytes, PatchWriter write) noexcept;
    }
}
