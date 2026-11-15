#pragma once
#include <cstddef>
#include <cstdint>

namespace dfl::chapter_two
{
    // Keeps native log files in the launcher's unique ordinary-process session.
    bool InstallNativeLog(unsigned char* image,std::size_t bytes) noexcept;
    namespace native_log_detail
    {
        using PatchWriter=bool(*)(std::uintptr_t,const void*,std::size_t);
        bool InstallImage(unsigned char* image,std::size_t bytes,std::uintptr_t fopenHook,PatchWriter write) noexcept;
        bool ResolveLogPath(const char* source,const char* directory,char* output,std::size_t capacity) noexcept;
    }
}
