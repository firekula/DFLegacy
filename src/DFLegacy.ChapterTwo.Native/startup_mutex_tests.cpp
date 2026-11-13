#include "ChapterTwoStartupMutex.h"
#include <array>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char startupMutexFixtureReserve[0x1300000]{1};

namespace
{
constexpr std::uintptr_t Base=0x400000, Entry=0x9bd930, Call=0x9bdef9;
constexpr std::size_t Size=0xe57000, FunctionBytes=0x668;
constexpr char OriginalName[]="dbefeuate_ccen_khxfor_lcar_blr";
unsigned Checks=0,Failures=0,Writes=0,FailAt=0,Constructors=0;
bool FailAfter=false;
std::vector<HANDLE> Handles;
std::vector<void*> Objects;
std::string FixturePrefix,LastName;
LPSECURITY_ATTRIBUTES LastAttributes=nullptr;
BOOL LastOwner=FALSE;

void Check(bool value,const char* text)
{
    ++Checks;
    if(!value){++Failures;std::fprintf(stderr,"FAIL startup mutex: %s\n",text);}
}
template<class T> T& At(std::uintptr_t address){return *reinterpret_cast<T*>(address);}
void Jump(std::uintptr_t address,const void* function)
{
    auto* bytes=reinterpret_cast<unsigned char*>(address);bytes[0]=0xe9;
    const auto rel=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(function)-address-5);
    std::memcpy(bytes+1,&rel,4);
}
std::vector<unsigned char> ReadImage(const char* path)
{
    std::ifstream file(path,std::ios::binary);
    const std::vector<unsigned char> raw((std::istreambuf_iterator<char>(file)),{});
    if(raw.size()<0x1000)return {};
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||static_cast<std::size_t>(dos->e_lfanew)>raw.size()-sizeof(IMAGE_NT_HEADERS32))return {};
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+dos->e_lfanew);
    if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.ImageBase!=Base||nt->OptionalHeader.SizeOfImage!=Size)return {};
    std::vector<unsigned char> image(Size);
    if(nt->OptionalHeader.SizeOfHeaders>raw.size())return {};
    std::memcpy(image.data(),raw.data(),nt->OptionalHeader.SizeOfHeaders);
    const auto* sections=IMAGE_FIRST_SECTION(nt);
    for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];
        if(s.VirtualAddress>Size||s.SizeOfRawData>Size-s.VirtualAddress||s.PointerToRawData>raw.size()||s.SizeOfRawData>raw.size()-s.PointerToRawData)return {};
        std::memcpy(image.data()+s.VirtualAddress,raw.data()+s.PointerToRawData,s.SizeOfRawData);}
    return image;
}
std::string FixtureName(const char* name)
{
    std::string result=FixturePrefix+(name?name:"");
    for(std::size_t i=FixturePrefix.size();i<result.size();++i)if(result[i]=='\\')result[i]='_';
    return result;
}
HANDLE WINAPI FixtureCreate(LPSECURITY_ATTRIBUTES attributes,BOOL owner,LPCSTR name)
{
    LastName=name?name:"<null>";LastAttributes=attributes;LastOwner=owner;
    const auto privateName=FixtureName(name);
    const auto handle=CreateMutexA(attributes,owner,name?privateName.c_str():nullptr);
    const auto error=GetLastError();
    if(handle)Handles.push_back(handle);
    SetLastError(error);return handle;
}
// Only allocator/string and post-admission object boundaries are substituted.
// The complete native 9BD930 constructor and its ERROR_ALREADY_EXISTS branch run.
void* __fastcall Append(void* text,void*,unsigned position,unsigned count,unsigned char value)
{
    auto* object=static_cast<unsigned char*>(text);
    auto& length=*reinterpret_cast<unsigned*>(object+20);
    auto& capacity=*reinterpret_cast<unsigned*>(object+24);
    Check(position==length&&count==1&&length<63,"native name appends one byte at its real end");
    if(position!=length||count!=1||length>=63)std::abort();
    char* buffer=capacity<16?reinterpret_cast<char*>(object+4):*reinterpret_cast<char**>(object+4);
    if(length+1>=16&&capacity<16){auto* next=static_cast<char*>(std::malloc(64));if(!next)std::abort();
        std::memcpy(next,buffer,length+1);*reinterpret_cast<char**>(object+4)=next;capacity=63;buffer=next;}
    buffer[length++]=static_cast<char>(value);buffer[length]=0;return text;
}
void __cdecl Free(void* memory){std::free(memory);}
void* __cdecl Allocate(unsigned bytes){Check(bytes==0x70,"native success allocates the original70-byte-hex owner object");auto* value=std::calloc(1,bytes);Objects.push_back(value);return value;}
void* __fastcall Construct(void* object,void*,HANDLE handle)
{
    ++Constructors;*static_cast<HANDLE*>(object)=handle;
    At<void*>(0xdfb9b8)=object;return object;
}
bool Write(std::uintptr_t address,const void* bytes,std::size_t size)
{
    ++Writes;if(Writes==FailAt&&!FailAfter)return false;
    std::memcpy(reinterpret_cast<void*>(address),bytes,size);return Writes!=FailAt;
}
using Context=dfl::chapter_two::startup_mutex_detail::Context;
Context MakeContext(wchar_t instance,bool privateHost=false,DWORD directory=1)
{
    Context c;c.ordinaryDesktop=true;c.instance=instance;c.volume=0x12345678;c.fileHigh=2;c.fileLow=directory;
    c.modulePath=L"F:\\FixtureGame\\s1c2_chapter2_";
    c.modulePath+=privateHost?L"testcontrol-":L"host-";c.modulePath+=instance;
    c.modulePath+=L"-20261113-120000-001-1.exe";
    c.sessionPath=L"F:\\FixtureGame\\Logs\\DFLegacy\\Sessions\\";c.sessionPath+=instance;c.sessionPath+=L"-20261113-120000-001-1";
    return c;
}
void BindFixture()
{
    Jump(0x864390,reinterpret_cast<const void*>(&Append));
    Jump(0xa5f27d,reinterpret_cast<const void*>(&Free));
    Jump(0xa5da4a,reinterpret_cast<const void*>(&Allocate));
    Jump(0x9bd8c0,reinterpret_cast<const void*>(&Construct));
    At<decltype(&FixtureCreate)>(0xaea324)=&FixtureCreate;
    At<decltype(&GetLastError)>(0xaea29c)=&GetLastError;
}
bool Native(const std::vector<unsigned char>& original,const Context& context)
{
    // Reset only this offline fixture's original function, never a game process.
    std::memcpy(reinterpret_cast<void*>(Entry),original.data()+Entry-Base,FunctionBytes);
    Constructors=0;
#ifndef STARTUP_MUTEX_BASELINE
    Writes=FailAt=0;FailAfter=false;
    Check(dfl::chapter_two::startup_mutex_detail::InstallImage(reinterpret_cast<unsigned char*>(Base),Size,context,&FixtureCreate,&Write),"verified candidate installs before the original native function");
#else
    (void)context;
#endif
    const bool result=reinterpret_cast<bool(__cdecl*)()>(Entry)();
    Check(LastAttributes==nullptr&&LastOwner==TRUE,"native original mutex arguments retained");
    Check(Constructors==(result?1u:0u),"actual native183 branch controls initialization, no forged success");
    return result;
}
#ifndef STARTUP_MUTEX_BASELINE
void VerifyNamesAndRouting()
{
    using namespace dfl::chapter_two::startup_mutex_detail;
    std::array<char,96>a{},b{},privateA{},other{};
    Check(MakeName(MakeContext(L'A'),a.data(),a.size()),"ordinary A context names a mutex");
    Check(MakeName(MakeContext(L'B'),b.data(),b.size())&&a!=b,"A/B names differ withoutPID or random data");
    Check(MakeName(MakeContext(L'A',true),privateA.data(),privateA.size())&&a==privateA,"formal and testcontrol same directory/instance share the name");
    Check(MakeName(MakeContext(L'A',false,7),other.data(),other.size())&&a!=other,"different directory identities remain independent");
    for(unsigned invalid=0;invalid<8;++invalid){auto c=MakeContext(L'A');
        if(invalid==0)c.ordinaryDesktop=false;if(invalid==1)c.instance=L'C';if(invalid==2)c.sessionPath=L"F:\\Other\\A-20261113-120000-001-1";
        if(invalid==3)c.modulePath=L"F:\\FixtureGame\\unrelated.exe";if(invalid==4)c.sessionPath[41]=L'Z';
        if(invalid==5)c.fileHigh=c.fileLow=0;if(invalid==6)c.modulePath.replace(c.modulePath.find(L"host-A"),6,L"host-B");
        if(invalid==7)c.sessionPath+=L"\\child";
        std::array<char,96> output{};Check(!MakeName(c,output.data(),output.size()),"invalid ordinary/session/instance/path/identity refuses scoping");}
    Check(!MakeName(MakeContext(L'A'),a.data(),2),"too-small scoped-name buffer is rejected");
    SECURITY_ATTRIBUTES attributes{sizeof(attributes),nullptr,FALSE};
    auto h=CreateForName(a.data(),&FixtureCreate,&attributes,FALSE,"unrelated-name");const auto first=GetLastError();
    Check(h&&first!=ERROR_ALREADY_EXISTS&&LastName=="unrelated-name"&&LastAttributes==&attributes&&LastOwner==FALSE,"unrelated name and security/owner parameters pass unchanged");
    h=CreateForName(a.data(),&FixtureCreate,&attributes,FALSE,"unrelated-name");
    Check(h&&GetLastError()==ERROR_ALREADY_EXISTS,"unrelated existing mutex keeps real183");
    h=CreateForName(a.data(),&FixtureCreate,nullptr,FALSE,OriginalName);
    Check(h&&LastName==OriginalName&&LastOwner==FALSE,"unexpected owner argument retains original name");
    h=CreateForName(a.data(),&FixtureCreate,nullptr,TRUE,nullptr);
    Check(h&&LastName=="<null>","unnamed mutex stays unnamed");
    const auto collision=FixtureName(other.data());auto event=CreateEventA(nullptr,FALSE,FALSE,collision.c_str());
    Check(event!=nullptr,"fixture owns an event with the candidate mutex name");
    h=CreateForName(other.data(),&FixtureCreate,nullptr,TRUE,OriginalName);
    Check(!h&&GetLastError()==ERROR_INVALID_HANDLE,"real kernel failure and LastError are preserved");if(event)CloseHandle(event);
}
void VerifyInstaller(const std::vector<unsigned char>& original)
{
    using namespace dfl::chapter_two::startup_mutex_detail;
    auto image=original;Writes=FailAt=0;
    Check(!InstallImage(nullptr,Size,MakeContext(L'A'),&FixtureCreate,&Write)&&
        !InstallImage(image.data(),Size-1,MakeContext(L'A'),&FixtureCreate,&Write)&&
        !InstallImage(image.data(),Size,MakeContext(L'A'),nullptr,&Write)&&
        !InstallImage(image.data(),Size,MakeContext(L'A'),&FixtureCreate,nullptr)&&Writes==0,"invalid installer inputs make no writes");
    for(const auto address:{Entry,std::uintptr_t{0x9bd95f},Call,std::uintptr_t{0x9bdf07},std::uintptr_t{0xa00edb}}){
        image=original;image[address-Base]^=1;const auto before=image;Writes=0;
        Check(!InstallImage(image.data(),Size,MakeContext(L'A'),&FixtureCreate,&Write)&&Writes==0&&image==before,"changed constructor/name/call/error/WinMain signatures reject before writes");}
    for(bool after:{false,true}){image=original;Writes=0;FailAt=1;FailAfter=after;
        Check(!InstallImage(image.data(),Size,MakeContext(L'A'),&FixtureCreate,&Write)&&image==original,"writer failure before/after copy restores original call");}
    image=original;Writes=FailAt=0;FailAfter=false;
    Check(InstallImage(image.data(),Size,MakeContext(L'A'),&FixtureCreate,&Write)&&Writes==1,"only one verified call-site write");
    unsigned differences=0;bool local=true;for(std::size_t n=0;n<image.size();++n)if(image[n]!=original[n]){++differences;if(n<Call-Base||n>=Call-Base+6)local=false;}
    Check(local&&differences>0&&differences<=6,"IAT, error branch and all other mutex calls remain byte-identical");
    Writes=0;Check(InstallImage(image.data(),Size,MakeContext(L'A'),&FixtureCreate,&Write)&&Writes==0,"same-context reinstall is a no-write success");
    Check(!InstallImage(image.data(),Size,MakeContext(L'B'),&FixtureCreate,&Write)&&Writes==0,"installed image cannot switch to another instance");
}
#endif
}
int main(int argc,char** argv)
{
    if(argc!=2)return 2;
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    const auto image=ReadImage(argv[1]);Check(image.size()==Size,"pinned native image maps for offline execution");if(image.empty())return 2;
    FixturePrefix="Local\\DFLegacy.C2.MutexFixture."+std::to_string(GetCurrentProcessId())+"."+std::to_string(GetTickCount64())+".";
    auto* mapped=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(Base),Size,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));
    const bool allocated=mapped!=nullptr;
    if(!mapped){const auto reserve=reinterpret_cast<std::uintptr_t>(startupMutexFixtureReserve);DWORD old=0;
        if(reserve<=Base&&Base+Size<=reserve+sizeof(startupMutexFixtureReserve)&&VirtualProtect(reinterpret_cast<void*>(Base),Size,PAGE_EXECUTE_READWRITE,&old))mapped=reinterpret_cast<unsigned char*>(Base);}
    Check(mapped==reinterpret_cast<void*>(Base),"offline native image obtains original base");if(!mapped)return 2;
    std::memcpy(mapped,image.data(),Size);BindFixture();
    Check(Native(image,MakeContext(L'A')),"native A reaches its original owner initialization");
    Check(Native(image,MakeContext(L'B')),"native B also reaches initialization while same-directory A remains alive");
    Check(!Native(image,MakeContext(L'A')),"duplicate same-directory A still takes original183 rejection");
#ifndef STARTUP_MUTEX_BASELINE
    Check(!Native(image,MakeContext(L'A',true)),"testcontrol A cannot evade an existing formal A");
    Check(Native(image,MakeContext(L'A',false,9)),"different game-directory identity can initialize A");
    VerifyNamesAndRouting();VerifyInstaller(image);
#endif
    for(auto h:Handles)CloseHandle(h);for(auto p:Objects)std::free(p);
    if(allocated)VirtualFree(mapped,0,MEM_RELEASE);
    std::printf("startup mutex checks=%u failures=%u\n",Checks,Failures);
    return Failures?1:0;
}
