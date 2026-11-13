#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <limits>
#include <vector>
#ifndef CHAPTER_TWO_SHOP_LAYOUT_BASELINE
#include "ChapterTwoShopLayout.h"
#endif

#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char shopLayoutFixtureReserve[0x1300000]{1};
namespace {
constexpr std::size_t ImageSize=0xe57000,Table=0x818c10,ItemX=Table+16;
unsigned Checks=0,Failures=0,Writes=0,FailAt=0;
bool FailAfter=false;
void Check(bool value,const char* message){++Checks;if(!value){++Failures;std::fprintf(stderr,"FAIL shop layout: %s\n",message);}}
template<class T>T& Field(void* object,std::size_t offset){return *reinterpret_cast<T*>(static_cast<unsigned char*>(object)+offset);}
bool Fits(std::size_t at,std::size_t count,std::size_t size){return at<=size&&count<=size-at;}
std::vector<unsigned char> Read(const wchar_t* path){std::ifstream f(path,std::ios::binary);return {std::istreambuf_iterator<char>(f),{}};}
std::vector<unsigned char> Load(const wchar_t* path){
    const auto raw=Read(path);if(raw.size()<sizeof(IMAGE_DOS_HEADER))return {};
    const auto* d=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
    if(d->e_magic!=IMAGE_DOS_SIGNATURE||d->e_lfanew<0||!Fits(d->e_lfanew,sizeof(IMAGE_NT_HEADERS32),raw.size()))return {};
    const auto* n=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+d->e_lfanew);
    if(n->Signature!=IMAGE_NT_SIGNATURE||n->FileHeader.Machine!=IMAGE_FILE_MACHINE_I386||n->OptionalHeader.ImageBase!=0x400000||n->OptionalHeader.SizeOfImage!=ImageSize||!Fits(0,n->OptionalHeader.SizeOfHeaders,raw.size())||!Fits(0,n->OptionalHeader.SizeOfHeaders,ImageSize))return {};
    const auto* s=IMAGE_FIRST_SECTION(n);if(!Fits(reinterpret_cast<const unsigned char*>(s)-raw.data(),sizeof(*s)*n->FileHeader.NumberOfSections,raw.size()))return {};
    std::vector<unsigned char> image(ImageSize);std::memcpy(image.data(),raw.data(),n->OptionalHeader.SizeOfHeaders);
    for(unsigned i=0;i<n->FileHeader.NumberOfSections;++i){if(!Fits(s[i].VirtualAddress,s[i].SizeOfRawData,image.size())||!Fits(s[i].PointerToRawData,s[i].SizeOfRawData,raw.size()))return {};
        std::memcpy(image.data()+s[i].VirtualAddress,raw.data()+s[i].PointerToRawData,s[i].SizeOfRawData);}
    return image;
}
struct Frame{unsigned width,height,x,y,canvasWidth,canvasHeight;};
std::vector<Frame> ReadFrames(const wchar_t* path){
    const auto file=Read(path);if(file.size()<20||std::memcmp(file.data(),"NeoplePack_Bill\0",16))return {};
    const auto u32=[&](std::size_t at){unsigned v=0;if(Fits(at,4,file.size()))std::memcpy(&v,file.data()+at,4);return v;};
    const unsigned count=u32(16);if(!Fits(20,static_cast<std::size_t>(count)*264,file.size()))return {};
    for(unsigned i=0;i<count;++i){const std::size_t entry=20+static_cast<std::size_t>(i)*264;
        constexpr char name[]="sprite/interface/cashshop/cashshopbtn.img";
        if(std::memcmp(file.data()+entry+8,name,sizeof(name)))continue;
        const std::size_t start=u32(entry),size=u32(entry+4),end=start+size;
        if(!Fits(start,size,file.size())||size<32||std::memcmp(file.data()+start,"Neople Image File",17)||u32(start+24)!=1)return {};
        const unsigned frames=u32(start+28);if(frames>1024)return {};std::vector<Frame> result;std::size_t at=start+32;
        for(unsigned f=0;f<frames;++f){if(!Fits(at,8,end))return {};const auto color=u32(at);
            if(color==17){const auto link=u32(at+4);if(link>=result.size())return {};result.push_back(result[link]);at+=8;continue;}
            if(!Fits(at,36,end))return {};const auto length=u32(at+16);
            result.push_back({u32(at+8),u32(at+12),u32(at+20),u32(at+24),u32(at+28),u32(at+32)});at+=36;
            if(!Fits(at,length,end))return {};at+=length;
        }
        return at==end?result:std::vector<Frame>{};
    }
    return {};
}
void Jump(std::uintptr_t from,std::uintptr_t to){unsigned char b[]{0xe9,0,0,0,0};const auto d=static_cast<std::uint32_t>(to-from-5);std::memcpy(b+1,&d,4);std::memcpy(reinterpret_cast<void*>(from),b,5);}
bool Write(std::uintptr_t at,const void* data,std::size_t size){++Writes;if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(at),data,size);return Writes!=FailAt;}
std::array<unsigned char,0x36b0> Shop{};
std::array<std::array<unsigned char,0x84>,5> Buttons{};
std::array<std::array<unsigned,4>,5> Frames{};
unsigned Allocations=0;
void* __cdecl Allocate(unsigned size){if(size!=0x84||Allocations>=Buttons.size())return nullptr;return Buttons[Allocations++].data();}
void __fastcall Base(void* self,void*){Field<unsigned char>(self,0x11)=1;}
void __fastcall Derived(void*,void*){}
void __fastcall Resource(void* self,void*,void*,unsigned a,unsigned b,unsigned c,unsigned d,unsigned,unsigned){for(std::size_t i=0;i<Buttons.size();++i)if(Buttons[i].data()==self)Frames[i]={a,b,c,d};}
void __fastcall Add(void*,void*,void*){}
void* __fastcall Lookup(void*,void*,unsigned frame){return reinterpret_cast<void*>(frame+1);}
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
void Prepare(){
    Jump(0x403700,reinterpret_cast<std::uintptr_t>(&Allocate));Jump(0x51fbd0,reinterpret_cast<std::uintptr_t>(&Base));
    Jump(0xa113f0,reinterpret_cast<std::uintptr_t>(&Derived));Jump(0xa11420,reinterpret_cast<std::uintptr_t>(&Resource));
    Jump(0xa3c7d0,reinterpret_cast<std::uintptr_t>(&Add));Jump(0xa28c00,reinterpret_cast<std::uintptr_t>(&Lookup));
    // Preserve the actual native position setter at A3C600. End the group loop
    // at the next unrelated constructor instruction, just as mall creature tests do.
    *reinterpret_cast<unsigned char*>(0x94700c)=0xc3;
}
#ifndef CHAPTER_TWO_SHOP_LAYOUT_BASELINE
void Installation(const std::vector<unsigned char>& original){
    using dfl::chapter_two::shop_layout_detail::InstallImage;auto image=original;
    Writes=FailAt=0;
    Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&!InstallImage(image.data(),ImageSize,nullptr)&&
        !InstallImage(reinterpret_cast<unsigned char*>(std::numeric_limits<std::uintptr_t>::max()-ImageSize+1),ImageSize,Write)&&Writes==0,"invalid and overflowing image arguments do not write");
    for(std::size_t offset=Table;offset<Table+48;++offset){image=original;image[offset]^=2;const auto before=image;Writes=0;
        Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"every original table byte is verified before writing");}
    for(std::size_t offset=0x546f8e;offset<0x546fb4;++offset){image=original;image[offset]^=2;const auto before=image;Writes=0;
        Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"every constructor table-read context byte is verified");}
    for(bool after:{false,true}){image=original;Writes=0;FailAt=1;FailAfter=after;
        Check(!InstallImage(image.data(),image.size(),Write)&&image==original,"writer failure before or after copying restores the original coordinate");}
    image=original;Writes=FailAt=0;
    Check(InstallImage(image.data(),image.size(),Write)&&Writes==1,"verified original table accepts the single byte coordinate edit");
    unsigned differences=0;bool onlyItem=true;for(std::size_t i=0;i<image.size();++i)if(image[i]!=original[i]){++differences;onlyItem&=i==ItemX;}
    Check(onlyItem&&differences==1,"one coordinate byte changes; all pet frames and gift instructions are preserved");
    Writes=0;Check(InstallImage(image.data(),image.size(),Write)&&Writes==0,"installation is idempotent without rewriting");
    image[Table+8]^=1;const auto altered=image;
    Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==altered,"already patched item rejects an unknown neighboring coordinate");
    image=original;image[0x546fcd]=1;Writes=0;
    Check(InstallImage(image.data(),image.size(),Write)&&image[0x546fcd]==1,"independent creature visibility patch remains compatible");
}
#endif
}
int wmain(int argc,wchar_t** argv){
    if(argc<3)return 2;const auto original=Load(argv[1]);const auto resources=ReadFrames(argv[2]);
    if(original.empty()||resources.size()!=131)return 2;
#ifndef CHAPTER_TWO_SHOP_LAYOUT_BASELINE
    Installation(original);
#endif
    auto* image=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));const bool allocated=image!=nullptr;
    if(!image){const auto start=reinterpret_cast<std::uintptr_t>(shopLayoutFixtureReserve);DWORD old=0;if(start<=0x400000&&0x400000+ImageSize<=start+sizeof(shopLayoutFixtureReserve)&&VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))image=reinterpret_cast<unsigned char*>(0x400000);}
    if(!image)return 2;std::memcpy(image,original.data(),original.size());
#ifndef CHAPTER_TWO_SHOP_LAYOUT_BASELINE
    Check(dfl::chapter_two::InstallShopLayout(image,ImageSize),"production protected-memory writer accepts the original image");
    if(argc>=4){std::ofstream output(argv[3],std::ios::binary);output.write(reinterpret_cast<const char*>(image),ImageSize);Check(output.good(),"production patched image is exported for independent checks");}
#endif
    Prepare();Construct(Shop.data());
    Check(Allocations==5,"actual native constructor creates all five defined tabs");
    const int x0=Field<int>(Buttons[0].data(),0x14),x1=Field<int>(Buttons[1].data(),0x14),x2=Field<int>(Buttons[2].data(),0x14),x3=Field<int>(Buttons[3].data(),0x14),x5=Field<int>(Buttons[4].data(),0x14);
    Check(x2==383,"actual constructor and position setter place the item tab at x383");
    Check(x0==221&&x1==307&&x3==448&&x5==553,"all other native tab positions remain unchanged");
    bool ys=true;for(auto& b:Buttons)ys&=Field<int>(b.data(),0x18)==8;Check(ys,"all native tab y coordinates remain eight");
    Check(Frames[2]==std::array<unsigned,4>{12,13,15,17}&&Frames[3]==std::array<unsigned,4>{88,89,91,93},"original Chinese item and creature frame IDs remain intact");
    const auto& item=resources[12];const auto& pet=resources[88];
    Check(item.width==59&&item.x==8&&item.canvasWidth==79&&pet.width==61&&pet.x==7&&pet.canvasWidth==82,"real NPK frame metadata matches the independently decoded Chinese geometry");
    Check(x2+static_cast<int>(item.x+item.width)<=x3+static_cast<int>(pet.x),"real item and pet resource rectangles no longer overlap horizontally");
    std::printf("shop layout native x=%d, item right=%d, pet left=%d\n",x2,x2+item.x+item.width,x3+pet.x);
    if(allocated)VirtualFree(image,0,MEM_RELEASE);
    std::printf("shop layout: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
