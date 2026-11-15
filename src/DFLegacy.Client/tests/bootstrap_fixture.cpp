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
    const auto status=[](const char* reason)
    {
        HANDLE file=CreateFileW(L"fixture-startup-status.txt",GENERIC_WRITE,FILE_SHARE_READ,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(file!=INVALID_HANDLE_VALUE) { DWORD count=0; WriteFile(file,reason,static_cast<DWORD>(strlen(reason)),&count,nullptr); CloseHandle(file); }
        return false;
    };
    if (!dfl::client_bootstrap::IsStandalone()) return status("not-standalone");
    // The production runtime uses the compatible client's absolute addresses.
    // Keep fixture assumptions executable, including the raw WinMain fixture.
    const auto image = reinterpret_cast<const unsigned char*>(GetModuleHandleW(nullptr));
    if (reinterpret_cast<ULONG_PTR>(image) != 0x400000) return status("fixture-image-base");
    const auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(image);
    const auto nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(image + dos->e_lfanew);
    if (nt->OptionalHeader.ImageBase != 0x400000
        || (nt->OptionalHeader.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE))
        return status("fixture-image-flags");
    if (wcsstr(GetCommandLineW(), L"fail")) return status("requested-failure");
    if (wcsstr(GetCommandLineW(), L"timeout")) { Sleep(20000); return false; }
    const auto expectedMode = wcsstr(GetCommandLineW(), L"test-mode")
        ? dfl::client_bootstrap::Mode::Test : dfl::client_bootstrap::Mode::Normal;
    if (dfl::client_bootstrap::GetMode() != expectedMode) return status("mode-mismatch");
    // Thread attach and LoadLibrary both need the loader lock. Waiting here
    // proves that the entry gate invokes initialization outside DllMain.
    HANDLE worker = CreateThread(nullptr, 0, LoaderWorker, nullptr, 0, nullptr);
    if (!worker) return status("worker-create");
    DWORD code = 1;
    const bool completed = WaitForSingleObject(worker, 2000) == WAIT_OBJECT_0
        && GetExitCodeThread(worker, &code) && code == 0;
    CloseHandle(worker);
    if(!completed) return status("worker-load-or-timeout");
    status("initialized"); return true;
}
#else
extern "C" __declspec(dllimport) int OriginalIjlFixture();
extern "C" __declspec(dllimport) int RsFixture();
#ifdef FIXTURE_WINMAIN
int WINAPI wWinMain(HINSTANCE, HINSTANCE, LPWSTR rawTail, int)
{
    // The original game parses lpCmdLine directly (atol of the first '?' field),
    // not CRT argv. An extra quote changes valid launcher mode 99 into 0.
    if (OriginalIjlFixture()!=1508 || RsFixture()!=17
        || wcscmp(rawTail,L"99?127.0.0.1?2311?fixture?synthetic")) return 4;
    HANDLE marker=CreateFileW(L"raw-entry.marker",GENERIC_WRITE,FILE_SHARE_READ,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(marker==INVALID_HANDLE_VALUE) return 3;
    CloseHandle(marker); Sleep(INFINITE); return 0;
}
#else
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
#endif
