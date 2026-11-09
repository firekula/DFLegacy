#include <windows.h>
#include <filesystem>
#include <cstdio>
#include <string>
#include "../client_paths.h"
#include "../../DFLegacy.Ijl15/src/ijl15_config.h"
int wmain(int argc,wchar_t** argv)
{
    namespace fs=std::filesystem; wchar_t executable[32768]{};GetModuleFileNameW(nullptr,executable,32768);
    if(argc==1)
    {
        auto root=fs::temp_directory_path()/(L"DFLegacy paths \u7a7a\u683c \U0001F9EA "+std::to_wstring(GetCurrentProcessId()));fs::create_directories(root);
        auto child=root/L"paths.exe";fs::copy_file(executable,child);std::wstring command=L"\""+child.wstring()+L"\" child";
        STARTUPINFOW si{sizeof(si)};PROCESS_INFORMATION pi{};if(!CreateProcessW(child.c_str(),command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&si,&pi))return 2;
        if(WaitForSingleObject(pi.hProcess,10000)!=WAIT_OBJECT_0){TerminateProcess(pi.hProcess,99);WaitForSingleObject(pi.hProcess,1000);}DWORD code=99;GetExitCodeProcess(pi.hProcess,&code);CloseHandle(pi.hProcess);CloseHandle(pi.hThread);fs::remove_all(root);std::printf("Unicode child failures=%lu\n",code);return static_cast<int>(code);
    }
    (void)argv;int failures=0;auto check=[&](bool b,const char* text){if(!b){++failures;std::fprintf(stderr,"FAIL paths: %s\n",text);}};
    wchar_t output[32768]{};auto root=fs::path(executable).parent_path();
    for(auto leaf:{L"rs_log.txt",L"stage-follow-123.log",L"auto-input-diag.log",L"render-diagnostics-123.json"})
    {check(dfl::client_paths::BuildLogPath(leaf,output,32768),"creates Unicode Logs/DFLegacy");check(fs::path(output)==root/L"Logs"/L"DFLegacy"/leaf,"preserves exact original log name");}
    const auto ini=root/L"Config.ini";
    WritePrivateProfileStringW(L"Resolution",L"Width",L"999",ini.c_str());
    WritePrivateProfileStringW(L"Network",L"ClientServiceUrl",L"https://unicode.example",ini.c_str());
    const auto& settings=dfl::ijl15_config::Get();
    check(settings.WindowWidth==999&&std::string(settings.ClientPayUrl)=="https://unicode.example/client/client_pay.htm","wide Config.ini resolution beside Unicode executable");
    check(dfl::client_paths::BuildLogPath(L"locked.log",output,32768),"locked log path");
    const auto locked=CreateFileW(output,GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
    const auto rejected=CreateFileW(output,GENERIC_WRITE,0,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    check(locked!=INVALID_HANDLE_VALUE&&rejected==INVALID_HANDLE_VALUE&&!fs::exists(root/L"locked.log"),"locked output failure never creates root fallback");
    if(locked!=INVALID_HANDLE_VALUE)CloseHandle(locked);if(rejected!=INVALID_HANDLE_VALUE)CloseHandle(rejected);
    for(auto bad:{L"",L"..",L".",L"../x",L"a\\b",L"C:x",L"name:stream",L"x.",L"x ",L"CON",L"NUL.txt"})
    {wcscpy_s(output,L"stale");check(!dfl::client_paths::BuildLogPath(bad,output,32768)&&!output[0],"rejects non-leaf/alias input and clears output");}
    check(!dfl::client_paths::BuildLogPath(L"normal.log",output,4),"rejects small buffer");
    fs::remove_all(root/L"Logs");const auto file=CreateFileW((root/L"Logs").c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,0,nullptr);CloseHandle(file);
    check(!dfl::client_paths::BuildLogPath(L"normal.log",output,32768)&&!fs::exists(root/L"normal.log"),"failed directory creation has no root fallback");
    std::printf("client paths: %d failure(s)\n",failures);return failures;
}
