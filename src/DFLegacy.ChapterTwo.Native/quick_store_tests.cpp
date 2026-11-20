#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>
#ifndef CHAPTER_TWO_QUICK_STORE_BASELINE
#include "ChapterTwoQuickStore.h"
#endif
#pragma section(".gameres",read,write)
__declspec(allocate(".gameres")) volatile unsigned char quickStoreFixtureReserve[0x1300000]{1};
namespace {
constexpr unsigned Base=0x400000,ImageSize=0xe57000;
unsigned Checks=0,Failures=0,Writes=0,FailAt=0;bool FailAfter=false;
void Check(bool ok,const char* message){++Checks;if(!ok){++Failures;std::fprintf(stderr,"FAIL quick store: %s\n",message);}}
template<class T>T& At(std::uintptr_t a){return *reinterpret_cast<T*>(a);}
template<class T>T& F(void* p,unsigned off){return *reinterpret_cast<T*>(static_cast<unsigned char*>(p)+off);}
std::vector<unsigned char> Load(const wchar_t* path){
 std::ifstream f(path,std::ios::binary);std::vector<unsigned char> raw{std::istreambuf_iterator<char>(f),{}};
 if(raw.size()<4096)return {};
 const auto* d=reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
 if(d->e_magic!=IMAGE_DOS_SIGNATURE||d->e_lfanew<0||static_cast<std::size_t>(d->e_lfanew)>raw.size()-sizeof(IMAGE_NT_HEADERS32))return {};
 const auto* n=reinterpret_cast<const IMAGE_NT_HEADERS32*>(raw.data()+d->e_lfanew);
 if(n->Signature!=IMAGE_NT_SIGNATURE||n->OptionalHeader.ImageBase!=Base||n->OptionalHeader.SizeOfImage!=ImageSize||n->OptionalHeader.SizeOfHeaders>4096)return {};
 const auto* s=IMAGE_FIRST_SECTION(n);if(reinterpret_cast<const unsigned char*>(s+n->FileHeader.NumberOfSections)>raw.data()+raw.size())return {};
 std::vector<unsigned char> image(ImageSize);std::memcpy(image.data(),raw.data(),n->OptionalHeader.SizeOfHeaders);
 for(unsigned i=0;i<n->FileHeader.NumberOfSections;++i){if(s[i].VirtualAddress>ImageSize||s[i].SizeOfRawData>ImageSize-s[i].VirtualAddress||s[i].PointerToRawData>raw.size()||s[i].SizeOfRawData>raw.size()-s[i].PointerToRawData)return {};
 std::memcpy(image.data()+s[i].VirtualAddress,raw.data()+s[i].PointerToRawData,s[i].SizeOfRawData);}return image;
}
bool Write(std::uintptr_t a,const void* b,std::size_t n){++Writes;if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(a),b,n);return Writes!=FailAt;}
void Jump(unsigned a,std::uintptr_t target){unsigned char b[]{0xe9,0,0,0,0};auto delta=static_cast<std::uint32_t>(target-a-5);std::memcpy(b+1,&delta,4);std::memcpy(reinterpret_cast<void*>(a),b,5);}
std::array<unsigned char,0x1400> Manager{};
std::array<unsigned char,0x100> Inventory{},Warehouse{},Item{},Other{},Window{},Storage{},Frame{},Quantity{},QuantityFrame{},Special{},Block{};
std::array<unsigned char,0x500> Actor{};
std::array<void*,137> Slots{};std::array<void*,120> Stored{};std::array<void*,1> Active{};
std::array<std::uintptr_t,64> ItemVtable{},DialogVtable{};
std::array<unsigned,1> BagQueue{},StoreQueue{};
unsigned Used=0,Opened=0,Confirmed=0,Closed=0,Ticks=0,Sent=0,Command=0,Selected=0,StackKind=1,RequestedCount=0;
bool Shift=false,WeightRejected=false,NativeStackable=true;std::vector<unsigned char> Packet{};
int __fastcall Secure(void* p,void*){
 if(p==Inventory.data())return 0;if(p==Warehouse.data())return 2;
 if(p==Inventory.data()+0x10)return 137;if(p==Warehouse.data()+0x10)return 120;
 // Fixture for the two native re-encoded values in the shared item record.
 if(p==Item.data()+0x44||p==Item.data()+0x54)return F<int>(p,0)^F<int>(p,4);
 return *static_cast<int*>(p);
}
bool __fastcall Classify(void*,void*,int kind){return NativeStackable?kind==1:kind==2;}
void* __fastcall Kind(void*,void*){return &StackKind;}
unsigned __fastcall Weight(void*,void*){return 1;}
bool __cdecl WarehouseWeight(unsigned,bool){return WeightRejected;}
SHORT WINAPI Key(int vk){return vk==VK_SHIFT&&Shift?static_cast<SHORT>(0x8000):0;}
int __fastcall Hit(void*,void*,int){return 0;}
bool __fastcall Use(void*,void*,int slot){++Used;Selected=static_cast<unsigned>(slot);return true;}
bool Classifier(void* p,void* item){
 if(At<unsigned char>(0x935f42)!=0xe8)return Classify(item,nullptr,1);
 auto target=0x935f47+At<std::int32_t>(0x935f43);bool result=false;
 __asm {push esi
 push edi
 mov esi,p
 mov edi,item
 call target
 mov result,al
 pop edi
 pop esi}
 return result;
}
int __fastcall WarehouseHit(void*,void*){return 77;}
void __fastcall Open(void* p,void*,int mode,int src,int slot,int dst,int target,int extra1,int extra2){
 ++Opened;Check(mode==1&&((src==0&&slot==73&&dst==2)||(src==2&&slot==77&&dst==0))&&target==-2&&extra1==0&&extra2==0,"native quantity receives correct source and destination ABI");
 F<int>(p,0x28)=mode;F<int>(p,0x2c)=src;F<int>(p,0x30)=dst;F<int>(p,0x34)=slot;F<int>(p,0x38)=target;
 if(!Classifier(p,Item.data()))return;
 F<unsigned>(p,0x44)=F<unsigned>(Item.data(),0x10);F<unsigned>(p,0x48)=F<unsigned>(Item.data(),0xb4);
 F<unsigned char>(QuantityFrame.data(),0x10)=1;
}
void __fastcall Begin(void*,void*,unsigned cmd){Command=cmd;Packet.clear();}
void __fastcall Bytes(void*,void*,const unsigned char* p,unsigned count){Packet.insert(Packet.end(),p,p+count);}
unsigned __fastcall TickValue(void*,void*){return 123456;}
void __cdecl Send(){++Sent;}
void __fastcall Queue(void* model,void*,unsigned operation){Check(operation==4,"native sender queues move operation");auto* q=model==Inventory.data()?BagQueue.data():StoreQueue.data();q[0]=operation;F<unsigned*>(model,0x28)=q;F<unsigned*>(model,0x2c)=q+1;}
void __fastcall OriginalConfirm(void* p,void*){++Confirmed;using Move=bool(__thiscall*)(void*,int,unsigned,void*,int);reinterpret_cast<Move>(0x471680)(F<int>(p,0x2c)==2?Warehouse.data():Inventory.data(),F<int>(p,0x34),RequestedCount?RequestedCount:F<unsigned>(p,0x48),F<int>(p,0x30)==2?Warehouse.data():Inventory.data(),F<int>(p,0x38));reinterpret_cast<void(__thiscall*)(void*)>(0x935cb0)(p);}
void __fastcall OriginalTick(void*,void*){++Ticks;}
void __fastcall CloseBody(void* p,void*){++Closed;F<unsigned char>(F<void*>(p,8),0x10)=0;F<int>(p,0x28)=6;}
__declspec(naked) void CloseTail(){__asm { mov ecx,esi
 call CloseBody
 pop esi
 ret
}}
void Call(unsigned site,void* p){const auto target=site+5+At<std::int32_t>(site+1);reinterpret_cast<void(__thiscall*)(void*)>(target)(p);}
void Right(){reinterpret_cast<bool(__thiscall*)(void*)>(0x878a80)(Window.data());}
void* AbiTarget=nullptr;void* AbiThis=nullptr;unsigned AbiArgument=0,BeforeEsp=0,AfterEsp=0,AfterEbx=0,AfterEsi=0,AfterEdi=0,AfterEbp=0;
__declspec(naked) void AbiCall(){__asm {
 pushad
 mov ebx,12345678h
 mov esi,23456789h
 mov edi,3456789ah
 mov ebp,456789abh
 mov ecx,AbiThis
 mov BeforeEsp,esp
 cmp AbiArgument,0
 je invoke
 push 73
invoke:
 call AbiTarget
 mov AfterEsp,esp
 mov AfterEbx,ebx
 mov AfterEsi,esi
 mov AfterEdi,edi
 mov AfterEbp,ebp
 popad
 ret
}}
void CheckAbi(unsigned target,void* object,bool argument){AbiTarget=reinterpret_cast<void*>(target);AbiThis=object;AbiArgument=argument?1u:0u;AbiCall();Check(BeforeEsp==AfterEsp&&AfterEbx==0x12345678&&AfterEsi==0x23456789&&AfterEdi==0x3456789a&&AfterEbp==0x456789ab,"hook preserves native stack and callee-saved registers");}
void Reset(bool open=true){
 if(F<unsigned char>(QuantityFrame.data(),0x10))reinterpret_cast<void(__thiscall*)(void*)>(0x935cb0)(Quantity.data());
 Manager.fill(0);Inventory.fill(0);Warehouse.fill(0);Item.fill(0);Other.fill(0);Window.fill(0);Storage.fill(0);Frame.fill(0);Quantity.fill(0);QuantityFrame.fill(0);Special.fill(0);Actor.fill(0);Slots.fill(nullptr);Stored.fill(nullptr);
 Used=Opened=Confirmed=Closed=Ticks=Sent=Command=RequestedCount=0;Shift=WeightRejected=false;NativeStackable=true;StackKind=1;
 At<void*>(0xd56d24)=Manager.data();At<void*>(0xd56d28)=Inventory.data();At<void*>(0xd56d2c)=Warehouse.data();At<void*>(0xd56d38)=Special.data();At<void*>(0xd7a8c0)=Actor.data();At<int>(0xdfa77c)=3;
 F<unsigned short>(Special.data(),4)=65535;F<void*>(Inventory.data(),0x20)=Slots.data();F<void*>(Warehouse.data(),0x20)=Stored.data();Slots[73]=Item.data();Stored[77]=Item.data();
 F<void*>(Item.data(),0)=ItemVtable.data();F<unsigned>(Item.data(),0x10)=3037;F<unsigned>(Item.data(),0xb4)=999;Other=Item;
 Active[0]=Storage.data();F<void*>(Manager.data(),0x238)=Active.data();F<void*>(Manager.data(),0x23c)=Active.data()+(open?1:0);F<void*>(Manager.data(),0x240)=Active.data()+1;
 F<void*>(Manager.data(),0xc4)=Quantity.data();F<void*>(Storage.data(),0)=reinterpret_cast<void*>(0xc16d2c);F<void*>(Storage.data(),8)=Frame.data();F<void*>(Storage.data(),0x10)=Manager.data();F<int>(Storage.data(),0x48)=120;
 F<unsigned char>(Frame.data(),0x10)=1;F<unsigned char>(Frame.data(),0x11)=1;F<unsigned>(Frame.data(),0xc)=123;
 F<void*>(Window.data(),4)=Frame.data();F<void*>(Window.data(),8)=Manager.data();F<int>(Window.data(),0x40)=2;At<unsigned>(0xd4dfc0)=123;
 F<void*>(Quantity.data(),0)=DialogVtable.data();F<void*>(Quantity.data(),4)=Manager.data();F<void*>(Quantity.data(),8)=QuantityFrame.data();F<int>(Quantity.data(),0x28)=6;
}
void Exercise(){
 ItemVtable[0x14/4]=0x442a40;ItemVtable[0x18/4]=reinterpret_cast<std::uintptr_t>(&Classify);ItemVtable[0x28/4]=reinterpret_cast<std::uintptr_t>(&Weight);ItemVtable[0x30/4]=0x473e90;ItemVtable[0xa8/4]=reinterpret_cast<std::uintptr_t>(&Kind);
 Jump(0xa46ab0,reinterpret_cast<std::uintptr_t>(&Secure));Jump(0x877c40,reinterpret_cast<std::uintptr_t>(&Hit));Jump(0x472f60,reinterpret_cast<std::uintptr_t>(&Use));Jump(0x935e60,reinterpret_cast<std::uintptr_t>(&Open));
 Jump(0x936540,reinterpret_cast<std::uintptr_t>(&OriginalConfirm));Jump(0x936a10,reinterpret_cast<std::uintptr_t>(&OriginalTick));Jump(0x935cb6,reinterpret_cast<std::uintptr_t>(&CloseTail));Jump(0x901f20,reinterpret_cast<std::uintptr_t>(&WarehouseWeight));Jump(0x8782d0,reinterpret_cast<std::uintptr_t>(&WarehouseWeight));Jump(0x901de0,reinterpret_cast<std::uintptr_t>(&WarehouseHit));
 Jump(0x9cbc20,reinterpret_cast<std::uintptr_t>(&Begin));Jump(0x9cbae0,reinterpret_cast<std::uintptr_t>(&Bytes));Jump(0x847710,reinterpret_cast<std::uintptr_t>(&TickValue));Jump(0x9cbcc0,reinterpret_cast<std::uintptr_t>(&Send));Jump(0x40f5d0,reinterpret_cast<std::uintptr_t>(&Queue));At<std::uintptr_t>(0xaea440)=reinterpret_cast<std::uintptr_t>(&Key);
 Reset(false);Right();Check(Used==1&&Selected==73&&Sent==0,"closed warehouse preserves native item use");
 Reset();Shift=true;Right();Check(Used==0&&Sent==1&&Command==19&&Opened==0,"Shift right-click deposits whole stack without prompt");
 const std::vector<unsigned char> expected{0,73,0,0xdd,0x0b,0xe7,3,0,0,2,0xfe,0xff,0,0,0x40,0xe2,1,0};Check(Packet==expected,"full native count and reserved destination are serialized");
 Right();Check(Sent==1&&Used==0,"in-flight second click neither sends nor uses");
 Reset();Right();Check(Opened==1&&Used==0&&Sent==0&&F<unsigned>(Quantity.data(),0x48)==999,"plain right-click opens native maximum-count dialog");
#ifndef CHAPTER_TWO_QUICK_STORE_BASELINE
 Call(0x936c8d,Quantity.data());Check(Confirmed==1&&Sent==1&&Used==0,"unchanged quantity confirmation deposits");
 Reset();F<unsigned>(Item.data(),0xb4)=1;Right();Check(Opened==0&&Sent==1&&Used==0&&Command==19,"plain single item deposits directly without quantity prompt");
 Reset();NativeStackable=false;F<unsigned>(Item.data(),0xb4)=1;Right();Check(Opened==0&&Sent==1&&Used==0,"plain equipment deposits directly without quantity prompt");
 Reset();Shift=true;NativeStackable=false;F<unsigned>(Item.data(),0xb4)=1;Right();Check(Opened==0&&Sent==1&&Used==0,"Shift equipment deposits directly");
 Reset();At<int>(0xdfa77c)=4;Right();Check(Used==0&&Sent==0&&Opened==0,"busy open warehouse consumes right-click without use");
 Reset();Shift=true;WeightRejected=true;Right();Check(Used==0&&Sent==0,"native weight rejection does not use item");
 Reset();StackKind=7;Right();Check(Used==0&&Sent==0,"quest stack is not sent or used");
 Reset();Right();Slots[73]=Other.data();Call(0x936c8d,Quantity.data());Check(Confirmed==0&&Sent==0&&Closed==1,"same-ID same-count replacement pointer rejects stale confirmation");
 Reset();Right();F<unsigned>(Item.data(),0x14)=123;Call(0x936c8d,Quantity.data());Check(Confirmed==0&&Sent==0&&Closed==1,"changed record rejects stale confirmation");
 Reset();Right();Slots[73]=nullptr;Call(0x936c8d,Quantity.data());Check(Confirmed==0&&Sent==0&&Closed==1,"missing source rejects quantity confirmation");
 Reset();Right();F<unsigned>(Item.data(),0xb4)=998;Call(0x936c8d,Quantity.data());Check(Confirmed==0&&Sent==0&&Closed==1,"changed source count rejects quantity confirmation");
 Reset();Right();F<void*>(Manager.data(),0x23c)=Active.data();Call(0x868b11,Quantity.data());Check(Closed==1&&Ticks==0&&Used==0,"closing warehouse cancels prompt on native tick");
 F<void*>(Manager.data(),0x23c)=Active.data()+1;Call(0x936c8d,Quantity.data());Check(Sent==0,"reopening warehouse cannot resurrect stale shortcut");
 Reset();Right();reinterpret_cast<void(__thiscall*)(void*)>(0x935cb0)(Quantity.data());Call(0x936c8d,Quantity.data());Check(Sent==0&&Used==0,"standard close clears ownership and cannot send stale marker");
 Reset();Right();Right();Check(Opened==1&&Sent==0&&Used==0,"repeated right-click cannot overwrite pending dialog");
 Reset();Right();F<int>(Quantity.data(),0x38)=5;Call(0x936c8d,Quantity.data());Check(Confirmed==1&&Sent==1,"a new manual dialog that replaces shortcut context remains native");
 Reset();F<int>(Window.data(),0x40)=4;Right();Check(Used==1&&Selected==137&&Sent==0,"extra special inventory tab retains original use behavior");
 Reset();F<int>(Quantity.data(),0x28)=1;F<int>(Quantity.data(),0x2c)=0;F<int>(Quantity.data(),0x30)=2;F<int>(Quantity.data(),0x34)=73;F<int>(Quantity.data(),0x38)=5;F<unsigned>(Quantity.data(),0x48)=10;
 Call(0x936c8d,Quantity.data());Check(Confirmed==1&&Sent==1,"unowned ordinary manual quantity confirm remains native");
 Reset();Shift=true;CheckAbi(0x878acf+At<std::int32_t>(0x878acb),Inventory.data(),true);Check(Sent==1,"use hook ABI fixture reaches native sender");
 Reset();Call(0x902367,Storage.data());Check(Opened==1&&Sent==0&&F<int>(Quantity.data(),0x2c)==2&&F<int>(Quantity.data(),0x30)==0&&F<unsigned>(Quantity.data(),0x48)==999,"warehouse plain right-click opens maximum withdrawal quantity");
 Call(0x936c8d,Quantity.data());Check(Sent==1&&Packet.size()==18&&Packet[0]==2&&Packet[1]==77&&Packet[9]==0&&Packet[10]==0xfe&&Packet[11]==0xff,"withdrawal uses real native sender in reverse direction");
 Reset();Shift=true;Call(0x902367,Storage.data());Check(Opened==0&&Sent==1,"warehouse Shift right-click withdraws whole stack");Call(0x902367,Storage.data());Check(Sent==1,"repeated withdrawal waits for completion");
 Reset();Right();RequestedCount=17;Call(0x936c8d,Quantity.data());Check(Sent==1&&Packet[5]==17&&Packet[6]==0,"deposit quantity accepts selected partial count without changing maximum");
 Reset();Right();F<unsigned>(Item.data(),0x44)^=0x1234;F<unsigned>(Item.data(),0x48)^=0x1234;F<unsigned>(Item.data(),0x54)^=0x5678;F<unsigned>(Item.data(),0x58)^=0x5678;
 Call(0x868b11,Quantity.data());Check(Closed==0&&Ticks==1,"deposit prompt survives equivalent native record re-encoding");RequestedCount=17;Call(0x936c8d,Quantity.data());Check(Sent==1&&Packet[5]==17,"deposit remains usable after native record re-encoding");
 Reset();Call(0x902367,Storage.data());F<unsigned>(Item.data(),0x44)^=0x1234;F<unsigned>(Item.data(),0x48)^=0x1234;F<unsigned>(Item.data(),0x54)^=0x5678;F<unsigned>(Item.data(),0x58)^=0x5678;
 Call(0x868b11,Quantity.data());Check(Closed==0&&Ticks==1,"withdrawal prompt survives equivalent native record re-encoding");RequestedCount=17;Call(0x936c8d,Quantity.data());Check(Sent==1&&Packet[0]==2&&Packet[5]==17,"withdrawal remains usable after native record re-encoding");
 Reset();Right();F<unsigned>(Item.data(),0x44)^=1;Call(0x936c8d,Quantity.data());Check(Confirmed==0&&Sent==0&&Closed==1,"actual decoded weight change rejects stale confirmation");
 Reset();Call(0x902367,Storage.data());F<unsigned>(Item.data(),0x54)^=1;Call(0x936c8d,Quantity.data());Check(Confirmed==0&&Sent==0&&Closed==1,"actual decoded value change rejects stale confirmation");
 Reset();Call(0x902367,Storage.data());RequestedCount=17;Call(0x936c8d,Quantity.data());Check(Sent==1&&Packet[0]==2&&Packet[5]==17&&Packet[6]==0,"withdrawal quantity accepts selected partial count");
 Reset();Call(0x902367,Storage.data());F<void*>(Manager.data(),0x23c)=Active.data();Call(0x868b11,Quantity.data());Check(Closed==1&&Sent==0,"warehouse close cancels pending withdrawal");
 Reset();Call(0x902367,Storage.data());Stored[77]=Other.data();Call(0x936c8d,Quantity.data());Check(Sent==0&&Closed==1,"withdrawal stale source confirmation rejected");
 Reset();Call(0x902367,Storage.data());reinterpret_cast<void(__thiscall*)(void*)>(0x935cb0)(Quantity.data());Check(Sent==0&&Closed==1,"withdrawal cancellation changes no items");
 Reset();Shift=true;WeightRejected=true;Call(0x902367,Storage.data());Check(Sent==0&&Used==0,"direct withdrawal retains native bag-weight rejection");
 Reset();At<unsigned>(0xd4dfc0)=124;Call(0x902367,Storage.data());Check(Sent==0&&Opened==0,"warehouse hover and other-window right-click do not transfer");
 Reset();F<int>(Storage.data(),0x48)=77;Call(0x902367,Storage.data());Check(Sent==0&&Opened==0,"scrolled slot outside unlocked capacity does not transfer");
 Reset();NativeStackable=false;F<unsigned>(Item.data(),0xb4)=1;Check(!Classifier(Quantity.data(),Item.data()),"unowned equipment quantity retains original classification");Call(0x902367,Storage.data());Check(Opened==0&&Sent==1,"warehouse single equipment withdraws directly");
 Reset();Right();CheckAbi(0x935cb0,Quantity.data(),false);Check(Closed==1,"close detour replays original six-byte prologue");
#endif
}
#ifndef CHAPTER_TWO_QUICK_STORE_BASELINE
void Installation(const std::vector<unsigned char>& original){using dfl::chapter_two::quick_store_detail::InstallImage;
 auto c=original;Writes=FailAt=0;Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(c.data(),ImageSize-1,Write)&&Writes==0,"invalid image writes nothing");
 for(unsigned va:{0x473f99u,0xa46ab0u,0xa47865u,0x902367u,0x935f42u,0x8782d0u,0x878acau,0x936c8du,0x868b11u,0x935cb0u,0x404115u,0x4716d2u,0x471800u}){c=original;c[va-Base]^=1;Writes=0;Check(!InstallImage(c.data(),ImageSize,Write)&&Writes==0,"mismatched native semantics rejected before writes");}
 for(bool after:{false,true})for(unsigned fail:{1u,2u,3u,4u,5u,6u}){c=original;Writes=0;FailAt=fail;FailAfter=after;Check(!InstallImage(c.data(),ImageSize,Write)&&c==original,"partial installation rolls back all sites");}
 c=original;Writes=FailAt=0;Check(InstallImage(c.data(),ImageSize,Write)&&Writes==6,"six narrowly scoped sites install");Writes=0;Check(InstallImage(c.data(),ImageSize,Write)&&Writes==0,"repeat installation is idempotent");
}
#endif
}
int wmain(int argc,wchar_t** argv){if(argc!=2)return 2;auto original=Load(argv[1]);if(original.empty())return 2;
#ifndef CHAPTER_TWO_QUICK_STORE_BASELINE
 Installation(original);
#endif
 auto* image=reinterpret_cast<unsigned char*>(Base);DWORD old=0;const auto start=reinterpret_cast<std::uintptr_t>(quickStoreFixtureReserve);
 if(start>Base||start+sizeof(quickStoreFixtureReserve)<Base+ImageSize||!VirtualProtect(image,ImageSize,PAGE_EXECUTE_READWRITE,&old))return 2;
 std::memcpy(image,original.data(),ImageSize);
#ifndef CHAPTER_TWO_QUICK_STORE_BASELINE
 Check(dfl::chapter_two::InstallQuickStore(image,ImageSize),"production install on mapped exact image");
#endif
 Exercise();std::printf("quick store: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
