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
#ifndef CHAPTER_TWO_JAR_BASELINE
#include "ChapterTwoJarCapacity.h"
#endif
#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char jarFixtureReserve[0x1300000]{1};
namespace {
constexpr std::size_t ImageSize=0xe57000;
unsigned Checks=0,Failures=0,Writes=0,FailAt=0;bool FailAfter=false;
void Check(bool ok,const char* label){++Checks;if(!ok){++Failures;std::fprintf(stderr,"FAIL jar capacity: %s\n",label);}}
template<class T>T& At(std::uintptr_t a){return *reinterpret_cast<T*>(a);}
template<class T>T& Field(void* p,std::size_t o){return *reinterpret_cast<T*>(static_cast<unsigned char*>(p)+o);}
    std::vector<unsigned char> Load(const wchar_t* path)
    {
        std::ifstream f(path,std::ios::binary);std::vector<unsigned char> raw{std::istreambuf_iterator<char>(f),{}};
        if(raw.size()<4096)return {};
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
        if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||static_cast<std::size_t>(dos->e_lfanew)>raw.size()-sizeof(IMAGE_NT_HEADERS32))return {};
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+dos->e_lfanew);
        if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.ImageBase!=0x400000||nt->OptionalHeader.SizeOfImage!=ImageSize||nt->OptionalHeader.SizeOfHeaders>4096)return {};
        const auto* sections=IMAGE_FIRST_SECTION(nt);
        if(reinterpret_cast<const unsigned char*>(sections+nt->FileHeader.NumberOfSections)>raw.data()+raw.size())return {};
        std::vector<unsigned char> image(ImageSize);std::memcpy(image.data(),raw.data(),nt->OptionalHeader.SizeOfHeaders);
        for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];
            if(s.VirtualAddress>ImageSize||s.SizeOfRawData>ImageSize-s.VirtualAddress||s.PointerToRawData>raw.size()||s.SizeOfRawData>raw.size()-s.PointerToRawData)return {};
            std::memcpy(image.data()+s.VirtualAddress,raw.data()+s.PointerToRawData,s.SizeOfRawData);}
        return image;
    }

bool Write(std::uintptr_t a,const void* b,std::size_t n){++Writes;if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(a),b,n);return Writes!=FailAt;}
void Jump(std::uintptr_t a,std::uintptr_t t){unsigned char b[]{0xe9,0,0,0,0};const auto d=static_cast<std::uint32_t>(t-a-5);std::memcpy(b+1,&d,4);std::memcpy(reinterpret_cast<void*>(a),b,5);}
std::array<unsigned char,0xd0> Jar{},Equipment{},Avatar{},Material{},Consumable{},Quest{};
std::array<unsigned char,0x30> ItemInstance{};
std::array<std::uintptr_t,6> Vtable{};
std::array<int,137> Free{};bool AvatarFull=false;unsigned Message=0;unsigned OriginalAvatarCalls=0;
void* __fastcall Lookup(void* catalog,void*,unsigned id){
if(catalog==reinterpret_cast<void*>(1)){if(id==100)return Equipment.data();if(id==101)return Avatar.data();}
if(catalog==reinterpret_cast<void*>(2)){if(id==7125)return Jar.data();if(id==102)return Material.data();if(id==103)return Consumable.data();if(id==104)return Quest.data();}
return nullptr;}
int __fastcall Empty(void*,void*,int category){return category>=0&&category<137&&Free[category]?category:-1;}
const char* __cdecl Text(unsigned id){Message=id;return "fixture";}
void __fastcall Dialog(void*,void*,unsigned,const char*,unsigned){}
bool __fastcall AvatarCheck(void*,void*){++OriginalAvatarCalls;return AvatarFull;}
void* Target=nullptr;unsigned BeforeEsp=0,AfterEsp=0,AfterEsi=0,AfterEbx=0,AfterEdi=0;unsigned Result=0;
__declspec(naked) void CallSite(){__asm {
pushad
mov ebx,12345678h
mov esi,23456789h
lea edi,ItemInstance
mov ecx,3456789ah
mov BeforeEsp,esp
call Target
movzx eax,al
mov Result,eax
mov AfterEsp,esp
mov AfterEsi,esi
mov AfterEbx,ebx
mov AfterEdi,edi
popad
ret
}}
bool Invoke(unsigned site){Target=reinterpret_cast<void*>(site+5+At<std::int32_t>(site+1));CallSite();
Check(BeforeEsp==AfterEsp&&AfterEsi==0x23456789&&AfterEbx==0x12345678&&AfterEdi==reinterpret_cast<unsigned>(ItemInstance.data()),"original caller callee-saved registers and stack intact");return Result!=0;}
void Table(std::vector<int>& values){Field<int*>(Jar.data(),0xa4)=values.data();Field<int*>(Jar.data(),0xa8)=values.data()+values.size();Field<int*>(Jar.data(),0xac)=values.data()+values.size();}
void Exercise(){
Vtable[5]=0x442a40;Field<void*>(ItemInstance.data(),0)=Vtable.data();Field<unsigned>(ItemInstance.data(),16)=7125;Field<unsigned>(Jar.data(),8)=7125;Field<int>(Jar.data(),0x88)=11;
Field<int>(Equipment.data(),0x8c)=9;Field<int>(Avatar.data(),0x8c)=0;Field<int>(Material.data(),0x88)=1;Field<int>(Consumable.data(),0x88)=10;Field<int>(Quest.data(),0x88)=7;
At<void*>(0xd56d28)=reinterpret_cast<void*>(3);At<void*>(0xd56cac)=reinterpret_cast<void*>(1);At<void*>(0xd56cb0)=reinterpret_cast<void*>(2);
Jump(0x9ce470,reinterpret_cast<std::uintptr_t>(&Lookup));Jump(0x46f820,reinterpret_cast<std::uintptr_t>(&Empty));
Jump(0x9bffe0,reinterpret_cast<std::uintptr_t>(&Text));Jump(0x8657d0,reinterpret_cast<std::uintptr_t>(&Dialog));Jump(0x8ebb80,reinterpret_cast<std::uintptr_t>(&AvatarCheck));
std::vector<int> values{100,1,100,100000,1};Table(values);Free.fill(0);Free[9]=1;AvatarFull=true;
Check(!Invoke(0x8ed09b),"weapon jar opens with material consumable quick full and equipment space");
Check(!Invoke(0x8ed0a6),"weapon-only jar ignores full avatar inventory");
Free[9]=0;Message=0;Check(Invoke(0x8ed09b)&&Message==767,"truly full equipment and quick slots still reject with native message");
Free[3]=1;Check(!Invoke(0x8ed09b),"one quick slot retains native fallback capacity");
#ifndef CHAPTER_TWO_JAR_BASELINE
values={100,1,101,100000,1};Table(values);Free.fill(0);AvatarFull=true;
Check(!Invoke(0x8ed09b)&&Invoke(0x8ed0a6),"avatar-only jar checks avatar capacity independently");
AvatarFull=false;Check(!Invoke(0x8ed0a6),"avatar space allows avatar reward");
values={102,1,100,100000,1,999,1,1};Table(values);Free[9]=1;
Check(!Invoke(0x8ed09b),"unreachable fallback and tail do not require unrelated space");
values={102,1,100,99999,1};Table(values);
Check(Invoke(0x8ed09b),"reachable material fallback requires material space");
Free[73]=1;Check(!Invoke(0x8ed09b),"mixed reachable reward categories all have room");
values={100,1,999,0,1,100,100000,1};Table(values);Free[73]=0;
Check(!Invoke(0x8ed09b),"zero chance missing item is not a reachable reward");
values={0,1,0,100000,100};Table(values);Free.fill(0);
Check(!Invoke(0x8ed09b)&&!Invoke(0x8ed0a6),"gold-only reward does not require an item slot");
for(int id:{102,103,104}){values={id,1,id,100000,1};Table(values);Free.fill(0);const int category=id==102?73:id==103?41:105;
Check(Invoke(0x8ed09b),"required stackable category full rejects");Free[category]=1;Check(!Invoke(0x8ed09b),"actual stackable category alone suffices");}
for(const auto& bad:std::vector<std::vector<int>>{{999,1,999,100000,1},{100,1,100,-1,1},{100,1,100,100000,0},{100,1,100,100000},{}}){values=bad;Table(values);Free.fill(0);Free[9]=1;
Check(Invoke(0x8ed09b),"unknown or malformed rewards retain original conservative checks");}
#endif
}
#ifndef CHAPTER_TWO_JAR_BASELINE
void Installation(const std::vector<unsigned char>& original){using dfl::chapter_two::jar_capacity_detail::InstallImage;
auto copy=original;Writes=FailAt=0;Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(copy.data(),ImageSize-1,Write)&&Writes==0,"invalid image rejected without writes");
for(unsigned at:{0x4ed070u,0x4ed09bu,0x4ed0a6u,0x4ebab0u,0x4ebb80u,0x6f820u,0x400ed7u}){copy=original;copy[at]^=1;Writes=0;Check(!InstallImage(copy.data(),ImageSize,Write)&&Writes==0,"unrecognized context rejected without writing");}
for(bool after:{false,true})for(unsigned fail:{1u,2u}){copy=original;Writes=0;FailAt=fail;FailAfter=after;Check(!InstallImage(copy.data(),ImageSize,Write)&&copy==original,"partial or failed two-call install rolls back");}
copy=original;Writes=FailAt=0;Check(InstallImage(copy.data(),ImageSize,Write)&&Writes==2,"recognized image changes two calls");
bool only=true;for(unsigned i=0;i<ImageSize;++i)if(copy[i]!=original[i]&&!((i>=0x4ed09b&&i<0x4ed0a0)||(i>=0x4ed0a6&&i<0x4ed0ab)))only=false;
Check(only,"only two five-byte capacity calls change");Writes=0;Check(InstallImage(copy.data(),ImageSize,Write)&&Writes==0,"repeat install idempotent");}
#endif
}
int wmain(int argc,wchar_t** argv){if(argc!=2)return 2;auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_JAR_BASELINE
Installation(original);
#endif
auto* image=reinterpret_cast<unsigned char*>(0x400000);DWORD old=0;auto start=reinterpret_cast<std::uintptr_t>(jarFixtureReserve);
if(start>0x400000||start+sizeof(jarFixtureReserve)<0x400000+ImageSize||!VirtualProtect(image,ImageSize,PAGE_EXECUTE_READWRITE,&old))return 2;
std::memcpy(image,original.data(),ImageSize);
#ifndef CHAPTER_TWO_JAR_BASELINE
Check(dfl::chapter_two::InstallJarCapacity(image,ImageSize),"production install on mapped game image");
#endif
Exercise();std::printf("jar capacity: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;}
