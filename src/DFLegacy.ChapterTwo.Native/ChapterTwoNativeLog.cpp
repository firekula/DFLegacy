#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoNativeLog.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <array>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <limits>

namespace
{
    constexpr std::size_t ImageSize=0xe57000;
    // Complete instructions around each local log encoder. The shared encoder
    // has other consumers and is only checked, never changed.
    constexpr unsigned char Buffer[]={0x8a,0x9c,0x3d,0x00,0xfc,0xff,0xff,0x8d,0xb4,0x3d,0x00,0xfc,0xff,0xff,0x6a,0x01,0x56,0xe8,0x2a,0xc1,0x03,0x00,0xa1,0xd0,0x23,0xe0,0x00,0x83,0xc4,0x08};
    constexpr unsigned char Main[]={0x80,0x3d,0xc4,0x23,0xe0,0x00,0x01,0x5f,0x75,0x13,0x50,0x8d,0x85,0x00,0xfc,0xff,0xff,0x50,0xe8,0x89,0xbc,0x03,0x00,0x8b,0x45,0x08,0x83,0xc4,0x08};
    constexpr unsigned char Rotate1[]={0x50,0x8d,0x85,0x00,0xfe,0xff,0xff,0x50,0xe8,0xca,0xbb,0x03,0x00,0x83,0xc4,0x08,0x8b,0x93,0x04,0x01,0x00,0x00,0x8d,0x8d,0x00,0xfe,0xff,0xff,0x51,0x52,0xe8,0x29,0x7d,0x05,0x00};
    constexpr unsigned char Rotate2[]={0x50,0x8d,0x8d,0x00,0xfe,0xff,0xff,0x51,0xe8,0x72,0xbb,0x03,0x00,0x83,0xc4,0x08,0x8b,0x83,0x04,0x01,0x00,0x00,0x8d,0x95,0x00,0xfe,0xff,0xff,0x52,0x50,0xe8,0xd1,0x7c,0x05,0x00};
    constexpr unsigned char Rotate3[]={0x50,0x8d,0x95,0x00,0xfe,0xff,0xff,0x52,0xe8,0xea,0xba,0x03,0x00,0x83,0xc4,0x08,0x8b,0x8b,0x04,0x01,0x00,0x00,0x8d,0x85,0x00,0xfe,0xff,0xff,0x50,0x51,0xe8,0x49,0x7c,0x05,0x00};
    constexpr unsigned char Rotate4[]={0x50,0x8d,0x85,0x00,0xfe,0xff,0xff,0x50,0xe8,0x93,0xba,0x03,0x00,0x83,0xc4,0x08,0x8b,0x93,0x04,0x01,0x00,0x00,0x8d,0x8d,0x00,0xfe,0xff,0xff,0x51,0x52,0xe8,0xf2,0x7b,0x05,0x00};
    constexpr unsigned char Engine[]={0x8b,0xf0,0x8d,0x95,0x00,0xfc,0xff,0xff,0x56,0x52,0xe8,0xf6,0xfd,0xff,0xff,0x57,0x6a,0x01,0x8d,0x85,0x00,0xfc,0xff,0xff,0x56,0x50,0xe8,0x27,0xbd,0x01,0x00,0x57,0xe8,0x13,0xb8,0x01,0x00};
    constexpr unsigned char Fopen[]={0x6a,0x40,0xff,0x74,0x24,0x0c,0xff,0x74,0x24,0x0c,0xe8,0x95,0xff,0xff,0xff,0x83,0xc4,0x0c,0xc3};
    constexpr unsigned char Encoder[]={0x55,0x8b,0xec,0x57,0x8b,0x7d,0x0c,0x33,0xc9,0x85,0xff,0x76,0x1d,0x56,0x8b,0x75,0x08,0x8a,0x04,0x31,0x8a,0xd0,0xc0,0xe2,0x06,0xc0,0xe8,0x02,0x0a,0xd0,0x80,0xf2,0x9d,0x88,0x14,0x31,0x41,0x3b,0xcf,0x72,0xe8,0x5e,0x5f,0x5d,0xc3};
    struct Site{std::size_t offset,call,size;const unsigned char* original;};
    constexpr Site Sites[]={
        {0x6094b0,17,sizeof(Buffer),Buffer},{0x609950,18,sizeof(Main),Main},
        {0x609a19,8,sizeof(Rotate1),Rotate1},{0x609a71,8,sizeof(Rotate2),Rotate2},
        {0x609af9,8,sizeof(Rotate3),Rotate3},{0x609b50,8,sizeof(Rotate4),Rotate4},
        {0x6457eb,10,sizeof(Engine),Engine},{0x6610d0,0,sizeof(Fopen),Fopen}};
    char LogDirectory[1400]{};
    wchar_t OwnedLogDirectory[1400]{};
    bool Installed=false;

    bool Same(const char* a,const char* b) noexcept
    {
        while(*a&&*b){char x=*a++,y=*b++;if(x>='A'&&x<='Z')x+='a'-'A';if(y>='A'&&y<='Z')y+='a'-'A';if(x!=y)return false;}
        return *a==*b;
    }
    void* __cdecl LogFopen(const char* filename,const char* mode) noexcept
    {
        char path[1500]{};
        const bool redirected=dfl::chapter_two::native_log_detail::ResolveLogPath(filename,LogDirectory,path,sizeof(path));
        // This is the original fopen body's exact delegate and sharing mode.
        using FsOpen=void*(__cdecl*)(const char*,const char*,int);
        return reinterpret_cast<FsOpen>(0xa61074)(redirected?path:filename,mode,0x40);
    }
    bool PlainDirectory(const wchar_t* path) noexcept
    {
        const auto attributes=GetFileAttributesW(path);
        return attributes!=INVALID_FILE_ATTRIBUTES&&(attributes&FILE_ATTRIBUTE_DIRECTORY)&&!(attributes&FILE_ATTRIBUTE_REPARSE_POINT);
    }
    bool SessionDirectory(wchar_t* root,std::size_t length) noexcept
    {
        // The launcher supplies a canonical local absolute session path. Reject
        // aliases/traversal and check every ancestor, not just the final folder.
        if(length<4||!((root[0]>=L'A'&&root[0]<=L'Z')||(root[0]>=L'a'&&root[0]<=L'z'))||root[1]!=L':'||root[2]!=L'\\')return false;
        const auto first=root[3];root[3]=0;const bool drive=PlainDirectory(root);root[3]=first;
        if(!drive)return false;
        std::size_t begin=3;
        for(std::size_t i=3;i<=length;++i){
            const auto c=root[i];
            if(c&&c!=L'\\'){
                if(c<L' '||std::wcschr(L"/:*?\"<>|",c))return false;
                continue;
            }
            if(i==begin||root[i-1]==L'.'||root[i-1]==L' ')return false;
            root[i]=0;const bool valid=PlainDirectory(root);root[i]=c;
            if(!valid)return false;
            begin=i+1;
        }
        return true;
    }
    bool Directory(const wchar_t* path) noexcept
    {
        if(!CreateDirectoryW(path,nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return false;
        return PlainDirectory(path);
    }
    void ClearPreparedDirectory() noexcept
    {
        // Only this invocation's newly created, still-empty PID directory.
        if(*OwnedLogDirectory)RemoveDirectoryW(OwnedLogDirectory);
        OwnedLogDirectory[0]=0;LogDirectory[0]=0;
    }
    bool PrepareDirectory() noexcept
    {
        wchar_t root[1024]{},path[1400]{};
        const auto length=GetEnvironmentVariableW(L"DFL_S1C2_SESSION_DATA",root,_countof(root));
        if(!length||length>=_countof(root)||!SessionDirectory(root,length))return false;
        if(_snwprintf_s(path,_countof(path),_TRUNCATE,L"%ls\\Temp",root)<0||!Directory(path))return false;
        if(_snwprintf_s(path,_countof(path),_TRUNCATE,L"%ls\\Temp\\NativeLogs",root)<0||!Directory(path))return false;
        if(_snwprintf_s(path,_countof(path),_TRUNCATE,L"%ls\\Temp\\NativeLogs\\%lu",root,GetCurrentProcessId())<0)return false;
        // Never silently mix a reused PID's old log files into this process.
        if(!CreateDirectoryW(path,nullptr))return false;
        wcscpy_s(OwnedLogDirectory,path);
        BOOL substituted=FALSE;
        if(!WideCharToMultiByte(CP_ACP,WC_NO_BEST_FIT_CHARS,path,-1,LogDirectory,sizeof(LogDirectory),nullptr,&substituted)||substituted){ClearPreparedDirectory();return false;}
        wchar_t probe[1500]{};
        if(_snwprintf_s(probe,_countof(probe),_TRUNCATE,L"%ls\\.write-probe",path)<0){ClearPreparedDirectory();return false;}
        const auto file=CreateFileW(probe,GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_TEMPORARY|FILE_FLAG_DELETE_ON_CLOSE,nullptr);
        if(file==INVALID_HANDLE_VALUE){ClearPreparedDirectory();return false;}
        CloseHandle(file);return true;
    }
}

bool dfl::chapter_two::native_log_detail::ResolveLogPath(const char* source,const char* directory,char* output,std::size_t capacity) noexcept
{
    if(!source||!directory||!*directory||!output||!capacity)return false;
    const char* tail=source;
    std::size_t i=0;for(;i<32768&&source[i];++i)if(source[i]=='/'||source[i]=='\\')tail=source+i+1;
    if(i==32768)return false;
    const char* name=Same(tail,"DNF.trc")?"DNF.trc":Same(tail,"NeopleEngine.LOG")?"NeopleEngine.LOG":nullptr;
    if(!name)return false;
    const auto rootLength=std::strlen(directory),nameLength=std::strlen(name);
    if(rootLength>=capacity||nameLength+2>capacity-rootLength)return false;
    std::memcpy(output,directory,rootLength);output[rootLength]='\\';std::memcpy(output+rootLength+1,name,nameLength+1);return true;
}

bool dfl::chapter_two::native_log_detail::InstallImage(unsigned char* image,std::size_t bytes,std::uintptr_t fopenHook,PatchWriter write) noexcept
{
    if(!image||bytes!=ImageSize||!fopenHook||!write||reinterpret_cast<std::uintptr_t>(image)>std::numeric_limits<std::uintptr_t>::max()-bytes)return false;
    if(std::memcmp(image+0x6455f0,Encoder,sizeof(Encoder)))return false;
    std::array<std::array<unsigned char,6>,8> replacements{};
    std::array<bool,8> original{};
    for(std::size_t i=0;i<std::size(Sites);++i){
        const auto& s=Sites[i];auto& patch=replacements[i];patch.fill(0x90);
        const auto size=i==7?6u:5u;
        if(i==7){patch[0]=0xe9;const auto delta=static_cast<std::uint32_t>(fopenHook-reinterpret_cast<std::uintptr_t>(image+s.offset)-5);std::memcpy(patch.data()+1,&delta,4);}
        original[i]=std::memcmp(image+s.offset,s.original,s.size)==0;
        if(!original[i]&&(std::memcmp(image+s.offset,s.original,s.call)||std::memcmp(image+s.offset+s.call,patch.data(),size)||
            std::memcmp(image+s.offset+s.call+size,s.original+s.call+size,s.size-s.call-size)))return false;
    }
    for(std::size_t i=0;i<std::size(Sites);++i){if(!original[i])continue;
        const auto& s=Sites[i];if(write(reinterpret_cast<std::uintptr_t>(image+s.offset+s.call),replacements[i].data(),i==7?6:5))continue;
        // Include the failing write: protection/cache errors may follow mutation.
        for(std::size_t n=i+1;n>0;--n){const auto j=n-1;if(original[j]){const auto& r=Sites[j];write(reinterpret_cast<std::uintptr_t>(image+r.offset+r.call),r.original+r.call,j==7?6:5);}}
        return false;
    }
    return true;
}

bool dfl::chapter_two::InstallNativeLog(unsigned char* image,std::size_t bytes) noexcept
{
    if(image!=reinterpret_cast<unsigned char*>(0x400000)||bytes!=ImageSize)return false;
    if(Installed)return native_log_detail::InstallImage(image,bytes,reinterpret_cast<std::uintptr_t>(&LogFopen),memory_patch::WriteBytes);
    if(!PrepareDirectory()){OutputDebugStringA("DFLegacy Chapter Two: native log directory unavailable.\n");return false;}
    if(!native_log_detail::InstallImage(image,bytes,reinterpret_cast<std::uintptr_t>(&LogFopen),memory_patch::WriteBytes)){ClearPreparedDirectory();return false;}
    Installed=true;
    OutputDebugStringA("DFLegacy Chapter Two: native plaintext logs installed in session Temp/NativeLogs/PID.\n");return true;
}
