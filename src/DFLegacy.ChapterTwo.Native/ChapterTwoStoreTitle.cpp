#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoStoreTitle.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <array>
#include <cstring>
namespace {
constexpr std::size_t ImageSize=0xe57000,Site=0x48126f;
constexpr unsigned char Prefix[]{0x8b,0x43,0x40,0x8b,0x88,0xe4,0x00,0x00,0x00,0x56,0x33,0xf6,0x3b,0xce,0x75,0x0b,0x8d,0x90,0xc4,0x00,0x00,0x00,0x89,0x55,0xfc,0xeb,0x09,0x8d,0x88,0xd8,0x00,0x00,0x00,0x89,0x4d,0xfc,0x8b,0x55,0xfc};
constexpr unsigned char Original[]{0x39,0x72,0x0c,0x0f,0x84,0xcf,0x00,0x00,0x00};
constexpr unsigned char Suffix[]{0x39,0xb0,0xe4,0x00,0x00,0x00,0x74,0x0c,0x39,0xb0,0xe0,0x00,0x00,0x00,0x0f,0x85,0xbb,0x00,0x00,0x00};
constexpr unsigned char Prompt[]{0x56,0x68,0x36,0x05,0x00,0x00,0xeb,0x8b};

bool __stdcall BlankTitle(const unsigned char* object) noexcept {
 __try {
  const auto length=*reinterpret_cast<const unsigned*>(object+0xc);
  if(!length)return true;
  // The edit control has a 40-byte title limit. Bound inspection and leave
  // unrecognized records on the original nonempty validation path.
  if(length>64)return false;
  const auto* text=*reinterpret_cast<const unsigned char* const*>(object+8);
  if(!text)return false;
  for(unsigned i=0;i<length;++i){
   const auto ch=text[i];
   if(ch==0x20||(ch>=0x09&&ch<=0x0d))continue;
   if(ch==0xa1&&i+1<length&&text[i+1]==0xa1){++i;continue;}
   return false;
  }
  return true;
 } __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
// EDX is the selected title record; EAX is the native edit control. Keep
// both live values and use the original empty-title prompt/return path.
__declspec(naked) void Guard(){__asm {
 push eax
 push ecx
 push edx
 push edx
 call BlankTitle
 test al,al
 pop edx
 pop ecx
 pop eax
 jnz Empty
 push 881278h
 ret
Empty:
 push 881347h
 ret
}}
}
bool dfl::chapter_two::store_title_detail::InstallImage(unsigned char* image,std::size_t bytes,PatchWriter write) noexcept {
 if(!image||bytes!=ImageSize||!write)return false;
 if(std::memcmp(image+0x481248,Prefix,sizeof(Prefix))||std::memcmp(image+0x481278,Suffix,sizeof(Suffix))||std::memcmp(image+0x481347,Prompt,sizeof(Prompt)))return false;
 std::array<unsigned char,9> patch{0xe9,0,0,0,0,0x90,0x90,0x90,0x90};
 const auto delta=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&Guard)-reinterpret_cast<std::uintptr_t>(image+Site)-5);
 std::memcpy(patch.data()+1,&delta,4);
 if(!std::memcmp(image+Site,patch.data(),patch.size()))return true;
 if(std::memcmp(image+Site,Original,sizeof(Original)))return false;
 if(write(reinterpret_cast<std::uintptr_t>(image+Site),patch.data(),patch.size()))return true;
 write(reinterpret_cast<std::uintptr_t>(image+Site),Original,sizeof(Original));return false;
}
bool dfl::chapter_two::InstallStoreTitle(unsigned char* image,std::size_t bytes) noexcept {
 return image==reinterpret_cast<unsigned char*>(0x400000)&&store_title_detail::InstallImage(image,bytes,memory_patch::WriteBytes);
}
