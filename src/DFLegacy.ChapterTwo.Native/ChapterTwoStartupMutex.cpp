#include "ChapterTwoStartupMutex.h"
#include "memory_patch.h"
#include <bcrypt.h>
#include <array>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <limits>

namespace
{
constexpr std::uintptr_t Base=0x400000, Function=0x9bd930, Call=0x9bdef9;
constexpr std::size_t ImageSize=0xe57000, FunctionSize=0x668;
constexpr char NativeName[]="dbefeuate_ccen_khxfor_lcar_blr";
constexpr unsigned char OriginalCall[]{0xff,0x15,0x24,0xa3,0xae,0x00};
constexpr unsigned char FunctionHash[]{0xc0,0x9b,0xc7,0x0b,0x70,0xf0,0x27,0xec,0xbc,0xd0,0x9f,0xec,0x95,0x85,0x80,0x4f,0xc9,0x82,0xd4,0xfb,0x44,0x7d,0x32,0xe6,0xa4,0xd0,0xd2,0xf4,0x84,0x34,0xd1,0x72};
constexpr unsigned char WinMain[]{0x55,0x8b,0xec,0x83,0xec,0x08,0xe8,0xa5,0x03,0xa0,0xff,0xe8,0x50,0xca,0xfb,0xff,0x84,0xc0,0x75,0x08,0x33,0xc0,0x8b,0xe5,0x5d,0xc2,0x10,0x00};
using dfl::chapter_two::startup_mutex_detail::Context;
using dfl::chapter_two::startup_mutex_detail::CreateMutexFunction;
struct HookState
{
    std::array<char,96> name{};
    CreateMutexFunction original=nullptr;
    const unsigned char* image=nullptr;
};
HookState State;

bool Normalize(const std::wstring& input,std::wstring& result)
{
    if(input.size()<3||input.size()>=2048||input[1]!=L':'||
        !((input[0]>=L'A'&&input[0]<=L'Z')||(input[0]>=L'a'&&input[0]<=L'z'))||
        (input[2]!=L'\\'&&input[2]!=L'/'))return false;
    std::array<wchar_t,2048> value{};
    const DWORD length=GetFullPathNameW(input.c_str(),static_cast<DWORD>(value.size()),value.data(),nullptr);
    if(!length||length>=value.size())return false;
    result.assign(value.data(),length);
    for(auto& ch:result)if(ch==L'/')ch=L'\\';
    return true;
}
bool SamePath(const std::wstring& a,const std::wstring& b) noexcept
{
    return CompareStringOrdinal(a.c_str(),-1,b.c_str(),-1,TRUE)==CSTR_EQUAL;
}
bool SessionPaths(const Context& context,std::wstring& root)
{
    if(!context.ordinaryDesktop||(context.instance!=L'A'&&context.instance!=L'B')||
        (context.fileHigh==0&&context.fileLow==0))return false;
    std::wstring module,session;
    if(!Normalize(context.modulePath,module)||!Normalize(context.sessionPath,session))return false;
    const auto slash=module.find_last_of(L'\\');if(slash==std::wstring::npos||slash<=2)return false;
    root=module.substr(0,slash);const auto leaf=module.substr(slash+1);
    std::size_t prefix=0;
    for(const auto* candidate:{L"s1c2_chapter2_host-",L"s1c2_chapter2_testcontrol-"})
        if(leaf.compare(0,std::wcslen(candidate),candidate)==0){prefix=std::wcslen(candidate);break;}
    if(!prefix||leaf.size()<prefix+4||leaf.compare(leaf.size()-4,4,L".exe")!=0)return false;
    const auto stem=leaf.substr(prefix,leaf.size()-prefix-4);
    if((stem.size()!=23&&stem.size()!=24)||stem[0]!=context.instance||stem[1]!=L'-'||stem[10]!=L'-'||stem[17]!=L'-'||stem[21]!=L'-')return false;
    for(std::size_t i=2;i<stem.size();++i)
        if(i!=10&&i!=17&&i!=21&&(stem[i]<L'0'||stem[i]>L'9'))return false;
    if(stem[22]==L'0')return false;
    const auto attempt=stem.size()==23?stem[22]-L'0':10*(stem[22]-L'0')+stem[23]-L'0';
    if(attempt<1||attempt>48)return false;
    return SamePath(session,root+L"\\Logs\\DFLegacy\\Sessions\\"+stem);
}
bool OrdinaryDirectory(std::wstring path)
{
    for(;;){const auto attributes=GetFileAttributesW(path.c_str());
        if(attributes==INVALID_FILE_ATTRIBUTES||!(attributes&FILE_ATTRIBUTE_DIRECTORY)||(attributes&FILE_ATTRIBUTE_REPARSE_POINT))return false;
        if(path.size()==3)return true;
        const auto slash=path.find_last_of(L'\\');if(slash==std::wstring::npos||slash<2)return false;
        path.resize(slash==2?3:slash);}
}
bool CurrentContext(Context& context)
{
    HANDLE token=nullptr;if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token))return false;
    DWORD size=0,container=1;
    const bool ordinary=GetTokenInformation(token,TokenIsAppContainer,&container,sizeof(container),&size)&&size==sizeof(container)&&container==0;
    CloseHandle(token);if(!ordinary)return false;
    std::array<wchar_t,2048> module{},session{};wchar_t instance[3]{};
    const DWORD moduleLength=GetModuleFileNameW(nullptr,module.data(),static_cast<DWORD>(module.size()));
    const DWORD sessionLength=GetEnvironmentVariableW(L"DFL_S1C2_SESSION_DATA",session.data(),static_cast<DWORD>(session.size()));
    if(!moduleLength||moduleLength>=module.size()||!sessionLength||sessionLength>=session.size()||
        GetEnvironmentVariableW(L"DFL_S1C2_INSTANCE",instance,3)!=1)return false;
    context.ordinaryDesktop=true;context.instance=instance[0];context.modulePath.assign(module.data(),moduleLength);context.sessionPath.assign(session.data(),sessionLength);
    // Validate the names before opening either directory. The temporary marker
    // only indicates that identity has not been read yet; it never names a mutex.
    context.fileLow=1;std::wstring root;
    if(!SessionPaths(context,root)||!OrdinaryDirectory(root)||!OrdinaryDirectory(context.sessionPath))return false;
    auto directory=CreateFileW(root.c_str(),FILE_READ_ATTRIBUTES,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
        nullptr,OPEN_EXISTING,FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT,nullptr);
    if(directory==INVALID_HANDLE_VALUE)return false;
    BY_HANDLE_FILE_INFORMATION info{};
    const bool read=GetFileInformationByHandle(directory,&info)&&
        (info.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY)&&!(info.dwFileAttributes&FILE_ATTRIBUTE_REPARSE_POINT);
    CloseHandle(directory);if(!read)return false;
    context.volume=info.dwVolumeSerialNumber;context.fileHigh=info.nFileIndexHigh;context.fileLow=info.nFileIndexLow;
    return context.fileHigh!=0||context.fileLow!=0;
}
HANDLE WINAPI ScopedCreate(LPSECURITY_ATTRIBUTES attributes,BOOL initialOwner,LPCSTR name) noexcept
{
    return dfl::chapter_two::startup_mutex_detail::CreateForName(State.name.data(),State.original,attributes,initialOwner,name);
}
std::array<unsigned char,6> Replacement(const unsigned char* image) noexcept
{
    std::array<unsigned char,6> bytes{0xe8,0,0,0,0,0x90};
    const auto address=reinterpret_cast<std::uintptr_t>(image)+Call-Base;
    const auto displacement=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&ScopedCreate)-address-5);
    std::memcpy(bytes.data()+1,&displacement,4);return bytes;
}
bool NativeContext(const unsigned char* image,bool patched) noexcept
{
    if(std::memcmp(image+0xa00ed0-Base,WinMain,sizeof(WinMain)))return false;
    // Exact digest of the entire original name construction and admission body.
    // Normalize only our verified six-byte call on an idempotent reinstallation.
    std::array<unsigned char,FunctionSize> body{};std::memcpy(body.data(),image+Function-Base,body.size());
    if(patched)std::memcpy(body.data()+Call-Function,OriginalCall,sizeof(OriginalCall));
    BCRYPT_ALG_HANDLE algorithm=nullptr;
    if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return false;
    std::array<unsigned char,32> digest{};
    const bool result=BCryptHash(algorithm,nullptr,0,body.data(),static_cast<ULONG>(body.size()),digest.data(),static_cast<ULONG>(digest.size()))>=0&&
        std::memcmp(digest.data(),FunctionHash,sizeof(FunctionHash))==0;
    BCryptCloseAlgorithmProvider(algorithm,0);return result;
}
}

bool dfl::chapter_two::startup_mutex_detail::MakeName(const Context& context,char* destination,std::size_t capacity) noexcept
{
    try{
        std::wstring root;if(!destination||capacity<2||!SessionPaths(context,root))return false;
        // Directory file identity is stable across casing/aliases, and common
        // to formal/private hosts. Neither PID, executable prefix nor retry is a key.
        const int count=std::snprintf(destination,capacity,"Local\\DFLegacy.C2.Startup.%08lX%08lX%08lX.%c",
            context.volume,context.fileHigh,context.fileLow,static_cast<char>(context.instance));
        return count>0&&static_cast<std::size_t>(count)<capacity;
    }catch(...){return false;}
}
HANDLE dfl::chapter_two::startup_mutex_detail::CreateForName(const char* scopedName,CreateMutexFunction original,
    LPSECURITY_ATTRIBUTES attributes,BOOL initialOwner,LPCSTR name) noexcept
{
    if(!original){SetLastError(ERROR_INVALID_PARAMETER);return nullptr;}
    const bool exact=attributes==nullptr&&initialOwner==TRUE&&name&&std::strcmp(name,NativeName)==0;
    // There is deliberately no logging, status rewriting or Windows call after
    // the real CreateMutexA. Its handle/failure and LastError reach native code.
    return original(attributes,initialOwner,exact&&scopedName&&scopedName[0]?scopedName:name);
}
bool dfl::chapter_two::startup_mutex_detail::InstallImage(unsigned char* image,std::size_t bytes,const Context& context,
    CreateMutexFunction original,PatchWriter write) noexcept
{
    static_assert(sizeof(void*)==4,"The C2 native call requires x86.");
    if(!image||bytes!=ImageSize||!original||!write||reinterpret_cast<std::uintptr_t>(image)>std::numeric_limits<std::uintptr_t>::max()-ImageSize)return false;
    HookState next;if(!MakeName(context,next.name.data(),next.name.size()))return false;
    next.original=original;next.image=image;
    const auto replacement=Replacement(image);auto* call=image+Call-Base;
    const bool patched=std::memcmp(call,replacement.data(),replacement.size())==0;
    if(!patched&&std::memcmp(call,OriginalCall,sizeof(OriginalCall)))return false;
    if(!NativeContext(image,patched))return false;
    if(patched)return State.image==image&&State.original==original&&State.name==next.name;
    const auto previous=State;State=next;
    if(write(reinterpret_cast<std::uintptr_t>(call),replacement.data(),replacement.size())&&
        std::memcmp(call,replacement.data(),replacement.size())==0)return true;
    write(reinterpret_cast<std::uintptr_t>(call),OriginalCall,sizeof(OriginalCall));State=previous;return false;
}
bool dfl::chapter_two::InstallStartupMutex(unsigned char* image,std::size_t bytes) noexcept
{
    if(image!=reinterpret_cast<unsigned char*>(Base)||bytes!=ImageSize)return false;
    try{Context context;if(!CurrentContext(context))return false;
        return startup_mutex_detail::InstallImage(image,bytes,context,&CreateMutexA,memory_patch::WriteBytes);
    }catch(...){return false;}
}
