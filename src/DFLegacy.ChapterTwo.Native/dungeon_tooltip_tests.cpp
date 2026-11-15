#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>
#ifndef CHAPTER_TWO_TOOLTIP_BASELINE
#include "ChapterTwoDungeonTooltip.h"
#endif

#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char tooltipFixtureReserve[0x1300000]{1};
namespace
{
    constexpr std::size_t ImageSize=0xe57000;
    unsigned Checks=0,Failures=0,TransitionTailCalls=0,RemovedTooltipModels=0;
    bool ExpectedReadFault=false;
    std::array<unsigned char,0x1400> Manager{};
    std::array<std::array<unsigned char,0x1000>,12> Models{};
    std::array<unsigned char,0x100> Window{};
    std::array<std::uintptr_t,2> Active{},Cached{};
    template<class T> T& At(std::uintptr_t address){return *reinterpret_cast<T*>(address);}
    template<class T> T& Field(void* address,std::size_t offset){return At<T>(reinterpret_cast<std::uintptr_t>(address)+offset);}
    void Check(bool ok,const char* label){++Checks;if(!ok){++Failures;std::fprintf(stderr,"FAIL tooltip: %s\n",label);}}
    void Jump(std::uintptr_t from,std::uintptr_t to)
    {unsigned char bytes[]{0xe9,0,0,0,0};const auto delta=static_cast<std::uint32_t>(to-from-5);std::memcpy(bytes+1,&delta,4);std::memcpy(reinterpret_cast<void*>(from),bytes,5);}
    LONG WINAPI Fault(EXCEPTION_POINTERS* p)
    {if(ExpectedReadFault||p->ExceptionRecord->ExceptionCode==0x40010006||p->ExceptionRecord->ExceptionCode==0x4001000a)return EXCEPTION_CONTINUE_SEARCH;
        std::fprintf(stderr,"NATIVE FAULT code=%08lx at=%08lx eip=%08lx esp=%08lx\n",p->ExceptionRecord->ExceptionCode,reinterpret_cast<DWORD>(p->ExceptionRecord->ExceptionAddress),p->ContextRecord->Eip,p->ContextRecord->Esp);std::fflush(stderr);return EXCEPTION_CONTINUE_SEARCH;}
    std::vector<unsigned char> Load(const wchar_t* path)
    {
        std::ifstream input(path,std::ios::binary);const std::vector<unsigned char> raw{std::istreambuf_iterator<char>(input),{}};
        if(raw.size()<4096)return {};const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
        if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||static_cast<std::size_t>(dos->e_lfanew)>raw.size()-sizeof(IMAGE_NT_HEADERS32))return {};
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+dos->e_lfanew);
        if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.ImageBase!=0x400000||nt->OptionalHeader.SizeOfImage!=ImageSize||nt->OptionalHeader.SizeOfHeaders>raw.size())return {};
        const auto* sections=IMAGE_FIRST_SECTION(nt);
        if(reinterpret_cast<const unsigned char*>(sections+nt->FileHeader.NumberOfSections)>raw.data()+raw.size())return {};
        std::vector<unsigned char> image(ImageSize);std::memcpy(image.data(),raw.data(),nt->OptionalHeader.SizeOfHeaders);
        for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];
            if(s.PointerToRawData>raw.size()||s.SizeOfRawData>raw.size()-s.PointerToRawData||s.VirtualAddress>ImageSize||s.SizeOfRawData>ImageSize-s.VirtualAddress)return {};
            std::memcpy(image.data()+s.VirtualAddress,raw.data()+s.PointerToRawData,s.SizeOfRawData);}
        return image;
    }
    __declspec(naked) void Return0(){__asm {xor eax,eax} __asm {ret}}
    __declspec(naked) void Return4(){__asm {xor eax,eax} __asm {ret 4}}
    __declspec(naked) void Return8(){__asm {xor eax,eax} __asm {ret 8}}
    void __fastcall RemoveModel(void*,void*,void* model)
    {if(model==Models[11].data())++RemovedTooltipModels;}
    void __fastcall Tail(void*,void*){++TransitionTailCalls;}
    void* __cdecl Allocate(std::size_t size){return std::malloc(size);}
    void __cdecl Release(void* memory){std::free(memory);}
    struct Registers{unsigned before,after,ebx,esi,edi,ebp;} Cpu{};
    __declspec(naked) void __cdecl Transition(int,int)
    {
        // Execute the actual native caller instructions, including argument order,
        // ECX load, unrelated frame timing update and the one candidate CALL.
        __asm {
            push ebp
            mov ebp,esp
            pushad
            mov Cpu.before,esp
            mov esi,[ebp+12]
            mov edi,[ebp+8]
            mov ebx,44556677h
            mov ebp,55667788h
            mov eax,0437906h
            call eax
            mov Cpu.after,esp
            mov Cpu.ebx,ebx
            mov Cpu.esi,esi
            mov Cpu.edi,edi
            mov Cpu.ebp,ebp
            mov esp,Cpu.before
            popad
            pop ebp
            ret
        }
    }
    void Vector(std::size_t offset,std::uintptr_t* data,std::size_t count,std::size_t cap)
    {Field<std::uintptr_t>(Manager.data(),offset)=reinterpret_cast<std::uintptr_t>(data);Field<std::uintptr_t>(Manager.data(),offset+4)=reinterpret_cast<std::uintptr_t>(data+count);Field<std::uintptr_t>(Manager.data(),offset+8)=reinterpret_cast<std::uintptr_t>(data+cap);}
    void Setup(bool window=true)
    {
        Manager.fill(0);for(auto& model:Models)model.fill(0);Window.fill(0);Active.fill(0);Cached.fill(0);
        At<void*>(0xd56d24)=Manager.data();At<void*>(0xdfa650)=nullptr;
        At<void*>(0xd56cf4)=Models[8].data();Field<void*>(Models[8].data(),0x2c)=Models[9].data();
        for(const auto pair:{std::pair<std::size_t,unsigned>{0x54,0},{0x60,1},{0x7c,2},{0x135c,3}})Field<void*>(Manager.data(),pair.first)=Models[pair.second].data();
        Field<void*>(Models[0].data(),8)=Models[4].data();
        for(const auto offset:{0xcu,0x10u,0x14u,0x98u,0xa8u})Field<void*>(Models[1].data(),offset)=Models[5].data();
        Field<unsigned>(Manager.data(),0xb8)=Field<unsigned>(Manager.data(),0xbc)=14;
        Field<unsigned>(Window.data(),0)=0xc138bc;Field<unsigned>(Window.data(),4)=0x44;
        Field<void*>(Window.data(),8)=Models[11].data();Field<void*>(Window.data(),0x10)=Manager.data();
        Active[0]=reinterpret_cast<std::uintptr_t>(Window.data());
        Vector(0x518,Active.data(),window?1:0,1);Vector(0xdd8,Cached.data(),0,2);
        Vector(0x1258,static_cast<std::uintptr_t*>(std::malloc(64*4)),0,64);
        At<int>(0xd31d74)=37;TransitionTailCalls=RemovedTooltipModels=0;
    }
    unsigned Pending(){return (Field<unsigned>(Manager.data(),0x125c)-Field<unsigned>(Manager.data(),0x1258))/4;}
    unsigned ActiveCount(){return (Field<unsigned>(Manager.data(),0x51c)-Field<unsigned>(Manager.data(),0x518))/4;}
    void Drain(){reinterpret_cast<void(__thiscall*)(void*)>(0x866cb0)(Manager.data());}
    void Boundaries()
    {
        // Native transition, kind-close, window-close, queue append, queue drain,
        // active erase, cache push, kind getter and dungeon-key reset are untouched.
        // Only unrelated UI controllers/render integration and CRT are replaced.
        At<unsigned char>(0x43792b)=0xc3;
        for(const auto p:{0x92a530u,0x921210u,0x9373d0u,0x92feb0u,0x435e20u})Jump(p,reinterpret_cast<std::uintptr_t>(&Return0));
        for(const auto p:{0x8f75b0u,0x9237c0u,0x93d080u,0x961330u,0x956820u,0x95cd40u,0x972f70u,0x974fe0u,0x954610u,0x945460u,0x93aaa0u,0x955cc0u,0x94f130u,0x93b630u,0x90ee20u,0x960380u,0x436110u,0x5349a0u,0x9233b0u,0x922ad0u,0x917e10u,0x90ef00u,0x96e7e0u,0x96c850u})Jump(p,reinterpret_cast<std::uintptr_t>(&Return4));
        for(const auto p:{0x922850u,0x92dca0u,0x937aa0u,0x8647a0u})Jump(p,reinterpret_cast<std::uintptr_t>(&Return8));
        Jump(0x43c350,reinterpret_cast<std::uintptr_t>(&Return0));Jump(0xa602ad,reinterpret_cast<std::uintptr_t>(&Return0));
        Jump(0x935cb0,reinterpret_cast<std::uintptr_t>(&Tail));
        Jump(0xa3c580,reinterpret_cast<std::uintptr_t>(&RemoveModel));Jump(0xa3c7d0,reinterpret_cast<std::uintptr_t>(&Return4));
        Jump(0xa5ef40,reinterpret_cast<std::uintptr_t>(&std::memmove));
        for(const auto p:{0xa5da4au,0xa5da6eu,0xa60448u})Jump(p,reinterpret_cast<std::uintptr_t>(&Allocate));
        for(const auto p:{0xa5f27du,0xa60854u,0xa5fe78u})Jump(p,reinterpret_cast<std::uintptr_t>(&Release));
    }
    void Exercise()
    {
        Setup();Transition(2,1);
        Check(Cpu.before==Cpu.after&&Cpu.ebx==0x44556677&&Cpu.esi==1&&Cpu.edi==2&&Cpu.ebp==0x55667788,"native caller and transition preserve ESP and nonvolatile registers");
        Check(TransitionTailCalls==1,"original complete interface transition reaches its final controller update");
        Check(Pending()==1,"selection to town queues existing native class 0x44 tooltip");
        Check(ActiveCount()==1&&At<int>(0xd31d74)==37,"cleanup stays deferred until native queue processing");
        std::printf("native 2->1 before drain: active=%u pending=%u key=%d\n",ActiveCount(),Pending(),At<int>(0xd31d74));
        Drain();
        Check(ActiveCount()==0,"native queue drain removes the active tooltip");
        Check(At<int>(0xd31d74)==-1,"native virtual close resets cached dungeon id");
        Check(RemovedTooltipModels==1,"native removal detaches exactly the tooltip render model");
        std::printf("native 2->1 after drain: active=%u pending=%u key=%d detached=%u\n",ActiveCount(),Pending(),At<int>(0xd31d74),RemovedTooltipModels);
        for(const auto scenes:{std::pair<int,int>{1,1},{2,2},{2,3},{2,10}}){Setup();Transition(scenes.first,scenes.second);
            Check(Pending()==0&&ActiveCount()==1&&At<int>(0xd31d74)==37,"other interface transitions receive no extra tooltip cleanup");Drain();}
        Setup(false);Transition(2,1);Check(Pending()==0,"empty tooltip vector remains empty");Drain();
        Setup();reinterpret_cast<void(__thiscall*)(void*)>(0x88bb80)(Window.data());
        Transition(2,1);Check(Pending()==1,"already queued tooltip is not queued twice");Drain();
        Check(ActiveCount()==0&&At<int>(0xd31d74)==-1,"already queued tooltip still completes native removal");
        // The full scene coordinator prepares new-scene 3/10 before calling
        // the interface transition. Execute its existing class-44 close blocks.
        for(const auto fragment:{0x436f90u,0x437022u}){Setup();
            At<unsigned char>(fragment+7)=0xc3;
            reinterpret_cast<void(__thiscall*)(void*)>(fragment)(Manager.data());
            Check(Pending()==1,"native dungeon-entry prepare already queues class 44");Drain();
            Check(ActiveCount()==0&&At<int>(0xd31d74)==-1,"existing normal dungeon-entry close still removes tooltip");}
#ifndef CHAPTER_TWO_TOOLTIP_BASELINE
        Setup();std::free(Field<void*>(Manager.data(),0x1258));
        std::memset(Manager.data()+0x1258,0,12);Transition(2,1);
        Check(Pending()==1,"empty native pending vector allocates and queues tooltip through native growth path");Drain();
        Check(ActiveCount()==0&&At<int>(0xd31d74)==-1&&RemovedTooltipModels==1,"native allocation-path queue drains and resets tooltip normally");
        Setup();Transition(2,1);Transition(2,1);Check(Pending()==1,"repeated selection-to-town notifications do not duplicate deferred close");Drain();
        Check(ActiveCount()==0&&RemovedTooltipModels==1,"repeated cleanup still removes only one tooltip");
        Transition(2,1);Check(Pending()==0,"later transition after removal does not recreate tooltip queue");Drain();
#endif
    }
#ifndef CHAPTER_TWO_TOOLTIP_BASELINE
    unsigned Writes=0,FailAt=0,CloseCalls=0;bool FailAfter=false,SkipCopy=false,PartialCopy=false;
    bool Write(std::uintptr_t address,const void* data,std::size_t size)
    {
        ++Writes;if(Writes==FailAt&&!FailAfter)return false;
        if(!SkipCopy)std::memcpy(reinterpret_cast<void*>(address),data,PartialCopy&&Writes==1?2:size);
        if(SkipCopy&&Writes>1)std::memcpy(reinterpret_cast<void*>(address),data,size);
        return Writes!=FailAt;
    }
    void Installation(const std::vector<unsigned char>& original)
    {
        using dfl::chapter_two::dungeon_tooltip_detail::InstallImage;
        auto image=original;
        Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&!InstallImage(image.data(),ImageSize,nullptr)&&Writes==0,"invalid install inputs cause no writes");
        Check(!dfl::chapter_two::InstallDungeonTooltip(image.data(),image.size()),"public installer rejects relocated image");
        struct Range{unsigned offset,size;};
        for(const auto range:{Range{0x37906,0x20},Range{0x37926,5},Range{0x3792b,6},Range{0x465970,0x6d4},Range{0x48bb80,0xa},Range{0x46d960,0x54},Range{0x4dea70,0xb},Range{0x8138bc,0x3c},Range{0x8126c0,4},Range{0x491590,0x58},Range{0x465600,0xf2},Range{0x466cb0,0x89},Range{0x5b8c63,0x10}})
            for(unsigned n=0;n<range.size;++n){image[range.offset+n]^=1;Writes=0;
                Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0,"every native context byte mutation rejects installation without writes");image[range.offset+n]^=1;}
        for(bool after:{false,true}){image=original;Writes=0;FailAt=1;FailAfter=after;
            Check(!InstallImage(image.data(),image.size(),Write)&&image==original,"failed patch rolls back including after-copy failure");}
        FailAt=0;
        for(bool partial:{false,true}){image=original;Writes=0;SkipCopy=!partial;PartialCopy=partial;
            Check(!InstallImage(image.data(),image.size(),Write)&&Writes==2&&image==original,"silent and partial writer success are detected and rolled back");}
        SkipCopy=PartialCopy=false;image=original;Writes=0;
        Check(InstallImage(image.data(),image.size(),Write)&&Writes==1,"supported native image installs exactly one call");
        bool only=true;for(std::size_t n=0;n<image.size();++n)if(image[n]!=original[n]&&(n<0x37926||n>=0x3792b))only=false;
        Check(only,"only the five-byte interface transition call changes");Writes=0;
        Check(InstallImage(image.data(),image.size(),Write)&&Writes==0,"same hook installs idempotently without writes");
        image[0x4dea70]^=1;Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0,"idempotent installation still verifies reset context");
    }
    struct ReadFault{unsigned managerReads=0;bool changedManager=false,changedCapacity=false,deny=false;};
    bool Read(void* opaque,std::uintptr_t address,void* output,std::size_t size) noexcept
    {
        auto& state=*static_cast<ReadFault*>(opaque);if(state.deny)return false;
        ExpectedReadFault=true;
        __try{std::memcpy(output,reinterpret_cast<const void*>(address),size);}__except(EXCEPTION_EXECUTE_HANDLER){ExpectedReadFault=false;return false;}
        ExpectedReadFault=false;
        if(address==0xd56d24){++state.managerReads;if(state.changedManager&&state.managerReads==2)*static_cast<unsigned*>(output)+=4;}
        if(address==reinterpret_cast<std::uintptr_t>(Manager.data())+0x518&&state.changedCapacity&&state.managerReads==2)static_cast<unsigned*>(output)[2]+=4;
        return true;
    }
    bool Close(void*,std::uintptr_t window) noexcept{++CloseCalls;return window==reinterpret_cast<std::uintptr_t>(Window.data());}
    void Guards()
    {
        using namespace dfl::chapter_two::dungeon_tooltip_detail;
        Setup();ReadFault read;const auto manager=reinterpret_cast<std::uintptr_t>(Manager.data());
        const Reader reader{&read,Read};
        for(const auto scenes:{std::pair<int,int>{1,1},{2,2},{2,3},{3,1},{-1,1},{2,-1}})
            Check(Cleanup(scenes.first,scenes.second,0,{},nullptr,nullptr)==Result::NotApplicable,"only exact scene 2 to 1 enters guarded cleanup");
        auto reject=[&](const char* label){read={};CloseCalls=0;Check(Cleanup(2,1,manager,reader,nullptr,Close)==Result::Unavailable&&CloseCalls==0,label);};
        Check(Cleanup(2,1,manager,{},nullptr,Close)==Result::Unavailable,"null reader fails closed");
        At<void*>(0xd56d24)=nullptr;reject("manager mismatch fails closed");At<void*>(0xd56d24)=Manager.data();
        for(const unsigned value:{0u,0xc1379cu}){Field<unsigned>(Window.data(),0)=value;reject("invalid or C1-like class 3f vtable is never closed");}Field<unsigned>(Window.data(),0)=0xc138bc;
        Field<unsigned>(Window.data(),4)=0x3f;reject("wrong window kind fails closed");Field<unsigned>(Window.data(),4)=0x44;
        Field<void*>(Window.data(),0x10)=nullptr;reject("wrong window owner fails closed");Field<void*>(Window.data(),0x10)=Manager.data();
        for(const auto invalid:{0u,0xfffffffcu}){Active[0]=invalid;reject("invalid object pointers fail closed");}Active[0]=reinterpret_cast<std::uintptr_t>(Window.data());
        const auto savedActive=std::array<unsigned,3>{Field<unsigned>(Manager.data(),0x518),Field<unsigned>(Manager.data(),0x51c),Field<unsigned>(Manager.data(),0x520)};
        for(const auto edit:{std::pair<unsigned,unsigned>{0x518,savedActive[0]+1},{0x51c,savedActive[0]-4},{0x51c,savedActive[0]+8},{0x520,savedActive[0]},{0x520,savedActive[2]+1},{0x518,0}}){Field<unsigned>(Manager.data(),edit.first)=edit.second;reject("malformed or oversized active vector fails closed");std::memcpy(Manager.data()+0x518,savedActive.data(),12);}
        const auto savedPending=std::array<unsigned,3>{Field<unsigned>(Manager.data(),0x1258),Field<unsigned>(Manager.data(),0x125c),Field<unsigned>(Manager.data(),0x1260)};
        for(const auto edit:{std::pair<unsigned,unsigned>{0x1258,savedPending[0]+1},{0x125c,savedPending[0]-4},{0x125c,savedPending[0]+260},{0x1260,savedPending[0]-4},{0x1258,0}}){Field<unsigned>(Manager.data(),edit.first)=edit.second;reject("malformed or oversized pending vector fails closed");std::memcpy(Manager.data()+0x1258,savedPending.data(),12);}
        read={};read.deny=true;CloseCalls=0;Check(Cleanup(2,1,manager,reader,nullptr,Close)==Result::Unavailable&&CloseCalls==0,"failed memory read never closes a window");
        read={};read.changedManager=true;Check(Cleanup(2,1,manager,reader,nullptr,Close)==Result::Unavailable&&CloseCalls==0,"manager changing during snapshot fails closed");
        read={};read.changedCapacity=true;Check(Cleanup(2,1,manager,reader,nullptr,Close)==Result::Unavailable&&CloseCalls==0,"vector capacity changing during snapshot fails closed");
        read={};Check(Cleanup(2,1,manager,reader,nullptr,nullptr)==Result::CloseFailed,"missing close callback reports failure");
        read={};Check(Cleanup(2,1,manager,reader,nullptr,Close)==Result::CloseRequested&&CloseCalls==1,"valid guarded object requests one close");
        reinterpret_cast<void(__thiscall*)(void*)>(0x88bb80)(Window.data());read={};CloseCalls=0;
        Check(Cleanup(2,1,manager,reader,nullptr,Close)==Result::AlreadyQueued&&CloseCalls==0,"existing pending identity avoids duplicate native enqueue");Drain();
    }
#endif
}
int wmain(int argc,wchar_t** argv)
{
    if(argc!=2)return 2;SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);AddVectoredExceptionHandler(1,Fault);
    const auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_TOOLTIP_BASELINE
    Installation(original);
#endif
    auto* image=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));
    const bool allocated=image!=nullptr;
    if(!image){const auto first=reinterpret_cast<std::uintptr_t>(tooltipFixtureReserve);DWORD old=0;
        if(first<=0x400000&&0x400000+ImageSize<=first+sizeof(tooltipFixtureReserve)&&VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))image=reinterpret_cast<unsigned char*>(0x400000);}
    if(!image)return 2;std::memcpy(image,original.data(),original.size());
#ifndef CHAPTER_TWO_TOOLTIP_BASELINE
    Check(dfl::chapter_two::InstallDungeonTooltip(image,ImageSize),"public installer patches native image");
#endif
    Boundaries();Exercise();
#ifndef CHAPTER_TWO_TOOLTIP_BASELINE
    Guards();
#endif
    if(allocated)VirtualFree(image,0,MEM_RELEASE);
    std::printf("dungeon tooltip: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
