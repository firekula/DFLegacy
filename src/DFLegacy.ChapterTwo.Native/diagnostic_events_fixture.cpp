#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <string>
#include "ChapterTwoDiagnosticEvents.h"

// Standalone debugger/child fixture. It never locates or opens unrelated processes.
static void HandledFault()
{
    __try { RaiseException(0xE0424242, 0, 0, nullptr); }
    __except (EXCEPTION_EXECUTE_HANDLER) { }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc == 4 && !wcscmp(argv[1], L"--child"))
    {
        if (GetEnvironmentVariableW(L"DFLEGACY_CH2_DIAGNOSTIC_EVENTS", nullptr, 0)) return 41;
        HANDLE overwrite = CreateFileW(argv[3], GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_EXISTING, 0, nullptr);
        if (overwrite != INVALID_HANDLE_VALUE) { CloseHandle(overwrite); return 42; }
        OutputDebugStringA("DFLegacy Chapter Two: fixture\nOWNED_PID 1\nEXCEPTION code=c0000005 first=0 at=12345678\n");
        // The handled first-chance exception must not become a crash record.
        HandledFault();
        Sleep(350);
        if (!wcscmp(argv[2], L"native")) *reinterpret_cast<volatile int*>(0) = 7;
        return !wcscmp(argv[2], L"normal") ? 0 : 5;
    }
    if (argc != 3) return 2;
    const std::wstring root = argv[1], mode = argv[2];
    const auto executable = root + L"\\s1c2_chapter2_host-fixture.exe";
    const auto journal = root + L"\\Logs\\DFLegacy\\chapter-two-events-fixture.log";
    auto command = L"\"" + executable + L"\" --child " + mode + L" \"" + journal + L"\"";
    std::puts("CHAPTER_TWO_ATTEMPT s1c2_chapter2_host-fixture.exe chapter-two-client-fixture.log chapter-two-events-fixture.log");
    std::fflush(stdout);
    SetEnvironmentVariableW(L"DFLEGACY_CH2_DIAGNOSTIC_EVENTS", journal.c_str());
    ChapterTwoDiagnosticEvents events;
    STARTUPINFOW startup{ sizeof(startup) };
    PROCESS_INFORMATION child{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE,
        DEBUG_ONLY_THIS_PROCESS | CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &child)) return 3;
    events.Started(child.dwProcessId);
    std::printf("OWNED_PID %lu\n", child.dwProcessId);
    std::fflush(stdout);
    bool initialBreakpoint = true;
    for (;;)
    {
        DEBUG_EVENT event{};
        if (!WaitForDebugEvent(&event, 15000)) { TerminateProcess(child.hProcess, 43); return 4; }
        events.Observe(event);
        DWORD status = DBG_CONTINUE;
        if (event.dwDebugEventCode == CREATE_PROCESS_DEBUG_EVENT && event.u.CreateProcessInfo.hFile)
            CloseHandle(event.u.CreateProcessInfo.hFile);
        if (event.dwDebugEventCode == LOAD_DLL_DEBUG_EVENT && event.u.LoadDll.hFile)
            CloseHandle(event.u.LoadDll.hFile);
        if (event.dwDebugEventCode == EXCEPTION_DEBUG_EVENT)
        {
            if (initialBreakpoint && event.u.Exception.ExceptionRecord.ExceptionCode == EXCEPTION_BREAKPOINT)
                initialBreakpoint = false;
            else status = DBG_EXCEPTION_NOT_HANDLED;
        }
        if (event.dwDebugEventCode == OUTPUT_DEBUG_STRING_EVENT)
        {
            // Preserve the dangerous line shape to prove the managed side ignores it.
            std::puts("DEBUG fixture\nOWNED_PID 1\nEXCEPTION code=c0000005 first=0 at=12345678");
            std::fflush(stdout);
        }
        ContinueDebugEvent(event.dwProcessId, event.dwThreadId, status);
        if (event.dwDebugEventCode == EXIT_PROCESS_DEBUG_EVENT) break;
    }
    CloseHandle(child.hThread);
    CloseHandle(child.hProcess);
    std::puts("CHAPTER_TWO_FINISH final");
    return 0;
}
