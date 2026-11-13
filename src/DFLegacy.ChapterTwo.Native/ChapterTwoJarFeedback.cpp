#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoJarFeedback.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <array>
#include <cstring>
namespace {
constexpr std::size_t ImageSize=0xe57000,Site=0x142e8;
constexpr unsigned char Original[]{0x3c,0x7c,0x0f,0x85,0xf9,0x88,0x00,0x00};
constexpr unsigned char Full[]{0x3c,0x04,0x75,0x16,0x53,0x68,0xff,0x02,0x00,0x00,0xe8,0x1d,0xbd,0x5a,0x00,0x83,0xc4,0x04,0x50,0x6a,0x25,0xe9,0x10,0x89,0x00,0x00};
constexpr unsigned char Head[]{0x3c,0x11,0x75,0x16,0x53,0x68,0xfd,0x02,0x00,0x00};
constexpr unsigned char Special[]{0x88,0x9d,0x80,0xee,0xff,0xff};
constexpr unsigned char Popup[]{0x8b,0x0d,0x24,0x6d,0xd5,0x00,0xe8,0xe7,0x8b,0x44,0x00};
void ShowOverweight(){
 char text[256]{};
 WideCharToMultiByte(936,0,L"开启袖珍罐的奖励会超出负重上限。\n请减轻负重后再试。\n袖珍罐和金币未扣除。",-1,text,sizeof(text),nullptr,nullptr);
 reinterpret_cast<void(__thiscall*)(void*,unsigned,const char*,unsigned)>(0x8657d0)(*reinterpret_cast<void**>(0xd56d24),0x25,text,0);
 OutputDebugStringA("DFLegacy Chapter Two: jar reward rejected as overweight; native explanation displayed.\n");
}
// This is inside the jar failure path after its native window cleanup. Only
// the new weight reason is handled here; every other reason keeps its branch.
__declspec(naked) void Hook(){__asm {
 cmp al,15h
 je Weight
 cmp al,7ch
 je OriginalSpecial
 push 41cbe9h
 ret
OriginalSpecial:
 push 4142f0h
 ret
Weight:
 pushfd
 pushad
 call ShowOverweight
 popad
 popfd
 push 41cbe9h
 ret
}}
}
bool dfl::chapter_two::jar_feedback_detail::InstallImage(unsigned char* image,std::size_t bytes,PatchWriter write) noexcept {
 if(!image||bytes!=ImageSize||!write)return false;
 if(std::memcmp(image+0x14280,Head,sizeof(Head))||std::memcmp(image+0x142b4,Full,sizeof(Full))||std::memcmp(image+0x142f0,Special,sizeof(Special))||std::memcmp(image+0x1cbde,Popup,sizeof(Popup)))return false;
 std::array<unsigned char,8> patch{0xe9,0,0,0,0,0x90,0x90,0x90};
 const auto delta=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&Hook)-reinterpret_cast<std::uintptr_t>(image+Site)-5);
 std::memcpy(patch.data()+1,&delta,4);
 if(!std::memcmp(image+Site,patch.data(),patch.size()))return true;
 if(std::memcmp(image+Site,Original,sizeof(Original)))return false;
 if(write(reinterpret_cast<std::uintptr_t>(image+Site),patch.data(),patch.size()))return true;
 write(reinterpret_cast<std::uintptr_t>(image+Site),Original,sizeof(Original));return false;
}
bool dfl::chapter_two::InstallJarFeedback(unsigned char* image,std::size_t bytes) noexcept {
 return image==reinterpret_cast<unsigned char*>(0x400000)&&jar_feedback_detail::InstallImage(image,bytes,memory_patch::WriteBytes);
}
