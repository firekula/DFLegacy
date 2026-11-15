#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <algorithm>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <string>
#include <vector>
#include "native_incidents.h"
#include "native_capture.h"
#include "memory_patch.h"
#include "../../DFLegacy.Client/client_paths.h"

namespace
{
    wchar_t Directory[32768]{};
    char Session[128]{},Version[256]{},Pvf[65]{};
    FILETIME Started{};
    LPTOP_LEVEL_EXCEPTION_FILTER Previous=nullptr;
    volatile LONG Busy=0,Next=0;
    volatile LONG HandlerBusy=0;
    bool ExternalCapture=false;
    HANDLE LegacyRequest=nullptr,LegacyDone=nullptr;
    EXCEPTION_RECORD LegacyException{};
    CONTEXT LegacyContext{};
    struct Entry { bool Used{},Connect{}; unsigned Code{},Count{}; wchar_t Path[32768]{}; char Id[33]{}; };
    Entry Entries[16]{};
    char Body[16384]{};
    bool PlainPath(const wchar_t* path)
    {
        wchar_t copy[32768]{};if(wcscpy_s(copy,path))return false;
        for(auto* p=copy;*p;++p)if(*p==L'\\'||*p==L'/') { auto c=*p;*p=0; const auto a=GetFileAttributesW(copy);*p=c;if(a!=INVALID_FILE_ATTRIBUTES&&(a&FILE_ATTRIBUTE_REPARSE_POINT))return false; }
        const auto a=GetFileAttributesW(path);return a==INVALID_FILE_ATTRIBUTES||!(a&FILE_ATTRIBUTE_REPARSE_POINT);
    }
    void Append(const char* format,...)
    {
        const auto n=strlen(Body);if(n+1>=sizeof(Body))return;
        va_list args;va_start(args,format);_vsnprintf_s(Body+n,sizeof(Body)-n,_TRUNCATE,format,args);va_end(args);
    }
    void Header(const char* evidence,Entry& entry)
    {
        SYSTEMTIME now{};GetLocalTime(&now);TIME_ZONE_INFORMATION zone{};const DWORD z=GetTimeZoneInformation(&zone);
        const LONG bias=zone.Bias+(z==TIME_ZONE_ID_DAYLIGHT?zone.DaylightBias:zone.StandardBias);
        Body[0]=0;
        Append("format=DFLegacy-incident-1\nevidence=%s\nsessionId=%s\neventId=%s\nversion=%s\narchitecture=x86\npid=%lu\nthreadId=%lu\ntime=%04u-%02u-%02uT%02u:%02u:%02u.%03u\ntimezoneOffsetMinutes=%ld\noccurrences=%u\nserverVersion=unknown\n",evidence,Session,entry.Id,Version,GetCurrentProcessId(),GetCurrentThreadId(),now.wYear,now.wMonth,now.wDay,now.wHour,now.wMinute,now.wSecond,now.wMilliseconds,-bias,entry.Count);
        Append("clientPvfSha256=%s\n",Pvf);
        wchar_t exe[32768]{};char utf8[4096]{};GetModuleFileNameW(nullptr,exe,32768);if(auto* slash=wcsrchr(exe,L'\\'))*slash=0;
        WideCharToMultiByte(CP_UTF8,0,exe,-1,utf8,sizeof(utf8),nullptr,nullptr);Append("installation=%s\n",utf8);
    }
    void Name(Entry& entry,const wchar_t* kind)
    {
        SYSTEMTIME now{};GetLocalTime(&now);
        sprintf_s(entry.Id,"%08lX%08lX%08lX%08lX",Started.dwHighDateTime,Started.dwLowDateTime,GetCurrentProcessId(),static_cast<unsigned long>(InterlockedIncrement(&Next)));
        for(auto& ch:entry.Id)if(ch>='A'&&ch<='F')ch=static_cast<char>(ch-'A'+'a');
        swprintf_s(entry.Path,L"%s\\%s-%04u%02u%02u-%02u%02u%02u-%03u-%lu-%hs.log",Directory,kind,now.wYear,now.wMonth,now.wDay,now.wHour,now.wMinute,now.wSecond,now.wMilliseconds,GetCurrentProcessId(),entry.Id);
    }
    void Save(Entry& entry)
    {
        if(!PlainPath(entry.Path))return;
        const HANDLE file=CreateFileW(entry.Path,GENERIC_WRITE,FILE_SHARE_READ,nullptr,entry.Count==1?CREATE_NEW:OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_OPEN_REPARSE_POINT,nullptr);
        if(file==INVALID_HANDLE_VALUE)return;
        FILE_ATTRIBUTE_TAG_INFO tag{};
        if(GetFileInformationByHandleEx(file,FileAttributeTagInfo,&tag,sizeof(tag))&&!(tag.FileAttributes&FILE_ATTRIBUTE_REPARSE_POINT))
        { DWORD written{};WriteFile(file,Body,static_cast<DWORD>(strlen(Body)),&written,nullptr);SetEndOfFile(file);FlushFileBuffers(file); }
        CloseHandle(file);
    }
    void SaveLegacy(EXCEPTION_POINTERS* exception)
    {
        if(InterlockedCompareExchange(&Busy,1,0))return;
        static Entry crash{};crash.Count=1;Name(crash,L"crash");Header("unhandled-exception-filter",crash);
        const auto& c=*exception->ContextRecord;const auto& e=*exception->ExceptionRecord;
        Append("exceptionCode=0x%08lX\nexceptionAddress=%p\nregister.eip=%08lX\nregister.esp=%08lX\nregister.ebp=%08lX\nregister.eax=%08lX\nregister.ebx=%08lX\nregister.ecx=%08lX\nregister.edx=%08lX\nregister.esi=%08lX\nregister.edi=%08lX\nregister.eflags=%08lX\n",e.ExceptionCode,e.ExceptionAddress,c.Eip,c.Esp,c.Ebp,c.Eax,c.Ebx,c.Ecx,c.Edx,c.Esi,c.Edi,c.EFlags);
        MEMORY_BASIC_INFORMATION memory{};char module[4096]{};wchar_t moduleWide[1024]{};
        if(VirtualQuery(e.ExceptionAddress,&memory,sizeof(memory))&&memory.Type==MEM_IMAGE)
        {
            GetModuleFileNameW(static_cast<HMODULE>(memory.AllocationBase),moduleWide,1024);
            WideCharToMultiByte(CP_UTF8,0,moduleWide,-1,module,sizeof(module),nullptr,nullptr);
        }
        Append("module=%s\nmoduleBase=%p\nmoduleOffset=0x%08lX\nstack.return[0]=%08lX\n",module,memory.AllocationBase,static_cast<DWORD>(reinterpret_cast<std::uintptr_t>(e.ExceptionAddress)-reinterpret_cast<std::uintptr_t>(memory.AllocationBase)),c.Eip);
        // Read only frame return addresses, never raw stack contents or payloads.
        DWORD frame=c.Ebp;
        for(unsigned i=1;i<32;++i) { DWORD pair[2]{};SIZE_T read{};if(frame<c.Esp||frame-c.Esp>1024*1024||!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(frame),pair,sizeof(pair),&read)||read!=sizeof(pair)||pair[0]<=frame)break;Append("stack.return[%u]=%08lX\n",i,pair[1]);frame=pair[0]; }
        Save(crash);InterlockedExchange(&Busy,0);
    }
    DWORD WINAPI LegacyWriter(void*)
    {
        for(;;){WaitForSingleObject(LegacyRequest,INFINITE);EXCEPTION_POINTERS pointers{&LegacyException,&LegacyContext};SaveLegacy(&pointers);SetEvent(LegacyDone);}
    }
    LONG WINAPI Unhandled(EXCEPTION_POINTERS* exception)
    {
        // Guard before the previous chain: original filters may chain back here.
        if(InterlockedCompareExchange(&HandlerBusy,1,0))return EXCEPTION_CONTINUE_SEARCH;
        const LONG slot=dfl::native_capture::Begin(exception,false);
        const LONG disposition=Previous?Previous(exception):EXCEPTION_CONTINUE_SEARCH;
        dfl::native_capture::Finish(slot,disposition);
        if(!ExternalCapture&&disposition!=EXCEPTION_CONTINUE_EXECUTION&&exception&&exception->ExceptionRecord&&exception->ContextRecord&&LegacyRequest) {
            LegacyException=*exception->ExceptionRecord;LegacyException.ExceptionRecord=nullptr;LegacyContext=*exception->ContextRecord;
            SetEvent(LegacyRequest);WaitForSingleObject(LegacyDone,1750);
        }
        InterlockedExchange(&HandlerBusy,0);return disposition;
    }
    constexpr std::uintptr_t Notice=0x00799EF0;
    void* OriginalNotice=reinterpret_cast<void*>(Notice);
    void __cdecl Notify(unsigned connect,unsigned error,unsigned visible) { dfl::native_incidents::NetworkNotification(connect!=0,error,visible!=0); }
    __declspec(naked) void ConnectHook()
    {
        __asm {
            pushfd
            pushad
            mov eax,[ebp+14h]
            shr eax,10h
            push 0
            push eax
            push 1
            call Notify
            add esp,12
            popad
            popfd
            jmp dword ptr [OriginalNotice]
        }
    }
    __declspec(naked) void DisconnectHook()
    {
        __asm {
            pushfd
            pushad
            mov eax,[ebp+14h]
            shr eax,10h
            push 1
            push eax
            push 0
            call Notify
            add esp,12
            popad
            popfd
            jmp dword ptr [OriginalNotice]
        }
    }
    template<std::size_t N> bool Match(std::uintptr_t address,const std::array<unsigned char,N>& bytes)
    { unsigned char current[N]{};SIZE_T read{};return ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(address),current,N,&read)&&read==N&&!memcmp(current,bytes.data(),N); }
    void Retain()
    {
        struct File { std::wstring Path; ULONGLONG Time,Size; };
        std::vector<File> files;WIN32_FIND_DATAW data{};wchar_t search[32768]{};swprintf_s(search,L"%s\\*.log",Directory);
        HANDLE find=FindFirstFileW(search,&data);if(find==INVALID_HANDLE_VALUE)return;
        do {
            if((data.dwFileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT))||
                (wcsncmp(data.cFileName,L"crash-",6)&&wcsncmp(data.cFileName,L"networkerror-",13)))continue;
            // Only our exact event filename shape; never sweep unrelated logs.
            const auto* id=wcsrchr(data.cFileName,L'-');if(!id||wcslen(id)!=37||wcscmp(id+33,L".log"))continue;
            bool valid=true;for(unsigned i=1;i<=32;++i)if(!((id[i]>=L'0'&&id[i]<=L'9')||(id[i]>=L'a'&&id[i]<=L'f')))valid=false;if(!valid)continue;
            files.push_back({std::wstring(Directory)+L"\\"+data.cFileName,(static_cast<ULONGLONG>(data.ftLastWriteTime.dwHighDateTime)<<32)|data.ftLastWriteTime.dwLowDateTime,(static_cast<ULONGLONG>(data.nFileSizeHigh)<<32)|data.nFileSizeLow});
        }while(FindNextFileW(find,&data));FindClose(find);
        std::sort(files.begin(),files.end(),[](const File& a,const File& b){return a.Time>b.Time;});FILETIME now{};GetSystemTimeAsFileTime(&now);const auto ticks=(static_cast<ULONGLONG>(now.dwHighDateTime)<<32)|now.dwLowDateTime;ULONGLONG size=0;
        for(std::size_t i=0;i<files.size();++i){size+=files[i].Size;if(i>=64||size>4*1024*1024||ticks-files[i].Time>14ULL*24*3600*10000000)DeleteFileW(files[i].Path.c_str());}
    }
}
namespace dfl::native_incidents
{
    bool Initialize(const wchar_t* directory,const char* session,const char* version) noexcept
    {
        if(Directory[0])return true;
        if(!directory||!PlainPath(directory)||wcscpy_s(Directory,directory))return false;
        if(!CreateDirectoryW(Directory,nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS){Directory[0]=0;return false;}
        strncpy_s(Session,session?session:"unknown",_TRUNCATE);strncpy_s(Version,version?version:"unknown",_TRUNCATE);
        if(GetEnvironmentVariableA("DFLEGACY_DIAGNOSTIC_PVF",Pvf,sizeof(Pvf))!=64)strcpy_s(Pvf,"unknown");
        else for(unsigned i=0;i<64;++i)if(!((Pvf[i]>='0'&&Pvf[i]<='9')||(Pvf[i]>='a'&&Pvf[i]<='f'))){strcpy_s(Pvf,"unknown");break;}
        FILETIME exit{},kernel{},user{};GetProcessTimes(GetCurrentProcess(),&Started,&exit,&kernel,&user);
        try { Retain(); } catch(...) { /* Optional retention must not block gameplay. */ }
        ExternalCapture=dfl::native_capture::Initialize();
        if(!ExternalCapture){LegacyRequest=CreateEventW(nullptr,FALSE,FALSE,nullptr);LegacyDone=CreateEventW(nullptr,FALSE,FALSE,nullptr);if(LegacyRequest&&LegacyDone){const auto worker=CreateThread(nullptr,0,LegacyWriter,nullptr,0,nullptr);if(worker)CloseHandle(worker);}}
        Previous=SetUnhandledExceptionFilter(Unhandled);
        dfl::native_capture::InstallOriginalHooks();return true;
    }
    void NetworkNotification(bool connecting,unsigned socketError,bool visibleDisconnect) noexcept
    {
        if(!Directory[0]||(connecting?socketError==0:!visibleDisconnect)||InterlockedCompareExchange(&Busy,1,0))return;
        Entry* entry=nullptr;
        for(auto& e:Entries)if(e.Used&&e.Connect==connecting&&e.Code==socketError){entry=&e;break;}
        if(!entry)for(auto& e:Entries)if(!e.Used){e.Used=true;e.Connect=connecting;e.Code=socketError;entry=&e;Name(e,L"networkerror");break;}
        if(entry&&entry->Count<1000)
        {
            if(entry->Count==0)dfl::native_capture::NetworkNotification(entry->Id,connecting,socketError);
            ++entry->Count;Header(connecting?"native-connect-failure-notification":"native-disconnect-notification",*entry);
            Append("protocol=TCP\ndirection=server->client\nphase=%s\nsocketError=%u\nport=unknown\nconnectionId=unknown\nelapsedMs=unknown\nclientToServerBytes=unknown\nserverToClientBytes=unknown\ncontext=ACT1 validated native user-visible notification; no packet content captured\n",connecting?"connect":"disconnect",socketError);Save(*entry);
        }
        InterlockedExchange(&Busy,0);
    }
    bool InstallNetworkHooks(std::uintptr_t image) noexcept
    {
        // Exact original ACT1 branches: FD_CONNECT high-word error is nonzero;
        // FD_CLOSE reaches the call only when the native message is nonempty.
        // The scene-5 silent channel handoff branches around this call.
        if(!image||image>UINT32_MAX-0x4B2D00)return false;
        IMAGE_DOS_HEADER dos{};IMAGE_NT_HEADERS32 nt{};SIZE_T read{};
        if(!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(image),&dos,sizeof(dos),&read)||read!=sizeof(dos)||dos.e_magic!=IMAGE_DOS_SIGNATURE||dos.e_lfanew<64||dos.e_lfanew>4096)return false;
        if(!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(image+dos.e_lfanew),&nt,sizeof(nt),&read)||read!=sizeof(nt)||nt.Signature!=IMAGE_NT_SIGNATURE||nt.FileHeader.Machine!=IMAGE_FILE_MACHINE_I386||nt.OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR32_MAGIC||nt.OptionalHeader.ImageBase!=0x400000||nt.OptionalHeader.SizeOfImage<0x4B2D00)return false;
        constexpr std::array<unsigned char,37> connectGuard{0xC1,0xE9,0x10,0x85,0xC9,0x0F,0x84,0xFB,0x01,0x00,0x00,0x6A,0x00,0x6A,0x01,0x68,0x95,0x00,0x00,0x00,0xE8,0x52,0x7A,0xFF,0xFF,0x8B,0x0D,0xE8,0xDC,0xB5,0x00,0x83,0xC4,0x04,0x50,0x6A,0x23};
        constexpr std::array<unsigned char,26> closeGuard{0x84,0xC0,0x74,0x1B,0x39,0x5D,0xE4,0x8B,0x45,0xD0,0x73,0x03,0x8D,0x45,0xD0,0x8B,0x0D,0xE8,0xDC,0xB5,0x00,0x57,0x56,0x50,0x6A,0x23};
        constexpr std::array<unsigned char,5> connect{0xE8,0x91,0x73,0xEE,0xFF},close{0xE8,0xF4,0x71,0xEE,0xFF};
        constexpr std::array<unsigned char,9> notice{0x55,0x8B,0xEC,0x83,0xEC,0x08,0x8B,0x55,0x08};
        constexpr std::array<unsigned char,10> entry{0x55,0x8B,0xEC,0x6A,0xFF,0x68,0xE6,0xD1,0x98,0x00};
        if(!Match(image+0x4B2880,entry)||!Match(image+0x4B2B35,connectGuard)||!Match(image+0x4B2CDD,closeGuard)||!Match(image+0x399EF0,notice)||!Match(image+0x4B2B5A,connect)||!Match(image+0x4B2CF7,close))return false;
        const auto write=[](std::uintptr_t at,std::uintptr_t target){unsigned char bytes[5]{0xE8};const auto delta=static_cast<std::uint32_t>(target-at-5);memcpy(bytes+1,&delta,4);return memory_patch::WriteBytes(at,bytes,5);};
        if(write(image+0x4B2B5A,reinterpret_cast<std::uintptr_t>(ConnectHook))&&write(image+0x4B2CF7,reinterpret_cast<std::uintptr_t>(DisconnectHook))){OriginalNotice=reinterpret_cast<void*>(image+0x399EF0);return true;}
        memory_patch::WriteBytes(image+0x4B2B5A,connect.data(),5);memory_patch::WriteBytes(image+0x4B2CF7,close.data(),5);return false;
    }
    void Install() noexcept
    {
        wchar_t directory[32768]{};char session[128]{},version[256]{};
        if(!client_paths::BuildLogPath(L"Incidents",directory,32768))return;
        GetEnvironmentVariableA("DFLEGACY_DIAGNOSTIC_SESSION",session,sizeof(session));GetEnvironmentVariableA("DFLEGACY_DIAGNOSTIC_VERSION",version,sizeof(version));
        if(!Initialize(directory,session[0]?session:"native-standalone",version[0]?version:"unknown"))return;
        const auto image=reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
        if(image!=0x400000||!InstallNetworkHooks(image))OutputDebugStringA("DFLegacy: native network signatures unsupported; no direct-network hook installed.\n");
    }
}
