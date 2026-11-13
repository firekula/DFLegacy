#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <limits>
#include <string>
#include <vector>
#ifndef CHAPTER_TWO_LOCAL_PATH_BASELINE
#include "ChapterTwoLocalPaths.h"
#endif

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char localPathFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::size_t ImageSize=0xe57000,Prefix=0x955ca0,Scratch=0x9554a0,PathCapacity=0x800;
    unsigned Checks=0,Failures=0,Writes=0,FailAt=0;
    unsigned VersionCalls=0,ShellCalls=0,FormatCalls=0,FileCalls=0;
    bool FailAfter=false;
    void Check(bool ok,const char* message)
    {++Checks;if(!ok){++Failures;std::printf("FAIL local paths: %s\n",message);}}
    template<class T>T& At(std::uintptr_t address){return *reinterpret_cast<T*>(address);}
    std::vector<unsigned char> Load(const wchar_t* path)
    {
        std::ifstream file(path,std::ios::binary);
        const std::vector<unsigned char> raw{std::istreambuf_iterator<char>(file),{}};
        if(raw.size()<4096)return {};
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
        if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||static_cast<std::size_t>(dos->e_lfanew)>raw.size()-sizeof(IMAGE_NT_HEADERS32))return {};
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+dos->e_lfanew);
        if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.ImageBase!=0x400000||nt->OptionalHeader.SizeOfImage!=ImageSize||nt->OptionalHeader.SizeOfHeaders>4096)return {};
        const auto* section=IMAGE_FIRST_SECTION(nt);
        if(reinterpret_cast<const unsigned char*>(section+nt->FileHeader.NumberOfSections)>raw.data()+raw.size())return {};
        std::vector<unsigned char> mapped(ImageSize);
        std::memcpy(mapped.data(),raw.data(),nt->OptionalHeader.SizeOfHeaders);
        for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=section[i];
            if(s.VirtualAddress>ImageSize||s.SizeOfRawData>ImageSize-s.VirtualAddress||s.PointerToRawData>raw.size()||s.SizeOfRawData>raw.size()-s.PointerToRawData)return {};
            std::memcpy(mapped.data()+s.VirtualAddress,raw.data()+s.PointerToRawData,s.SizeOfRawData);}
        return mapped;
    }
    bool Write(std::uintptr_t address,const void* bytes,std::size_t count)
    {++Writes;if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(address),bytes,count);return Writes!=FailAt;}
    void Jump(std::uintptr_t address,std::uintptr_t target)
    {unsigned char code[]{0xe9,0,0,0,0};const auto delta=static_cast<std::uint32_t>(target-address-5);std::memcpy(code+1,&delta,4);std::memcpy(reinterpret_cast<void*>(address),code,sizeof(code));}
    BOOL WINAPI Version(OSVERSIONINFOA* info)
    {++VersionCalls;Check(info&&info->dwOSVersionInfoSize==sizeof(*info),"native resolver supplies the correct version structure");info->dwMajorVersion=6;return TRUE;}
    HRESULT WINAPI Folder(HWND owner,int folder,void** item)
    {++ShellCalls;Check(!owner&&folder==0x801c,"old resolver requests the observed local application folder");*item=reinterpret_cast<void*>(0x1234);return S_OK;}
    BOOL WINAPI FolderPath(const void* item,char* output)
    {++ShellCalls;Check(item==reinterpret_cast<void*>(0x1234),"native shell result is passed to path conversion");strcpy_s(output,PathCapacity,"X:\\fixture-local\\");return TRUE;}
    void WINAPI FreeItem(void* item)
    {++ShellCalls;Check(item==reinterpret_cast<void*>(0x1234),"native shell allocation is released");}
    int __stdcall Length(const char* value,std::size_t capacity,std::size_t* length)
    {*length=std::strlen(value);return *length<capacity?0:-1;}
    int __stdcall Copy(char* output,std::size_t capacity,const char* input)
    {return strcpy_s(output,capacity,input);}
    int __cdecl Format(char* output,std::size_t capacity,const char* format,...)
    {++FormatCalls;va_list args;va_start(args,format);const int result=vsnprintf(output,capacity,format,args);va_end(args);return result;}
    int __cdecl Access(const char* path,int mode)
    {++FileCalls;Check(std::strcmp(path,"X:\\fixture-local\\Low\\DNF\\")==0&&mode==0,"native file probe stays inside the mocked path");return 0;}
    BOOL WINAPI MakeDirectoryStub(const char*,SECURITY_ATTRIBUTES*)
    {++FileCalls;return TRUE;}

#ifndef CHAPTER_TWO_LOCAL_PATH_BASELINE
    void Installation(const std::vector<unsigned char>& original)
    {
        using dfl::chapter_two::local_paths_detail::InstallImage;
        auto image=original;Writes=FailAt=0;
        auto* overflowing=reinterpret_cast<unsigned char*>(std::numeric_limits<std::uintptr_t>::max()-ImageSize+1);
        Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&
            !InstallImage(image.data(),ImageSize,nullptr)&&!InstallImage(overflowing,ImageSize,Write)&&Writes==0,
            "null, truncated, overflow and missing writer inputs cannot mutate memory");
        Check(!dfl::chapter_two::InstallLocalPaths(image.data(),ImageSize),"public installer rejects an image outside the native base");
        // Every byte of the complete native function, including both early returns
        // and the old concatenation tail, must be guarded before setting the mode.
        for(std::size_t offset=0x16b0;offset<0x1805;++offset){
            image=original;image[offset]^=1;const auto before=image;Writes=0;
            Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"a changed native resolver instruction prevents all mutation");}
        for(const auto offset:{0x6ea6d8u,0x6ea6dcu,0x6ea6e0u,0x6ea6e1u,0x6ea6f4u,0x6ea6fbu,
            0x9554a0u,0x955c9fu,0x955ca0u,0x955ca1u,0x95649fu}){
            image=original;image[offset]^=1;const auto before=image;Writes=0;
            Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"changed constants or noninitial static path buffers prevent installation");}
        image=original;image[Prefix]='\\';image[Prefix+2]='x';const auto malformed=image;Writes=0;
        Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==malformed,"a sentinel with stale prefix contents is not accepted as already installed");
        for(const bool after:{false,true}){
            image=original;Writes=0;FailAt=1;FailAfter=after;
            Check(!InstallImage(image.data(),image.size(),Write)&&image==original&&Writes==2,
                "a failed mode write rolls back both before-write and after-write failures");}
        image=original;Writes=FailAt=0;
        Check(InstallImage(image.data(),image.size(),Write)&&Writes==1,"one verified write selects the existing native passthrough mode");
        auto expected=original;expected[Prefix]='\\';
        Check(image==expected,"the only changed image byte is the native mode sentinel");
        Writes=0;Check(InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==expected,"repeat installation accepts only the complete sentinel without writing");
        image[0x17b7]^=1;const auto changed=image;Writes=0;
        Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==changed,"already installed mode still requires the complete resolver signature");
    }
#endif
}

int wmain(int argc,wchar_t** argv)
{
    if(argc!=2){std::fprintf(stderr,"usage: local_path_tests IMAGE\n");return 2;}
    const auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_LOCAL_PATH_BASELINE
    Installation(original);
#endif
    auto* mapped=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_COMMIT|MEM_RESERVE,PAGE_EXECUTE_READWRITE));
    const bool allocated=mapped!=nullptr;
    if(!mapped){const auto begin=reinterpret_cast<std::uintptr_t>(localPathFixtureReserve);DWORD old=0;
        if(begin<=0x400000&&0x400000+ImageSize<=begin+sizeof(localPathFixtureReserve)&&
            VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))mapped=reinterpret_cast<unsigned char*>(0x400000);}
    if(mapped!=reinterpret_cast<void*>(0x400000)){std::fprintf(stderr,"fixture image mapping failed %lu\n",GetLastError());return 2;}
    std::memcpy(mapped,original.data(),original.size());
#ifndef CHAPTER_TWO_LOCAL_PATH_BASELINE
    Check(!dfl::chapter_two::InstallLocalPaths(mapped,ImageSize-1),"public installer rejects an incomplete native image");
    Check(dfl::chapter_two::InstallLocalPaths(mapped,ImageSize),"public installer accepts the original image at its native base");
    Check(dfl::chapter_two::InstallLocalPaths(mapped,ImageSize),"public installer supports repetition before initialization");
#endif
    At<void*>(0xaea27c)=reinterpret_cast<void*>(&Version);
    At<void*>(0xaea394)=reinterpret_cast<void*>(&Folder);
    At<void*>(0xaea398)=reinterpret_cast<void*>(&FolderPath);
    At<void*>(0xaea674)=reinterpret_cast<void*>(&FreeItem);
    At<void*>(0xaea278)=reinterpret_cast<void*>(&MakeDirectoryStub);
    Jump(0x401230,reinterpret_cast<std::uintptr_t>(&Length));
    Jump(0x401180,reinterpret_cast<std::uintptr_t>(&Copy));
    Jump(0x401d90,reinterpret_cast<std::uintptr_t>(&Format));
    Jump(0xa5f8ae,reinterpret_cast<std::uintptr_t>(&Access));
    using Resolve=const char*(__cdecl*)(const char*);
    const auto resolve=reinterpret_cast<Resolve>(0x4016b0);
    for(const auto* value:{"D:\\isolated-session\\Temp\\dnf.str","dnf.str","ScreenShot\\capture.jpg","loginInfo.dat","D:\\session with spaces\\Temp\\dnf.str","config.xml"}){
        const std::string before=value;
        const auto* result=resolve(value);
        Check(result==value&&std::strcmp(result,value)==0,"the actual native resolver preserves absolute and relative input pointers and paths");
        Check(before==value,"the caller's input buffer is unchanged");
#ifdef CHAPTER_TWO_LOCAL_PATH_BASELINE
        const std::string wrong=std::string("X:\\fixture-local\\Low\\DNF\\")+value;
        Check(result&&wrong==result,"baseline deterministically concatenates the mocked user prefix, even for absolute paths");
        std::printf("baseline native result: %s\n",result?result:"<null>");
#endif
    }
    Check(VersionCalls==0&&ShellCalls==0,"passthrough never queries Windows version or the user's shell folders");
    Check(FileCalls==0&&FormatCalls==0,"passthrough never probes, creates or formats a user-specific directory");
#ifdef CHAPTER_TWO_LOCAL_PATH_BASELINE
    Check(VersionCalls==1&&ShellCalls==3&&FileCalls==1&&FormatCalls==6,"baseline exercises initialization once then repeats its cached prefix path");
#else
    const auto beforeRepeat=std::vector<unsigned char>(mapped,mapped+ImageSize);
    Check(dfl::chapter_two::InstallLocalPaths(mapped,ImageSize)&&std::memcmp(mapped,beforeRepeat.data(),ImageSize)==0,"repeat public install after real resolver calls is nonmutating");
    Check(resolve(nullptr)==nullptr,"native passthrough retains null without dereferencing it");
    Check(std::memcmp(mapped+Scratch,original.data()+Scratch,PathCapacity)==0,"passthrough leaves the native shared output buffer unused");
#endif
    if(allocated)VirtualFree(mapped,0,MEM_RELEASE);
    std::printf("local paths: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
