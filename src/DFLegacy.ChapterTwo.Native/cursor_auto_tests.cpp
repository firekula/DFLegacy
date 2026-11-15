#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>
#ifndef CHAPTER_TWO_CURSOR_AUTO_BASELINE
#include "ChapterTwoCursorAuto.h"
#endif

#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char cursorFixtureReserve[0x1300000]{1};
namespace {
constexpr std::size_t ImageSize=0xe57000;
unsigned Checks=0,Failures=0,Writes=0,FailAt=0;
bool FailAfter=false;
void Check(bool ok,const char* text){++Checks;if(!ok){++Failures;std::printf("FAIL cursor: %s\n",text);}}
template<class T>T& At(std::uintptr_t address){return *reinterpret_cast<T*>(address);}
std::vector<unsigned char> Load(const wchar_t* path){
    std::ifstream file(path,std::ios::binary);std::vector<unsigned char> raw{std::istreambuf_iterator<char>(file),{}};
    if(raw.size()<4096)return {};
    const auto* d=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
    if(d->e_magic!=IMAGE_DOS_SIGNATURE||d->e_lfanew<0||static_cast<std::size_t>(d->e_lfanew)>raw.size()-sizeof(IMAGE_NT_HEADERS32))return {};
    const auto* n=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+d->e_lfanew);
    if(n->Signature!=IMAGE_NT_SIGNATURE||n->OptionalHeader.ImageBase!=0x400000||n->OptionalHeader.SizeOfImage!=ImageSize||n->OptionalHeader.SizeOfHeaders>4096)return {};
    const auto* s=IMAGE_FIRST_SECTION(n);
    if(reinterpret_cast<const unsigned char*>(s+n->FileHeader.NumberOfSections)>raw.data()+raw.size())return {};
    std::vector<unsigned char> mapped(ImageSize);std::memcpy(mapped.data(),raw.data(),n->OptionalHeader.SizeOfHeaders);
    for(unsigned i=0;i<n->FileHeader.NumberOfSections;++i){
        if(s[i].VirtualAddress>ImageSize||s[i].SizeOfRawData>ImageSize-s[i].VirtualAddress||s[i].PointerToRawData>raw.size()||s[i].SizeOfRawData>raw.size()-s[i].PointerToRawData)return {};
        std::memcpy(mapped.data()+s[i].VirtualAddress,raw.data()+s[i].PointerToRawData,s[i].SizeOfRawData);
    }return mapped;
}
bool Write(std::uintptr_t address,const void* bytes,std::size_t size){++Writes;if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(address),bytes,size);return Writes!=FailAt;}
void Jump(std::uintptr_t at,std::uintptr_t target){unsigned char b[]{0xe9,0,0,0,0};const auto delta=static_cast<unsigned>(target-at-5);std::memcpy(b+1,&delta,4);std::memcpy(reinterpret_cast<void*>(at),b,5);}
int Width=1600,Height=1200,Calls=0;
BOOL MoveResult=FALSE;
POINT Physical{},Requested{};
POINT ToScreen(int x,int y){return {((2*x+1)*Width)/(2*640)+50,((2*y+1)*Height)/(2*480)+70};}
BOOL WINAPI Convert(HWND,LPPOINT p){*p=ToScreen(p->x,p->y);return TRUE;}
BOOL WINAPI Move(int x,int y){++Calls;Requested={x,y};if(MoveResult)Physical=Requested;SetLastError(MoveResult?0:ERROR_ACCESS_DENIED);return MoveResult;}
void __cdecl Queue(void*,int,int){At<int>(0xf2e7f4)=111;At<int>(0xf2e7f8)=123;Physical=ToScreen(111,123);At<unsigned>(0xf2e840)=0;}
using Auto=void(__cdecl*)(int,int);
using Restore=void(__cdecl*)();
unsigned StackBefore=0,StackAfter=0,SavedEbx=0,SavedEsi=0,SavedEdi=0;
void ExerciseAuto(int x,int y){
    const auto fn=reinterpret_cast<Auto>(0xa42260);
    __asm {
        push ebx
        push esi
        push edi
        mov ebx,13579bdfh
        mov esi,2468ace0h
        mov edi,11223344h
        mov StackBefore,esp
        push y
        push x
        call fn
        add esp,8
        mov StackAfter,esp
        mov SavedEbx,ebx
        mov SavedEsi,esi
        mov SavedEdi,edi
        pop edi
        pop esi
        pop ebx
    }
    Check(StackBefore==StackAfter&&SavedEbx==0x13579bdf&&SavedEsi==0x2468ace0&&SavedEdi==0x11223344,"original automatic function preserves callee registers and stack");
}
#ifndef CHAPTER_TWO_CURSOR_AUTO_BASELINE
void Installation(const std::vector<unsigned char>& original){
    using dfl::chapter_two::cursor_auto_detail::InstallImage;
    auto mapped=original;Writes=FailAt=0;
    Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(mapped.data(),ImageSize-1,Write)&&!InstallImage(mapped.data(),ImageSize,nullptr)&&Writes==0,"invalid image does not write");
    for(auto offset:{0x642260u,0x64227fu,0x64228du,0x642293u,0x642a20u,0x642adau,0x642ae8u,0x642aeeu}){
        mapped=original;mapped[offset]^=1;const auto before=mapped;Writes=0;
        Check(!InstallImage(mapped.data(),mapped.size(),Write)&&Writes==0&&mapped==before,"both original paths are validated before mutation");
    }
    for(unsigned fail:{1u,2u})for(bool after:{false,true}){
        mapped=original;Writes=0;FailAt=fail;FailAfter=after;
        Check(!InstallImage(mapped.data(),mapped.size(),Write)&&mapped==original,"single writer failure before or after either site rolls back both sites");
    }
    mapped=original;Writes=FailAt=0;
    Check(InstallImage(mapped.data(),mapped.size(),Write)&&Writes==2,"only two commit instructions are replaced");
    bool only=true;for(std::size_t i=0;i<mapped.size();++i)if(mapped[i]!=original[i])only&=(i>=0x642293&&i<0x642299)||(i>=0x642aee&&i<0x642af4);
    Check(only,"coordinate conversion and all other native instructions are unchanged");
    Writes=0;Check(InstallImage(mapped.data(),mapped.size(),Write)&&Writes==0,"repeated install performs no writes");
}
#endif
}
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;const auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_CURSOR_AUTO_BASELINE
    Installation(original);
#endif
    auto* mapped=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(0x400000),ImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));const bool allocated=mapped!=nullptr;
    if(!mapped){const auto start=reinterpret_cast<std::uintptr_t>(cursorFixtureReserve);DWORD old=0;if(start<=0x400000&&0x400000+ImageSize<=start+sizeof(cursorFixtureReserve)&&VirtualProtect(reinterpret_cast<void*>(0x400000),ImageSize,PAGE_EXECUTE_READWRITE,&old))mapped=reinterpret_cast<unsigned char*>(0x400000);}
    if(!mapped)return 2;std::memcpy(mapped,original.data(),original.size());
#ifndef CHAPTER_TWO_CURSOR_AUTO_BASELINE
    Check(dfl::chapter_two::InstallCursorAuto(mapped,ImageSize),"production writer installs the verified guards");
#endif
    At<void*>(0xaea46c)=reinterpret_cast<void*>(&Convert);At<void*>(0xaea468)=reinterpret_cast<void*>(&Move);
    Jump(0xa42800,reinterpret_cast<std::uintptr_t>(&Queue));
    std::array<void*,256> items{};items[0]=reinterpret_cast<void*>(1);void* buckets=items.data();
    At<void*>(0xf2e83c)=&buckets;
    for(const int width:{640,960,1600})for(BOOL success:{FALSE,TRUE})for(const POINT target:{POINT{320,240},POINT{0,0},POINT{639,479}}){
        Width=width;Height=width*3/4;Calls=0;MoveResult=success;
        At<int>(0xf2e7f4)=90;At<int>(0xf2e7f8)=100;Physical=ToScreen(90,100);
        ExerciseAuto(target.x,target.y);
        const POINT wanted=success?target:POINT{90,100};const auto expected=ToScreen(wanted.x,wanted.y);const auto request=ToScreen(target.x,target.y);
        Check(Calls==1&&Requested.x==request.x&&Requested.y==request.y,"one native move uses the converted target");
        Check(Physical.x==expected.x&&Physical.y==expected.y,"system move result controls the simulated physical position");
        Check(At<int>(0xf2e7f4)==wanted.x&&At<int>(0xf2e7f8)==wanted.y,"automatic model follows success and keeps prior position on failure");
        Check(GetLastError()==(success?0u:ERROR_ACCESS_DENIED),"original move last-error survives the guard and diagnostics");
    }
    for(BOOL success:{FALSE,TRUE})for(unsigned queued:{0u,1u}){
        Width=1600;Height=1200;MoveResult=success;Calls=0;At<int>(0xf2e7f4)=350;At<int>(0xf2e7f8)=250;Physical=ToScreen(350,250);
        At<unsigned>(0xf2e840)=queued;At<unsigned>(0xf2e844)=0;
        reinterpret_cast<Restore>(0xa42a20)();
        const POINT wanted=success||!queued?POINT{350,250}:POINT{111,123};const auto expected=ToScreen(wanted.x,wanted.y);
        Check(Calls==1&&At<unsigned>(0xf2e840)==0,"original restore consumes its input queue and moves once");
        Check(At<int>(0xf2e7f4)==wanted.x&&At<int>(0xf2e7f8)==wanted.y,"failed restore retains the latest queued pointer instead of stale entry coordinates");
        Check(Physical.x==expected.x&&Physical.y==expected.y,"restore model matches the simulated actual pointer");
    }
    if(allocated)VirtualFree(mapped,0,MEM_RELEASE);
    std::printf("cursor auto: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
