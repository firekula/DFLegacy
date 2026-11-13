#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    // Install while the client is stopped at startup, like the other local
    // patches. false is an explicit isolated-test baseline: observe, do not mute.
    // Reinstalling in a different mode is rejected. Normal callers use default.
    bool InstallType16Audio(unsigned char* image,std::size_t bytes,bool suppressType16=true) noexcept;

    struct Type16AudioCounts
    {
        std::uint32_t originalCalls=0;
        std::uint32_t skipped=0;
    };
    struct Type16AudioDiagnostics
    {
        // 0..255 are exact message types; 256 aggregates all other signed values.
        // Cumulative modulo-2^32 counters, never reset during an installed run.
        std::array<Type16AudioCounts,257> types{};
    };
    Type16AudioDiagnostics ReadType16AudioDiagnostics() noexcept;
    // Call on ONE existing slow diagnostic path (e.g. once per second).
    // Outputs only changed buckets, at most 16 lines per call, outside audio.
    void LogType16AudioDiagnostics() noexcept;

    namespace type16_audio_detail
    {
        using WriteMemory=bool(*)(std::uintptr_t,const void*,std::size_t);
        bool InstallImage(unsigned char* image,std::size_t bytes,WriteMemory write,bool suppressType16=true) noexcept;
    }
}
