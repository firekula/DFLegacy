#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>
#ifndef CHAPTER_TWO_MALL_CREATURE_BASELINE
#include "ChapterTwoMallCreature.h"
#endif
#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char mallFixtureReserve[0x1300000]{1};
namespace {
constexpr std::size_t ImageSize=0xe57000;
unsigned Checks=0,Failures=0,Writes=0,FailAt=0;bool FailAfter=false;
void Check(bool ok,const char* text){++Checks;if(!ok){++Failures;std::fprintf(stderr,"FAIL mall creature: %s\n",text);}}
template<class T>T& Field(void* object,std::size_t offset){return *reinterpret_cast<T*>(static_cast<unsigned char*>(object)+offset);}
void Jump(std::uintptr_t from,std::uintptr_t to){unsigned char b[]{0xe9,0,0,0,0};const auto d=static_cast<std::uint32_t>(to-from-5);std::memcpy(b+1,&d,4);std::memcpy(reinterpret_cast<void*>(from),b,5);}
bool Write(std::uintptr_t at,const void* bytes,std::size_t size){++Writes;if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(at),bytes,size);return Writes!=FailAt;}
std::vector<unsigned char> Load(const wchar_t* path){
    std::ifstream f(path,std::ios::binary);std::vector<unsigned char> raw{std::istreambuf_iterator<char>(f),{}};
    if(raw.size()<4096)return {};const auto* d=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
    if(d->e_magic!=IMAGE_DOS_SIGNATURE||d->e_lfanew<0||static_cast<std::size_t>(d->e_lfanew)>raw.size()-sizeof(IMAGE_NT_HEADERS32))return {};
    const auto* n=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+d->e_lfanew);
    if(n->Signature!=IMAGE_NT_SIGNATURE||n->OptionalHeader.ImageBase!=0x400000||n->OptionalHeader.SizeOfImage!=ImageSize||n->OptionalHeader.SizeOfHeaders>4096)return {};
    std::vector<unsigned char> image(ImageSize);std::memcpy(image.data(),raw.data(),n->OptionalHeader.SizeOfHeaders);
    const auto* s=IMAGE_FIRST_SECTION(n);if(reinterpret_cast<const unsigned char*>(s+n->FileHeader.NumberOfSections)>raw.data()+raw.size())return {};
    for(unsigned i=0;i<n->FileHeader.NumberOfSections;++i){if(s[i].VirtualAddress>ImageSize||s[i].SizeOfRawData>ImageSize-s[i].VirtualAddress||s[i].PointerToRawData>raw.size()||s[i].SizeOfRawData>raw.size()-s[i].PointerToRawData)return {};
        std::memcpy(image.data()+s[i].VirtualAddress,raw.data()+s[i].PointerToRawData,s[i].SizeOfRawData);}
    return image;
}
std::array<unsigned char,0x36b0> Shop{};
std::array<std::array<unsigned char,0x84>,5> Buttons{};
std::array<unsigned char,0x84> Disabled{};
std::array<std::array<unsigned,4>,5> Frames{};
std::array<unsigned,10> Lookups{};
unsigned Allocations=0,LookupCount=0,Adds=0,Refreshes=0,PreviewCalls=0;
int PreviewVisible=-1;
void* __cdecl Allocate(unsigned size){if(size!=0x84||Allocations>=Buttons.size())return nullptr;return Buttons[Allocations++].data();}
void __fastcall Base(void* self,void*){Field<unsigned char>(self,0x11)=1;Field<unsigned>(self,0xc)=100+Allocations;}
void __fastcall Derived(void*,void*){}
void __fastcall Resource(void* self,void*,void*,unsigned a,unsigned b,unsigned c,unsigned d,unsigned,unsigned){
    for(std::size_t i=0;i<Buttons.size();++i)if(Buttons[i].data()==self)Frames[i]={a,b,c,d};
}
void __fastcall Add(void*,void*,void*){++Adds;}
void __fastcall Position(void*,void*,unsigned,unsigned){}
void* __fastcall Lookup(void*,void*,unsigned frame){if(LookupCount<Lookups.size())Lookups[LookupCount++]=frame;return reinterpret_cast<void*>(frame+1);}
void __fastcall Preview(void*,void*,int visible){++PreviewCalls;PreviewVisible=visible;}
void __fastcall AvatarPreview(void*,void*,int){}
void __fastcall Refresh(void*,void*){++Refreshes;}
void __cdecl Sound(const char*,int,int,int){}
__declspec(naked) void __cdecl Construct(void*){
    __asm {
        push ebp
        mov ebp,esp
        sub esp,0c0h
        push ebx
        push esi
        push edi
        mov esi,[ebp+8]
        mov eax,946de9h
        call eax
        pop edi
        pop esi
        pop ebx
        mov esp,ebp
        pop ebp
        ret
    }
}
__declspec(naked) void __cdecl DisableEmpty(void*){
    __asm {
        push ebp
        mov ebp,esp
        sub esp,20h
        push ebx
        push esi
        push edi
        mov esi,[ebp+8]
        mov eax,94770bh
        call eax
        pop edi
        pop esi
        pop ebx
        mov esp,ebp
        pop ebp
        ret
    }
}
void Prepare(){
    Jump(0x403700,reinterpret_cast<std::uintptr_t>(&Allocate));Jump(0x51fbd0,reinterpret_cast<std::uintptr_t>(&Base));
    Jump(0xa113f0,reinterpret_cast<std::uintptr_t>(&Derived));Jump(0xa11420,reinterpret_cast<std::uintptr_t>(&Resource));
    Jump(0xa3c7d0,reinterpret_cast<std::uintptr_t>(&Add));Jump(0xa3c600,reinterpret_cast<std::uintptr_t>(&Position));
    Jump(0xa28c00,reinterpret_cast<std::uintptr_t>(&Lookup));Jump(0x9450c0,reinterpret_cast<std::uintptr_t>(&Preview));
    Jump(0x945300,reinterpret_cast<std::uintptr_t>(&AvatarPreview));Jump(0x9436b0,reinterpret_cast<std::uintptr_t>(&Refresh));
    Jump(0x43c160,reinterpret_cast<std::uintptr_t>(&Sound));
    // Run the actual group constructor and empty-catalog loop. Only unrelated
    // subsequent constructor work is terminated at its instruction boundary.
    *reinterpret_cast<unsigned char*>(0x94700c)=0xc3;*reinterpret_cast<unsigned char*>(0x94776d)=0xc3;
    for(const auto offset:{0x68,0x2560,0x2564,0x2568,0x256c,0x2570,0x2574,0x2578})Field<void*>(Shop.data(),offset)=Disabled.data();
    Field<void*>(Shop.data(),0x74)=Disabled.data();
}
void ResetGroups(){
    Allocations=LookupCount=Adds=0;for(auto& b:Buttons)b.fill(0);Frames={};Lookups={};
    std::memset(Shop.data()+0x184,0,16*5);Construct(Shop.data());
}
void Input(){reinterpret_cast<void(__thiscall*)(void*)>(0x945700)(Shop.data());}
#ifndef CHAPTER_TWO_MALL_CREATURE_BASELINE
void Installation(const std::vector<unsigned char>& original){
    using dfl::chapter_two::mall_creature_detail::InstallImage;auto image=original;
    Writes=FailAt=0;Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&!InstallImage(image.data(),ImageSize,nullptr)&&Writes==0,"invalid installation arguments do not write");
    for(auto offset:{0x546e8cu,0x546fcdu,0x547758u,0x54595eu,0x543603u}){image=original;image[offset]^=2;const auto before=image;Writes=0;
        Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"resource, hide, empty-catalog, input and gift guards are verified before writes");}
    for(bool after:{false,true}){image=original;Writes=0;FailAt=1;FailAfter=after;Check(!InstallImage(image.data(),image.size(),Write)&&image==original,"writer failure before or after mutation restores hidden byte");}
    image=original;Writes=FailAt=0;Check(InstallImage(image.data(),image.size(),Write)&&Writes==1,"one visibility byte is restored");
    bool only=true;for(std::size_t i=0;i<image.size();++i)if(image[i]!=original[i])only&=i==0x546fcd;
    Check(only,"Chinese resource IDs, enabled state and all other code stay unchanged");Writes=0;
    Check(InstallImage(image.data(),image.size(),Write)&&Writes==0,"installation is idempotent");
}
#endif
}
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;const auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_MALL_CREATURE_BASELINE
    Installation(original);
#endif
    auto* image=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));const bool allocated=image!=nullptr;
    if(!image){const auto start=reinterpret_cast<std::uintptr_t>(mallFixtureReserve);DWORD old=0;if(start<=0x400000&&0x400000+ImageSize<=start+sizeof(mallFixtureReserve)&&VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))image=reinterpret_cast<unsigned char*>(0x400000);}
    if(!image)return 2;std::memcpy(image,original.data(),original.size());
#ifndef CHAPTER_TWO_MALL_CREATURE_BASELINE
    Check(dfl::chapter_two::InstallMallCreature(image,ImageSize),"production writer installs verified visibility byte");
#endif
    Prepare();ResetGroups();
    auto* pet=Field<unsigned char*>(Shop.data(),0x2524);
    Check(Allocations==5&&Adds==5,"actual native constructor creates only the five defined tabs");
    Check(pet&&pet[0x10]==1,"native creature tab is visible after construction");
    Check(Frames[3]==std::array<unsigned,4>{88,89,91,93}&&Lookups[6]==90&&Lookups[7]==92,"native Chinese creature frames and outlines are preserved");
    Check(Buttons[0][0x10]&&Buttons[1][0x10]&&Buttons[2][0x10]&&Buttons[4][0x10],"other native tabs remain visible");
    DisableEmpty(Shop.data());Check(pet[0x11]==0,"actual native empty-catalog loop still disables the creature tab");
    ResetGroups();pet=Field<unsigned char*>(Shop.data(),0x2524);
    Field<unsigned>(Shop.data(),0x1b8)=0x10000;Field<unsigned>(Shop.data(),0x1bc)=0x10058;
    DisableEmpty(Shop.data());Check(pet[0x11]==1&&Buttons[0][0x11]==0,"one native 88-byte creature product enables only its nonempty group");
    Field<unsigned>(Shop.data(),0x60)=2;*reinterpret_cast<unsigned*>(0xd4dfbc)=Field<unsigned>(pet,0xc);Input();
    Check(Field<unsigned>(Shop.data(),0x60)==3&&PreviewVisible==1&&Refreshes==1,"complete original input handler enters group3 and invokes its native preview and refresh paths");
    Buttons[2][0x11]=1;*reinterpret_cast<unsigned*>(0xd4dfbc)=Field<unsigned>(Buttons[2].data(),0xc);Input();
    Check(Field<unsigned>(Shop.data(),0x60)==2&&PreviewVisible==0&&Refreshes==2&&PreviewCalls==2,"complete original input handler returns from creature to items");
    if(allocated)VirtualFree(image,0,MEM_RELEASE);std::printf("mall creature: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
