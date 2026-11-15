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
#ifndef CHAPTER_TWO_ADULT_BLOOD_BASELINE
#include "ChapterTwoAdultBlood.h"
#endif

#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char adultBloodFixtureReserve[0x1300000]{1};
namespace {
constexpr std::size_t ImageSize=0xe57000;
unsigned Checks=0,Failures=0,Writes=0,FailAt=0,TraceCalls=0,UiCalls=0,AgeCalls=0;
bool FailAfter=false,Special=false;
std::uint32_t Age=0,TraceMode=0,ObservedEsi=0,ObservedEbx=0;
std::uintptr_t ObservedEdi=0,ExpectedSp=0,ObservedSp=0,Selected=0;
const char* Account=nullptr;
std::array<unsigned char,0x400> ModeObject{};
std::array<unsigned char,16> Adult{},Young{},Piece{};
void Check(bool value,const char* message){++Checks;if(!value){++Failures;std::fprintf(stderr,"FAIL adult blood: %s\n",message);}}
template<class T>T& At(std::uintptr_t address){return *reinterpret_cast<T*>(address);}
bool Fits(std::size_t at,std::size_t size,std::size_t total){return at<=total&&size<=total-at;}
std::vector<unsigned char> Load(const wchar_t* path){
    std::ifstream input(path,std::ios::binary);const std::vector<unsigned char> raw{std::istreambuf_iterator<char>(input),{}};
    if(raw.size()<sizeof(IMAGE_DOS_HEADER))return {};
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||!Fits(dos->e_lfanew,sizeof(IMAGE_NT_HEADERS32),raw.size()))return {};
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+dos->e_lfanew);
    if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_I386||nt->OptionalHeader.ImageBase!=0x400000||nt->OptionalHeader.SizeOfImage!=ImageSize||!Fits(0,nt->OptionalHeader.SizeOfHeaders,raw.size())||!Fits(0,nt->OptionalHeader.SizeOfHeaders,ImageSize))return {};
    const auto* sections=IMAGE_FIRST_SECTION(nt);
    if(!Fits(reinterpret_cast<const unsigned char*>(sections)-raw.data(),sizeof(*sections)*nt->FileHeader.NumberOfSections,raw.size()))return {};
    std::vector<unsigned char> image(ImageSize);std::memcpy(image.data(),raw.data(),nt->OptionalHeader.SizeOfHeaders);
    for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];if(!Fits(s.PointerToRawData,s.SizeOfRawData,raw.size())||!Fits(s.VirtualAddress,s.SizeOfRawData,ImageSize))return {};
        std::memcpy(image.data()+s.VirtualAddress,raw.data()+s.PointerToRawData,s.SizeOfRawData);}
    return image;
}
void Jump(std::uintptr_t from,std::uintptr_t to){unsigned char code[]{0xe9,0,0,0,0};const auto delta=static_cast<std::uint32_t>(to-from-5);std::memcpy(code+1,&delta,4);std::memcpy(reinterpret_cast<void*>(from),code,5);}
bool Write(std::uintptr_t at,const void* data,std::size_t bytes){++Writes;if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(at),data,bytes);return Writes!=FailAt;}
unsigned __fastcall AgeValue(void*,void*){++AgeCalls;return Age;}
bool __fastcall SpecialState(void*,void*){return Special;}
void __fastcall Ui(void*,void*){++UiCalls;}
void __cdecl Trace(unsigned code,unsigned zero,unsigned one,unsigned three,unsigned mode){
    ++TraceCalls;TraceMode=mode;Check(code==0x7497296d&&zero==0&&one==1&&three==3,"genuine setter retains original trace arguments");
}
void __cdecl AccountLocal(void* frame){
    auto* text=static_cast<unsigned char*>(frame)-0x86c;std::memset(text,0,28);
    const auto size=std::strlen(Account);std::memcpy(text+4,Account,size);
    *reinterpret_cast<unsigned*>(text+0x14)=static_cast<unsigned>(size);*reinterpret_cast<unsigned*>(text+0x18)=15;
}
__declspec(naked) void FinishLogin(){
    __asm {
        mov ObservedSp,esp
        mov ObservedEsi,esi
        mov ObservedEbx,ebx
        mov ObservedEdi,edi
        lea esp,[ebp-100ch]
        pop edi
        pop esi
        pop ebx
        mov esp,ebp
        pop ebp
        ret
    }
}
__declspec(naked) void Login(){
    __asm {
        push ebp
        mov ebp,esp
        sub esp,1000h
        mov byte ptr[esp],0
        push ebx
        push esi
        push edi
        push ebp
        call AccountLocal
        add esp,4
        mov ExpectedSp,esp
        xor ebx,ebx
        mov edi,0d564ach
        mov edi,[edi]
        sub esp,14h
        mov eax,41e071h
        jmp eax
    }
}
__declspec(naked) void FinishParticle(){
    __asm {
        mov Selected,ebx
        pop ebx
        mov esp,ebp
        pop ebp
        ret
    }
}
__declspec(naked) void SkipParticle(){
    __asm {
        mov Selected,0
        pop ebx
        mov esp,ebp
        pop ebp
        ret
    }
}
__declspec(naked) void Particle(){
    __asm {
        push ebp
        mov ebp,esp
        sub esp,50h
        push ebx
        mov eax,4f9685h
        jmp eax
    }
}
void Prepare(){
    Jump(0x42fbd0,reinterpret_cast<std::uintptr_t>(&AgeValue));Jump(0x4364a0,reinterpret_cast<std::uintptr_t>(&SpecialState));
    Jump(0x4340c0,reinterpret_cast<std::uintptr_t>(&Ui));Jump(0x5228f0,reinterpret_cast<std::uintptr_t>(&Ui));
    Jump(0x41e122,reinterpret_cast<std::uintptr_t>(&FinishLogin));
    Jump(0x4f96bb,reinterpret_cast<std::uintptr_t>(&FinishParticle));Jump(0x4f97e3,reinterpret_cast<std::uintptr_t>(&SkipParticle));
    At<void*>(0xd564ac)=ModeObject.data();At<void*>(0xd4e3b0)=reinterpret_cast<void*>(&Trace);
    At<void*>(0xd56a98)=Adult.data();At<void*>(0xd56a9c)=Young.data();At<void*>(0xd56aa0)=Piece.data();
}
void LoginCase(const char* name,unsigned age,bool special,unsigned initial){
    Account=name;Age=age;Special=special;TraceCalls=UiCalls=AgeCalls=0;
    ModeObject.fill(0xa5);At<unsigned>(reinterpret_cast<std::uintptr_t>(ModeObject.data())+0x128)=initial;
    Login();const auto mode=At<unsigned>(reinterpret_cast<std::uintptr_t>(ModeObject.data())+0x128);
    if(ObservedSp!=ExpectedSp||ObservedEsi!=2||ObservedEbx!=0||ObservedEdi!=reinterpret_cast<std::uintptr_t>(ModeObject.data()))
        std::fprintf(stderr,"ABI observed sp=%08x expected=%08x esi=%08x ebx=%08x edi=%08x object=%08x\n",static_cast<unsigned>(ObservedSp),static_cast<unsigned>(ExpectedSp),ObservedEsi,ObservedEbx,static_cast<unsigned>(ObservedEdi),static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(ModeObject.data())));
    Check(mode==1,"original login convergence selects adult mode one for C1 parity");
    Check(TraceCalls==1&&TraceMode==mode&&UiCalls==2,"one genuine setter and both original UI call edges execute");
    Check(AgeCalls==static_cast<unsigned>(!special&&std::strcmp(name,"player01")==0),"ordinary age path and four account/special-state branches remain native");
    Check(ObservedSp==ExpectedSp&&ObservedEsi==2&&ObservedEbx==0&&ObservedEdi==reinterpret_cast<std::uintptr_t>(ModeObject.data()),"native stack cleanup and nonvolatile selection registers are preserved");
    bool onlyMode=true;for(std::size_t i=0;i<ModeObject.size();++i)if((i<0x128||i>=0x12c)&&ModeObject[i]!=0xa5)onlyMode=false;
    Check(onlyMode,"original setter changes no neighboring object fields");
    Particle();Check(Selected==reinterpret_cast<std::uintptr_t>(Adult.data()),"subsequent original particle consumer selects adult template");
}
void DirectSetterCases(){
    using Setter=void(__thiscall*)(void*,unsigned);
    for(unsigned mode:{0u,1u,2u,99u}){
        TraceCalls=0;reinterpret_cast<Setter>(0x435ba0)(ModeObject.data(),mode);
        Check(TraceCalls==1&&TraceMode==mode&&At<unsigned>(reinterpret_cast<std::uintptr_t>(ModeObject.data())+0x128)==mode,"all other callers retain original setter semantics");
        Particle();const auto expected=mode==0?reinterpret_cast<std::uintptr_t>(Young.data()):mode==1?reinterpret_cast<std::uintptr_t>(Adult.data()):0;
        Check(Selected==expected,"mode zero, one, two and unknown particle branches remain native");
    }
}
#ifndef CHAPTER_TWO_ADULT_BLOOD_BASELINE
void Installation(const std::vector<unsigned char>& original){
    using dfl::chapter_two::adult_blood_detail::InstallImage;auto image=original;Writes=FailAt=0;
    Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(image.data(),ImageSize-1,Write)&&!InstallImage(image.data(),ImageSize,nullptr)&&
        !InstallImage(reinterpret_cast<unsigned char*>(std::numeric_limits<std::uintptr_t>::max()-ImageSize+1),ImageSize,Write)&&Writes==0,"invalid/overflowing images reject without writes");
    Check(!dfl::chapter_two::InstallAdultBlood(image.data(),image.size()),"public installer rejects a relocated runtime image");
    for(const auto range:{std::array<unsigned,2>{0x1e071,0x1e154},{0x35ba0,0x35bc5},{0x35e30,0x35e38},{0xf9685,0xf96ca},{0x2f568,0x2f56c}}){
        for(unsigned at=range[0];at<range[1];++at){image=original;image[at]^=1;const auto before=image;Writes=0;
            Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==before,"every verified native context byte rejects unknown builds before mutation");}
    }
    for(bool after:{false,true}){image=original;Writes=0;FailAt=1;FailAfter=after;
        Check(!InstallImage(image.data(),image.size(),Write)&&image==original,"writer failure before or after copying restores original call");}
    image=original;Writes=FailAt=0;Check(InstallImage(image.data(),image.size(),Write)&&Writes==1,"verified login call installs once");
    bool onlyCall=true;for(std::size_t i=0;i<image.size();++i)if(image[i]!=original[i]&&(i<0x1e11d||i>=0x1e122))onlyCall=false;
    Check(onlyCall,"only the login call changes; three other setters, resources and fragment logic remain intact");
    Writes=0;Check(InstallImage(image.data(),image.size(),Write)&&Writes==0,"matching installed call is idempotent");
    image[0x35e31]^=1;const auto corrupt=image;Check(!InstallImage(image.data(),image.size(),Write)&&Writes==0&&image==corrupt,"already patched call still rejects incompatible policy context");
}
#endif
}
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);const auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_ADULT_BLOOD_BASELINE
    Installation(original);
#endif
    auto* image=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));const bool allocated=image!=nullptr;
    if(!image){const auto begin=reinterpret_cast<std::uintptr_t>(adultBloodFixtureReserve);DWORD old=0;if(begin<=0x400000&&0x400000+ImageSize<=begin+sizeof(adultBloodFixtureReserve)&&VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))image=reinterpret_cast<unsigned char*>(0x400000);}
    if(!image)return 2;std::memcpy(image,original.data(),original.size());
#ifndef CHAPTER_TWO_ADULT_BLOOD_BASELINE
    Check(dfl::chapter_two::InstallAdultBlood(image,ImageSize),"production protected-memory writer installs at the original base");
#endif
    Prepare();
    for(unsigned initial:{0u,1u,2u}){
        for(unsigned age:{0u,17u,18u,99u,0xffffffffu})LoginCase("player01",age,false,initial);
        for(const auto* name:{"testman111","dnftest248","dnftest249","dnftest250"})LoginCase(name,18,false,initial);
        LoginCase("player01",18,true,initial);
    }
    DirectSetterCases();
    if(allocated)VirtualFree(image,0,MEM_RELEASE);
    std::printf("adult blood: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
