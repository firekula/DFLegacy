#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "native_capture.h"
#include <bcrypt.h>
#include <detours.h>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <string>

namespace dfl::native_capture
{
namespace {
Shared* Data=nullptr;
HANDLE Request=nullptr,Ack=nullptr;
volatile LONG Busy=0,Next=0;
using Filter=LONG (WINAPI*)(EXCEPTION_POINTERS*);
Filter BugTrap=nullptr,DumpReport=nullptr;
using ActivitySetter=void (WINAPI*)(DWORD);
ActivitySetter OriginalActivity=nullptr;
void WINAPI LocalOnlyActivity(DWORD) {OriginalActivity(5);}
bool Id(const char* text) { if(strlen(text)!=32)return false;for(unsigned i=0;i<32;++i)if(!((text[i]>='0'&&text[i]<='9')||(text[i]>='a'&&text[i]<='f')))return false;return true; }
bool Plain(const wchar_t* path)
{
    std::wstring copy(path);
    for(size_t i=0;i<=copy.size();++i)if(i==copy.size()||copy[i]==L'\\'||copy[i]==L'/') {
        const auto part=copy.substr(0,i);const DWORD a=GetFileAttributesW(part.c_str());if(a!=INVALID_FILE_ATTRIBUTES&&(a&FILE_ATTRIBUTE_REPARSE_POINT))return false;
    }
    return true;
}
void WaitAck(DWORD budget,LONG sequence)
{
    const auto until=GetTickCount64()+budget;
    while(InterlockedCompareExchange(&Data->Acknowledged,0,0)<sequence) {
        const auto now=GetTickCount64();if(now>=until)break;
        WaitForSingleObject(Ack,static_cast<DWORD>(until-now));
    }
}
LONG WINAPI BugFilter(EXCEPTION_POINTERS* e) {const LONG slot=Begin(e,true);const LONG result=BugTrap(e);Finish(slot,result);return result;}
LONG WINAPI DumpFilter(EXCEPTION_POINTERS* e) {const LONG slot=Begin(e,true);const LONG result=DumpReport(e);Finish(slot,result);return result;}
bool Hash(HMODULE module,const char* expected)
{
    wchar_t path[32768]{};if(!GetModuleFileNameW(module,path,32768)||!Plain(path))return false;
    HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT,nullptr);
    if(file==INVALID_HANDLE_VALUE)return false;
    BCRYPT_ALG_HANDLE algorithm{};BCRYPT_HASH_HANDLE hash{};unsigned char digest[32]{},buffer[16384]{};DWORD read{};
    bool ok=BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0&&BCryptCreateHash(algorithm,&hash,nullptr,0,nullptr,0,0)>=0;
    while(ok) {if(!ReadFile(file,buffer,sizeof(buffer),&read,nullptr)){ok=false;break;}if(!read)break;ok=BCryptHashData(hash,buffer,read,0)>=0;}
    if(ok)ok=BCryptFinishHash(hash,digest,sizeof(digest),0)>=0;
    if(hash)BCryptDestroyHash(hash);if(algorithm)BCryptCloseAlgorithmProvider(algorithm,0);CloseHandle(file);
    char hex[65]{};for(unsigned i=0;i<32;++i)sprintf_s(hex+i*2,3,"%02x",digest[i]);return ok&&!strcmp(hex,expected);
}
}
bool Initialize() noexcept
{
    if(Data)return true;
    // This guarantee applies to the entry thread. Other game threads still use
    // Windows' emergency stack; the actual fault path has no large locals.
    ULONG guarantee=32768;SetThreadStackGuarantee(&guarantee);
    Shared initial{};initial.MagicValue=Magic;initial.Size=sizeof(Shared);initial.Pid=GetCurrentProcessId();
    const DWORD directoryLength=GetEnvironmentVariableW(L"DFL_DIAGNOSTIC_SESSION_DIR",initial.Directory,1024);
    if(!directoryLength||directoryLength>=1024||GetEnvironmentVariableA("DFL_DIAGNOSTIC_SESSION_ID",initial.Session,33)!=32||GetEnvironmentVariableA("DFL_DIAGNOSTIC_CAPABILITY",initial.Capability,33)!=32||!Id(initial.Session)||!Id(initial.Capability))return false;
    wchar_t toggle[8]{};GetEnvironmentVariableW(L"DFL_DIAGNOSTIC_SMALL_DUMPS",toggle,8);initial.SmallDumps=wcscmp(toggle,L"0")!=0;
    FILETIME created{},exit{},kernel{},user{};if(!GetProcessTimes(GetCurrentProcess(),&created,&exit,&kernel,&user))return false;
    initial.CreatedTicks=((static_cast<std::uint64_t>(created.dwHighDateTime)<<32)|created.dwLowDateTime)+504911232000000000ULL;
    for(auto& id:initial.Ids) {unsigned char random[16]{};if(BCryptGenRandom(nullptr,random,16,BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0)return false;for(unsigned i=0;i<16;++i)sprintf_s(id+i*2,3,"%02x",random[i]);}
    wchar_t helper[32768]{};
    DWORD length=GetEnvironmentVariableW(L"DFL_DIAGNOSTIC_CAPTURE_HELPER",helper,32768);
    if(!length) {
        HMODULE module{};GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(&Initialize),&module);
        if(!GetModuleFileNameW(module,helper,32768))return false;auto* slash=wcsrchr(helper,L'\\');if(!slash)return false;
        wcscpy_s(slash+1,32768-static_cast<size_t>(slash+1-helper),L"DFLegacy.CrashCapture.exe");
    }
    if(length>=32768||!Plain(helper)||GetFileAttributesW(helper)==INVALID_FILE_ATTRIBUTES)return false;
    SECURITY_ATTRIBUTES security{sizeof(security),nullptr,TRUE};
    HANDLE mapping=CreateFileMappingW(INVALID_HANDLE_VALUE,&security,PAGE_READWRITE,0,sizeof(Shared),nullptr);
    Request=CreateEventW(&security,FALSE,FALSE,nullptr);Ack=CreateEventW(&security,FALSE,FALSE,nullptr);
    HANDLE process=OpenProcess(PROCESS_QUERY_INFORMATION|PROCESS_VM_READ|SYNCHRONIZE,TRUE,GetCurrentProcessId());
    Data=mapping?static_cast<Shared*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Shared))):nullptr;
    bool ok=Data&&Request&&Ack&&process;
    if(ok) {
        *Data=initial;
        STARTUPINFOEXW start{};start.StartupInfo.cb=sizeof(start);SIZE_T bytes{};
        InitializeProcThreadAttributeList(nullptr,1,0,&bytes);
        start.lpAttributeList=static_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(HeapAlloc(GetProcessHeap(),0,bytes));
        HANDLE handles[]{mapping,Request,Ack,process};
        ok=start.lpAttributeList&&InitializeProcThreadAttributeList(start.lpAttributeList,1,0,&bytes)&&UpdateProcThreadAttribute(start.lpAttributeList,0,PROC_THREAD_ATTRIBUTE_HANDLE_LIST,handles,sizeof(handles),nullptr,nullptr);
        if(ok) {
            std::wstring command=L"\""+std::wstring(helper)+L"\" --local-session";
            for(auto handle:handles)command+=L" "+std::to_wstring(reinterpret_cast<std::uintptr_t>(handle));
            const std::wstring helperPath=helper;const auto working=helperPath.substr(0,helperPath.find_last_of(L"\\/"));
            PROCESS_INFORMATION child{};ok=CreateProcessW(helper,command.data(),nullptr,nullptr,TRUE,EXTENDED_STARTUPINFO_PRESENT|CREATE_NO_WINDOW,nullptr,working.c_str(),&start.StartupInfo,&child)!=FALSE;
            if(ok) {CloseHandle(child.hThread);WaitForSingleObject(Ack,1000);ok=InterlockedCompareExchange(&Data->Ready,0,0)==1||WaitForSingleObject(child.hProcess,0)==WAIT_TIMEOUT;CloseHandle(child.hProcess);}
        }
        if(start.lpAttributeList){DeleteProcThreadAttributeList(start.lpAttributeList);HeapFree(GetProcessHeap(),0,start.lpAttributeList);}
    }
    // Inheritable handles exist only during this explicitly allowlisted spawn.
    for(auto handle:{mapping,Request,Ack,process})if(handle)SetHandleInformation(handle,HANDLE_FLAG_INHERIT,0);
    if(mapping)CloseHandle(mapping);if(process)CloseHandle(process);
    if(!ok){if(Data)UnmapViewOfFile(Data);Data=nullptr;if(Request)CloseHandle(Request);if(Ack)CloseHandle(Ack);Request=nullptr;Ack=nullptr;}
    return ok;
}
LONG Begin(EXCEPTION_POINTERS* e,bool originalEntry) noexcept
{
    if(!Data||!e||!e->ExceptionRecord||!e->ContextRecord)return -1;
    // Busy owns an odd sequence, not just a boolean. A later entry may rearm
    // an acknowledged cancellation after Finish's deadline, but cannot clear
    // another thread's newly acquired slot using stale cancellation evidence.
    const LONG owner=InterlockedCompareExchange(&Busy,-1,0);
    if(owner!=0) {
        if(owner<0)return -1;
        const LONG sequence=InterlockedCompareExchange(&Data->Sequence,0,0);
        if(sequence!=owner+1||InterlockedCompareExchange(&Data->StateValue,0,0)!=Cancelled||InterlockedCompareExchange(&Data->Acknowledged,0,0)<sequence||InterlockedCompareExchange(&Busy,-1,owner)!=owner)return -1;
    }
    const LONG slot=InterlockedIncrement(&Next)-1;
    if(slot>=7){InterlockedExchange(&Busy,-2);return -1;} // Saturated; slot 7 belongs to window-only evidence.
    Data->Started=GetTickCount64();Data->ThreadId=GetCurrentThreadId();Data->OriginalEntry=originalEntry?1:0;
    Data->Exception=*e->ExceptionRecord;Data->Exception.ExceptionRecord=nullptr;Data->Context=*e->ContextRecord;
    InterlockedExchange(&Data->StateValue,Provisional);InterlockedExchange(&Busy,slot*2+1);InterlockedExchange(&Data->Sequence,slot*2+1);SetEvent(Request);
    WaitAck(Data->SmallDumps?4500:1750,slot*2+1);return slot;
}
void Finish(LONG slot,LONG disposition) noexcept
{
    if(slot<0||!Data)return;
    InterlockedExchange(&Data->StateValue,disposition==EXCEPTION_CONTINUE_EXECUTION?Cancelled:Confirmed);
    InterlockedExchange(&Data->Sequence,slot*2+2);SetEvent(Request);
    const auto elapsed=GetTickCount64()-Data->Started;const DWORD total=Data->SmallDumps?5000:2000;
    if(elapsed<total)WaitAck(static_cast<DWORD>(total-elapsed),slot*2+2);
    // Only reuse a slot after cancellation has durably reached the helper.
    if(disposition==EXCEPTION_CONTINUE_EXECUTION&&Data->Acknowledged>=slot*2+2)InterlockedCompareExchange(&Busy,0,slot*2+1);
}
void NetworkNotification(const char* id,bool connecting,unsigned socketError) noexcept
{
    // Called under the existing notification producer lock, never the fault lock.
    if(!Data||!Id(id)||socketError>65535)return;
    const LONG slot=InterlockedCompareExchange(&Data->NetworkCount,0,0);
    if(slot<0||slot>=16)return;
    auto& record=Data->Network[slot];memcpy(record.Id,id,33);record.Connecting=connecting?1:0;record.SocketError=socketError;
    InterlockedExchange(&Data->NetworkCount,slot+1);SetEvent(Request);
}
bool InstallOriginalHooks() noexcept
{
    HMODULE bug=GetModuleHandleW(L"BugTrap.dll"),dump=GetModuleHandleW(L"DumpReport.dll");
    Filter bugEntry=nullptr,dumpEntry=nullptr;ActivitySetter activity=nullptr;
    if(bug&&Hash(bug,"be56494fefcd27a4cfa9caa668f8ffe6e6bbd92c9ddb83eabf5b0e14b12a8875")) {
        auto* entry=reinterpret_cast<unsigned char*>(GetProcAddress(bug,"BT_SehFilter"));
        const unsigned char signature[]{0x8b,0x4c,0x24,0x04,0x6a,0x01,0xe8,0x72,0xff,0xff,0xff,0x59,0xc2,0x04,0x00};
        auto* setter=reinterpret_cast<unsigned char*>(GetProcAddress(bug,"BT_SetActivityType"));
        const unsigned char setterPrefix[]{0x8b,0x44,0x24,0x04,0xa3};DWORD global{};
        if(setter)memcpy(&global,setter+5,4);
        if(entry==reinterpret_cast<unsigned char*>(bug)+0x11ce1&&!memcmp(entry,signature,sizeof(signature))&&setter==reinterpret_cast<unsigned char*>(bug)+0x103c8&&!memcmp(setter,setterPrefix,sizeof(setterPrefix))&&global==reinterpret_cast<std::uintptr_t>(bug)+0x36098&&setter[9]==0xc2&&setter[10]==4&&setter[11]==0) {
            bugEntry=reinterpret_cast<Filter>(entry);activity=reinterpret_cast<ActivitySetter>(setter);
        }
    }
    if(dump&&Hash(dump,"f9962904a1d8c6c09c7df5186dc36e46795108d4cf54e0481c592616897afccb")) {
        auto* entry=reinterpret_cast<unsigned char*>(dump)+0x10d0;
        const unsigned char prefix[]{0x55,0x8b,0xec,0xa1};DWORD global{};memcpy(&global,entry+4,4);
        if(!memcmp(entry,prefix,sizeof(prefix))&&global==reinterpret_cast<std::uintptr_t>(dump)+0x3018&&entry[8]==0x50&&entry[9]==0xff&&entry[10]==0x15)dumpEntry=reinterpret_cast<Filter>(entry);
    }
    if(!bugEntry&&!dumpEntry)return false;
    if(DetourTransactionBegin()!=NO_ERROR)return false;
    DetourUpdateThread(GetCurrentThread());BugTrap=bugEntry;DumpReport=dumpEntry;OriginalActivity=activity;
    LONG error=NO_ERROR;if(BugTrap)error=DetourAttach(reinterpret_cast<PVOID*>(&BugTrap),BugFilter);
    if(error==NO_ERROR&&DumpReport)error=DetourAttach(reinterpret_cast<PVOID*>(&DumpReport),DumpFilter);
    if(error==NO_ERROR&&OriginalActivity)error=DetourAttach(reinterpret_cast<PVOID*>(&OriginalActivity),LocalOnlyActivity);
    if(error!=NO_ERROR){DetourTransactionAbort();BugTrap=nullptr;DumpReport=nullptr;OriginalActivity=nullptr;return false;}
    if(DetourTransactionCommit()!=NO_ERROR){BugTrap=nullptr;DumpReport=nullptr;OriginalActivity=nullptr;return false;}
    // Original RVA1367a dispatches only activities1(UI),3(mail),4(network).
    // 5 skips that dispatch. Keep the original filter/disposition, but prevent
    // later DNF setup from re-enabling its historical raw-report transport.
    if(OriginalActivity)OriginalActivity(5);
    return true;
}
}
