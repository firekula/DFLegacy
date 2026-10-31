#include <windows.h>
#include <detours.h>
#include "bootstrap_protocol.h"
#include "bootstrap_runtime.h"
#include "early_log_redirect.h"
#ifndef FIXTURE_DLL
#include "../DFLegacy.Ijl15/src/dnf_patches.h"
static bool InitializeClient() noexcept
{
    return dfl::dnf_patches::IsTargetProcess() && dfl::dnf_patches::Apply();
}
#else
#include "tests/bootstrap_diagnostics.h"
bool InitializeClient() noexcept;
#endif
#ifndef FIXTURE_DLL
#define DFL_FIXTURE_OBSERVE(stage, expression) (expression)
#endif

namespace dfl::client_bootstrap { void SetStandaloneMode(Mode mode) noexcept; }
namespace
{
    using namespace dfl::client_bootstrap;
    SharedState* shared = nullptr;
    HANDLE ready = nullptr, release = nullptr;
    HMODULE self = nullptr;
    using Entry = void (WINAPI*)();
    Entry originalEntry = nullptr;

    void WINAPI EntryGate()
    {
        bool success = InitializeClient();
        const DWORD length = GetModuleFileNameW(self, shared->modulePath, PathCapacity);
        if (!length || length >= PathCapacity) success = false;
        const HANDLE file = CreateFileW(shared->modulePath, GENERIC_READ, FILE_SHARE_READ,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        BY_HANDLE_FILE_INFORMATION info{};
        if (file == INVALID_HANDLE_VALUE || !GetFileInformationByHandle(file, &info)) success = false;
        if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
        shared->volume = info.dwVolumeSerialNumber;
        shared->fileIndexHigh = info.nFileIndexHigh;
        shared->fileIndexLow = info.nFileIndexLow;
        InterlockedExchange(&shared->initialized, success ? 1 : -1);
        SetEvent(ready);
        if (!success || WaitForSingleObject(release, CommitTimeout + 2000) != WAIT_OBJECT_0)
            ExitProcess(ERROR_DLL_INIT_FAILED);
        UnmapViewOfFile(shared); shared = nullptr;
        CloseHandle(ready); CloseHandle(release);
        originalEntry();
    }
}

// Detours requires ordinal 1 when adding the import descriptor. This is not
// a public attach API: initialization only accepts the private child payload.
extern "C" void WINAPI DFLegacyBootstrapMarker() {}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    using namespace dfl::client_bootstrap;
    DWORD size = 0;
    const auto* payload = static_cast<const Payload*>(DetourFindPayloadEx(PayloadId, &size));
    if (!payload || size != sizeof(Payload) || payload->magic != ProtocolMagic
        || payload->size != sizeof(Payload)) return FALSE;
    shared = static_cast<SharedState*>(MapViewOfFile(reinterpret_cast<HANDLE>(payload->mapping), FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedState)));
    CloseHandle(reinterpret_cast<HANDLE>(payload->mapping));
    if (!shared || shared->magic != ProtocolMagic || shared->size != sizeof(SharedState)
        || shared->pid != GetCurrentProcessId() || shared->mode > 1) return FALSE;
    ready = reinterpret_cast<HANDLE>(payload->ready);
    release = reinterpret_cast<HANDLE>(payload->release);
    self = instance;
    SetStandaloneMode(shared->mode ? Mode::Test : Mode::Normal);
    if(!DFL_FIXTURE_OBSERVE("early-rs",dfl::client_paths::InstallEarlyRsLogRedirect()))return FALSE;
    originalEntry = reinterpret_cast<Entry>(DFL_FIXTURE_OBSERVE("entry-point",DetourGetEntryPoint(GetModuleHandleW(nullptr))));
    if (!originalEntry || !DFL_FIXTURE_OBSERVE("restore-imports",DetourRestoreAfterWith())) return FALSE;
    if (DFL_FIXTURE_OBSERVE("detour-begin",DetourTransactionBegin()) != NO_ERROR) return FALSE;
    if (DFL_FIXTURE_OBSERVE("detour-thread",DetourUpdateThread(GetCurrentThread())) != NO_ERROR
        || DFL_FIXTURE_OBSERVE("detour-attach",DetourAttach(reinterpret_cast<PVOID*>(&originalEntry), EntryGate)) != NO_ERROR)
    { DetourTransactionAbort(); return FALSE; }
    return DFL_FIXTURE_OBSERVE("detour-commit",DetourTransactionCommit()) == NO_ERROR;
}
