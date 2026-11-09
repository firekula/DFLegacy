// Default local capture is independent of the explicit debugger in main.cpp.
// Unnamed inherited objects are capabilities, passed with an exact handle list.
#include <windows.h>
#include <dbghelp.h>
#include <psapi.h>
#include <shlobj.h>
#include <algorithm>
#include <cstdio>
#include <cwchar>
#include <string>
#include <vector>
#include <map>
#include <bcrypt.h>
#include <mutex>
#include <memory>
#include "../../../src/DFLegacy.Ijl15/src/native_capture.h"
std::wstring LocalCaptureRoot();
decltype(&MiniDumpWriteDump) LocalCaptureDumpWriter();

namespace {
using dfl::native_capture::Shared;
using namespace dfl::native_capture;
constexpr ULONGLONG MaxDump=16ULL*1024*1024;
bool Plain(const std::wstring& path)
{
    for(size_t i=0;i<=path.size();++i)if(i==path.size()||path[i]==L'\\'||path[i]==L'/') {
        const DWORD a=GetFileAttributesW(path.substr(0,i).c_str());if(a!=INVALID_FILE_ATTRIBUTES&&(a&FILE_ATTRIBUTE_REPARSE_POINT))return false;
    }return true;
}
struct Handle {
    HANDLE value=INVALID_HANDLE_VALUE;
    explicit Handle(HANDLE h=INVALID_HANDLE_VALUE):value(h){}
    ~Handle(){if(value&&value!=INVALID_HANDLE_VALUE)CloseHandle(value);}
    Handle(const Handle&)=delete;Handle& operator=(const Handle&)=delete;
    explicit operator bool()const{return value&&value!=INVALID_HANDLE_VALUE;}
};
bool Id(const char* p) {for(unsigned i=0;i<32;++i)if(!((p[i]>='0'&&p[i]<='9')||(p[i]>='a'&&p[i]<='f')))return false;return p[32]==0;}
std::wstring Wide(const char* s){std::wstring result;for(;*s;++s)result+=static_cast<unsigned char>(*s);return result;}
bool SameUser(HANDLE process)
{
    HANDLE target{},self{};if(!OpenProcessToken(process,TOKEN_QUERY,&target)||!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&self)){if(target)CloseHandle(target);return false;}
    Handle a(target),b(self);BYTE first[1024]{},second[1024]{};DWORD need{};
    return GetTokenInformation(target,TokenUser,first,sizeof(first),&need)&&GetTokenInformation(self,TokenUser,second,sizeof(second),&need)&&EqualSid(reinterpret_cast<TOKEN_USER*>(first)->User.Sid,reinterpret_cast<TOKEN_USER*>(second)->User.Sid);
}
bool Identity(const Shared& data,HANDLE process)
{
    FILETIME created{},exit{},kernel{},user{};
    return data.MagicValue==Magic&&data.Size==sizeof(Shared)&&GetProcessId(process)==data.Pid&&GetProcessTimes(process,&created,&exit,&kernel,&user)&&
        ((static_cast<std::uint64_t>(created.dwHighDateTime)<<32)|created.dwLowDateTime)+504911232000000000ULL==data.CreatedTicks;
}
// session.json has only scalar root fields. Parse all members (including JSON
// escapes); reject duplicates/nesting/trailing content instead of substring matching.
bool Scalars(const std::string& text,std::map<std::string,std::string>& values)
{
    size_t at=0;const auto space=[&](){while(at<text.size()&&(text[at]==' '||text[at]=='\r'||text[at]=='\n'||text[at]=='\t'))++at;};
    const auto string=[&](std::string& out) {
        if(at>=text.size()||text[at++]!='"')return false;
        while(at<text.size()) {unsigned char c=text[at++];if(c=='"')return true;if(c<32)return false;if(c=='\\') {
            if(at>=text.size())return false;c=text[at++];
            if(c=='u'){if(at+4>text.size())return false;for(unsigned i=0;i<4;++i)if(!isxdigit(static_cast<unsigned char>(text[at+i])))return false;at+=4;out+='?';continue;}
            if(c!='"'&&c!='\\'&&c!='/'&&c!='b'&&c!='f'&&c!='n'&&c!='r'&&c!='t')return false;
        }out+=static_cast<char>(c);}return false;
    };
    space();if(at>=text.size()||text[at++]!='{')return false;space();
    while(at<text.size()&&text[at]!='}') {
        std::string key,value;if(!string(key))return false;space();if(at>=text.size()||text[at++]!=':')return false;space();
        if(at<text.size()&&text[at]=='"'){if(!string(value))return false;}
        else {const size_t begin=at;while(at<text.size()&&text[at]!=','&&text[at]!='}'&&text[at]!=' '&&text[at]!='\r'&&text[at]!='\n'&&text[at]!='\t')++at;value=text.substr(begin,at-begin);if(value.empty()||value.find_first_not_of("0123456789-truefalsn")!=std::string::npos)return false;}
        if(!values.emplace(key,value).second)return false;space();if(at<text.size()&&text[at]==','){++at;space();if(at>=text.size()||text[at]=='}')return false;}else break;
    }
    if(at>=text.size()||text[at++]!='}')return false;space();return at==text.size();
}
bool Registration(const Shared& data,bool requireBound)
{
    const std::wstring path=std::wstring(data.Directory)+L"\\session.json";if(!Plain(path))return false;
    Handle file(CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT,nullptr));if(!file)return false;
    LARGE_INTEGER size{};if(!GetFileSizeEx(file.value,&size)||size.QuadPart<=0||size.QuadPart>16384)return false;
    std::string bytes(static_cast<size_t>(size.QuadPart),'\0');DWORD read{};if(!ReadFile(file.value,bytes.data(),static_cast<DWORD>(bytes.size()),&read,nullptr)||read!=bytes.size())return false;
    std::map<std::string,std::string> fields;if(!Scalars(bytes,fields)||fields["sessionId"]!=data.Session||fields["capability"]!=data.Capability)return false;
    return !requireBound||(fields["gamePid"]==std::to_string(data.Pid)&&fields["gameCreatedUtcTicks"]==std::to_string(data.CreatedTicks));
}
bool Location(const Shared& data,std::wstring& root)
{
    if(!Id(data.Session)||!Id(data.Capability)||!wmemchr(data.Directory,0,1024))return false;
    for(const auto& id:data.Ids)if(!Id(id))return false;
    root=LocalCaptureRoot();if(root.empty())return false;
    const std::wstring prefix=root+L"\\sessions\\",directory=data.Directory;
    if(directory.size()!=prefix.size()+32||_wcsnicmp(directory.c_str(),prefix.c_str(),prefix.size())||!Plain(directory))return false;
    for(size_t i=prefix.size();i<directory.size();++i)if(!((directory[i]>=L'0'&&directory[i]<=L'9')||(directory[i]>=L'a'&&directory[i]<=L'f')))return false;
    return Registration(data,false);
}
std::string Module(HANDLE process,DWORD address,std::string& offset)
{
    MEMORY_BASIC_INFORMATION memory{};wchar_t path[1024]{};
    if(!VirtualQueryEx(process,reinterpret_cast<void*>(address),&memory,sizeof(memory))||memory.Type!=MEM_IMAGE||!GetMappedFileNameW(process,reinterpret_cast<void*>(address),path,1024)){offset="unknown";return "unknown";}
    const auto* leaf=wcsrchr(path,L'\\');if(!leaf){offset="unknown";return "unknown";}++leaf;std::string result;
    while(*leaf&&result.size()<128){const wchar_t c=*leaf++;if(!((c>=L'a'&&c<=L'z')||(c>=L'A'&&c<=L'Z')||(c>=L'0'&&c<=L'9')||c==L'_'||c==L'.'||c==L'-')){offset="unknown";return "unknown";}result+=static_cast<char>(c);}
    if(*leaf||result.empty()){offset="unknown";return "unknown";}char hex[32]{};sprintf_s(hex,"0x%lX",address-static_cast<DWORD>(reinterpret_cast<std::uintptr_t>(memory.AllocationBase)));offset=hex;return result;
}
// Hash image files on a separate best-effort worker. Fault handling only
// performs a try-lock lookup, so slow disk/AV cannot extend its 2s/5s budget.
// The read handle denies replacement for the lifetime of the cached identity.
struct CachedModule {DWORD Base;std::string Hash;std::wstring MappedPath;HANDLE File;};
struct ModuleCache {std::mutex Gate;std::vector<CachedModule> Items;~ModuleCache(){for(const auto& item:Items)CloseHandle(item.File);}};
struct HashWork {HANDLE Process;std::shared_ptr<ModuleCache> Cache;};
DWORD WINAPI CacheModules(void* parameter)
{
    std::unique_ptr<HashWork> work(static_cast<HashWork*>(parameter));Handle process(work->Process);
    HMODULE modules[128]{};DWORD bytes{};const auto until=GetTickCount64()+1500;ULONGLONG total=0;
    if(!EnumProcessModules(process.value,modules,sizeof(modules),&bytes))return 0;
    const auto count=std::min<DWORD>(bytes/sizeof(HMODULE),128);
    for(DWORD i=0;i<count&&GetTickCount64()<until;++i) {
        wchar_t path[1024]{};if(!GetModuleFileNameExW(process.value,modules[i],path,1024)||!Plain(path))continue;
        // Own patch libraries and the main executable are sufficient to match
        // our private symbols; never inventory unrelated modules on the wire.
        const auto* leaf=wcsrchr(path,L'\\');leaf=leaf?leaf+1:path;
        if(i!=0&&_wcsicmp(leaf,L"DFLegacy.Client.dll")&&_wcsicmp(leaf,L"ijl15.dll")&&_wcsicmp(leaf,L"debug.dll")&&_wcsicmp(leaf,L"original_game_probe.dll"))continue;
        Handle file(CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT,nullptr));LARGE_INTEGER size{};
        if(!file||!GetFileSizeEx(file.value,&size)||size.QuadPart<=0||size.QuadPart>32*1024*1024||total+size.QuadPart>64*1024*1024)continue;
        total+=size.QuadPart;BCRYPT_ALG_HANDLE algorithm{};BCRYPT_HASH_HANDLE hash{};BYTE digest[32]{},buffer[65536]{};DWORD read{};ULONGLONG consumed=0;
        bool ok=BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0&&BCryptCreateHash(algorithm,&hash,nullptr,0,nullptr,0,0)>=0;
        while(ok&&consumed<static_cast<ULONGLONG>(size.QuadPart)) {
            if(GetTickCount64()>=until||!ReadFile(file.value,buffer,sizeof(buffer),&read,nullptr)||!read){ok=false;break;}
            consumed+=read;ok=BCryptHashData(hash,buffer,read,0)>=0;
        }
        if(ok)ok=consumed==static_cast<ULONGLONG>(size.QuadPart)&&BCryptFinishHash(hash,digest,sizeof(digest),0)>=0;
        if(hash)BCryptDestroyHash(hash);if(algorithm)BCryptCloseAlgorithmProvider(algorithm,0);
        if(ok) {
            wchar_t mapped[1024]{};if(!GetMappedFileNameW(process.value,modules[i],mapped,1024))continue;
            char hex[65]{};for(unsigned n=0;n<32;++n)sprintf_s(hex+n*2,3,"%02x",digest[n]);
            std::lock_guard<std::mutex> guard(work->Cache->Gate);work->Cache->Items.push_back({static_cast<DWORD>(reinterpret_cast<std::uintptr_t>(modules[i])),hex,mapped,file.value});file.value=INVALID_HANDLE_VALUE;
        }
    }
    return 0;
}
std::string ModuleHash(HANDLE process,DWORD address,const std::shared_ptr<ModuleCache>& cache)
{
    MEMORY_BASIC_INFORMATION memory{};if(!VirtualQueryEx(process,reinterpret_cast<void*>(address),&memory,sizeof(memory))||memory.Type!=MEM_IMAGE)return "unknown";
    std::unique_lock<std::mutex> guard(cache->Gate,std::try_to_lock);if(!guard.owns_lock())return "unknown";
    wchar_t mapped[1024]{};if(!GetMappedFileNameW(process,reinterpret_cast<void*>(address),mapped,1024))return "unknown";
    for(const auto& item:cache->Items)if(item.Base==reinterpret_cast<std::uintptr_t>(memory.AllocationBase)&&item.MappedPath==mapped)return item.Hash;
    return "unknown";
}
std::string Incident(const Shared& data,HANDLE process,const char* id,const std::shared_ptr<ModuleCache>& cache,bool window=false)
{
    std::string offset,module=window?"unknown":Module(process,data.Context.Eip,offset);if(window)offset="unknown";
    char code[32]{};sprintf_s(code,"0x%08lX",data.Exception.ExceptionCode);
    std::string stack;
    if(!window&&module!="unknown")stack="\""+module+"+"+offset+"\"";
    DWORD frame=data.Context.Ebp;
    for(unsigned i=1;!window&&i<32;++i) {
        DWORD pair[2]{};SIZE_T read{};if(frame<data.Context.Esp||frame-data.Context.Esp>1024*1024||!ReadProcessMemory(process,reinterpret_cast<void*>(frame),pair,sizeof(pair),&read)||read!=sizeof(pair)||pair[0]<=frame)break;
        std::string relative;const auto name=Module(process,pair[1],relative);if(name!="unknown"){if(!stack.empty())stack+=",";stack+="\""+name+"+"+relative+"\"";}frame=pair[0];
    }
    return "{\"eventId\":\""+std::string(id)+"\",\"kind\":\""+(window?"error":"crash")+"\",\"stage\":\"game\",\"evidence\":\""+(window?"owned-crash-window":data.OriginalEntry?"original-exception-context":"confirmed-unhandled-exception")+"\",\"architecture\":\"x86\",\"exceptionCode\":\""+(window?"unknown":code)+"\",\"module\":\""+module+"\",\"moduleHash\":\""+(window?"unknown":ModuleHash(process,data.Context.Eip,cache))+"\",\"moduleOffset\":\""+offset+"\",\"stack\":["+stack+"],\"relatedEventId\":null,\"protocol\":\"unknown\",\"port\":0,\"socketCode\":null}";
}
bool Event(const Shared& data,const char* id,const char* state,const std::string& incident)
{
    const std::wstring directory=std::wstring(data.Directory)+L"\\events";if(!Plain(directory))return false;
    Handle lease(CreateFileW((directory+L"\\events.lock").c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_ALWAYS,FILE_FLAG_OPEN_REPARSE_POINT,nullptr));if(!lease)return false;
    unsigned count=0;WIN32_FIND_DATAW entry{};HANDLE find=FindFirstFileW((directory+L"\\*.json").c_str(),&entry);
    if(find!=INVALID_HANDLE_VALUE){do{if(++count>=100)break;}while(FindNextFileW(find,&entry));FindClose(find);}
    if(count>=100||(count>97&&!strcmp(state,"provisional")))return false;
    const std::wstring path=directory+L"\\"+Wide(id)+L"-"+Wide(state)+L".json",temp=path+L".tmp";
    if(!Plain(path)||!Plain(temp))return false;
    const std::string body="{\"sessionId\":\""+std::string(data.Session)+"\",\"capability\":\""+data.Capability+"\",\"state\":\""+state+"\",\"incident\":"+incident+",\"producerPid\":"+std::to_string(data.Pid)+",\"producerCreatedUtcTicks\":"+std::to_string(data.CreatedTicks)+"}";
    if(body.size()>16384)return false;
    HANDLE file=CreateFileW(temp.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_FLAG_OPEN_REPARSE_POINT,nullptr);if(file==INVALID_HANDLE_VALUE)return false;
    DWORD written{};bool ok=WriteFile(file,body.data(),static_cast<DWORD>(body.size()),&written,nullptr)&&written==body.size()&&FlushFileBuffers(file);CloseHandle(file);
    if(ok)ok=MoveFileExW(temp.c_str(),path.c_str(),MOVEFILE_WRITE_THROUGH)!=FALSE;
    if(!ok)DeleteFileW(temp.c_str());return ok;
}
HANDLE Spawn(const wchar_t* mode,const std::vector<HANDLE>& handles)
{
    wchar_t exe[32768]{};if(!GetModuleFileNameW(nullptr,exe,32768))return nullptr;
    std::wstring command=L"\""+std::wstring(exe)+L"\" "+mode;for(auto h:handles){SetHandleInformation(h,HANDLE_FLAG_INHERIT,HANDLE_FLAG_INHERIT);command+=L" "+std::to_wstring(reinterpret_cast<std::uintptr_t>(h));}
    STARTUPINFOEXW start{};start.StartupInfo.cb=sizeof(start);SIZE_T bytes{};InitializeProcThreadAttributeList(nullptr,1,0,&bytes);
    start.lpAttributeList=static_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(HeapAlloc(GetProcessHeap(),0,bytes));PROCESS_INFORMATION child{};
    if(start.lpAttributeList&&InitializeProcThreadAttributeList(start.lpAttributeList,1,0,&bytes)&&UpdateProcThreadAttribute(start.lpAttributeList,0,PROC_THREAD_ATTRIBUTE_HANDLE_LIST,const_cast<HANDLE*>(handles.data()),handles.size()*sizeof(HANDLE),nullptr,nullptr))
        CreateProcessW(exe,command.data(),nullptr,nullptr,TRUE,EXTENDED_STARTUPINFO_PRESENT|CREATE_NO_WINDOW,nullptr,nullptr,&start.StartupInfo,&child);
    if(start.lpAttributeList){DeleteProcThreadAttributeList(start.lpAttributeList);HeapFree(GetProcessHeap(),0,start.lpAttributeList);}for(auto h:handles)SetHandleInformation(h,HANDLE_FLAG_INHERIT,0);
    if(child.hThread)CloseHandle(child.hThread);return child.hProcess;
}
struct Budget {HANDLE file;ULONGLONG started;};
BOOL CALLBACK Limit(PVOID parameter,MINIDUMP_CALLBACK_INPUT* input,MINIDUMP_CALLBACK_OUTPUT* output)
{
    auto& budget=*static_cast<Budget*>(parameter);
    switch(input->CallbackType) {
    case IoStartCallback:output->Status=S_FALSE;break;
    case IoWriteAllCallback: {
        if(input->Io.Offset>MaxDump||input->Io.BufferBytes>MaxDump-input->Io.Offset||GetTickCount64()-budget.started>=3000){output->Status=HRESULT_FROM_WIN32(ERROR_CANCELLED);break;}
        LARGE_INTEGER offset{};offset.QuadPart=input->Io.Offset;DWORD written{};
        output->Status=SetFilePointerEx(budget.file,offset,nullptr,FILE_BEGIN)&&WriteFile(budget.file,input->Io.Buffer,input->Io.BufferBytes,&written,nullptr)&&written==input->Io.BufferBytes?S_OK:E_FAIL;break;
    }
    case IoFinishCallback:output->Status=S_OK;break;
    case CancelCallback:output->CheckCancel=TRUE;output->Cancel=GetTickCount64()-budget.started>=3000;break;
    default:break;
    }return TRUE;
}
DWORD WINAPI DumpDeadline(void*) {Sleep(3000);ExitProcess(124);}
int DumpWorker(HANDLE mapping,HANDLE process,HANDLE file)
{
    // A supervisor may itself exit. This only terminates this dedicated dump
    // worker, and bounds an OS DbgHelp call even when it stops calling callbacks.
    Handle deadline(CreateThread(nullptr,0,DumpDeadline,nullptr,0,nullptr));if(!deadline)return 3;
    auto* data=static_cast<Shared*>(MapViewOfFile(mapping,FILE_MAP_READ,0,0,sizeof(Shared)));if(!data)return 2;
    Shared snapshot=*data;UnmapViewOfFile(data);std::wstring root;if(!Identity(snapshot,process)||!SameUser(process)||!Location(snapshot,root)||!Registration(snapshot,true))return 2;
    auto write=LocalCaptureDumpWriter();if(!write)return 3;
    EXCEPTION_POINTERS pointers{&snapshot.Exception,&snapshot.Context};MINIDUMP_EXCEPTION_INFORMATION info{snapshot.ThreadId,&pointers,FALSE};
    Budget budget{file,GetTickCount64()};MINIDUMP_CALLBACK_INFORMATION callback{Limit,&budget};
    const bool ok=write(process,snapshot.Pid,file,MiniDumpNormal,&info,nullptr,&callback)!=FALSE;
    LARGE_INTEGER size{};return ok&&GetFileSizeEx(file,&size)&&size.QuadPart>0&&static_cast<ULONGLONG>(size.QuadPart)<=MaxDump&&FlushFileBuffers(file)?0:4;
}
bool OwnedName(const wchar_t* name,const wchar_t* extension)
{
    if(wcslen(name)!=32+wcslen(extension)||wcscmp(name+32,extension))return false;
    for(unsigned i=0;i<32;++i)if(!((name[i]>=L'0'&&name[i]<=L'9')||(name[i]>=L'a'&&name[i]<=L'f')))return false;return true;
}
void SmallDump(const Shared& data,HANDLE process,const std::wstring& root,const char* id)
{
    if(!data.SmallDumps||GetTickCount64()-data.Started>=4000)return;
    const auto directory=root+L"\\captures";if(!Plain(directory))return;if(!CreateDirectoryW(directory.c_str(),nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return;
    Handle lease(CreateFileW((directory+L"\\captures.lock").c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_ALWAYS,FILE_FLAG_OPEN_REPARSE_POINT,nullptr));if(!lease)return;
    struct File{std::wstring path;ULONGLONG time;};std::vector<File> files;WIN32_FIND_DATAW entry{};HANDLE search=FindFirstFileW((directory+L"\\*").c_str(),&entry);
    if(search!=INVALID_HANDLE_VALUE){do {
        if(entry.dwFileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT))continue;
        const auto path=directory+L"\\"+entry.cFileName;
        // No active writer can coexist with this lease. Only exact owned files
        // participate in retention; a prior helper's orphan part is incomplete.
        if(OwnedName(entry.cFileName,L".part"))DeleteFileW(path.c_str());
        if(OwnedName(entry.cFileName,L".dmp"))files.push_back({path,(static_cast<ULONGLONG>(entry.ftLastWriteTime.dwHighDateTime)<<32)|entry.ftLastWriteTime.dwLowDateTime});
    }while(FindNextFileW(search,&entry));FindClose(search);}
    std::sort(files.begin(),files.end(),[](const File& a,const File& b){return a.time>b.time;});
    for(size_t i=1;i<files.size();++i)if(!DeleteFileW(files[i].path.c_str()))return;
    const auto final=directory+L"\\"+Wide(id)+L".dmp",partial=directory+L"\\"+Wide(id)+L".part";if(!Plain(final)||!Plain(partial))return;
    Handle file(CreateFileW(partial.c_str(),GENERIC_WRITE|GENERIC_READ|DELETE,FILE_SHARE_READ|FILE_SHARE_DELETE,nullptr,CREATE_NEW,FILE_FLAG_OPEN_REPARSE_POINT,nullptr));if(!file)return;
    // Keep incomplete output delete-pending for its entire lifetime. Even if
    // the collector dies, the worker's deadline closes the last inherited
    // handle and Windows removes the partial without a future cleanup run.
    FILE_DISPOSITION_INFO disposition{TRUE};
    if(!SetFileInformationByHandle(file.value,FileDispositionInfo,&disposition,sizeof(disposition))){CloseHandle(file.value);file.value=INVALID_HANDLE_VALUE;DeleteFileW(partial.c_str());return;}
    Handle mapping(CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Shared),nullptr));
    auto* copy=mapping?static_cast<Shared*>(MapViewOfFile(mapping.value,FILE_MAP_WRITE,0,0,sizeof(Shared))):nullptr;
    bool success=false;
    if(copy) {
        *copy=data;UnmapViewOfFile(copy);Handle child(Spawn(L"--local-dump",{mapping.value,process,file.value,lease.value}));
        if(child) {
            const auto elapsed=GetTickCount64()-data.Started;const DWORD budget=elapsed<4500?static_cast<DWORD>(std::min<ULONGLONG>(3000,4500-elapsed)):0;
            if(WaitForSingleObject(child.value,budget)==WAIT_TIMEOUT){TerminateProcess(child.value,124);WaitForSingleObject(child.value,250);} // Our dump writer only, never the game.
            DWORD exit{};success=GetExitCodeProcess(child.value,&exit)&&exit==0;
        }
    }
    if(success){disposition.DeleteFile=FALSE;success=SetFileInformationByHandle(file.value,FileDispositionInfo,&disposition,sizeof(disposition))!=FALSE;}
    CloseHandle(file.value);file.value=INVALID_HANDLE_VALUE;
    if(success)success=MoveFileExW(partial.c_str(),final.c_str(),MOVEFILE_WRITE_THROUGH)!=FALSE;
    if(!success)DeleteFileW(partial.c_str());
}
struct WindowSearch {DWORD pid;bool found;};
BOOL CALLBACK Window(HWND window,LPARAM value)
{
    auto& search=*reinterpret_cast<WindowSearch*>(value);DWORD pid{};GetWindowThreadProcessId(window,&pid);if(pid!=search.pid)return TRUE;
    wchar_t title[128]{};GetWindowTextW(window,title,128);
    if(!_wcsicmp(title,L"CrashSender")||!_wcsicmp(title,L"BugTrap")){search.found=true;return FALSE;}return TRUE;
}
int Monitor(HANDLE mapping,HANDLE request,HANDLE ack,HANDLE process)
{
    auto* data=static_cast<Shared*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Shared)));if(!data)return 2;
    Shared identity=*data;std::wstring root;
    if(!Identity(identity,process)||!SameUser(process)||!Location(identity,root)){UnmapViewOfFile(data);return 3;}
    auto cache=std::make_shared<ModuleCache>();HANDLE hashProcess{};
    if(DuplicateHandle(GetCurrentProcess(),process,GetCurrentProcess(),&hashProcess,0,FALSE,DUPLICATE_SAME_ACCESS)) {
        auto* work=new HashWork{hashProcess,cache};Handle thread(CreateThread(nullptr,0,CacheModules,work,0,nullptr));
        if(!thread){CloseHandle(hashProcess);delete work;}
        else WaitForSingleObject(thread.value,750); // Prewarm inside the existing 1s startup handshake, never on a fault.
    }
    InterlockedExchange(&data->Ready,1);SetEvent(ack);
    LONG networkSeen=0;
    LONG seen=0;unsigned activeSlot=8;Shared pending{};std::string incident;bool provisional=false,finished=false,windowSent=false;ULONGLONG exited=0;
    for(;;) {
        const bool dead=WaitForSingleObject(process,0)==WAIT_OBJECT_0;
        if(dead&&!exited)exited=GetTickCount64();
        const bool bound=Registration(identity,true);
        const LONG notifications=InterlockedCompareExchange(&data->NetworkCount,0,0);
        while(bound&&networkSeen<notifications&&networkSeen<16) {
            const auto record=data->Network[networkSeen++];if(!Id(record.Id)||record.Connecting>1||record.SocketError>65535||(record.Connecting&&record.SocketError==0))continue;
            const std::string evidence=record.Connecting?"native-connect-failure-notification":"native-disconnect-notification";
            const std::string body="{\"eventId\":\""+std::string(record.Id)+"\",\"kind\":\"networkerror\",\"stage\":\"native-"+(record.Connecting?"connect":"disconnect")+"\",\"evidence\":\""+evidence+"\",\"architecture\":\"x86\",\"protocol\":\"tcp\",\"socketCode\":"+std::to_string(record.SocketError)+"}";
            Event(identity,record.Id,"confirmed",body);
        }
        const LONG sequence=InterlockedCompareExchange(&data->Sequence,0,0);
        if(bound&&sequence>seen&&sequence<=16) {
            const unsigned slot=static_cast<unsigned>(sequence-1)/2;
            // The producer can finish its bounded wait before this process
            // observes provisional. Final-only observations still belong to a
            // new slot and must never reuse a preceding cancelled incident.
            if(slot!=activeSlot){activeSlot=slot;incident.clear();pending={};provisional=false;finished=false;}
            pending=*data;const LONG state=InterlockedCompareExchange(&data->StateValue,0,0);
            if(state==Provisional) {
                incident=Incident(pending,process,pending.Ids[slot],cache);provisional=Event(pending,pending.Ids[slot],"provisional",incident);finished=false;
                if(provisional)SmallDump(pending,process,root,pending.Ids[slot]);
            }else if(state==Confirmed||state==Cancelled) {
                if(incident.empty())incident=Incident(pending,process,pending.Ids[slot],cache);
                Event(pending,pending.Ids[slot],state==Cancelled?"cancelled":"confirmed",incident);finished=true;
                if(state==Cancelled){provisional=false;const auto path=root+L"\\captures\\"+Wide(pending.Ids[slot])+L".dmp";if(Plain(path))DeleteFileW(path.c_str());}
            }
            seen=sequence;InterlockedExchange(&data->Acknowledged,sequence);SetEvent(ack);
        }
        if(dead) {
            // Exit is checked only after the producer's final state. A timeout
            // while still alive is never confirmation of an unhandled crash.
            if(bound&&provisional&&!finished&&data->StateValue!=Cancelled) {
                DWORD code{};if(GetExitCodeProcess(process,&code)&&code!=0&&code!=STILL_ACTIVE){const unsigned slot=static_cast<unsigned>(seen-1)/2;Event(pending,pending.Ids[slot],"confirmed",incident);}finished=true;
            }
            // Hold early captures for a late launcher PID/time bind, up to 5s.
            if(bound||GetTickCount64()-exited>=5000)break;
        }else if(bound&&!windowSent&&(data->StateValue==Idle||data->StateValue==Cancelled)) {
            WindowSearch search{identity.Pid,false};EnumWindows(Window,reinterpret_cast<LPARAM>(&search));
            if(search.found){Event(identity,identity.Ids[7],"confirmed",Incident(identity,process,identity.Ids[7],cache,true));windowSent=true;}
        }
        WaitForSingleObject(request,50);
    }
    UnmapViewOfFile(data);return 0;
}
}

int LocalCaptureMain(int argc,wchar_t** argv)
{
    const bool monitor=argc==6&&!wcscmp(argv[1],L"--local-session"),dump=argc==6&&!wcscmp(argv[1],L"--local-dump");if(!monitor&&!dump)return 2;
    HANDLE handles[4]{};for(int i=2;i<argc;++i){wchar_t* end{};const auto raw=wcstoul(argv[i],&end,10);if(!raw||!end||*end)return 2;handles[i-2]=reinterpret_cast<HANDLE>(raw);DWORD flags{};if(!GetHandleInformation(handles[i-2],&flags))return 2;}
    const int result=monitor?Monitor(handles[0],handles[1],handles[2],handles[3]):DumpWorker(handles[0],handles[1],handles[2]);
    for(auto handle:handles)if(handle)CloseHandle(handle);return result;
}
