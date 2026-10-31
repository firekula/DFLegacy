#include <windows.h>
#include <string>

#ifdef FIXTURE_ORIGINAL_IJL
extern "C" __declspec(dllexport) int OriginalIjlFixture() { return 1508; }
#elif defined(FIXTURE_DLL)
#include "../bootstrap_runtime.h"
static DWORD WINAPI LoaderWorker(void*)
{
    const HMODULE module = LoadLibraryW(L"version.dll");
    if (module) FreeLibrary(module);
    return module ? 0 : 1;
}
bool InitializeClient() noexcept
{
    if (!dfl::client_bootstrap::IsStandalone()) return false;
    if (wcsstr(GetCommandLineW(), L"fail")) return false;
    if (wcsstr(GetCommandLineW(), L"timeout")) { Sleep(20000); return false; }
    const auto expectedMode = wcsstr(GetCommandLineW(), L"test-mode")
        ? dfl::client_bootstrap::Mode::Test : dfl::client_bootstrap::Mode::Normal;
    if (dfl::client_bootstrap::GetMode() != expectedMode) return false;
    // Thread attach and LoadLibrary both need the loader lock. Waiting here
    // proves that the entry gate invokes initialization outside DllMain.
    HANDLE worker = CreateThread(nullptr, 0, LoaderWorker, nullptr, 0, nullptr);
    if (!worker) return false;
    DWORD code = 1;
    const bool completed = WaitForSingleObject(worker, 2000) == WAIT_OBJECT_0
        && GetExitCodeThread(worker, &code) && code == 0;
    CloseHandle(worker);
    return completed;
}
#else
extern "C" __declspec(dllimport) int OriginalIjlFixture();
extern "C" __declspec(dllimport) int RsFixture();
int wmain(int argc, wchar_t** argv)
{
    if (argc != 2 || OriginalIjlFixture() != 1508 || RsFixture()!=17) return 2;
    const HANDLE marker = CreateFileW(argv[1], GENERIC_WRITE, FILE_SHARE_READ,
        nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (marker == INVALID_HANDLE_VALUE) return 3;
    FILETIME now{};
    GetSystemTimeAsFileTime(&now);
    DWORD written = 0;
    WriteFile(marker, &now, sizeof(now), &written, nullptr);
    CloseHandle(marker);
    Sleep(INFINITE);
    return 0;
}
#endif
