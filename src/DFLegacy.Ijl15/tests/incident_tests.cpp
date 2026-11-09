#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <initializer_list>
#include <malloc.h>
#include <string>
#include "native_incidents.h"

__declspec(naked) void Fault() { __asm { mov eax,123h } __asm { mov dword ptr [eax],1 } __asm { ret } }
__declspec(naked) void SecondFault() { __asm { _emit 0x0f } __asm { _emit 0x0b } __asm { ret } }
unsigned FaultNumber=0;bool DelayRecovery=false;
LONG WINAPI TwoFaults(EXCEPTION_POINTERS* e) {if(++FaultNumber==1){if(DelayRecovery)Sleep(6000);e->ContextRecord->Eip+=6;return EXCEPTION_CONTINUE_EXECUTION;}return EXCEPTION_EXECUTE_HANDLER;}
#pragma warning(push)
#pragma warning(disable:4717) // Intentional genuine recursive exhaustion, not a raised status code.
__declspec(noinline) int Overflow(unsigned depth) { volatile char page[4096];page[depth%4096]=static_cast<char>(depth);return Overflow(depth+1)+page[depth%4096]; }
#pragma warning(pop)
LONG WINAPI Previous(EXCEPTION_POINTERS*) { return EXCEPTION_EXECUTE_HANDLER; }
LONG WINAPI Recover(EXCEPTION_POINTERS* e) { e->ContextRecord->Eip+=6;return EXCEPTION_CONTINUE_EXECUTION; }
LONG WINAPI ExitBeforeReturn(EXCEPTION_POINTERS*) { ExitProcess(91); }
LONG WINAPI BlockBeforeReturn(EXCEPTION_POINTERS*) { Sleep(20000);return EXCEPTION_CONTINUE_SEARCH; }
LONG WINAPI BlockThenRecover(EXCEPTION_POINTERS* e) {auto window=CreateWindowExW(0,L"STATIC",L"CrashSender",WS_OVERLAPPEDWINDOW,0,0,200,100,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);Sleep(6000);DestroyWindow(window);e->ContextRecord->Eip+=6;return EXCEPTION_CONTINUE_EXECUTION;}
volatile LONG StackReady=0;
DWORD WINAPI LargeStack(void*) {volatile char* bytes=static_cast<volatile char*>(_alloca(1024*1024));for(unsigned i=0;i<1024*1024;i+=4096)bytes[i]=1;InterlockedIncrement(&StackReady);Sleep(20000);return bytes[0];}
ULONG_PTR WINAPI DenySocket(int,int,int,void*,unsigned,unsigned) {SetLastError(ERROR_ACCESS_DENIED);return ~ULONG_PTR{};}
void* WINAPI DenyHost(const char*) {SetLastError(ERROR_ACCESS_DENIED);return nullptr;}
void* WINAPI DenyInternet(const char*,DWORD,const char*,const char*,DWORD) {SetLastError(ERROR_ACCESS_DENIED);return nullptr;}
DWORD WINAPI DenyAttempt(DWORD) {return ERROR_ACCESS_DENIED;}
bool DisableLegacyTransport(HMODULE module)
{
    auto* base=reinterpret_cast<unsigned char*>(module);auto* dos=reinterpret_cast<IMAGE_DOS_HEADER*>(base);auto* nt=reinterpret_cast<IMAGE_NT_HEADERS32*>(base+dos->e_lfanew);
    auto* imports=reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base+nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);unsigned patched=0;
    for(;imports->Name;++imports) {
        auto* names=reinterpret_cast<IMAGE_THUNK_DATA32*>(base+imports->OriginalFirstThunk);auto* slots=reinterpret_cast<IMAGE_THUNK_DATA32*>(base+imports->FirstThunk);
        for(;names->u1.AddressOfData;++names,++slots) {
            const bool ordinal=IMAGE_SNAP_BY_ORDINAL32(names->u1.Ordinal)!=0;
            const char* name=ordinal?(IMAGE_ORDINAL32(names->u1.Ordinal)==52&&!_stricmp(reinterpret_cast<char*>(base+imports->Name),"WS2_32.dll")?"gethostbyname":"ordinal"):reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base+names->u1.AddressOfData)->Name;void* deny=nullptr;
            if(!strcmp(name,"WSASocketA"))deny=reinterpret_cast<void*>(DenySocket);
            if(!strcmp(name,"gethostbyname"))deny=reinterpret_cast<void*>(DenyHost);
            if(!strcmp(name,"InternetOpenA"))deny=reinterpret_cast<void*>(DenyInternet);
            if(!strcmp(name,"InternetAttemptConnect"))deny=reinterpret_cast<void*>(DenyAttempt);
            if(deny){DWORD protection{};if(!VirtualProtect(&slots->u1.Function,sizeof(DWORD),PAGE_READWRITE,&protection))return false;slots->u1.Function=reinterpret_cast<DWORD>(deny);DWORD ignored{};VirtualProtect(&slots->u1.Function,sizeof(DWORD),protection,&ignored);++patched;fprintf(stderr,"blocked-import=%s\n",name);}
        }
    }if(patched!=4)fprintf(stderr,"transport-import-patches=%u\n",patched);return patched==4;
}
HMODULE Original(const wchar_t* mode,const wchar_t* directory)
{
    wchar_t root[1024]{};if(!GetEnvironmentVariableW(L"DFL_TEST_ORIGINAL_DIR",root,1024))return nullptr;
    const bool bug=wcsstr(mode,L"bug")!=nullptr;
    const auto path=std::wstring(root)+(bug?L"\\BugTrap.dll":L"\\DumpReport.dll");
    HMODULE module=LoadLibraryExW(path.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);if(!module){fprintf(stderr,"original-load-error=%lu\n",GetLastError());return nullptr;}
    if(bug) {
        if(!wcsstr(mode,L"privacy")&&!DisableLegacyTransport(module)){fprintf(stderr,"transport-guard-failed\n");return nullptr;}
        // ABI independently checked in original disassembly: stdcall scalar,
        // stdcall ANSI string+port, stdcall ANSI path. Never use SENDREPORT=4.
        reinterpret_cast<void(WINAPI*)(DWORD)>(GetProcAddress(module,"BT_SetActivityType"))(2);
        wchar_t port[16]{};GetEnvironmentVariableW(L"DFL_TEST_RECEIVER_PORT",port,16);
        reinterpret_cast<void(WINAPI*)(const char*,DWORD)>(GetProcAddress(module,"BT_SetSupportServer"))("127.0.0.1",port[0]?wcstoul(port,nullptr,10):9);
        reinterpret_cast<void(WINAPI*)(DWORD)>(GetProcAddress(module,"BT_SetFlags"))(0);
        char report[1024]{};WideCharToMultiByte(CP_ACP,0,directory,-1,report,1024,nullptr,nullptr);
        reinterpret_cast<void(WINAPI*)(const char*)>(GetProcAddress(module,"BT_SetReportFilePath"))(report);
        if(reinterpret_cast<DWORD(WINAPI*)()>(GetProcAddress(module,"BT_GetActivityType"))()!=2)return nullptr;
        if(wcsstr(mode,L"badsig")){auto* entry=reinterpret_cast<unsigned char*>(GetProcAddress(module,"BT_SehFilter"));DWORD protection{};if(!VirtualProtect(entry,1,PAGE_EXECUTE_READWRITE,&protection))return nullptr;*entry=0x90;DWORD ignored{};VirtualProtect(entry,1,protection,&ignored);}
    }
    return module;
}
__declspec(naked) int InvokeNotice(void*,void*,unsigned,unsigned)
{
    __asm {
        push ebp
        mov ebp,esp
        push 0
        push 1
        push 0
        push 23h
        mov ecx,[ebp+0ch]
        call dword ptr [ebp+8]
        cmp esp,ebp
        je balanced
        xor eax,eax
    balanced:
        mov esp,ebp
        pop ebp
        ret
    }
}
bool SignatureFixture(int mutate)
{
    auto* image=static_cast<unsigned char*>(VirtualAlloc(nullptr,0xb3c000,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));
    if(!image)return false;
    const unsigned char connectGuard[]{0xC1,0xE9,0x10,0x85,0xC9,0x0F,0x84,0xFB,0x01,0x00,0x00,0x6A,0x00,0x6A,0x01,0x68,0x95,0x00,0x00,0x00,0xE8,0x52,0x7A,0xFF,0xFF,0x8B,0x0D,0xE8,0xDC,0xB5,0x00,0x83,0xC4,0x04,0x50,0x6A,0x23};
    const unsigned char closeGuard[]{0x84,0xC0,0x74,0x1B,0x39,0x5D,0xE4,0x8B,0x45,0xD0,0x73,0x03,0x8D,0x45,0xD0,0x8B,0x0D,0xE8,0xDC,0xB5,0x00,0x57,0x56,0x50,0x6A,0x23};
    const unsigned char connect[]{0xE8,0x91,0x73,0xEE,0xFF},close[]{0xE8,0xF4,0x71,0xEE,0xFF};
    const unsigned char notice[]{0x55,0x8B,0xEC,0x83,0xEC,0x08,0x8B,0x55,0x08};
    const unsigned char entry[]{0x55,0x8B,0xEC,0x6A,0xFF,0x68,0xE6,0xD1,0x98,0x00};
    auto* dos=reinterpret_cast<IMAGE_DOS_HEADER*>(image);dos->e_magic=IMAGE_DOS_SIGNATURE;dos->e_lfanew=0x80;
    auto* nt=reinterpret_cast<IMAGE_NT_HEADERS32*>(image+0x80);nt->Signature=IMAGE_NT_SIGNATURE;nt->FileHeader.Machine=IMAGE_FILE_MACHINE_I386;nt->OptionalHeader.Magic=IMAGE_NT_OPTIONAL_HDR32_MAGIC;nt->OptionalHeader.SizeOfImage=0xb3c000;nt->OptionalHeader.ImageBase=0x400000;
    memcpy(image+0x4b2b35,connectGuard,sizeof(connectGuard));memcpy(image+0x4b2cdd,closeGuard,sizeof(closeGuard));
    memcpy(image+0x4b2b5a,connect,sizeof(connect));memcpy(image+0x4b2cf7,close,sizeof(close));memcpy(image+0x399ef0,notice,sizeof(notice));
    memcpy(image+0x4b2880,entry,sizeof(entry));
    if(mutate==1)image[0x4b2cdd+2]=0x75; // opposite branch would include silent handoff
    if(mutate==2)nt->FileHeader.Machine=IMAGE_FILE_MACHINE_AMD64;
    if(mutate==3)image[0x4b2880]=0x90;
    if(mutate==4)image[0x4b2b35+20]=0x90;
    const bool installed=dfl::native_incidents::InstallNetworkHooks(reinterpret_cast<std::uintptr_t>(image));
    printf("installed=%d connect=%02x%02x%02x%02x%02x close=%02x%02x%02x%02x%02x\n",installed,image[0x4b2b5a],image[0x4b2b5b],image[0x4b2b5c],image[0x4b2b5d],image[0x4b2b5e],image[0x4b2cf7],image[0x4b2cf8],image[0x4b2cf9],image[0x4b2cfa],image[0x4b2cfb]);
    bool ok=mutate ? !installed&&!memcmp(image+0x4b2b5a,connect,5)&&!memcmp(image+0x4b2cf7,close,5) : installed&&memcmp(image+0x4b2b5a,connect,5)&&memcmp(image+0x4b2cf7,close,5);
    if(ok&&!mutate)
    {
        // Execute the installed thunks against a harmless native target. It
        // returns a marker and performs the original thiscall callee cleanup.
        const unsigned char target[]{0xB8,0x68,0x24,0x57,0x13,0xC2,0x10,0x00};memcpy(image+0x399ef0,target,sizeof(target));
        for(auto site:{0x4b2b5a,0x4b2cf7}) { std::uint32_t delta{};memcpy(&delta,image+site+1,4);auto* hook=image+site+5+delta;ok &= InvokeNotice(hook,nullptr,0,site==0x4b2b5a ? (10061u<<16)|16u : 32u)==0x13572468; }
    }
    VirtualFree(image,0,MEM_RELEASE);return ok;
}
int wmain(int argc,wchar_t** argv)
{
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    if(argc!=3)return 2;
    HMODULE faultModule=nullptr;
    if(!wcscmp(argv[1],L"module-crash")){wchar_t path[1024]{};GetEnvironmentVariableW(L"DFL_TEST_FAULT_MODULE",path,1024);faultModule=LoadLibraryW(path);if(!faultModule)return 21;}
    HMODULE original=nullptr;
    if(!wcsncmp(argv[1],L"original-",9)){original=Original(argv[1],argv[2]);if(!original)return 13;}
    if(!wcscmp(argv[1],L"previous"))SetUnhandledExceptionFilter(Previous);
    if(!wcscmp(argv[1],L"recover"))SetUnhandledExceptionFilter(Recover);
    if(!wcscmp(argv[1],L"previous-exit"))SetUnhandledExceptionFilter(ExitBeforeReturn);
    if(!wcscmp(argv[1],L"previous-block"))SetUnhandledExceptionFilter(BlockBeforeReturn);
    if(!wcscmp(argv[1],L"block-recover"))SetUnhandledExceptionFilter(BlockThenRecover);
    if(!wcscmp(argv[1],L"delayed-recovery-twice")||!wcscmp(argv[1],L"skip-provisional")){DelayRecovery=!wcscmp(argv[1],L"delayed-recovery-twice");SetUnhandledExceptionFilter(TwoFaults);}
    if(!dfl::native_incidents::Initialize(argv[2],"fixture-session","fixture-version+commit"))return 3;
    if(original) {
        if(wcsstr(argv[1],L"bad")) {
            reinterpret_cast<void(WINAPI*)(DWORD)>(GetProcAddress(original,"BT_SetActivityType"))(4);
            if(reinterpret_cast<DWORD(WINAPI*)()>(GetProcAddress(original,"BT_GetActivityType"))()!=4)return 19;
        }
        else if(wcsstr(argv[1],L"bug"))reinterpret_cast<void(WINAPI*)()>(GetProcAddress(original,"BT_InstallSehFilter"))();
        else reinterpret_cast<void(__cdecl*)()>(GetProcAddress(original,"InitStackTraceLibrary"))();
        if(wcsstr(argv[1],L"privacy")) {
            reinterpret_cast<void(WINAPI*)(DWORD)>(GetProcAddress(original,"BT_SetActivityType"))(4);
            if(reinterpret_cast<DWORD(WINAPI*)()>(GetProcAddress(original,"BT_GetActivityType"))()!=5){fprintf(stderr,"legacy SENDREPORT was not suppressed\n");return 16;}
            WSADATA ws{};if(WSAStartup(MAKEWORD(2,2),&ws))return 17;
            const SOCKET socket=::socket(AF_INET,SOCK_STREAM,IPPROTO_TCP);sockaddr_in address{};address.sin_family=AF_INET;address.sin_addr.s_addr=htonl(INADDR_LOOPBACK);
            wchar_t port[16]{};GetEnvironmentVariableW(L"DFL_TEST_RECEIVER_PORT",port,16);address.sin_port=htons(static_cast<u_short>(wcstoul(port,nullptr,10)));
            const bool ok=connect(socket,reinterpret_cast<sockaddr*>(&address),sizeof(address))==0&&send(socket,"ordinary-game-socket",20,0)==20;closesocket(socket);WSACleanup();if(!ok)return 18;
        }
    }
    if(GetEnvironmentVariableW(L"DFL_DIAGNOSTIC_SESSION_DIR",nullptr,0)) {
        FILETIME created{},exit{},kernel{},user{};GetProcessTimes(GetCurrentProcess(),&created,&exit,&kernel,&user);
        printf("ready %lu %llu\n",GetCurrentProcessId(),((static_cast<unsigned long long>(created.dwHighDateTime)<<32)|created.dwLowDateTime)+504911232000000000ULL);fflush(stdout);
        (void)getchar();
    }
    if(faultModule){reinterpret_cast<void(__cdecl*)()>(GetProcAddress(faultModule,"FixtureFault"))();return 22;}
    if(!wcscmp(argv[1],L"recover")||!wcscmp(argv[1],L"previous-exit")||!wcscmp(argv[1],L"previous-block")||!wcscmp(argv[1],L"block-recover")){Fault();return 0;}
    if(!wcscmp(argv[1],L"delayed-recovery-twice")||!wcscmp(argv[1],L"skip-provisional")){Fault();puts("recovered");fflush(stdout);(void)getchar();SecondFault();return 20;}
    if(!wcscmp(argv[1],L"large-dump")){for(int i=0;i<24;++i){HANDLE thread=CreateThread(nullptr,2*1024*1024,LargeStack,nullptr,0,nullptr);if(!thread)return 15;CloseHandle(thread);}while(StackReady<24)Sleep(10);Fault();return 0;}
    if(original){if(wcsstr(argv[1],L"bad"))return 0;Fault();return 14;}
    if(!wcscmp(argv[1],L"overflow"))return Overflow(0);
    if(!wcscmp(argv[1],L"abnormal-exit"))return 92;
    if(!wcscmp(argv[1],L"normal"))return 0;
    if(!wcscmp(argv[1],L"later")){SetUnhandledExceptionFilter(Previous);Fault();return 4;}
    if(!wcscmp(argv[1],L"dialog")){auto window=CreateWindowExW(0,L"STATIC",L"CrashSender",WS_OVERLAPPEDWINDOW,0,0,200,100,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);if(!window)return 8;ShowWindow(window,SW_HIDE);Sleep(2000);DestroyWindow(window);return 93;}
    if(!wcscmp(argv[1],L"handled")) { __try { Fault(); } __except(EXCEPTION_EXECUTE_HANDLER) {} return 0; }
    if(!wcscmp(argv[1],L"crash")||!wcscmp(argv[1],L"previous")) { Fault(); return 4; }
    if(!wcscmp(argv[1],L"network-normal")){dfl::native_incidents::NetworkNotification(true,0);dfl::native_incidents::NetworkNotification(false,0,false);Sleep(150);return 0;}
    if(!wcscmp(argv[1],L"network"))
    {
        dfl::native_incidents::NetworkNotification(false,0);
        dfl::native_incidents::NetworkNotification(true,0);
        dfl::native_incidents::NetworkNotification(false,0,false);
        for(int i=0;i<20;++i)dfl::native_incidents::NetworkNotification(true,10061);
        dfl::native_incidents::NetworkNotification(false,0,true);
        return 0;
    }
    if(!wcscmp(argv[1],L"signature"))
        return dfl::native_incidents::InstallNetworkHooks(reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr))) ? 5 : 0;
    if(!wcscmp(argv[1],L"signature-match"))return SignatureFixture(0)?0:6;
    if(!wcscmp(argv[1],L"signature-mismatch"))return SignatureFixture(1)?0:7;
    if(!wcscmp(argv[1],L"signature-wrong-image"))return SignatureFixture(2)?0:8;
    if(!wcscmp(argv[1],L"signature-wrong-frame"))return SignatureFixture(3)?0:9;
    if(!wcscmp(argv[1],L"signature-wrong-arguments"))return SignatureFixture(4)?0:10;
    return 2;
}
