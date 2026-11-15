#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <fstream>
#include <iterator>
#include <vector>
#ifndef CHAPTER_TWO_JAR_FEEDBACK_BASELINE
#include "ChapterTwoJarFeedback.h"
#endif
#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char feedbackReserve[0x1300000]{1};
namespace {
constexpr std::size_t ImageSize=0xe57000;
unsigned Checks=0,Failures=0,Writes=0;bool FailWrite=false;
void Check(bool ok,const char* label){++Checks;if(!ok){++Failures;std::fprintf(stderr,"FAIL jar feedback: %s\n",label);}}
std::vector<unsigned char> Load(const wchar_t* path){
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
void Jump(unsigned a,std::uintptr_t target){unsigned char b[]{0xe9,0,0,0,0};auto d=static_cast<unsigned>(target-a-5);std::memcpy(b+1,&d,4);std::memcpy(reinterpret_cast<void*>(a),b,5);}
#ifndef CHAPTER_TWO_JAR_FEEDBACK_BASELINE
bool Write(std::uintptr_t a,const void* b,std::size_t n){++Writes;std::memcpy(reinterpret_cast<void*>(a),b,n);if(FailWrite){FailWrite=false;return false;}return true;}
#endif
unsigned Message=0,Dialogs=0,Code=0,Before=0,After=0,SavedEsi=0,SavedEdi=0,SavedEbx=0;
wchar_t Shown[256]{};
const char* __cdecl Text(unsigned id){Message=id;return "native";}
void __fastcall Dialog(void*,void*,unsigned kind,const char* text,unsigned flags){++Dialogs;Check(kind==0x25&&flags==0,"normal native acknowledgement dialog");MultiByteToWideChar(936,0,text,-1,Shown,256);}
void Special(){Message=30005;}
__declspec(naked) void Invoke(){__asm {
 pushad
 mov Before,esp
 xor ebx,ebx
 mov esi,12345678h
 mov edi,23456789h
 mov eax,Code
 push offset Returned
 push 414280h
 ret
Returned:
 mov After,esp
 mov SavedEsi,esi
 mov SavedEdi,edi
 mov SavedEbx,ebx
 popad
 ret
}}
void Exercise(){Jump(0x9bffe0,reinterpret_cast<std::uintptr_t>(&Text));Jump(0x8657d0,reinterpret_cast<std::uintptr_t>(&Dialog));
 *reinterpret_cast<unsigned char*>(0x41cbe9)=0xc3;Jump(0x4142f0,reinterpret_cast<std::uintptr_t>(&Special));
 for(Code=0;Code<256;++Code){Message=Dialogs=0;Shown[0]=0;Invoke();
  Check(Before==After&&SavedEsi==0x12345678&&SavedEdi==0x23456789&&SavedEbx==0,"handler returns with stack and callee-saved registers intact");
  if(Code==21)Check(Dialogs==1&&std::wcsstr(Shown,L"负重")&&std::wcsstr(Shown,L"袖珍罐")&&!std::wcsstr(Shown,L"空间不足"),"overweight shows weight-specific jar explanation");
  else {unsigned expected=Code==17?765:Code==14?766:Code==4?767:Code==19?768:Code==124?30005:0;
   Check(Message==expected&&Dialogs==((expected&&Code!=124)?1u:0u),"existing full level gold and unknown error behavior preserved");}
 }
}
}
int wmain(int argc,wchar_t** argv){if(argc!=2)return 2;auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_JAR_FEEDBACK_BASELINE
 using dfl::chapter_two::jar_feedback_detail::InstallImage;
 auto copy=original;Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(copy.data(),ImageSize-1,Write)&&Writes==0,"bad image has no writes");
 for(unsigned offset:{0x14280u,0x142b4u,0x142e8u,0x142f0u,0x1cbdeu}){copy=original;copy[offset]^=1;Writes=0;Check(!InstallImage(copy.data(),ImageSize,Write)&&Writes==0,"wrong context fails closed");}
 copy=original;Writes=0;FailWrite=true;Check(!InstallImage(copy.data(),ImageSize,Write)&&copy==original,"failed partial write restores original branch");
 copy=original;Writes=0;Check(InstallImage(copy.data(),ImageSize,Write)&&Writes==1,"verified image installs one branch");
 bool only=true;for(unsigned i=0;i<ImageSize;++i)if(copy[i]!=original[i]&&(i<0x142e8||i>=0x142f0))only=false;
 Check(only,"only jar rejection branch changes");Writes=0;Check(InstallImage(copy.data(),ImageSize,Write)&&Writes==0,"repeat installation no writes");
#endif
 auto* image=reinterpret_cast<unsigned char*>(0x400000);DWORD old=0;auto start=reinterpret_cast<std::uintptr_t>(feedbackReserve);
 if(start>0x400000||start+sizeof(feedbackReserve)<0x400000+ImageSize||!VirtualProtect(image,ImageSize,PAGE_EXECUTE_READWRITE,&old))return 2;
 std::memcpy(image,original.data(),ImageSize);
#ifndef CHAPTER_TWO_JAR_FEEDBACK_BASELINE
 Check(dfl::chapter_two::InstallJarFeedback(image,ImageSize),"production install on real mapped client");
#endif
 Exercise();std::printf("jar feedback: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
