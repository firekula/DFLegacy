#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>
#ifndef CHAPTER_TWO_CREATURE_TAB_BASELINE
#include "ChapterTwoCreatureTabs.h"
#endif
#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char creatureFixtureReserve[0x1300000]{1};
namespace
{
    constexpr std::size_t ImageSize=0xe57000;
    unsigned Checks=0,Failures=0,Writes=0,FailAt=0;bool FailAfter=false;
    void Check(bool ok,const char* text){++Checks;if(!ok){++Failures;std::fprintf(stderr,"FAIL creature tab: %s\n",text);}}
    template<class T> T& At(std::uintptr_t address){return *reinterpret_cast<T*>(address);}
    template<class T> T& Field(void* object,std::size_t offset){return *reinterpret_cast<T*>(static_cast<unsigned char*>(object)+offset);}
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
    unsigned Closes=0,Adds=0,Sounds=0;
    void __fastcall Close(void*,void*,int,int){++Closes;}
    void __fastcall Tooltip(void*,void*,int){}
    void __fastcall Add(void*,void*,void*){++Adds;}
    void __cdecl Sound(const char*,int,int,int){++Sounds;}
    bool __fastcall Allow(void*,void*){return true;}
    unsigned __fastcall ScrollPosition(void*,void*){return 2;}
    std::array<unsigned char,0x1378> Manager{};
    std::array<unsigned char,0x44> Main{};
    std::array<unsigned char,0x40> Avatar{};
    std::array<unsigned char,0xa4> Creature{};
    std::array<unsigned char,0x24> Disabled{},PetButton{},BackMain{},BackAvatar{},Root{};
    void Input(void* object){auto* table=Field<std::uintptr_t*>(object,0);reinterpret_cast<void(__thiscall*)(void*)>(table[2])(object);}
    void Prepare(){
        At<void*>(0xd7a8bc)=Root.data();At<void*>(0xd56d24)=Manager.data();
        Field<void*>(Manager.data(),0x80)=Main.data();Field<void*>(Manager.data(),0x84)=Avatar.data();Field<void*>(Manager.data(),0x88)=Creature.data();Field<void*>(Manager.data(),0x7c)=Root.data();
        Field<int>(Manager.data(),0xb8)=14;Field<int>(Manager.data(),0xbc)=0;
        Field<int>(Manager.data(),0x1328)=1;
        for(auto* o:{Main.data(),Avatar.data(),Creature.data()}){Field<void*>(o,4)=Root.data();Field<void*>(o,8)=Manager.data();Field<int>(o,12)=1;}
        Field<std::uintptr_t>(Main.data(),0)=0xc11534;Field<std::uintptr_t>(Avatar.data(),0)=0xc11570;Field<std::uintptr_t>(Creature.data(),0)=0xc115ac;
        for(const auto offset:{0x18,0x1c,0x20,0x24,0x28,0x30,0x34,0x38})Field<void*>(Main.data(),offset)=Disabled.data();
        Field<void*>(Main.data(),0x2c)=PetButton.data();Field<int>(Main.data(),0x40)=3;
        Field<void*>(Avatar.data(),0x14)=Disabled.data();Field<void*>(Avatar.data(),0x18)=PetButton.data();Field<void*>(Avatar.data(),0x1c)=Disabled.data();
        for(const auto offset:{0x20,0x24,0x28,0x30})Field<void*>(Avatar.data(),offset)=Disabled.data();
        for(const auto offset:{0x14,0x18,0x1c,0x20})Field<void*>(Creature.data(),offset)=Disabled.data();
        Field<void*>(Creature.data(),0x28)=BackAvatar.data();Field<void*>(Creature.data(),0x2c)=BackMain.data();
        for(auto* b:{PetButton.data(),BackMain.data(),BackAvatar.data()}){b[0x10]=b[0x11]=1;}
        Field<unsigned>(PetButton.data(),12)=200;Field<unsigned>(BackMain.data(),12)=301;Field<unsigned>(BackAvatar.data(),12)=302;
        Jump(0x8647a0,reinterpret_cast<std::uintptr_t>(&Close));Jump(0x865700,reinterpret_cast<std::uintptr_t>(&Tooltip));
        Jump(0xa3c7d0,reinterpret_cast<std::uintptr_t>(&Add));Jump(0x43c160,reinterpret_cast<std::uintptr_t>(&Sound));
        Jump(0x877c30,reinterpret_cast<std::uintptr_t>(&Allow));Jump(0x86fe10,reinterpret_cast<std::uintptr_t>(&Allow));
        Jump(0xa335a0,reinterpret_cast<std::uintptr_t>(&ScrollPosition));
        // Both source callbacks run in full, including their native tails.
        // Only the creature's unrelated post-tab interactions are cut off.
        const unsigned char petTail[]{0x5b,0x5f,0x5e,0x8b,0xe5,0x5d,0xc3};std::memcpy(reinterpret_cast<void*>(0x8750c2),petTail,sizeof(petTail));
    }
#ifndef CHAPTER_TWO_CREATURE_TAB_BASELINE
    void Installation(const std::vector<unsigned char>& original){
        using dfl::chapter_two::creature_tabs_detail::InstallImage;auto image=original;
        Writes=FailAt=0;Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&!InstallImage(image.data(),ImageSize,nullptr)&&Writes==0,"invalid installation arguments do not write");
        for(const auto offset:{0x81153cu,0x811578u,0x4679ebu,0x470ea8u,0x479020u,0x46fef2u,0x478b7bu,0x4736d0u,0x47505au,0x468ae1u}){
            image=original;image[offset]^=1;const auto before=image;Writes=0;
            Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"constructors, dispatchers, vtables, page2 admission and return paths must match");}
        for(unsigned at=1;at<=2;++at)for(bool after:{false,true}){image=original;Writes=0;FailAt=at;FailAfter=after;
            Check(!InstallImage(image.data(),image.size(),Write)&&image==original,"either slot failure restores the exact original vtables");}
        image=original;Writes=FailAt=0;Check(InstallImage(image.data(),image.size(),Write)&&Writes==2,"only two input callback slots are replaced");
        bool only=true;for(std::size_t i=0;i<image.size();++i)if(image[i]!=original[i])only&=(i>=0x81153c&&i<0x811540)||(i>=0x811578&&i<0x81157c);
        Check(only,"inventory selection store and all executable instructions remain unchanged");
        Writes=0;Check(InstallImage(image.data(),image.size(),Write)&&Writes==0,"callback installation is idempotent");
    }
#endif
}
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;const auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_CREATURE_TAB_BASELINE
    Installation(original);
#endif
    auto* image=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));const bool allocated=image!=nullptr;
    if(!image){const auto start=reinterpret_cast<std::uintptr_t>(creatureFixtureReserve);DWORD old=0;if(start<=0x400000&&0x400000+ImageSize<=start+sizeof(creatureFixtureReserve)&&VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))image=reinterpret_cast<unsigned char*>(0x400000);}
    if(!image)return 2;std::memcpy(image,original.data(),original.size());
#ifndef CHAPTER_TWO_CREATURE_TAB_BASELINE
    Check(dfl::chapter_two::InstallCreatureTabs(image,ImageSize),"production writer installs verified callback slots");
#endif
    Prepare();
    for(int source=0;source<2;++source){
        auto* panel=source==0?Main.data():Avatar.data();Field<int>(Manager.data(),0xbc)=source;At<unsigned>(0xd4dfbc)=200;
        const auto adds=Adds;Input(panel);Check(Field<int>(Manager.data(),0xbc)==2&&Adds==adds+1,"real source button dispatcher reaches registered native creature page2");
        Check(Field<int>(Main.data(),0x40)==3,"switching to creature preserves the user's backpack category");
        if(source==1)Check(Field<unsigned>(Avatar.data(),0x38)==2,"complete original avatar callback still updates its scrollbar state");
        Field<int>(Manager.data(),0xbc)=2;At<unsigned>(0xd4dfbc)=source==0?301u:302u;Input(Creature.data());
        Check(Field<int>(Manager.data(),0xbc)==source,"real creature return-tab dispatcher restores the requested original page");
        Check(Field<int>(Main.data(),0x40)==3,"returning from creature preserves the user's backpack category");
#ifndef CHAPTER_TWO_CREATURE_TAB_BASELINE
        for(unsigned mode=0;mode<5;++mode){Field<int>(Manager.data(),0xbc)=source;At<unsigned>(0xd4dfbc)=mode==0?999:200;PetButton[0x10]=mode==1?0:1;PetButton[0x11]=mode==2?0:1;
            Field<void*>(Creature.data(),4)=mode==3?nullptr:Root.data();Field<void*>(Manager.data(),0x80+4*source)=mode==4?nullptr:panel;
            Input(panel);Check(Field<int>(Manager.data(),0xbc)==source,"unrelated capture, hidden/disabled button, unready target or detached source does not open page2");
            Field<void*>(Manager.data(),0x80+4*source)=panel;Field<void*>(Creature.data(),4)=Root.data();PetButton[0x10]=PetButton[0x11]=1;}
#endif
    }
    if(allocated)VirtualFree(image,0,MEM_RELEASE);std::printf("creature tabs: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
