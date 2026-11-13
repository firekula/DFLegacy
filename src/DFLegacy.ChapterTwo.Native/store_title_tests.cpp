#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>
#ifndef CHAPTER_TWO_STORE_TITLE_BASELINE
#include "ChapterTwoStoreTitle.h"
#endif

#pragma section(".c2game",read,write)
__declspec(allocate(".c2game")) unsigned char titleReserve[0x1600000];
namespace {
constexpr unsigned ImageSize=0xe57000,Site=0x48126f;
unsigned Checks=0,Failures=0,Writes=0;bool FailWrite=false;
void Check(bool ok,const char* what){++Checks;if(!ok){++Failures;std::fprintf(stderr,"FAIL store title: %s\n",what);}}
std::vector<unsigned char> Load(const wchar_t* path){
 std::ifstream f(path,std::ios::binary);std::vector<unsigned char> raw{std::istreambuf_iterator<char>(f),{}};
 if(raw.size()<4096)return {};
 auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
 if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||static_cast<std::size_t>(dos->e_lfanew)>raw.size()-sizeof(IMAGE_NT_HEADERS32))return {};
 auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+dos->e_lfanew);
 if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.ImageBase!=0x400000||nt->OptionalHeader.SizeOfImage!=ImageSize||nt->OptionalHeader.SizeOfHeaders>raw.size())return {};
 auto* sections=IMAGE_FIRST_SECTION(nt);if(reinterpret_cast<const unsigned char*>(sections+nt->FileHeader.NumberOfSections)>raw.data()+raw.size())return {};
 std::vector<unsigned char> image(ImageSize);std::memcpy(image.data(),raw.data(),nt->OptionalHeader.SizeOfHeaders);
 for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];
  if(s.VirtualAddress>ImageSize||s.SizeOfRawData>ImageSize-s.VirtualAddress||s.PointerToRawData>raw.size()||s.SizeOfRawData>raw.size()-s.PointerToRawData)return {};
  std::memcpy(image.data()+s.VirtualAddress,raw.data()+s.PointerToRawData,s.SizeOfRawData);}
 return image;
}
void Jump(unsigned address,std::uintptr_t target){unsigned char b[]{0xe9,0,0,0,0};auto delta=static_cast<unsigned>(target-address-5);std::memcpy(b+1,&delta,4);std::memcpy(reinterpret_cast<void*>(address),b,5);}
#ifndef CHAPTER_TWO_STORE_TITLE_BASELINE
bool Write(std::uintptr_t a,const void* b,std::size_t n){++Writes;std::memcpy(reinterpret_cast<void*>(a),b,n);if(FailWrite){FailWrite=false;return false;}return true;}
#endif
unsigned Text[4]{},Widget[64]{},Accepted=0,Rejected=0,Before=0,After=0,SavedEax=0,SavedEcx=0,SavedEdx=0,SavedEbx=0,SavedEsi=0,SavedEdi=0;
void Accept(){++Accepted;}
void Reject(){++Rejected;}
__declspec(naked) void Invoke(){__asm {
 pushad
 mov Before,esp
 mov eax,offset Widget
 mov ecx,12345678h
 mov edx,offset Text
 mov ebx,23456789h
 xor esi,esi
 mov edi,3456789ah
 push offset Returned
 push 88126fh
 ret
Returned:
 mov After,esp
 mov SavedEax,eax
 mov SavedEcx,ecx
 mov SavedEdx,edx
 mov SavedEbx,ebx
 mov SavedEsi,esi
 mov SavedEdi,edi
 popad
 ret
}}
// Terminal spies keep the original decision instructions and object layout.
// Preserve registers as the surrounding native code still needs EAX/ECX/EDX.
__declspec(naked) void AcceptEnd(){__asm {
 pushad
 call Accept
 popad
 ret
}}
__declspec(naked) void RejectEnd(){__asm {
 pushad
 call Reject
 popad
 ret
}}
void Exercise(){
 Jump(0x88128c,reinterpret_cast<std::uintptr_t>(&AcceptEnd));Jump(0x881347,reinterpret_cast<std::uintptr_t>(&RejectEnd));
 struct Case{const char* text;bool blank;};
 const Case cases[]{{"",true},{" ",true},{"    ",true},{"\t\r\n",true},{"\xa1\xa1",true},{" \xa1\xa1 ",true},
  {"a",false},{" a ",false},{"\xb5\xea",false},{" \xb5\xea ",false},{"\xa1\xa1\xb5\xea",false},
  {"1234567890123456789012345678901234567890",false}};
 for(const auto& c:cases){
  Text[2]=static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(c.text));Text[3]=static_cast<unsigned>(std::strlen(c.text));
  Accepted=Rejected=0;Invoke();
  Check(c.blank?Rejected==1&&Accepted==0:Accepted==1&&Rejected==0,c.blank?"blank title uses native title prompt before submission":"nonblank title preserves native submission checks");
  Check(Before==After&&SavedEax==reinterpret_cast<std::uintptr_t>(Widget)&&SavedEcx==0x12345678&&SavedEdx==reinterpret_cast<std::uintptr_t>(Text)&&SavedEbx==0x23456789&&SavedEsi==0&&SavedEdi==0x3456789a,"native branch preserves stack and live registers");
 }
}
}
int wmain(int argc,wchar_t** argv){
 if(argc!=2)return 2;auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_STORE_TITLE_BASELINE
 using dfl::chapter_two::store_title_detail::InstallImage;
 auto copy=original;Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(copy.data(),ImageSize-1,Write)&&Writes==0,"invalid image never writes");
 for(unsigned off:{0x481248u,0x48126fu,0x481278u,0x481347u}){copy=original;copy[off]^=1;Writes=0;Check(!InstallImage(copy.data(),ImageSize,Write)&&Writes==0,"changed native context fails closed");}
 copy=original;Writes=0;FailWrite=true;Check(!InstallImage(copy.data(),ImageSize,Write)&&copy==original,"partial write failure restores original guard");
 copy=original;Writes=0;Check(InstallImage(copy.data(),ImageSize,Write)&&Writes==1,"verified guard installs");
 bool only=true;for(unsigned i=0;i<ImageSize;++i)if(copy[i]!=original[i]&&(i<Site||i>=Site+9))only=false;
 Check(only,"only title validation guard changes");Writes=0;Check(InstallImage(copy.data(),ImageSize,Write)&&Writes==0,"repeated installation is idempotent");
#endif
 auto* image=reinterpret_cast<unsigned char*>(0x400000);DWORD old=0;const auto start=reinterpret_cast<std::uintptr_t>(titleReserve);
 if(start>0x400000||start+sizeof(titleReserve)<0x400000+ImageSize||!VirtualProtect(image,ImageSize,PAGE_EXECUTE_READWRITE,&old))return 2;
 std::memcpy(image,original.data(),ImageSize);
#ifndef CHAPTER_TWO_STORE_TITLE_BASELINE
 Check(dfl::chapter_two::InstallStoreTitle(image,ImageSize),"production hook installs on original mapped image");
#endif
 Exercise();std::printf("store title: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
