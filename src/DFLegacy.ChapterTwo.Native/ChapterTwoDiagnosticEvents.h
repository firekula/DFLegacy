#pragma once
#include <windows.h>
#include <psapi.h>
#include <cstdio>
#include <cwchar>

// Separate from stdout: OutputDebugString can contain arbitrary embedded newlines.
// The non-inherited journal handle is held by the debugger and denies other writers.
class ChapterTwoDiagnosticEvents
{
public:
    ChapterTwoDiagnosticEvents()
    {
        wchar_t path[32768]{};
        const auto length = GetEnvironmentVariableW(L"DFLEGACY_CH2_DIAGNOSTIC_EVENTS", path, _countof(path));
        SetEnvironmentVariableW(L"DFLEGACY_CH2_DIAGNOSTIC_EVENTS", nullptr);
        if (length && length < _countof(path))
            file_ = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW,
                FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH, nullptr);
        if (length && file_ == INVALID_HANDLE_VALUE)
            std::printf("DIAGNOSTIC_EVENTS_OPEN_FAILED error=%lu\n", GetLastError());
    }
    ~ChapterTwoDiagnosticEvents() { if (file_ != INVALID_HANDLE_VALUE) CloseHandle(file_); }
    ChapterTwoDiagnosticEvents(const ChapterTwoDiagnosticEvents&) = delete;
    ChapterTwoDiagnosticEvents& operator=(const ChapterTwoDiagnosticEvents&) = delete;

    void Started(DWORD process, HANDLE processHandle)
    {
        process_ = process;
        processHandle_ = processHandle;
        char line[64]{};
        const auto length = sprintf_s(line, "START %lu\n", process);
        Write(line, length);
    }
    void Observe(const DEBUG_EVENT& event)
    {
        if (!process_ || event.dwProcessId != process_) return;
        char line[256]{};
        int length = 0;
        if (event.dwDebugEventCode == EXCEPTION_DEBUG_EVENT &&
            !event.u.Exception.dwFirstChance && exceptions_++ < 8)
        {
            const auto& fault = event.u.Exception.ExceptionRecord;
            const auto address = reinterpret_cast<ULONG_PTR>(fault.ExceptionAddress);
            char module[129]{};
            unsigned long long offset = 0;
            if (FindModule(fault.ExceptionAddress, module, offset))
                length = sprintf_s(line, "EXCEPTION %lu %lu %08lX %016llX %s %016llX\n", process_, event.dwThreadId,
                    fault.ExceptionCode, static_cast<unsigned long long>(address), module, offset);
            else
                length = sprintf_s(line, "EXCEPTION %lu %lu %08lX %016llX\n", process_, event.dwThreadId,
                    fault.ExceptionCode, static_cast<unsigned long long>(address));
        }
        else if (event.dwDebugEventCode == EXIT_PROCESS_DEBUG_EVENT)
            length = sprintf_s(line, "EXIT %lu %08lX\n", process_, event.u.ExitProcess.dwExitCode);
        Write(line, length);
    }
private:
    bool FindModule(const void* address, char (&module)[129], unsigned long long& offset) const
    {
        MEMORY_BASIC_INFORMATION region{};
        if (!VirtualQueryEx(processHandle_, address, &region, sizeof(region)) || region.Type != MEM_IMAGE)
            return false;
        wchar_t path[1024]{};
        const auto length = K32GetMappedFileNameW(processHandle_, region.AllocationBase, path, _countof(path));
        if (length == 0 || length >= _countof(path)) return false;
        const auto separator = std::wcsrchr(path, L'\\');
        if (!separator) return false;
        const auto leaf = separator + 1;
        const auto size = std::wcslen(leaf);
        if (size == 0 || size >= _countof(module) || std::wcsstr(leaf, L"..")) return false;
        for (size_t i = 0; i < size; ++i)
        {
            const auto c = leaf[i];
            if (!((c >= L'A' && c <= L'Z') || (c >= L'a' && c <= L'z') ||
                (c >= L'0' && c <= L'9') || c == L'.' || c == L'_' || c == L'-')) return false;
            module[i] = static_cast<char>(c);
        }
        offset = reinterpret_cast<ULONG_PTR>(address) - reinterpret_cast<ULONG_PTR>(region.AllocationBase);
        return true;
    }

    void Write(const char* line, int length)
    {
        if (file_ == INVALID_HANDLE_VALUE || length <= 0) return;
        DWORD written = 0;
        if (!WriteFile(file_, line, static_cast<DWORD>(length), &written, nullptr) || written != static_cast<DWORD>(length))
            std::puts("DIAGNOSTIC_EVENTS_WRITE_FAILED");
    }
    HANDLE file_ = INVALID_HANDLE_VALUE;
    HANDLE processHandle_ = nullptr;
    DWORD process_ = 0;
    unsigned exceptions_ = 0;
};
