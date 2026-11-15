#pragma once
#include <windows.h>
#include <cstdio>

// Separate from stdout: OutputDebugString can contain arbitrary embedded newlines.
// The journal handle is never inherited, and the confined game's directory grant
// remains RX. Only the debugger can write these OS-sourced process events.
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

    void Started(DWORD process)
    {
        process_ = process;
        char line[64]{};
        const auto length = sprintf_s(line, "START %lu\n", process);
        Write(line, length);
    }
    void Observe(const DEBUG_EVENT& event)
    {
        if (!process_ || event.dwProcessId != process_) return;
        char line[160]{};
        int length = 0;
        if (event.dwDebugEventCode == EXCEPTION_DEBUG_EVENT &&
            !event.u.Exception.dwFirstChance && exceptions_++ < 8)
            length = sprintf_s(line, "EXCEPTION %lu %lu %08lX %016llX\n", process_, event.dwThreadId,
                event.u.Exception.ExceptionRecord.ExceptionCode,
                static_cast<unsigned long long>(reinterpret_cast<ULONG_PTR>(event.u.Exception.ExceptionRecord.ExceptionAddress)));
        else if (event.dwDebugEventCode == EXIT_PROCESS_DEBUG_EVENT)
            length = sprintf_s(line, "EXIT %lu %08lX\n", process_, event.u.ExitProcess.dwExitCode);
        Write(line, length);
    }
private:
    void Write(const char* line, int length)
    {
        if (file_ == INVALID_HANDLE_VALUE || length <= 0) return;
        DWORD written = 0;
        if (!WriteFile(file_, line, static_cast<DWORD>(length), &written, nullptr) || written != static_cast<DWORD>(length))
            std::puts("DIAGNOSTIC_EVENTS_WRITE_FAILED");
    }
    HANDLE file_ = INVALID_HANDLE_VALUE;
    DWORD process_ = 0;
    unsigned exceptions_ = 0;
};
