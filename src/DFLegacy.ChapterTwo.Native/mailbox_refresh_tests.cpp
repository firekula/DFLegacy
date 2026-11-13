#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>
#ifndef CHAPTER_TWO_MAILBOX_BASELINE
#include "ChapterTwoMailboxRefresh.h"
#endif

#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char mailboxFixtureReserve[0x1300000]{1};
namespace
{
    constexpr std::size_t ImageSize=0xe57000;
    unsigned Checks=0,Failures=0,Writes=0,FailAt=0;
    bool FailAfter=false;
    void Check(bool ok,const char* name){++Checks;if(!ok){++Failures;std::fprintf(stderr,"FAIL mailbox refresh: %s\n",name);}}
    template<class T> T& At(std::uintptr_t address){return *reinterpret_cast<T*>(address);}
    template<class T> T& Field(void* object,std::size_t offset){return *reinterpret_cast<T*>(static_cast<unsigned char*>(object)+offset);}
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
    bool Write(std::uintptr_t at,const void* bytes,std::size_t size)
    {++Writes;if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(at),bytes,size);return Writes!=FailAt;}
    void Jump(std::uintptr_t at,std::uintptr_t target)
    {unsigned char bytes[]{0xe9,0,0,0,0};const auto delta=static_cast<std::uint32_t>(target-at-5);std::memcpy(bytes+1,&delta,4);std::memcpy(reinterpret_cast<void*>(at),bytes,5);}
    std::vector<void*> Allocations;
    void* __cdecl Allocate(std::size_t bytes){void* p=std::malloc(bytes);if(!p)std::abort();Allocations.push_back(p);return p;}
    void __cdecl Release(void* p){for(auto& item:Allocations)if(item==p){item=nullptr;break;}std::free(p);}
    struct Mailbox
    {
        std::array<unsigned char,16> manager{};
        std::array<unsigned char,16> vector{};
        std::array<unsigned char,0x84> pane{};
        std::array<unsigned char,0x24> previous{},next{};
        std::array<std::array<unsigned char,0x70>,5> rows{};
        std::array<void*,5> rowPointers{};
        explicit Mailbox(bool archive)
        {
            Field<void*>(manager.data(),12)=vector.data();
            Field<std::uintptr_t>(pane.data(),0)=archive?0xc161b0:0xc1614c;
            Field<void*>(pane.data(),0x78)=manager.data();Field<int>(pane.data(),0x7c)=1;
            Field<void*>(pane.data(),0x74)=previous.data();Field<void*>(pane.data(),0x70)=next.data();
            for(unsigned i=0;i<5;++i)rowPointers[i]=rows[i].data();
            Field<void*>(pane.data(),0x80)=rowPointers.data();
            At<void*>(archive?0xdf9dec:0xdf9de8)=manager.data();
        }
        unsigned Count(){const auto begin=Field<std::uintptr_t>(vector.data(),4);return begin?static_cast<unsigned>((Field<std::uintptr_t>(vector.data(),8)-begin)/4):0;}
        void* Item(unsigned i){return Field<void**>(vector.data(),4)[i];}
        void* Row(unsigned i){return Field<void*>(rows[i].data(),0x6c);}
        bool Insert(int index,unsigned id,int gold=0,const char* body="archive",unsigned key=0,unsigned state=3)
        {
            using Insert=bool(__thiscall*)(void*,int,unsigned,unsigned,const char*,int,unsigned,const char*,unsigned,unsigned);
            return reinterpret_cast<Insert>(0x8e6190)(manager.data(),index,id,state,"sender",gold,0,body,key,0x12345678);
        }
        void Update(bool archive){reinterpret_cast<void(__thiscall*)(void*)>(archive?0x8e5c60:0x8e5c10)(pane.data());}
    };
    void ResetDirty(){At<unsigned char>(0xdf9df4)=At<unsigned char>(0xdf9df5)=0;At<unsigned>(0xd4dfbc)=0;}
    void Exercise()
    {
        Mailbox inbox(false),archive(true);
        ResetDirty();inbox.Update(false);archive.Update(true);
        Check(inbox.Row(0)==nullptr&&archive.Row(0)==nullptr,"empty native consumers leave rows empty");
        Check(archive.Insert(0,101),"native first archive insertion succeeds");
        Check(archive.Count()==1,"complete append grows the original vector from null");
        auto* first=archive.Item(0);
        Check(Field<unsigned>(first,4)==101&&Field<unsigned>(first,8)==3&&std::strcmp(static_cast<char*>(first)+0x10,"sender")==0&&std::strcmp(static_cast<char*>(first)+0x3c,"archive")==0,"complete native constructor preserves mail identity state sender and text");
        Check(At<unsigned char>(0xdf9df4)==1,"first append retains inbox dirty");
        Check(At<unsigned char>(0xdf9df5)==1,"first append also marks archive dirty");
        archive.Update(true);
        Check(archive.Row(0)==first,"real archive consumer binds first loaded archived mail to row");
        Check(archive.Row(1)==nullptr&&archive.Row(4)==nullptr&&Field<int>(archive.pane.data(),0x7c)==1,"real refresh leaves unused rows empty and retains page one");
        ResetDirty();
        Check(archive.Insert(0,102),"native second indexed insertion succeeds");
        Check(archive.Count()==2&&Field<unsigned>(archive.Item(0),4)==102&&archive.Item(1)==first,"native indexed insert retains order and earlier mail");
        Check(At<unsigned char>(0xdf9df4)==1&&At<unsigned char>(0xdf9df5)==1,"existing indexed insertion still marks both dirty flags");
        archive.Update(true);Check(archive.Row(0)==archive.Item(0)&&archive.Row(1)==first,"consumer also refreshes existing nonempty archive");
        ResetDirty();
        Check(inbox.Insert(0,201,0,"inbox",0,1)&&inbox.Count()==1,"native inbox first insertion remains valid");
        inbox.Update(false);Check(inbox.Row(0)==inbox.Item(0)&&Field<unsigned>(inbox.Item(0),8)==1,"real inbox consumer continues binding inbox mail");
        archive.Update(true);Check(archive.Count()==2&&archive.Row(1)==first,"inbox insertion never adds a mail to archive");
        ResetDirty();const auto count=archive.Count();
        Check(!archive.Insert(-1,999)&&archive.Count()==count&&At<unsigned char>(0xdf9df4)==0&&At<unsigned char>(0xdf9df5)==0,"negative index retains native refusal without dirty writes");
        Check(archive.Insert(99,998,-1)&&archive.Count()==count&&At<unsigned char>(0xdf9df4)==0&&At<unsigned char>(0xdf9df5)==0,"invalid append payload retains native no-op semantics");
        ResetDirty();Mailbox empty(false);
        using Append=void(__thiscall*)(void*,unsigned,unsigned,const char*,int,unsigned,const char*,unsigned,unsigned);
        reinterpret_cast<Append>(0x8e60e0)(empty.manager.data(),999,1,"sender",0,0,nullptr,0,0);
        Check(empty.Count()==0&&At<unsigned char>(0xdf9df4)==0&&At<unsigned char>(0xdf9df5)==0,"native all-empty payload bypasses allocation and dirty writes");
    }
    struct Cpu{unsigned eax,ebx,ecx,edx,esi,edi,ebp,esp,flags;};
    Cpu Captured{};unsigned BeforeEsp=0,BeforeFlags=0;void* Instruction=nullptr;
    __declspec(naked) void Probe()
    {
        __asm {
            pushad
            pushfd
            mov eax,7fffffffh
            add eax,1
            stc
            mov eax,11223344h
            mov ebx,22334455h
            mov ecx,33445566h
            mov edx,44556677h
            mov esi,55667788h
            mov edi,66778899h
            mov ebp,778899aah
            mov BeforeEsp,esp
            pushfd
            pop BeforeFlags
            call Instruction
            mov Captured.eax,eax
            mov Captured.ebx,ebx
            mov Captured.ecx,ecx
            mov Captured.edx,edx
            mov Captured.esi,esi
            mov Captured.edi,edi
            mov Captured.ebp,ebp
            mov Captured.esp,esp
            pushfd
            pop Captured.flags
            popfd
            popad
            ret
        }
    }
    void MachineState()
    {
        auto* code=static_cast<unsigned char*>(VirtualAlloc(nullptr,16,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));
        if(!code){Check(false,"allocate instruction probe");return;}
        std::memcpy(code,reinterpret_cast<void*>(0x8e6169),7);code[7]=0xc3;
        if(code[0]==0xe8){const auto target=static_cast<std::uintptr_t>(0x8e616e+At<std::int32_t>(0x8e616a));const auto delta=static_cast<std::uint32_t>(target-reinterpret_cast<std::uintptr_t>(code)-5);std::memcpy(code+1,&delta,4);}
        ResetDirty();At<unsigned char>(0xdf9df3)=0x5a;At<unsigned char>(0xdf9df6)=0xa5;
        Instruction=code;Probe();
        Check(Captured.eax==0x11223344&&Captured.ebx==0x22334455&&Captured.ecx==0x33445566&&Captured.edx==0x44556677&&Captured.esi==0x55667788&&Captured.edi==0x66778899&&Captured.ebp==0x778899aa,"installed dirty instruction preserves all general registers");
        Check(Captured.esp==BeforeEsp&&Captured.flags==BeforeFlags,"installed dirty instruction preserves complete flags and net stack depth");
        Check(At<unsigned char>(0xdf9df3)==0x5a&&At<unsigned char>(0xdf9df6)==0xa5,"dirty writes preserve adjacent global bytes");
        VirtualFree(code,0,MEM_RELEASE);
    }
#ifndef CHAPTER_TWO_MAILBOX_BASELINE
    void Installation(const std::vector<unsigned char>& original)
    {
        using dfl::chapter_two::mailbox_refresh_detail::InstallImage;
        auto image=original;Writes=FailAt=0;
        Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&!InstallImage(image.data(),ImageSize,nullptr)&&Writes==0,"invalid install arguments do not write");
        for(const auto offset:{0x4e60e0u,0x4e6169u,0x4e6190u,0x4e632au,0x4e5c49u,0x4e5c99u,0x4e599bu,0x4e5a9bu}){
            image=original;image[offset]^=1;const auto before=image;Writes=0;
            Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"unknown insert or consumer signatures reject without writing");}
        for(bool after:{false,true}){image=original;Writes=0;FailAt=1;FailAfter=after;
            Check(!InstallImage(image.data(),image.size(),Write)&&image==original,"failed write restores original instruction even after copy");}
        image=original;Writes=FailAt=0;Check(InstallImage(image.data(),image.size(),Write)&&Writes==1,"recognized image installs one instruction replacement");
        bool only=true;for(std::size_t i=0;i<image.size();++i)if(image[i]!=original[i]&&(i<0x4e6169||i>=0x4e6170))only=false;
        Check(only,"only the seven-byte append dirty write changes");Writes=0;
        Check(InstallImage(image.data(),image.size(),Write)&&Writes==0,"repeat installation is idempotent");
    }
#endif
}
int wmain(int argc,wchar_t** argv)
{
    if(argc!=2)return 2;const auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_MAILBOX_BASELINE
    Installation(original);
#endif
    auto* image=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));const bool allocated=image!=nullptr;
    if(!image){const auto start=reinterpret_cast<std::uintptr_t>(mailboxFixtureReserve);DWORD old=0;if(start<=0x400000&&0x400000+ImageSize<=start+sizeof(mailboxFixtureReserve)&&VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))image=reinterpret_cast<unsigned char*>(0x400000);}
    if(!image)return 2;std::memcpy(image,original.data(),original.size());
#ifndef CHAPTER_TWO_MAILBOX_BASELINE
    Check(dfl::chapter_two::InstallMailboxRefresh(image,ImageSize),"production writer installs in native image");
#endif
    // The full native constructor, vector growth, insert, both consumers and
    // row refresh and draw callbacks run. Only CRT allocation/copy and
    // platform time are proxied; empty graphics fields take native no-draw paths.
    Jump(0xa5da4a,reinterpret_cast<std::uintptr_t>(&Allocate));Jump(0xa5f27d,reinterpret_cast<std::uintptr_t>(&Release));
    Jump(0xa5ef40,reinterpret_cast<std::uintptr_t>(&std::memmove));At<std::uintptr_t>(0xaea50c)=reinterpret_cast<std::uintptr_t>(&GetTickCount);
    Exercise();MachineState();
    for(void* p:Allocations)std::free(p);
    if(allocated)VirtualFree(image,0,MEM_RELEASE);
    std::printf("mailbox refresh: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
