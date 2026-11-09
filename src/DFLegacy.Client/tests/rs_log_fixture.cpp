#include <windows.h>
extern "C" __declspec(dllexport) int RsFixture(){return 17;}
BOOL WINAPI DllMain(HINSTANCE,DWORD reason,LPVOID)
{
    if(reason==DLL_PROCESS_ATTACH)
    {
        const auto file=CreateFileA("rs_log.txt",GENERIC_WRITE,FILE_SHARE_READ,nullptr,CREATE_ALWAYS,0,nullptr);
        if(file==INVALID_HANDLE_VALUE)return FALSE;
        const auto other=CreateFileA("unrelated.txt",GENERIC_WRITE,FILE_SHARE_READ,nullptr,CREATE_ALWAYS,0,nullptr);
        if(other==INVALID_HANDLE_VALUE){CloseHandle(file);return FALSE;}CloseHandle(other);
        DWORD written=0;WriteFile(file,"early-rs",8,&written,nullptr);CloseHandle(file);
    }
    return TRUE;
}
