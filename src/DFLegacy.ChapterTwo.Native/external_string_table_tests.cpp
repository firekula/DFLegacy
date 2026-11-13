#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <algorithm>
#include <array>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>
#ifndef CHAPTER_TWO_EXTERNAL_STRINGS_BASELINE
#include "ChapterTwoExternalStringTable.h"
#include "../DFLegacy.Ijl15/src/external_string_table.h"
#endif

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char externalStringsFixtureReserve[0x1300000]{1};

namespace
{
    constexpr std::size_t ImageSize=0xe57000;
    unsigned Checks=0,Failures=0,Writes=0,FailAt=0;
    bool FailAfter=false;
    HMODULE Resources=nullptr;
    std::filesystem::path FixtureExecutable,Source,Stage;
    std::string StageAnsi;
    void Check(bool ok,const char* name)
    { ++Checks; if(!ok){++Failures;std::fprintf(stderr,"FAIL external strings: %s\n",name);} }
    std::vector<unsigned char> Read(const std::filesystem::path& p)
    { std::ifstream f(p,std::ios::binary);return {std::istreambuf_iterator<char>(f),{}}; }
    void Save(const std::filesystem::path& p,const std::vector<unsigned char>& data)
    { std::ofstream f(p,std::ios::binary|std::ios::trunc);f.write(reinterpret_cast<const char*>(data.data()),static_cast<std::streamsize>(data.size())); }
    std::vector<unsigned char> LoadImage(const std::filesystem::path& path)
    {
        const auto file=Read(path);if(file.size()<4096)return {};
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
        if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||static_cast<std::size_t>(dos->e_lfanew)>file.size()-sizeof(IMAGE_NT_HEADERS32))return {};
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data()+dos->e_lfanew);
        if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.ImageBase!=0x400000||nt->OptionalHeader.SizeOfImage!=ImageSize)return {};
        const auto* sections=IMAGE_FIRST_SECTION(nt);
        if(reinterpret_cast<const unsigned char*>(sections+nt->FileHeader.NumberOfSections)>file.data()+file.size()||nt->OptionalHeader.SizeOfHeaders>file.size())return {};
        std::vector<unsigned char> image(ImageSize);
        std::memcpy(image.data(),file.data(),nt->OptionalHeader.SizeOfHeaders);
        for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];
            if(s.PointerToRawData>file.size()||s.SizeOfRawData>file.size()-s.PointerToRawData||s.VirtualAddress>ImageSize||s.SizeOfRawData>ImageSize-s.VirtualAddress)return {};
            std::memcpy(image.data()+s.VirtualAddress,file.data()+s.PointerToRawData,s.SizeOfRawData);}
        return image;
    }
    bool Write(std::uintptr_t at,const void* bytes,std::size_t size)
    { ++Writes;if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(at),bytes,size);return Writes!=FailAt; }
    void Jump(std::uintptr_t at,std::uintptr_t target)
    { unsigned char code[]{0xe9,0,0,0,0};const auto delta=static_cast<std::uint32_t>(target-at-5);std::memcpy(code+1,&delta,4);std::memcpy(reinterpret_cast<void*>(at),code,5); }
    template<typename T> void Iat(std::uintptr_t at,T target){*reinterpret_cast<std::uintptr_t*>(at)=reinterpret_cast<std::uintptr_t>(target);}
    HRSRC WINAPI FindResourceProxy(HMODULE,LPCSTR name,LPCSTR type){return FindResourceA(Resources,name,type);}
    HGLOBAL WINAPI LoadResourceProxy(HMODULE,HRSRC resource){return LoadResource(Resources,resource);}
    DWORD WINAPI SizeResourceProxy(HMODULE,HRSRC resource){return SizeofResource(Resources,resource);}
    int WINAPI NoMessageBox(HWND,LPCSTR text,LPCSTR,UINT)
    { std::fprintf(stderr,"unexpected native error: %s\n",text);std::exit(4); }
    const char* __cdecl Resolve(const char* path){return path;}
    void* __cdecl Open(const char* path,const char* mode)
    { FILE* f=nullptr;fopen_s(&f,path,mode);return f; }
    const char* Lookup(int id){return reinterpret_cast<const char*(__cdecl*)(int)>(0x9bffe0)(id);}
    void Clear(){reinterpret_cast<void(__cdecl*)()>(0x9bfca0)();}
#ifndef CHAPTER_TWO_EXTERNAL_STRINGS_BASELINE
    int __cdecl Extract(const char* destination)
    {
        return dfl::chapter_two::external_string_table_detail::ExtractForExecutable(
            FixtureExecutable.c_str(),destination,Resolve,
            reinterpret_cast<dfl::external_string_table::BuiltinExtractor>(0x9bfbb0));
    }
    void Installation(const std::vector<unsigned char>& original)
    {
        using dfl::chapter_two::external_string_table_detail::InstallImage;
        const auto hook=reinterpret_cast<std::uintptr_t>(&Extract);
        auto image=original;Writes=FailAt=0;
        Check(!InstallImage(nullptr,ImageSize,hook,Write)&&!InstallImage(image.data(),ImageSize-1,hook,Write)&&
            !InstallImage(image.data(),ImageSize,0,Write)&&!InstallImage(image.data(),ImageSize,hook,nullptr)&&Writes==0,"invalid installation arguments do not write");
        for(const auto offset:{0x5c002du,0x5c003fu,0x5bfbb0u,0x5bfc40u,0x5bfcd0u,0x16b0u,0x81d108u}){
            image=original;image[offset]^=1;const auto before=image;Writes=0;
            Check(!InstallImage(image.data(),image.size(),hook,Write)&&Writes==0&&image==before,"unrecognized native extraction/parser/path context rejects without a write");}
        for(const bool after:{false,true}){
            image=original;Writes=0;FailAt=1;FailAfter=after;
            Check(!InstallImage(image.data(),image.size(),hook,Write)&&image==original,"single-call install restores original bytes even after a failed copy");}
        image=original;Writes=FailAt=0;
        Check(InstallImage(image.data(),image.size(),hook,Write)&&Writes==1,"verified extraction call installs");
        bool only=true;for(std::size_t i=0;i<image.size();++i)if(image[i]!=original[i]&&(i<0x5c003a||i>=0x5c003f))only=false;
        Check(only,"native resource decoder, parser, cleanup and shared file code stay unmodified");
        Writes=0;Check(InstallImage(image.data(),image.size(),hook,Write)&&Writes==0,"same hook installation is idempotent");
        Check(!InstallImage(image.data(),image.size(),hook+16,Write)&&Writes==0,"foreign hook is not overwritten");
    }
    void RejectedInputs(const std::vector<unsigned char>& original)
    {
        const std::vector<std::vector<unsigned char>> invalid{
            {},{0xef,0xbb,0xbf,'1','>','x'}, {0xff,0xfe,'1',0}, {0xfe,0xff,0,'1'},
            {'1','>',0xe4,0xb8,0xad,0xe6,0x96,0x87}, {'1','>',0x81,0x7f}, {'1','>',0,'x'},
            std::vector<unsigned char>(16U*1024U*1024U+1,'a')};
        for(const auto& data:invalid){
            Save(Source,data);Save(Stage,{'s','e','n','t','i','n','e','l'});
            Check(Extract(StageAnsi.c_str())==-1&&Read(Stage)==std::vector<unsigned char>({'s','e','n','t','i','n','e','l'}),"empty, Unicode, malformed CP936, NUL and oversized inputs reject before touching staging");
            Check(Read(Source)==data,"rejected external input is never rewritten or removed");}
        Save(Source,original);
        HANDLE locked=CreateFileW(Source.c_str(),GENERIC_READ,0,nullptr,OPEN_EXISTING,0,nullptr);
        Check(locked!=INVALID_HANDLE_VALUE&&Extract(StageAnsi.c_str())==-1,"existing locked input rejects instead of falling back");
        if(locked!=INVALID_HANDLE_VALUE)CloseHandle(locked);
        const auto badOutput=(Stage.parent_path()/L"missing"/L"dnf.str").string();
        Check(Extract(badOutput.c_str())==-1,"staging failure is reported");
        Check(dfl::chapter_two::external_string_table_detail::ExtractForExecutable(nullptr,StageAnsi.c_str(),Resolve,nullptr)==-1&&
            dfl::chapter_two::external_string_table_detail::ExtractForExecutable(FixtureExecutable.c_str(),nullptr,Resolve,nullptr)==-1&&
            dfl::chapter_two::external_string_table_detail::ExtractForExecutable(FixtureExecutable.c_str(),StageAnsi.c_str(),nullptr,nullptr)==-1,"invalid adapter arguments fail closed");
        Check(dfl::chapter_two::external_string_table_detail::ExtractForExecutable(L"relative.exe",StageAnsi.c_str(),Resolve,nullptr)==-1&&
            dfl::chapter_two::external_string_table_detail::ExtractForExecutable(L"C:relative.exe",StageAnsi.c_str(),Resolve,nullptr)==-1,
            "relative executable paths cannot silently choose the process CWD");
        std::vector<unsigned char> maximum(16U*1024U*1024U,'a');
        Save(Source,maximum);
        Check(Extract(StageAnsi.c_str())==1&&Read(Stage)==maximum,"exact 16 MiB limit is accepted without truncation");
        std::filesystem::remove(Source);
        const auto resources=Resources;Resources=nullptr;
        Check(Extract(StageAnsi.c_str())==-1,"missing source preserves the original extractor resource failure");
        Resources=resources;
    }
#endif
}

int wmain(int argc,wchar_t** argv)
{
    if(argc!=3){std::fprintf(stderr,"usage: external_string_table_tests IMAGE NEW_OUTPUT_DIRECTORY\n");return 2;}
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    const auto original=LoadImage(argv[1]);if(original.empty())return 2;
    const auto output=std::filesystem::absolute(argv[2]);
    if(std::filesystem::exists(output)){std::fprintf(stderr,"fixture output must not already exist\n");return 2;}
    FixtureExecutable=output/L"Game"/L"s1c2_chapter2_host-fixture.exe";
    Source=FixtureExecutable.parent_path()/L"Plugin"/L"dnf.str";
    Stage=output/L"Container"/L"dnf.str";StageAnsi=Stage.string();
    std::filesystem::create_directories(Source.parent_path());std::filesystem::create_directories(Stage.parent_path());
    std::filesystem::current_path(Stage.parent_path());
    Resources=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE|LOAD_LIBRARY_AS_IMAGE_RESOURCE);
    if(!Resources){std::fprintf(stderr,"could not open native resources\n");return 2;}
    const auto resource=FindResourceA(Resources,MAKEINTRESOURCEA(115),"File");
    const auto resourceSize=SizeofResource(Resources,resource);
    const auto* raw=static_cast<const unsigned char*>(LockResource(LoadResource(Resources,resource)));
    if(!raw||resourceSize!=118208)return 2;
    std::vector<unsigned char> decoded(raw,raw+resourceSize);for(auto& b:decoded)b^=0xbd;
    Save(output/L"decoded-native-dnf.str",decoded);
    auto custom=decoded;const std::string needle="187>";
    auto begin=std::search(custom.begin(),custom.end(),needle.begin(),needle.end());
    if(begin==custom.end())return 2;
    const auto end=std::find(begin,custom.end(),'\n');
    const std::string newLine="187>C2 External fixture <O>\r";
    const auto index=begin-custom.begin();custom.erase(begin,end);custom.insert(custom.begin()+index,newLine.begin(),newLine.end());
#ifndef CHAPTER_TWO_EXTERNAL_STRINGS_BASELINE
    Installation(original);
#endif
    auto* mapped=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_COMMIT|MEM_RESERVE,PAGE_EXECUTE_READWRITE));
    const bool allocated=mapped!=nullptr;
    if(!mapped){const auto first=reinterpret_cast<std::uintptr_t>(externalStringsFixtureReserve);DWORD old=0;
        if(first<=0x400000&&0x400000+ImageSize<=first+sizeof(externalStringsFixtureReserve)&&VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))mapped=reinterpret_cast<unsigned char*>(0x400000);}
    if(mapped!=reinterpret_cast<void*>(0x400000))return 2;
    std::memcpy(mapped,original.data(),original.size());
#ifndef CHAPTER_TWO_EXTERNAL_STRINGS_BASELINE
    Check(dfl::chapter_two::external_string_table_detail::InstallImage(mapped,ImageSize,reinterpret_cast<std::uintptr_t>(&Extract),Write),"install call into executable native fixture");
#endif
    // Only platform resource/file/heap boundaries are replaced. The original
    // extraction, XOR loop, string objects, parser, lookup and cleanup execute.
    Iat(0xaea33c,&VirtualQuery);Iat(0xaea338,&FindResourceProxy);Iat(0xaea334,&LoadResourceProxy);
    Iat(0xaea330,&LockResource);Iat(0xaea32c,&SizeResourceProxy);Iat(0xaea2bc,&DeleteFileA);
    Iat(0xaea3cc,&NoMessageBox);Iat(0xaea404,&GetActiveWindow);
    Jump(0x4016b0,reinterpret_cast<std::uintptr_t>(&Resolve));
    Jump(0xa610d0,reinterpret_cast<std::uintptr_t>(&Open));
    Jump(0xa61023,reinterpret_cast<std::uintptr_t>(&std::fclose));
    Jump(0xa61531,reinterpret_cast<std::uintptr_t>(&std::fwrite));
    Jump(0xa62c28,reinterpret_cast<std::uintptr_t>(&std::fgets));
    Jump(0xa60605,reinterpret_cast<std::uintptr_t>(&std::atoi));
    Jump(0xa5da4a,reinterpret_cast<std::uintptr_t>(&std::malloc));
    Jump(0xa5da6e,reinterpret_cast<std::uintptr_t>(&std::malloc));
    Jump(0xa5f27d,reinterpret_cast<std::uintptr_t>(&std::free));
    Jump(0xa60854,reinterpret_cast<std::uintptr_t>(&std::free));
    *reinterpret_cast<const char**>(0xd49ab8)=StageAnsi.c_str();
    const std::string builtin=Lookup(187);
    Check(builtin=="\xcf\xb5\xcd\xb3\xc9\xe8\xd6\xc3 <O>","missing external table executes native File115 decode and parser");
    Check(!std::filesystem::exists(Stage),"native successful lookup removes its staged table");
    Clear();Save(Source,custom);
    const std::string value=Lookup(187);
    Check(value=="C2 External fixture <O>","complete external table replaces selected text through original parser and lookup");
    Check(std::string(Lookup(186))=="\xce\xef\xc6\xb7\xc0\xb8 <I>","unmodified neighboring key retains native text");
    Check(Read(Source)==custom&&!std::filesystem::exists(Stage),"native cleanup deletes only staging and preserves external source");
    Clear();
#ifndef CHAPTER_TWO_EXTERNAL_STRINGS_BASELINE
    RejectedInputs(decoded);
    Save(Source,decoded);
    Check(Extract(StageAnsi.c_str())==1&&Read(Stage)==decoded,"valid full CP936 source stages byte for byte from executable-root Plugin, independent of CWD");
#endif
    std::filesystem::remove(Source);
    Check(std::string(Lookup(187))==builtin,"removing optional override restores original native text on a fresh table load");Clear();
    std::filesystem::remove(Source.parent_path());
    Check(std::string(Lookup(187))==builtin,"absent Plugin directory also preserves native resource fallback");Clear();
    if(allocated)VirtualFree(mapped,0,MEM_RELEASE);FreeLibrary(Resources);
    std::printf("external strings: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
