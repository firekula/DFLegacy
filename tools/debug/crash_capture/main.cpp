// External ACT1 diagnostic tool. Never modifies game memory or handles an
// application exception. First-chance records may also describe handled probes.
#include <windows.h>
#include <dbghelp.h>
#include <psapi.h>
#include <conio.h>
#include <cstdio>
#include <cwchar>
#include <string>
#include <shlobj.h>

std::wstring LocalCaptureRoot() {
    PWSTR path{};if(FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData,0,nullptr,&path)))return {};
    const std::wstring root=std::wstring(path)+L"\\DFLegacy\\Diagnostics";CoTaskMemFree(path);return root;
}
decltype(&MiniDumpWriteDump) LocalCaptureDumpWriter() {
    HMODULE module=LoadLibraryExW(L"dbghelp.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    return module?reinterpret_cast<decltype(&MiniDumpWriteDump)>(GetProcAddress(module,"MiniDumpWriteDump")):nullptr;
}

namespace {
decltype(&MiniDumpWriteDump) writeDump = nullptr;
volatile LONG stopping = 0;
BOOL WINAPI Stop(DWORD signal) {
    if (signal == CTRL_C_EVENT || signal == CTRL_BREAK_EVENT || signal == CTRL_CLOSE_EVENT) {
        InterlockedExchange(&stopping, 1);
        return TRUE;
    }
    return FALSE;
}
bool Relevant(DWORD code) {
    switch (code) {
    case EXCEPTION_ACCESS_VIOLATION: case EXCEPTION_IN_PAGE_ERROR: case EXCEPTION_ILLEGAL_INSTRUCTION:
    case EXCEPTION_INT_DIVIDE_BY_ZERO: case EXCEPTION_INT_OVERFLOW:
    case EXCEPTION_STACK_OVERFLOW: case EXCEPTION_NONCONTINUABLE_EXCEPTION:
    case 0xC0000374: case 0xC0000409: case 0xC0000417: case 0xC0000420: case 0x40000015:
        return true;
    default: return false;
    }
}
struct Budget { ULONGLONG begin; HANDLE file; };
BOOL CALLBACK Limit(PVOID parameter, MINIDUMP_CALLBACK_INPUT* input,
    MINIDUMP_CALLBACK_OUTPUT* output) {
    if (input->CallbackType == CancelCallback) {
        const auto& budget = *static_cast<Budget*>(parameter);
        LARGE_INTEGER size{};
        GetFileSizeEx(budget.file, &size);
        output->CheckCancel = TRUE;
        output->Cancel = GetTickCount64() - budget.begin > 5000
            || size.QuadPart > 64LL * 1024 * 1024;
    }
    return TRUE;
}
bool Dump(HANDLE process, DWORD pid, const std::wstring& path,
    MINIDUMP_EXCEPTION_INFORMATION* exception, FILE* log) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr,
        CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        fwprintf(log, L"dump-open-error=%lu\n", GetLastError()); return false;
    }
    Budget budget{GetTickCount64(), file};
    MINIDUMP_CALLBACK_INFORMATION callback{Limit, &budget};
    auto flags = static_cast<MINIDUMP_TYPE>(MiniDumpWithIndirectlyReferencedMemory
        | MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules | MiniDumpWithFullMemoryInfo);
    const bool ok = writeDump(process, pid, file, flags, exception, nullptr, &callback) != FALSE;
    const DWORD error = ok ? 0 : GetLastError();
    FlushFileBuffers(file); CloseHandle(file);
    if (!ok) DeleteFileW(path.c_str()); // Only the exact output file we created.
    fwprintf(log, L"dump=%s result=%s error=%08lX elapsed_ms=%llu\n",
        path.c_str(), ok ? L"saved" : L"failed", error, GetTickCount64() - budget.begin);
    fflush(log);
    return ok;
}
void Record(HANDLE process, const DEBUG_EVENT& event, const CONTEXT& context, FILE* log) {
    const auto& record = event.u.Exception.ExceptionRecord;
    SYSTEMTIME time{}; GetLocalTime(&time);
    fwprintf(log, L"\n%04u-%02u-%02u %02u:%02u:%02u.%03u tid=%lu first_chance=%lu code=%08lX address=%p\n",
        time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond,
        time.wMilliseconds, event.dwThreadId, event.u.Exception.dwFirstChance,
        record.ExceptionCode, record.ExceptionAddress);
    MEMORY_BASIC_INFORMATION memory{};
    wchar_t module[32768]{};
    if (VirtualQueryEx(process, record.ExceptionAddress, &memory, sizeof(memory))
        && GetMappedFileNameW(process, record.ExceptionAddress, module, 32768)) {
        fwprintf(log, L"module=%s base=%p offset=%08lX\n", module, memory.AllocationBase,
            static_cast<DWORD>(reinterpret_cast<ULONG_PTR>(record.ExceptionAddress)
                - reinterpret_cast<ULONG_PTR>(memory.AllocationBase)));
    }
    for (DWORD i = 0; i < record.NumberParameters && i < EXCEPTION_MAXIMUM_PARAMETERS; ++i)
        fwprintf(log, L"exception_parameter[%lu]=%08lX\n", i, record.ExceptionInformation[i]);
    fwprintf(log, L"EIP=%08lX ESP=%08lX EBP=%08lX EFLAGS=%08lX\nEAX=%08lX EBX=%08lX ECX=%08lX EDX=%08lX ESI=%08lX EDI=%08lX\n",
        context.Eip, context.Esp, context.Ebp, context.EFlags, context.Eax, context.Ebx,
        context.Ecx, context.Edx, context.Esi, context.Edi);
    unsigned char instruction[32]{}; SIZE_T instructionRead = 0;
    ReadProcessMemory(process, reinterpret_cast<const void*>(context.Eip), instruction, sizeof(instruction), &instructionRead);
    fwprintf(log, L"instruction_bytes=");
    for (SIZE_T i = 0; i < instructionRead; ++i) fwprintf(log, L"%02X", instruction[i]);
    fwprintf(log, L"\n");
    DWORD stack[64]{}; SIZE_T read = 0;
    ReadProcessMemory(process, reinterpret_cast<const void*>(context.Esp), stack, sizeof(stack), &read);
    for (SIZE_T i = 0; i < read / sizeof(DWORD); ++i)
        fwprintf(log, L"stack+%03X=%08lX\n", static_cast<unsigned>(i * sizeof(DWORD)), stack[i]);
    fflush(log);
}
}

int LocalCaptureMain(int argc,wchar_t** argv);
int wmain(int argc, wchar_t** argv) {
    if(argc>1&&(!wcscmp(argv[1],L"--local-session")||!wcscmp(argv[1],L"--local-dump")))return LocalCaptureMain(argc,argv);
    if (argc != 4 || (wcscmp(argv[1], L"monitor") && wcscmp(argv[1], L"snapshot"))) {
        puts("Usage: DFLegacy.CrashCapture.exe monitor|snapshot PID EXISTING_REPORT_DIRECTORY"); return 2;
    }
    wchar_t* end = nullptr;
    const DWORD pid = wcstoul(argv[2], &end, 10);
    if (!pid || !end || *end) return 2;
    const std::wstring directory = argv[3];
    const DWORD attributes = GetFileAttributesW(directory.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES || !(attributes & FILE_ATTRIBUTE_DIRECTORY)) return 2;
    // Use the OS-supplied x86 DbgHelp, never a game-directory DLL.
    HMODULE dbghelp = LoadLibraryExW(L"dbghelp.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (dbghelp) writeDump = reinterpret_cast<decltype(writeDump)>(GetProcAddress(dbghelp, "MiniDumpWriteDump"));
    if (!writeDump) { puts("Windows DbgHelp could not be loaded."); return 3; }
    HANDLE process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | SYNCHRONIZE, FALSE, pid);
    if (!process) { printf("OpenProcess failed: %lu. Run at the same privilege level as the game.\n", GetLastError()); return 3; }
    wchar_t executable[32768]{}; DWORD capacity = 32768;
    if (!QueryFullProcessImageNameW(process, 0, executable, &capacity)) { CloseHandle(process); return 3; }
    const auto leaf = wcsrchr(executable, L'\\');
    BOOL hostWow = FALSE, targetWow = FALSE;
    if (!leaf || _wcsicmp(leaf + 1, L"DNF.exe")
        || !IsWow64Process(GetCurrentProcess(), &hostWow) || !IsWow64Process(process, &targetWow)
        || (hostWow && !targetWow)) {
        puts("Refusing: select a 32-bit DNF.exe process."); CloseHandle(process); return 3;
    }
    FILE* log = nullptr;
    if (_wfopen_s(&log, (directory + L"\\exceptions.txt").c_str(), L"w, ccs=UTF-8") || !log) {
        CloseHandle(process); return 3;
    }
    fwprintf(log, L"capture_version=1 pid=%lu executable=%s\n", pid, executable);
    fflush(log);
    if (!wcscmp(argv[1], L"snapshot")) {
        const bool ok = Dump(process, pid, directory + L"\\snapshot.dmp", nullptr, log);
        fclose(log); CloseHandle(process); return ok ? 0 : 4;
    }
    SetConsoleCtrlHandler(Stop, TRUE);
    if (!DebugActiveProcess(pid)) {
        printf("Attach failed: %lu. Close other debuggers or match the game's privilege level.\n", GetLastError());
        fclose(log); CloseHandle(process); return 5;
    }
    if (!DebugSetProcessKillOnExit(FALSE)) {
        DebugActiveProcessStop(pid); fclose(log); CloseHandle(process); return 5;
    }
    puts("MONITOR READY. Reproduce the issue. Press Q or Ctrl+C to detach (game stays open).");
    fflush(stdout);
    bool firstBreakpoint = true, exited = false;
    unsigned sequence = 0;
    DWORD previousCode = 0; PVOID previousAddress = nullptr; ULONGLONG previousTime = 0;
    int result = 0;
    while (!InterlockedCompareExchange(&stopping, 0, 0)) {
        if (_kbhit() && (_getch() | 32) == 'q') break;
        if (GetFileAttributesW((directory + L"\\STOP").c_str()) != INVALID_FILE_ATTRIBUTES) break;
        DEBUG_EVENT event{};
        if (!WaitForDebugEvent(&event, 200)) {
            if (GetLastError() == ERROR_SEM_TIMEOUT) continue;
            result = 6; break;
        }
        DWORD disposition = DBG_CONTINUE;
        if (event.dwDebugEventCode == CREATE_PROCESS_DEBUG_EVENT && event.u.CreateProcessInfo.hFile)
            CloseHandle(event.u.CreateProcessInfo.hFile);
        if (event.dwDebugEventCode == LOAD_DLL_DEBUG_EVENT && event.u.LoadDll.hFile)
            CloseHandle(event.u.LoadDll.hFile);
        if (event.dwDebugEventCode == EXIT_PROCESS_DEBUG_EVENT) {
            fwprintf(log, L"process-exit=%08lX\n", event.u.ExitProcess.dwExitCode); exited = true;
        }
        if (event.dwDebugEventCode == EXCEPTION_DEBUG_EVENT) {
            auto& exception = event.u.Exception;
            auto& record = exception.ExceptionRecord;
            disposition = DBG_EXCEPTION_NOT_HANDLED;
            if (firstBreakpoint && record.ExceptionCode == EXCEPTION_BREAKPOINT && exception.dwFirstChance) {
                firstBreakpoint = false; disposition = DBG_CONTINUE;
            } else if (Relevant(record.ExceptionCode) || !exception.dwFirstChance) {
                HANDLE thread = OpenThread(THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION, FALSE, event.dwThreadId);
                CONTEXT context{}; context.ContextFlags = CONTEXT_ALL;
                const bool gotContext = thread && GetThreadContext(thread, &context);
                if (thread) CloseHandle(thread);
                // Keep observing after handled probes. Throttle repeated identical
                // first-chance faults; always capture second chance exceptions.
                const auto now = GetTickCount64();
                if (!exception.dwFirstChance || record.ExceptionCode != previousCode
                    || record.ExceptionAddress != previousAddress || now - previousTime >= 1000) {
                    // Rotate diagnostics too; a faulting loop must not fill the disk.
                    if (sequence && sequence % 64 == 0) {
                        fclose(log);
                        _wfopen_s(&log, (directory + L"\\exceptions.txt").c_str(), L"w, ccs=UTF-8");
                        if (!log) { result = 7; ContinueDebugEvent(event.dwProcessId, event.dwThreadId, disposition); break; }
                    }
                    Record(process, event, context, log);
                    if (gotContext) {
                        // Exception and context are copied into the debugger process.
                        EXCEPTION_POINTERS pointers{&record, &context};
                        MINIDUMP_EXCEPTION_INFORMATION info{event.dwThreadId, &pointers, FALSE};
                        wchar_t name[40]{}; swprintf_s(name, L"\\exception-%02u.dmp", sequence % 16);
                        const bool saved = Dump(process, pid, directory + name, &info, log);
                        printf("Exception %08lX at %p: %s (slot %u)\n", record.ExceptionCode,
                            record.ExceptionAddress, saved ? "dump saved" : "dump failed", sequence % 16);
                        fflush(stdout);
                    } else fwprintf(log, L"context-unavailable; no exception dump\n");
                    ++sequence; previousCode = record.ExceptionCode; previousAddress = record.ExceptionAddress; previousTime = now;
                }
            }
        }
        if (!ContinueDebugEvent(event.dwProcessId, event.dwThreadId, disposition)) { result = 6; break; }
        if (exited) break;
    }
    if (!exited && !DebugActiveProcessStop(pid)) {
        printf("Detach returned error %lu; exiting with kill-on-exit disabled.\n", GetLastError()); result = 6;
    }
    if (log) { fflush(log); fclose(log); }
    CloseHandle(process);
    puts("Capture stopped. Collected first-chance exceptions are evidence, not automatically crashes.");
    return result;
}
