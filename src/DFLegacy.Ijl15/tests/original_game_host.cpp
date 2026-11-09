#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <detours.h>
#include <cstdio>
#include <string>
int wmain(int argc,wchar_t** argv)
{
    if(argc!=3)return 2;const std::wstring exe=argv[1],directory=exe.substr(0,exe.find_last_of(L"\\/"));std::wstring command=L"\""+exe+L"\"";
    char dll[1024]{};if(!WideCharToMultiByte(CP_ACP,WC_NO_BEST_FIT_CHARS,argv[2],-1,dll,1024,nullptr,nullptr))return 3;
    STARTUPINFOW start{};start.cb=sizeof(start);start.dwFlags=STARTF_USESHOWWINDOW;start.wShowWindow=SW_HIDE;PROCESS_INFORMATION child{};
    if(!DetourCreateProcessWithDllExW(exe.c_str(),command.data(),nullptr,nullptr,FALSE,CREATE_SUSPENDED|CREATE_DEFAULT_ERROR_MODE,nullptr,directory.c_str(),&start,&child,dll,nullptr)){fprintf(stderr,"create-error=%lu\n",GetLastError());return 4;}
    FILETIME created{},exit{},kernel{},user{};GetProcessTimes(child.hProcess,&created,&exit,&kernel,&user);
    printf("ready %lu %llu\n",child.dwProcessId,((static_cast<unsigned long long>(created.dwHighDateTime)<<32)|created.dwLowDateTime)+504911232000000000ULL);fflush(stdout);
    (void)getchar();ResumeThread(child.hThread);CloseHandle(child.hThread);
    if(WaitForSingleObject(child.hProcess,20000)==WAIT_TIMEOUT){fputs("own-probe-timeout\n",stderr);TerminateProcess(child.hProcess,124);WaitForSingleObject(child.hProcess,2000);}
    DWORD code{};GetExitCodeProcess(child.hProcess,&code);CloseHandle(child.hProcess);printf("game-exit=%lu\n",code);return 0;
}
