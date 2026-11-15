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
#ifndef CHAPTER_TWO_WHEEL_BASELINE
#include "ChapterTwoMouseWheel.h"
#endif

#pragma section(".gameres", read, write)
__declspec(allocate(".gameres")) volatile unsigned char wheelFixtureReserve[0x1300000]{1};
namespace {
constexpr std::uintptr_t Base=0x400000;
constexpr std::size_t ImageSize=0xe57000;
unsigned Checks=0,Failures=0,Forwarded=0,OffsetReads=0,PeekCalls=0;
MSG LastMessage{};
template<class T> T& At(std::uintptr_t address) { return *reinterpret_cast<T*>(address); }
void Check(bool okay,const char* message) { ++Checks; if(!okay){++Failures;std::fprintf(stderr,"FAIL wheel: %s\n",message);} }
std::vector<unsigned char> ReadImage(const wchar_t* path) {
 std::ifstream stream(path,std::ios::binary);std::vector<unsigned char> file((std::istreambuf_iterator<char>(stream)),{});
 if(file.size()<0x100)return {};
 const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(file.data());
 if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||static_cast<std::size_t>(dos->e_lfanew)+sizeof(IMAGE_NT_HEADERS32)>file.size())return {};
 const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(file.data()+dos->e_lfanew);
 if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_I386||nt->OptionalHeader.ImageBase!=Base||nt->OptionalHeader.SizeOfImage!=ImageSize)return {};
 const auto* sections=IMAGE_FIRST_SECTION(nt);std::vector<unsigned char> image(ImageSize);
 if(nt->OptionalHeader.SizeOfHeaders>file.size()||nt->OptionalHeader.SizeOfHeaders>image.size())return {};
 std::memcpy(image.data(),file.data(),nt->OptionalHeader.SizeOfHeaders);
 for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];
  if(static_cast<std::size_t>(s.VirtualAddress)+s.SizeOfRawData>image.size()||static_cast<std::size_t>(s.PointerToRawData)+s.SizeOfRawData>file.size())return {};
  std::memcpy(image.data()+s.VirtualAddress,file.data()+s.PointerToRawData,s.SizeOfRawData);}
 return image;
}
struct Node { Node* next; Node* previous; void* object; };
struct Model {
 alignas(4) unsigned char bytes[0x88]{};
 Node children{&children,&children,nullptr};
 template<class T> T& Field(unsigned offset){return *reinterpret_cast<T*>(bytes+offset);}
 void Init(unsigned id,int x,int current=2) {
  std::memset(bytes,0,sizeof(bytes));Field<unsigned>(0)=0xc11df4;Field<unsigned>(0xc)=id;
  Field<unsigned char>(0x10)=1;Field<unsigned char>(0x11)=1;
  Field<int>(0x14)=x;Field<int>(0x18)=50;Field<int>(0x1c)=11;Field<int>(0x20)=100;
  using Initialize=int(__thiscall*)(void*,void*,int,int);
  Check(reinterpret_cast<Initialize>(0xa335f0)(bytes,nullptr,-1,-1)==1,"original model initializer retains its success result");
  Field<int>(0x70)=current;Field<int>(0x74)=5;
  Field<Node*>(0x60)=&children;
 }
 int Current(){return Field<int>(0x70);}
};
int __fastcall Noop(void*,void*) { return 1; }
struct Tree {
 alignas(4) unsigned char bytes[0x68]{};
 void* methods[7]{};
 Node head{&head,&head,nullptr};Node nodes[2]{};unsigned size=0;
 Tree(){methods[6]=reinterpret_cast<void*>(&Noop);*reinterpret_cast<void**>(bytes)=methods;bytes[0x10]=bytes[0x11]=1;*reinterpret_cast<Node**>(bytes+0x60)=&head;}
 void Add(void* object){auto& node=nodes[size++];node={&head,head.previous,object};head.previous->next=&node;head.previous=&node;*reinterpret_cast<unsigned*>(bytes+0x64)=size;}
};
Tree* ActiveTree=nullptr;
void Draw(){using Update=int(__thiscall*)(void*);reinterpret_cast<Update>(0xa3cbf0)(ActiveTree->bytes);}
void Poll(){MSG msg{};using Peek=BOOL(WINAPI*)(MSG*,HWND,UINT,UINT,UINT);At<Peek>(0xaea3a4)(&msg,nullptr,0,0,PM_NOREMOVE);}
BOOL WINAPI ExistingPeek(MSG*,HWND window,UINT first,UINT last,UINT remove){++PeekCalls;return window==reinterpret_cast<HWND>(0x1234)&&first==5&&last==9&&remove==3?7:FALSE;}
alignas(4) unsigned char NativeWindow[0xc0]{};
LRESULT CALLBACK WindowProcedure(HWND window,UINT message,WPARAM w,LPARAM l) {
 if(message!=WM_MOUSEWHEEL)return DefWindowProcA(window,message,w,l);
 using Proc=LRESULT(__thiscall*)(void*,HWND,UINT,WPARAM,LPARAM);
 return reinterpret_cast<Proc>(0xa3d330)(NativeWindow,window,message,w,l);
}
LRESULT WINAPI ExistingDispatch(const MSG* message) {
 ++Forwarded;if(message)LastMessage=*message;
 // The existing display/input chain remains the terminal owner. Only wheel
 // messages need the real native WndProc in this bounded fixture.
 return message&&message->message==WM_MOUSEWHEEL?DispatchMessageA(message):0x1234;
}
int __fastcall ReadOffset(void* address,void*) {
 ++OffsetReads;
 Check(address==reinterpret_cast<void*>(0xf2e824)||address==reinterpret_cast<void*>(0xf2e82c),"wheel reads the same protected coordinate objects as native mouse input");
 return address==reinterpret_cast<void*>(0xf2e824)?7:9;
}
void Jump(std::uintptr_t address,const void* target) {
 At<unsigned char>(address)=0xe9;At<std::uint32_t>(address+1)=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(target)-address-5);
}
void QueueWheel(HWND window,int delta,int x,int y) {
 POINT point{x-7,y-9};ClientToScreen(window,&point);
 MSG message{};message.hwnd=window;message.message=WM_MOUSEWHEEL;message.wParam=MAKEWPARAM(0,static_cast<short>(delta));message.lParam=MAKELPARAM(point.x,point.y);
 const auto before=Forwarded;using Dispatch=LRESULT(WINAPI*)(const MSG*);
 At<Dispatch>(0xaea3d8)(&message);
 Check(Forwarded==before+1&&LastMessage.hwnd==message.hwnd&&LastMessage.message==message.message&&LastMessage.wParam==message.wParam&&LastMessage.lParam==message.lParam,"each wheel preserves the existing dispatch chain and original screen coordinates exactly once");
}
void Wheel(HWND window,int delta,int x,int y) {QueueWheel(window,delta,x,y);Draw();Poll();}
#ifndef CHAPTER_TWO_WHEEL_BASELINE
unsigned Writes=0,FailAt=0;bool FailAfter=false;
bool Write(std::uintptr_t address,const void* value,std::size_t size){++Writes;if(Writes==FailAt&&!FailAfter)return false;std::memcpy(reinterpret_cast<void*>(address),value,size);return Writes!=FailAt;}
void CheckInstallation(const std::vector<unsigned char>& original){
 using dfl::chapter_two::mouse_wheel_detail::InstallImage;
 auto scratch=original;
 Check(!InstallImage(nullptr,ImageSize,Write)&&!InstallImage(scratch.data(),ImageSize-1,Write)&&!InstallImage(scratch.data(),ImageSize,nullptr)&&Writes==0,"invalid installer arguments write nothing");
 for(unsigned address:{0xa33690U,0xa336a7U,0xa3a720U,0xa428f4U,0xa3d4f4U,0xa3cc24U,0xa3c788U,0xc11e18U,0xc11e1cU,0xa3c6f0U,0xa3c5cfU}){
  scratch=original;
  *reinterpret_cast<decltype(&ExistingDispatch)*>(scratch.data()+0x6ea3d8)=&ExistingDispatch;
  *reinterpret_cast<decltype(&ExistingPeek)*>(scratch.data()+0x6ea3a4)=&ExistingPeek;
  scratch[address-Base]^=1;const auto before=scratch;Writes=0;
  Check(!InstallImage(scratch.data(),scratch.size(),Write)&&Writes==0&&scratch==before,"mutated original instruction or model method rejects atomically");
 }
 const std::vector<unsigned char> before(reinterpret_cast<unsigned char*>(Base),reinterpret_cast<unsigned char*>(Base+ImageSize));
 for(unsigned failure=1;failure<=4;++failure)for(bool after:{false,true}){
  Writes=0;FailAt=failure;FailAfter=after;
  Check(!InstallImage(reinterpret_cast<unsigned char*>(Base),ImageSize,Write)&&std::memcmp(reinterpret_cast<void*>(Base),before.data(),ImageSize)==0,"each failed hook write restores every destination, including post-write failure");
 }
 FailAt=Writes=0;
}
#endif
}
int wmain(int argc,wchar_t** argv) {
 if(argc!=2)return 2;const auto original=ReadImage(argv[1]);if(original.empty())return 2;
 DWORD old=0;if(!VirtualProtect(reinterpret_cast<void*>(Base),ImageSize,PAGE_EXECUTE_READWRITE,&old))return 2;
 std::memcpy(reinterpret_cast<void*>(Base),original.data(),ImageSize);
 using Dispatch=LRESULT(WINAPI*)(const MSG*);
 At<Dispatch>(0xaea3d8)=&ExistingDispatch;At<WNDPROC>(0xaea48c)=&DefWindowProcA;
 using Peek=BOOL(WINAPI*)(MSG*,HWND,UINT,UINT,UINT);At<Peek>(0xaea3a4)=&ExistingPeek;
#ifndef CHAPTER_TWO_WHEEL_BASELINE
 CheckInstallation(original);
 Check(dfl::chapter_two::InstallMouseWheel(reinterpret_cast<unsigned char*>(Base),ImageSize),"verified original image installs");
 Check(dfl::chapter_two::InstallMouseWheel(reinterpret_cast<unsigned char*>(Base),ImageSize),"same installation is idempotent");
#endif
 // Isolate only the obfuscated protection-runtime getter. Native initialization,
 // native setter, input branch, hook dispatch, and visibility logic execute.
 Jump(0xa46ab0,reinterpret_cast<void*>(&ReadOffset));
 FlushInstructionCache(GetCurrentProcess(),reinterpret_cast<void*>(Base),ImageSize);
 WNDCLASSA cls{};cls.lpfnWndProc=&WindowProcedure;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName="ChapterTwoWheelFixture";RegisterClassA(&cls);
 HWND window=CreateWindowExA(0,cls.lpszClassName,"",WS_OVERLAPPEDWINDOW,0,0,640,480,nullptr,nullptr,cls.hInstance,nullptr);if(!window)return 2;
 At<HWND>(0xf2e634)=window;
 Model primary;primary.Init(10,200);Tree root,panel;ActiveTree=&root;root.Add(panel.bytes);panel.Add(primary.bytes);
 Wheel(window,-120,205,80);Check(primary.Current()==3,"wheel down advances original C2 vertical model");
 Wheel(window,120,205,80);Check(primary.Current()==2,"wheel up reverses original C2 vertical model");
 Wheel(window,-60,160,80);Check(primary.Current()==2,"half detent waits");
 Wheel(window,-60,160,80);Check(primary.Current()==3,"second half detent scrolls list content");
 Wheel(window,-360,160,80);Check(primary.Current()==5,"multi detents clamp at native maximum");
 Model underneath;underneath.Init(11,260);root.Add(underneath.bytes);
 Wheel(window,-120,205,80);Check(primary.Current()==5&&underneath.Current()==2,"end stop owns input over an underlying movable list");
 primary.Field<unsigned char>(0x10)=0;Wheel(window,-120,160,80);Check(primary.Current()==5&&underneath.Current()==3,"hidden foreground list cannot receive wheel");
 primary.Field<unsigned char>(0x10)=1;primary.Field<int>(0x70)=1;Wheel(window,-120,160,80);Check(primary.Current()==2,"reopening a hidden list retains registration");
 panel.bytes[0x10]=0;Wheel(window,-120,205,80);Check(primary.Current()==2,"a hidden parent blocks wheel even when the model remains locally visible");panel.bytes[0x10]=1;
 panel.bytes[0x11]=0;Wheel(window,-120,205,80);Check(primary.Current()==2,"a disabled parent blocks wheel while its children are still drawn");panel.bytes[0x11]=1;
 primary.Field<unsigned char>(0x11)=0;Wheel(window,-120,205,80);Check(primary.Current()==2,"disabled list is skipped");primary.Field<unsigned char>(0x11)=1;
 primary.Field<unsigned>(0xc)=99;primary.Field<unsigned char>(4)=0;Wheel(window,-120,205,80);Check(primary.Current()==2,"uninitialized reused model is not eligible");
 primary.Init(100,200,1);Wheel(window,-120,205,80);Check(primary.Current()==2,"a reused address registers its new lifetime after native initialization");
 primary.Field<unsigned>(0)=0xc11dc4;Wheel(window,-120,205,80);Check(primary.Current()==2,"horizontal or foreign model is not interpreted as a vertical list");
 primary.Field<unsigned>(0)=0xc11df4;primary.Field<int>(0x70)=1;primary.Field<int>(0x74)=-1;Wheel(window,-120,205,80);Check(primary.Current()==1,"invalid native range is rejected");
 primary.Field<int>(0x74)=5;Wheel(window,-120,500,300);Check(primary.Current()==1&&underneath.Current()<=5,"wheel outside all list rectangles has no effect");
 primary.Field<int>(0x70)=1;QueueWheel(window,-120,205,80);Poll();Check(primary.Current()==1,"queue draining before UI traversal cannot act on stale model candidates");Draw();primary.Field<unsigned>(0xc)=101;Poll();Check(primary.Current()==1,"candidate whose creation ID changes after drawing cannot receive queued input");
 QueueWheel(window,-120,205,80);Draw();Check(primary.Current()==1,"wheel is committed only after all native roots have drawn");
 Model overlay;overlay.Init(110,200,2);Tree overlayRoot;overlayRoot.Add(overlay.bytes);ActiveTree=&overlayRoot;Draw();Poll();Check(primary.Current()==1&&overlay.Current()==3,"last drawn overlapping root wins without scrolling underlying root");ActiveTree=&root;
 QueueWheel(window,-120,205,80);Draw();panel.bytes[0x10]=0;Poll();Check(primary.Current()==1,"parent hidden after drawing invalidates the pending candidate");panel.bytes[0x10]=1;
 primary.Field<int>(0x70)=1;QueueWheel(window,-120,205,80);Draw();Tree detached;root.nodes[0].object=detached.bytes;Poll();Check(primary.Current()==1,"detaching an ancestor after drawing invalidates the pending candidate");root.nodes[0].object=panel.bytes;
 const auto peekBefore=PeekCalls;MSG peekMessage{};Check(At<Peek>(0xaea3a4)(&peekMessage,reinterpret_cast<HWND>(0x1234),5,9,3)==7&&PeekCalls==peekBefore+1,"existing PeekMessage timing/IME handler receives its arguments exactly once and retains return value");
 for(UINT kind:{WM_KEYDOWN,WM_CHAR,WM_IME_STARTCOMPOSITION,WM_IME_COMPOSITION,WM_IME_ENDCOMPOSITION}){
  MSG message{};message.hwnd=window;message.message=kind;message.wParam=VK_PROCESSKEY;message.lParam=0x12345678;
  const auto result=At<Dispatch>(0xaea3d8)(&message);Check(result==0x1234&&LastMessage.message==kind&&LastMessage.wParam==VK_PROCESSKEY&&LastMessage.lParam==0x12345678,"keyboard and IME messages retain original values and return result");
 }
 DestroyWindow(window);std::printf("mouse_wheel_tests: %u checks, %u failures, %u coordinate reads\n",Checks,Failures,OffsetReads);return Failures?1:0;
}
