#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdarg>
#include <cstdio>

namespace dfl::chapter_two::debug_trace
{
    // Debug-client-only trace. The launcher passes a per-attempt session
    // directory; the host and its native hooks append to one plain file there
    // because the broker does not forward native stderr.
    inline void Write(const char* format, ...) noexcept
    {
        static FILE* file = nullptr;
        static bool tried = false;
        if (!tried)
        {
            tried = true;
            char directory[512]{};
            const auto length = GetEnvironmentVariableA(
                "DFL_S1C2_SESSION_DATA", directory, static_cast<DWORD>(sizeof(directory)));
            if (length && length < sizeof(directory))
            {
                char path[600]{};
                std::snprintf(path, sizeof(path), "%s\\debug-trace.log", directory);
                fopen_s(&file, path, "a");
            }
        }
        if (!file) return;
        va_list arguments;
        va_start(arguments, format);
        std::vfprintf(file, format, arguments);
        va_end(arguments);
        std::fflush(file);
    }
}
