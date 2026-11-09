#include <windows.h>
#include <dbghelp.h>
#include <string>
// Dependency injection is linked only into the test helper. The released CLI
// always uses the OS current-user known folder and has no root override.
std::wstring LocalCaptureRoot() {wchar_t path[1024]{};const DWORD n=GetEnvironmentVariableW(L"DFL_TEST_CAPTURE_ROOT",path,1024);return n&&n<1024?path:L"";}
BOOL WINAPI StalledDump(HANDLE,DWORD,HANDLE,MINIDUMP_TYPE,PMINIDUMP_EXCEPTION_INFORMATION,PMINIDUMP_USER_STREAM_INFORMATION,PMINIDUMP_CALLBACK_INFORMATION) {Sleep(20000);return FALSE;}
decltype(&MiniDumpWriteDump) LocalCaptureDumpWriter() {
    if(GetEnvironmentVariableW(L"DFL_TEST_STALL_DUMP",nullptr,0))return StalledDump;
    HMODULE module=LoadLibraryExW(L"dbghelp.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    return module?reinterpret_cast<decltype(&MiniDumpWriteDump)>(GetProcAddress(module,"MiniDumpWriteDump")):nullptr;
}
int LocalCaptureMain(int,wchar_t**);
int wmain(int argc,wchar_t** argv){return LocalCaptureMain(argc,argv);}
