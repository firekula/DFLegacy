#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>
#ifndef CHAPTER_TWO_TYPE16_BASELINE
#include "ChapterTwoType16Audio.h"
#endif

#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char type16FixtureReserve[0x1300000]{1};
namespace
{
    constexpr std::size_t ImageSize=0xe57000;
    unsigned Checks=0,Failures=0,Writes=0,FailAt=0,Events=0,ExpectedLines=0;
    bool FailAfter=false,SkipCopy=false,PartialCopy=false;
    std::vector<void*> Allocations;
    std::array<unsigned char,0x1000> Chat{};
#ifndef CHAPTER_TWO_TYPE16_BASELINE
    struct CpuState { unsigned edi,esi,ebp,esp,ebx,edx,ecx,eax,flags; } Arrived{},Returned{};
    std::uintptr_t GateAddress=0;
    unsigned GateStackBefore=0,GateStackAfter=0;
    __declspec(naked) void SoundEntryProbe()
    {
        __asm {
            pushfd
            pushad
            mov esi,esp
            lea edi,Arrived
            mov ecx,9
            cld
            rep movsd
            popad
            popfd
            ret
        }
    }
    __declspec(naked) void __cdecl InvokeGate(int)
    {
        __asm {
            push ebp
            mov ebp,esp
            pushad
            mov GateStackBefore,esp
            push 0
            push [ebp+8]
            push 0
            push 0
            push 0
            push 0
            push 0
            push -1
            push 00c18494h
            mov eax,11223344h
            mov ecx,22334455h
            mov edx,33445566h
            mov ebx,44556677h
            mov ebp,55667788h
            mov esi,66778899h
            mov edi,778899aah
            push 246h
            popfd
            call GateAddress
            pushfd
            pushad
            mov esi,esp
            lea edi,Returned
            mov ecx,9
            cld
            rep movsd
            popad
            popfd
            add esp,36
            mov GateStackAfter,esp
            mov esp,GateStackBefore
            popad
            pop ebp
            ret
        }
    }
#endif
    struct CallState { unsigned before,after,ebx,esi,edi,ebp; } State{};
    // The real wrapper is thiscall and must pop twelve bytes. Sentinels catch
    // hooks that accidentally consume a second return address or clobber the
    // nonvolatile registers; compiler-generated callers can hide either bug.
    __declspec(naked) void __cdecl DisplayChecked(void*,const char*,int,const char*)
    {
        __asm {
            push ebp
            mov ebp,esp
            push ebx
            push esi
            push edi
            mov State.before,esp
            push [ebp+20]
            push [ebp+16]
            push [ebp+12]
            mov ecx,[ebp+8]
            push ebp
            pop eax
            mov State.ebp,eax
            mov ebx,13579bdfh
            mov esi,2468ace0h
            mov edi,0badc0deh
            mov eax,00926d70h
            call eax
            mov State.after,esp
            mov State.ebx,ebx
            mov State.esi,esi
            mov State.edi,edi
            cmp ebp,State.ebp
            je frame_preserved
            mov State.ebp,0
        frame_preserved:
            mov esp,State.before
            pop edi
            pop esi
            pop ebx
            pop ebp
            ret
        }
    }
    template<typename T>T& Field(void* p,std::size_t n){return *reinterpret_cast<T*>(static_cast<unsigned char*>(p)+n);}
    template<typename T>T& At(std::uintptr_t p){return *reinterpret_cast<T*>(p);}
    void Check(bool ok,const char* text){++Checks;if(!ok){++Failures;std::fprintf(stderr,"FAIL type16: %s\n",text);}}
    LONG WINAPI ReportException(EXCEPTION_POINTERS* fault)
    {
        if(fault->ExceptionRecord->ExceptionCode==0x40010006||fault->ExceptionRecord->ExceptionCode==0x4001000a)
            return EXCEPTION_CONTINUE_SEARCH; // OutputDebugString is informational.
        std::fprintf(stderr,"type16 fixture exception %08lx at %08lx: ESP=%08lx EBP=%08lx\n",
            fault->ExceptionRecord->ExceptionCode,fault->ContextRecord->Eip,
            fault->ContextRecord->Esp,fault->ContextRecord->Ebp);
        return EXCEPTION_CONTINUE_SEARCH;
    }
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
    bool Write(std::uintptr_t address,const void* data,std::size_t bytes)
    {
        ++Writes;if(Writes==FailAt&&!FailAfter)return false;
        if(Writes==1&&SkipCopy)return true;
        if(Writes==1&&PartialCopy){std::memcpy(reinterpret_cast<void*>(address),data,2);return true;}
        std::memcpy(reinterpret_cast<void*>(address),data,bytes);return Writes!=FailAt;
    }
    void Jump(std::uintptr_t address,std::uintptr_t target)
    {unsigned char bytes[]{0xe9,0,0,0,0};const auto delta=static_cast<std::uint32_t>(target-address-5);std::memcpy(bytes+1,&delta,4);std::memcpy(reinterpret_cast<void*>(address),bytes,5);}
    void* __cdecl Allocate(std::size_t n){void* p=std::malloc(n?n:1);if(!p)std::abort();Allocations.push_back(p);return p;}
    void __cdecl Release(void* p){for(auto& x:Allocations)if(x==p){x=nullptr;break;}std::free(p);}
    int __stdcall Copy(char* out,std::size_t n,const char* input){return strcpy_s(out,n,input);}
    int __cdecl Format(char* out,std::size_t n,const char* fmt,...)
    {va_list args;va_start(args,fmt);const int result=vsnprintf(out,n,fmt,args);va_end(args);return result;}
    int __fastcall Clock(void*,void*){return 0;}
    // Measure consumes only text and length. Native caller has already pushed
    // Wrap's output and flags below them and reuses those two arguments.
    int __fastcall Measure(void*,void*,const char*,int){return 0;}
    int __fastcall Wrap(void*,void*,const char* text,int,void* output,int)
    {
        // Font measurement is unavailable without a device. Supply one real
        // native string, then let native display build its actual chat deque.
        void* line=Allocate(28);std::memset(line,0,28);Field<unsigned>(line,24)=15;
        reinterpret_cast<void(__thiscall*)(void*,const char*,unsigned)>(0x402a20)(line,text,static_cast<unsigned>(std::strlen(text)));
        Field<void*>(output,4)=line;Field<void*>(output,8)=static_cast<unsigned char*>(line)+28;Field<void*>(output,12)=static_cast<unsigned char*>(line)+28;
        return 1;
    }
    void __fastcall SetMaximum(void* list,void*,unsigned n)
    {Field<unsigned>(Field<void*>(list,0x70),0x74)=n;}
    void __fastcall Play(void*,void*,const char* name,int a,int b,int c,int d,int e,int f,int g)
    {
        ++Events;
        Check(std::strcmp(name,"CHAT_MESSAGE")==0&&a==0&&b==0&&c==0&&d==0&&e==65535&&f==-1&&g==0,
            "original sound dispatcher preserves CHAT_MESSAGE engine arguments");
        Check(Field<unsigned>(Chat.data(),0x100)==ExpectedLines,"native text enters chat history before sound dispatch");
    }
    const char* NativeString(void* object)
    {return Field<unsigned>(object,24)>=16?Field<const char*>(object,4):static_cast<char*>(object)+4;}
    void* Line(unsigned index)
    {
        const auto offset=Field<unsigned>(Chat.data(),0x104)+index;
        auto** blocks=Field<unsigned char**>(Chat.data(),0xfc);
        return blocks[offset>>8]+(offset&255)*80;
    }
    void Exercise(bool suppressType16)
    {
        std::array<void*,11> fontVtable{};fontVtable[8]=reinterpret_cast<void*>(&Measure);fontVtable[10]=reinterpret_cast<void*>(&Wrap);
        void* font=fontVtable.data();At<void*>(0xd54144)=&font;
        std::array<void*,9> listVtable{};listVtable[8]=reinterpret_cast<void*>(&SetMaximum);
        std::array<unsigned char,0x80> list{};std::array<unsigned char,0xa0> scroll{};
        Field<void*>(list.data(),0)=listVtable.data();Field<void*>(list.data(),0x70)=scroll.data();
        Field<void*>(Chat.data(),0x54)=list.data();Field<void*>(Chat.data(),0x64)=list.data();
        std::memset(Chat.data()+0x695,1,0x80);Chat[0x715]=0;
        const char* text="C2 ordinary message: \xcd\xa8\xd6\xaa";
        for(const int type:{3,16,4,0,16,33})for(const char* sender:{static_cast<const char*>(nullptr),"fixture"}){
            const auto before=Events;const auto count=Field<unsigned>(Chat.data(),0x100);ExpectedLines=count+1;
            DisplayChecked(Chat.data(),text,type,sender);
            Check(State.before==State.after,"real wrapper restores exact stack pointer including three thiscall arguments");
            Check(State.ebx==0x13579bdf&&State.esi==0x2468ace0&&State.edi==0x0badc0de&&State.ebp!=0,
                "real wrapper preserves EBX ESI EDI EBP across both sound paths");
            Check(Field<unsigned>(Chat.data(),0x100)==count+1,"all message types still append exactly one real native chat line");
            auto* line=Line(count);
            Check(std::strcmp(NativeString(static_cast<unsigned char*>(line)+28),text)==0&&Field<int>(line,0x38)==type,
                "native chat line keeps exact text bytes and original message type");
            Check(std::strcmp(NativeString(line),sender?sender:"")==0,"native chat line keeps sender or system-message empty sender");
            Check(Events==before+(type==16&&suppressType16?0:1),"only Type16 suppresses audio; normal chat still reaches original sound dispatcher");
        }
        const auto before=Events;ExpectedLines=Field<unsigned>(Chat.data(),0x100);
        reinterpret_cast<void(__cdecl*)(const char*,int,int,int)>(0x43c160)("CHAT_MESSAGE",-1,0,0);
        Check(Events==before+1,"unrelated direct callers of the original sound function remain audible");
    }
#ifndef CHAPTER_TWO_TYPE16_BASELINE
    void Installation(const std::vector<unsigned char>& original)
    {
        using dfl::chapter_two::type16_audio_detail::InstallImage;
        auto image=original;Writes=FailAt=0;
        Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&!InstallImage(image.data(),ImageSize,nullptr)&&Writes==0,"invalid installer inputs cause no writes");
        Check(!dfl::chapter_two::InstallType16Audio(image.data(),image.size()),"public native installer rejects relocated images before attempting writes");
        struct Range{unsigned offset,size;};
        for(const auto range:{Range{0x526d70,50},Range{0x5257d0,32},Range{0x525bc0,15},
            Range{0x527421,16},Range{0x5274e0,17},Range{0x527562,16},Range{0x818494,13},Range{0x3c160,64}})
            for(unsigned index=0;index<range.size;++index){
                image=original;image[range.offset+index]^=1;const auto before=image;Writes=0;
                Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,
                    "every validated wrapper, display, caller, event and sound byte rejects mutation without writing");}
        for(bool after:{false,true}){image=original;Writes=0;FailAt=1;FailAfter=after;
            Check(!InstallImage(image.data(),image.size(),Write)&&image==original,"failed patch restores original sound call including after-copy failure");}
        FailAt=0;
        for(bool partial:{false,true}){image=original;Writes=0;SkipCopy=!partial;PartialCopy=partial;
            Check(!InstallImage(image.data(),image.size(),Write)&&Writes==2&&image==original,
                "silent or partial writer success is detected and original call restored");}
        SkipCopy=PartialCopy=false;
        image=original;Writes=FailAt=0;
        Check(InstallImage(image.data(),image.size(),Write)&&Writes==1,"recognized native sound call installs");
        bool only=true;for(std::size_t i=0;i<image.size();++i)if(image[i]!=original[i]&&(i<0x526d96||i>=0x526d9b))only=false;
        Check(only,"only one five-byte sound call changes; text and shared sound function remain intact");
        Writes=0;Check(InstallImage(image.data(),image.size(),Write)&&Writes==0,"same hook installs idempotently");
        Check(!InstallImage(image.data(),image.size(),Write,false)&&Writes==0,"cannot silently change installed production gate to baseline mode");
        image[0x525bcc]^=1;Writes=0;
        Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0,"idempotent install still validates original surrounding context");
        image=original;Writes=0;
        Check(InstallImage(image.data(),image.size(),Write,false)&&Writes==1,"explicit diagnostic baseline installs without suppressing any type");
        Writes=0;Check(InstallImage(image.data(),image.size(),Write,false)&&Writes==0,"diagnostic baseline installs idempotently");
    }
    bool SameRegisters(const CpuState& state)
    {
        return state.eax==0x11223344&&state.ecx==0x22334455&&state.edx==0x33445566&&state.ebx==0x44556677&&
            state.ebp==0x55667788&&state.esi==0x66778899&&state.edi==0x778899aa&&(state.flags&0xcd5)==0x44;
    }
    void GateBoundary(bool suppressType16)
    {
        unsigned char saved[5]{};std::memcpy(saved,reinterpret_cast<void*>(0x43c160),5);
        Jump(0x43c160,reinterpret_cast<std::uintptr_t>(&SoundEntryProbe));
        GateAddress=0x926d9b+At<unsigned>(0x926d97);
        for(const int type:{16,3,0,255,-1,256,0x7fffffff,static_cast<int>(0x80000000u)})
        {
            Arrived={};Returned={};
            const auto before=dfl::chapter_two::ReadType16AudioDiagnostics();
            InvokeGate(type);
            const auto after=dfl::chapter_two::ReadType16AudioDiagnostics();
            const bool skip=type==16&&suppressType16;
            const unsigned bucket=static_cast<unsigned>(type)<256?static_cast<unsigned>(type):256;
            Check(GateStackBefore==GateStackAfter,"gate returns with exactly the original four audio arguments left for caller cleanup");
            Check(SameRegisters(Returned),"gate preserves all seven general registers and arithmetic/control flags");
            Check(skip?Arrived.eax==0:SameRegisters(Arrived),"only skip avoids original entry; forwarding preserves all entry registers and flags");
            Check(after.types[bucket].originalCalls==before.types[bucket].originalCalls+(skip?0:1)&&
                after.types[bucket].skipped==before.types[bucket].skipped+(skip?1:0),
                "bounded diagnostics count actual forwarding versus skipped calls including unknown types");
        }
        std::memcpy(reinterpret_cast<void*>(0x43c160),saved,5);
    }
#endif
}
int wmain(int argc,wchar_t** argv)
{
    if(argc<2||argc>3)return 2;SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
    const bool suppressType16=argc==2;
    if(argc==3&&std::wcscmp(argv[2],L"--observe"))return 2;
    AddVectoredExceptionHandler(1,ReportException);
    const auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_TYPE16_BASELINE
    Installation(original);
#endif
    auto* image=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));
    const bool allocated=image!=nullptr;
    if(!image){const auto first=reinterpret_cast<std::uintptr_t>(type16FixtureReserve);DWORD old=0;
        if(first<=0x400000&&0x400000+ImageSize<=first+sizeof(type16FixtureReserve)&&VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))image=reinterpret_cast<unsigned char*>(0x400000);}
    if(!image)return 2;std::memcpy(image,original.data(),original.size());
#ifndef CHAPTER_TWO_TYPE16_BASELINE
    Check(dfl::chapter_two::InstallType16Audio(image,ImageSize,suppressType16),"public installer patches executable native fixture");
    GateBoundary(suppressType16);
    const auto before=dfl::chapter_two::ReadType16AudioDiagnostics();
#endif
    // Full wrapper, text display, history insertion and sound dispatcher run.
    // Only device/font/clock and CRT allocation/formatting boundaries are replaced.
    for(const auto address:{0xa5da4au,0xa5da6eu,0xa60448u})Jump(address,reinterpret_cast<std::uintptr_t>(&Allocate));
    for(const auto address:{0xa5f27du,0xa60854u,0xa5fe78u})Jump(address,reinterpret_cast<std::uintptr_t>(&Release));
    Jump(0xa5ef40,reinterpret_cast<std::uintptr_t>(&std::memmove));
    Jump(0x401180,reinterpret_cast<std::uintptr_t>(&Copy));Jump(0x401d90,reinterpret_cast<std::uintptr_t>(&Format));
    Jump(0xa39820,reinterpret_cast<std::uintptr_t>(&Clock));Jump(0xa53ab0,reinterpret_cast<std::uintptr_t>(&Play));
    Exercise(suppressType16);
    std::printf("type16 native path: expected_suppression=%u lines=%u original_dispatches=%u (includes one unrelated direct call)\n",
        suppressType16?1u:0u,Field<unsigned>(Chat.data(),0x100),Events);
#ifndef CHAPTER_TWO_TYPE16_BASELINE
    const auto after=dfl::chapter_two::ReadType16AudioDiagnostics();
    for(const auto type:{0,3,4,16,33}){
        const unsigned count=type==16?4:2;
        Check(after.types[type].originalCalls-before.types[type].originalCalls==(type==16&&suppressType16?0:count)&&
            after.types[type].skipped-before.types[type].skipped==(type==16&&suppressType16?count:0),
            "diagnostics agree with real native text/audio execution for each message type");
        std::printf("type16 native diagnostic: type=%d original=%u skipped=%u\n",type,
            after.types[type].originalCalls-before.types[type].originalCalls,
            after.types[type].skipped-before.types[type].skipped);}
    dfl::chapter_two::LogType16AudioDiagnostics();
#endif
    for(void* p:Allocations)std::free(p);
    if(allocated)VirtualFree(image,0,MEM_RELEASE);
    std::printf("type16 audio: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
