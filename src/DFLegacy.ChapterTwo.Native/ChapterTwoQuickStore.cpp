#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "ChapterTwoQuickStore.h"
#include "../DFLegacy.Ijl15/src/memory_patch.h"
#include <array>
#include <cstring>
#include <limits>

namespace {
constexpr std::uintptr_t Base=0x400000;
constexpr std::size_t ImageSize=0xe57000;
// Native int32 -2 survives the signed capacity guard; native slot lookup rejects
// it before indexing. The writer emits uint16 FFFE. Never pass positive 65534.
constexpr int QuickDestination=-2;
template<class T>T Read(std::uintptr_t p){return *reinterpret_cast<const T*>(p);}
template<class T>T Field(const void* p,std::size_t off){return Read<T>(reinterpret_cast<std::uintptr_t>(p)+off);}
template<class T>T Virtual(void* p,std::size_t offset){return reinterpret_cast<T>(Field<std::uintptr_t>(Field<void*>(p,0),offset));}
using Getter=void*(__thiscall*)(void*);
using Count=unsigned(__thiscall*)(void*);
using Classify=bool(__thiscall*)(void*,int);
using Move=bool(__thiscall*)(void*,int,unsigned,void*,int);
using Close=void(__thiscall*)(void*);

struct Pending {
 bool active=false,opening=false;
 void* manager=nullptr;void* warehouse=nullptr;void* model=nullptr;void* destination=nullptr;
 void* dialog=nullptr;void* item=nullptr;void* record=nullptr;void* vtable=nullptr;
 int slot=0,sourceList=0,destinationList=2;unsigned id=0,count=0;
 // +54 is the icon field read by the original warehouse drag path; the record
 // getter returns this contiguous embedded value record. Secure values are
 // canonicalized below; their storage representation can change on a read.
 // This is not a persistent InstanceId or a claim to defeat allocator ABA.
 std::array<unsigned char,0x58> recordBytes{};
};
Pending pending;

void* Slot(void* model,int slot){return reinterpret_cast<void*(__thiscall*)(void*,int)>(0x404100)(model,slot);}
unsigned ItemCount(void* item){return Virtual<Count>(item,0x30)(item);}
void* Record(void* item){return Virtual<Getter>(item,0x14)(item);}
bool QueueEmpty(void* model){return Field<std::uintptr_t>(model,0x28)==Field<std::uintptr_t>(model,0x2c);}

bool WarehouseWindow(void* manager,void*& window){
 window=nullptr;
 const auto begin=Field<std::uintptr_t>(manager,0x238),end=Field<std::uintptr_t>(manager,0x23c),capacity=Field<std::uintptr_t>(manager,0x240);
 if(end<begin||capacity<end||(end-begin)%sizeof(void*)||(end-begin)/sizeof(void*)>8||(!begin&&end))return false;
 for(auto p=begin;p<end;p+=sizeof(void*)){
  auto* candidate=Read<void*>(p);
  if(candidate&&Field<std::uintptr_t>(candidate,0)==0xc16d2c){window=candidate;return true;}
 }
 return true;
}
bool Actionable(void* manager,void* warehouse,void* model,void* destination){
 void* current=nullptr;
 if(!manager||!warehouse||!model||!destination||Read<void*>(0xd56d24)!=manager||
    !((Read<void*>(0xd56d28)==model&&Read<void*>(0xd56d2c)==destination)||
      (Read<void*>(0xd56d2c)==model&&Read<void*>(0xd56d28)==destination))||!WarehouseWindow(manager,current)||current!=warehouse||
    Field<void*>(warehouse,0x10)!=manager||Read<int>(0xdfa77c)!=3||Field<int>(manager,0x1328)!=0)return false;
 auto* frame=Field<void*>(warehouse,8);
 return frame&&Field<unsigned char>(frame,0x10)&&Field<unsigned char>(frame,0x11)&&
  QueueEmpty(model)&&QueueEmpty(destination)&&!Field<unsigned char>(model,0x34)&&!Field<unsigned char>(destination,0x34);
}
bool Marker(void* dialog){return dialog&&
 ((Field<int>(dialog,0x2c)==0&&Field<int>(dialog,0x30)==2)||(Field<int>(dialog,0x2c)==2&&Field<int>(dialog,0x30)==0))&&
 Field<int>(dialog,0x38)==QuickDestination;}
bool Owns(void* dialog){return pending.active&&pending.dialog==dialog&&Marker(dialog)&&
 Field<int>(dialog,0x28)==1&&Field<int>(dialog,0x2c)==pending.sourceList&&Field<int>(dialog,0x30)==pending.destinationList&&Field<int>(dialog,0x34)==pending.slot&&
 Field<unsigned>(dialog,0x44)==pending.id&&Field<unsigned>(dialog,0x48)==pending.count;}
std::array<unsigned char,0x58> RecordSnapshot(void* record){
 std::array<unsigned char,0x58> result{};
 std::memcpy(result.data(),record,result.size());
 // Common constructor 473F30 creates the two address-bound secure ints.
 // Decode the live fields, never the local copy; equivalent re-keying is not
 // an item mutation, while a changed decoded value must still invalidate it.
 for(std::size_t offset:{0x3cu,0x4cu}){
  const int value=reinterpret_cast<int(__thiscall*)(void*)>(0xa46ab0)(static_cast<unsigned char*>(record)+offset);
  std::memset(result.data()+offset,0,8);
  std::memcpy(result.data()+offset,&value,sizeof(value));
 }
 return result;
}
bool SourceUnchanged(){
 // Resolve current slot before touching item fields; saved objects may be freed.
 auto* item=Slot(pending.model,pending.slot);
 if(!item||item!=pending.item||Field<void*>(item,0)!=pending.vtable||ItemCount(item)!=pending.count)return false;
 auto* record=Record(item);
 return record&&record==pending.record&&Field<unsigned>(record,8)==pending.id&&
  RecordSnapshot(record)==pending.recordBytes;
}
bool ValidPending(void* dialog){
 if(!Owns(dialog)||!Actionable(pending.manager,pending.warehouse,pending.model,pending.destination)||
    Field<void*>(pending.manager,0xc4)!=dialog)return false;
 auto* frame=Field<void*>(dialog,8);
 if(!frame||!Field<unsigned char>(frame,0x10))return false;
 return SourceUnchanged();
}
void __stdcall ClearClosed(void* dialog){if(pending.dialog==dialog){pending.active=false;pending.opening=false;}}
void SafeClose(void* dialog) noexcept {
 pending.active=false;
 __try {if(dialog)reinterpret_cast<Close>(0x935cb0)(dialog);} __except(EXCEPTION_EXECUTE_HANDLER){}
}

bool Transfer(void* manager,void* warehouse,void* model,void* destination,int slot,bool withdraw){
 if(!Actionable(manager,warehouse,model,destination))return true;
 auto* dialog=Field<void*>(manager,0xc4);
 if(!dialog||pending.active)return true;
 auto* frame=Field<void*>(dialog,8);
 if(!frame||Field<unsigned char>(frame,0x10))return true;
 auto* item=Slot(model,slot);
 if(!item)return true;
 auto* record=Record(item);
 if(!record)return true;
 const unsigned count=ItemCount(item),id=Field<unsigned>(record,8);
 if(!count||!id)return true;
 const bool stackable=Virtual<Classify>(item,0x18)(item,1);
 if(stackable){auto* kind=Virtual<Getter>(item,0xa8)(item);if(!kind||Field<int>(kind,0)==7)return true;}
 const auto key=Read<SHORT(WINAPI*)(int)>(0xaea440);
 const bool shift=key&&(key(VK_SHIFT)&0x8000);
 // Single items have no count to choose: deposit/withdraw them directly (the same
 // native move used by Shift+right-click). Only stacks above one open the count
 // dialog, matching the original game's right-click behavior.
 if(!shift&&count>1){
  pending.manager=manager;pending.warehouse=warehouse;pending.model=model;pending.destination=destination;
  pending.dialog=dialog;pending.item=item;pending.record=record;pending.vtable=Field<void*>(item,0);
  pending.slot=slot;pending.id=id;pending.count=count;pending.sourceList=withdraw?2:0;pending.destinationList=withdraw?0:2;
  pending.recordBytes=RecordSnapshot(record);pending.active=true;
  using Open=void(__thiscall*)(void*,int,int,int,int,int,int,int);
  pending.opening=true;
  reinterpret_cast<Open>(0x935e60)(dialog,1,pending.sourceList,slot,pending.destinationList,QuickDestination,0,0);
  pending.opening=false;
  if(!Owns(dialog)||!Field<unsigned char>(frame,0x10))pending.active=false;
  return true;
 }
 const auto weight=Virtual<Count>(item,0x28)(item);
 if(reinterpret_cast<bool(__cdecl*)(unsigned,bool)>(withdraw?0x8782d0:0x901f20)(weight,true))return true;
 reinterpret_cast<Move>(0x471680)(model,slot,count,destination,QuickDestination);
 return true;
}
bool UseOrStore(void* model,int slot){
 if(slot<9||slot>=137)return reinterpret_cast<bool(__thiscall*)(void*,int)>(0x472f60)(model,slot);
 auto* manager=Read<void*>(0xd56d24);
 if(!manager)return true;
 void* warehouse=nullptr;
 if(!WarehouseWindow(manager,warehouse))return true;
 if(!warehouse){
  if(pending.active)SafeClose(pending.dialog);
  return reinterpret_cast<bool(__thiscall*)(void*,int)>(0x472f60)(model,slot);
 }
 return Transfer(manager,warehouse,model,Read<void*>(0xd56d2c),slot,false);
}
int __fastcall WarehouseHitHook(void* window,void*) noexcept {
 int slot=-1;
 __try {
  slot=reinterpret_cast<int(__thiscall*)(void*)>(0x901de0)(window);
  auto* frame=Field<void*>(window,8);
  if(slot>=0&&slot<120&&slot<Field<int>(window,0x48)&&frame&&
     Read<unsigned>(0xd4dfc0)==Field<unsigned>(frame,0xc))
   Transfer(Read<void*>(0xd56d24),window,Read<void*>(0xd56d2c),Read<void*>(0xd56d28),slot,true);
 } __except(EXCEPTION_EXECUTE_HANDLER){pending.active=false;pending.opening=false;}
 return slot;
}
bool __stdcall QuantityClassify(void* dialog,void* item) noexcept {
 __try {
  if(Virtual<Classify>(item,0x18)(item,1))return true;
  // Original Open has set the direction fields, but not ID/max yet. Restrict
  // this exception to the current owned opening and a verified single gear.
  return pending.active&&pending.opening&&pending.dialog==dialog&&pending.item==item&&pending.count==1&&
   Field<void*>(pending.manager,0xc4)==dialog&&Field<int>(dialog,0x28)==1&&
   Field<int>(dialog,0x2c)==pending.sourceList&&Field<int>(dialog,0x30)==pending.destinationList&&
   Field<int>(dialog,0x34)==pending.slot&&Marker(dialog)&&
   Actionable(pending.manager,pending.warehouse,pending.model,pending.destination)&&SourceUnchanged()&&
   Virtual<Classify>(item,0x18)(item,2);
 } __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
__declspec(naked) void QuantityClassifyHook(){__asm {
 push edi
 push esi
 call QuantityClassify
 ret
}}
bool __fastcall UseHook(void* model,void*,int slot) noexcept {
 __try {return UseOrStore(model,slot);} __except(EXCEPTION_EXECUTE_HANDLER){pending.active=false;pending.opening=false;return true;}
}
void __fastcall ConfirmHook(void* dialog,void*) noexcept {
 bool forward=false;
 __try {
  if(pending.active&&pending.dialog==dialog){
   if(!Marker(dialog)){pending.active=false;forward=true;}
   else if(ValidPending(dialog)){pending.active=false;forward=true;}
   else {SafeClose(dialog);return;}
  } else if(Marker(dialog)){SafeClose(dialog);return;}
  else forward=true;
 } __except(EXCEPTION_EXECUTE_HANDLER){SafeClose(dialog);return;}
 if(forward)reinterpret_cast<Close>(0x936540)(dialog);
}
void __fastcall TickHook(void* dialog,void*) noexcept {
 __try {
  if(pending.active){
   if(pending.dialog!=dialog||!Marker(dialog)){pending.active=false;}
   else if(!ValidPending(dialog)){SafeClose(dialog);return;}
  }
 } __except(EXCEPTION_EXECUTE_HANDLER){SafeClose(dialog);return;}
 reinterpret_cast<Close>(0x936a10)(dialog);
}
std::uintptr_t CloseContinuation=0x935cb6;
__declspec(naked) void CloseHook(){__asm {
 pushfd
 pushad
 push ecx
 call ClearClosed
 popad
 popfd
 push esi
 mov esi,ecx
 mov eax,[esi+0ch]
 jmp dword ptr [CloseContinuation]
}}

struct Signature{std::uintptr_t address;const char* hex;};
constexpr Signature Context[]{
 {0x473f99,"8d4e3cc645fc03e8ab01f9ff8d4e4cc645fc04e89f01f9ff"},
 {0xa46ab0,"e9ac145500"},{0xa47865,"8947048b0df45af3008d143833d18917"},
 {0x902362,"8bce894648"},{0x90236c,"8946448b462c8a481184c9"},
 {0x935f38,"8bf885ff0f842f030000"},{0x935f4b,"84c00f841e0300008b078bcfff5014"},
 {0x8782d0,"558bec81ec000400005657b92cacdf00e8cbe71c008bf8e8d4680b008bf08b45082bf72bf07958"},
 {0x878a80,"568bf18b46048a481184c974468b0dc0dfd4003b480c753b8b56088b822813000085c0752e6a018bcee892f1ffff8bc883f9ff741e8b464050e852f3ffff03c18b0d286dd50083c40450"},
 {0x878acf,"b0015ec332c05ec3"},
 {0x936c68,"8b46108a481184c9740b8b0dbcdfd4003b480c740e8b460c8a487484c9c640740074078bce"},
 {0x868b0b,"8b8ec4000000"},{0x935cb6,"8b0ddcdfd4003b480c7523"},
 {0x404100,"558bec568bf18d4e10e8a22964008b4d083bc87d0f85c97c0b8b46208b04885e5dc2040033c05e5dc20400"},
 {0x4716ca,"8d4b10e8de535d003945147d42"},
 {0x471800,"8b7d148b0d98ccdf006a028d550c52897d0ce8c9a25500578bcee8e128f9ff"},
 {0x935e66,"8b450c8b5514538b5d0883fb05568bf18b4d1089462c8b4518894e348b4d208946388b451c894e4057895e2889563089463c8bce"},
 {0x936716,"8b46388b4e305051e89d5ab0ff8b563483c404508b462c575250e88b5ab0ff83c4048bc8e841afb3ff"},
 {0x473e90,"81c1b4000000e9152c5d00"},{0x442a40,"8d4108c3"},
 {0xc1155c,"808a8700"},{0xc16d30,"3028900030239000001d9000801d9000"},
 {0x9026e3,"a17ca7df0048b9030000003bc10f871f010000ff24851c289000"}
};
unsigned Nibble(char c){return c<='9'?static_cast<unsigned>(c-'0'):static_cast<unsigned>(c-'a'+10);}
bool Matches(const unsigned char* p,const char* hex){for(std::size_t i=0;hex[i];i+=2,++p)if(*p!=((Nibble(hex[i])<<4)|Nibble(hex[i+1])))return false;return true;}
}

bool dfl::chapter_two::quick_store_detail::InstallImage(unsigned char* image,std::size_t bytes,PatchWriter write) noexcept {
 static_assert(sizeof(void*)==4,"C2 quick store requires x86");
 if(!image||bytes!=ImageSize||!write||reinterpret_cast<std::uintptr_t>(image)>std::numeric_limits<std::uintptr_t>::max()-ImageSize)return false;
 for(const auto& s:Context)if(!Matches(image+s.address-Base,s.hex))return false;
 struct Patch {std::uintptr_t address,target;std::size_t size;std::array<unsigned char,9> original,replacement;};
 Patch sites[]{
 {0x902367,reinterpret_cast<std::uintptr_t>(&WarehouseHitHook),5,{0xe8,0x74,0xfa,0xff,0xff},{}},
 {0x935f42,reinterpret_cast<std::uintptr_t>(&QuantityClassifyHook),9,{0x8b,0x17,0x6a,0x01,0x8b,0xcf,0xff,0x52,0x18},{}},
 {0x878aca,reinterpret_cast<std::uintptr_t>(&UseHook),5,{0xe8,0x91,0xa4,0xbf,0xff},{}},
 {0x936c8d,reinterpret_cast<std::uintptr_t>(&ConfirmHook),5,{0xe8,0xae,0xf8,0xff,0xff},{}},
 {0x868b11,reinterpret_cast<std::uintptr_t>(&TickHook),5,{0xe8,0xfa,0xde,0x0c,0x00},{}},
 {0x935cb0,reinterpret_cast<std::uintptr_t>(&CloseHook),6,{0x56,0x8b,0xf1,0x8b,0x46,0x0c},{}}
 };
 unsigned installed=0;
 for(auto& p:sites){p.replacement.fill(0x90);p.replacement[0]=p.size==6?0xe9:0xe8;p.replacement[5]=0x90;
  const auto delta=static_cast<std::uint32_t>(p.target-reinterpret_cast<std::uintptr_t>(image+p.address-Base)-5);
  std::memcpy(p.replacement.data()+1,&delta,4);const auto* at=image+p.address-Base;
  if(!std::memcmp(at,p.replacement.data(),p.size))++installed;else if(std::memcmp(at,p.original.data(),p.size))return false;
 }
 if(installed)return installed==std::size(sites);
 for(unsigned i=0;i<std::size(sites);++i){const auto& p=sites[i];const auto address=reinterpret_cast<std::uintptr_t>(image+p.address-Base);
  if(write(address,p.replacement.data(),p.size)&&!std::memcmp(reinterpret_cast<void*>(address),p.replacement.data(),p.size))continue;
  for(unsigned j=0;j<=i;++j)write(reinterpret_cast<std::uintptr_t>(image+sites[j].address-Base),sites[j].original.data(),sites[j].size);
  return false;
 }
 return true;
}
bool dfl::chapter_two::InstallQuickStore(unsigned char* image,std::size_t bytes) noexcept {
 return image==reinterpret_cast<unsigned char*>(Base)&&quick_store_detail::InstallImage(image,bytes,memory_patch::WriteBytes);
}
