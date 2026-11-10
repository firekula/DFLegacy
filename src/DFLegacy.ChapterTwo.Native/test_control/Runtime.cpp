// Explicitly linked into isolated test hosts only. Ordinary C2 delivery does not
// call or link this entry. There is no arbitrary-address write/call interface.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "Policy.h"
#include <array>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <climits>
namespace dfl::chapter_two::test_control { namespace {
using U=std::uint32_t;
template<class T>T Read(U a){return *reinterpret_cast<const T*>(a);}
struct Target {U actor=0,uid=0,hp=0;};
struct Observation {State state{};std::array<Target,256> targets{};unsigned count=0;};
struct Published {Observation observation{};std::uint64_t request=0;unsigned applied=0;char reason[64]="none";};
SRWLOCK Lock=SRWLOCK_INIT;
Published Out{};Request Inbox{};bool Pending=false;
State Identity{};std::uint64_t Consumed=0,Sequence=0,LastFrame=0;
DWORD GameThread=0;wchar_t Directory[MAX_PATH]{};
using Draw=int(__fastcall*)(void*,void*);Draw PreviousDraw=nullptr;
bool Secure(U address,U& value){
 const auto manager=Read<U>(0xf35af0),key=Read<U>(0xf35af4);
 const auto encoded=Read<U>(address),index=Read<U>(address+4);
 if(!manager||index>0x1000000||(encoded^key)-address!=index)return false;
 const auto first=Read<U>(manager+0x44),second=Read<U>(manager+0x48);
 if(!first||!second||first>UINT32_MAX-index*4||second>UINT32_MAX-index*4)return false;
 const auto a=first+index*4,b=second+index*4;
 const auto va=Read<U>(a)^a^key,vb=Read<U>(b)^b^key;
 if(va!=vb)return false;value=va;return true;
}
bool Method(U actor,U offset,U expected){const auto vt=Read<U>(actor);return vt>=0x400000&&vt<0x1257000&&Read<U>(vt+offset)==expected;}
bool Signatures(){
 constexpr unsigned char authority[]={0x8b,0x01,0xff,0x90,0x60,0x01,0,0,0x8b,0x0d,0xbc,0xa8,0xd7,0};
 constexpr unsigned char entry[]={0xe9,0x93,0xb0,0xb5,0};
 constexpr unsigned char owner[]={0x8b,0x81,0xb4,0x04,0,0,0xc3};
 constexpr unsigned char hp[]={0x81,0xc1,0x04,0x12,0,0,0xe9,0xf5,0x21,0x35,0};
 constexpr unsigned char dead[]={0x8b,0x91,0x24,0x15,0,0,0x33,0xc0,0x85,0xd2,0x0f,0x95,0xc0,0xc3};
 return !std::memcmp(reinterpret_cast<void*>(0x489bc0),authority,sizeof(authority))&&
 !std::memcmp(reinterpret_cast<void*>(0x499850),entry,sizeof(entry))&&
 !std::memcmp(reinterpret_cast<void*>(0x498ea0),owner,sizeof(owner))&&
 !std::memcmp(reinterpret_cast<void*>(0x6f48b0),hp,sizeof(hp))&&
 !std::memcmp(reinterpret_cast<void*>(0x6f4500),dead,sizeof(dead))&&
 Read<unsigned char>(0x4dafae)==0xc2&&Read<unsigned short>(0x4dafaf)==0x0c;
}
bool RegistryBinding(U registry,U uid,U user){
 const auto head=Read<U>(registry+0xc),count=Read<U>(registry+0x10);
 if(!head||!count||count>128)return false;
 auto node=Read<U>(head),previous=head;bool found=false;
 for(U i=0;i<count;++i){
  if(!node||node==head||Read<U>(node+4)!=previous)return false;
  if(Read<U>(node+8)==uid){if(found||Read<U>(node+12)!=user)return false;found=true;}
  previous=node;node=Read<U>(node);
 }return found&&node==head&&Read<U>(head+4)==previous;
}
bool Context(State& s){
 const auto game=Read<U>(0xd564ac),registry=Read<U>(0xd56cf4),ui=Read<U>(0xd56d1c);
 U scene=0,hp=0,uid=0;if(!game||!registry||!ui||!Secure(game+0x10,scene)||scene!=3)return false;
 const auto actor=Read<U>(0xd7a8bc),user=Read<U>(registry+0x2c);
 if(!actor||!user||Read<U>(user+0x168)!=actor||!Secure(actor+0xe8,uid)||!uid||uid>65535||!RegistryBinding(registry,uid,user))return false;
 U globalUid=0;if(!Secure(0xd7a8e4,globalUid)||globalUid!=uid)return false;
 const auto map=Read<U>(actor+0xfc),stage=Read<U>(ui+0x200010);
 if(!map||!stage||Read<U>(stage+0xb0)!=map||Read<U>(actor+0x1524)||!Secure(actor+0x1204,hp)||!hp||hp>INT_MAX)return false;
 if(!Method(actor,0x22c,0x6f48b0)||!Method(actor,0x3d0,0x6f4500))return false;
 s.actor=actor;s.map=map;s.stage=stage;s.uid=uid;return true;
}
bool TargetFor(U actor,const State&s,Target& t){
 if(!actor||actor==s.actor||(Read<U>(actor+0xe0)&0x211)!=0x211||Read<U>(actor+0xfc)!=s.map||Read<U>(actor+0x4b4)!=s.actor||Read<U>(actor+0x1524))return false;
 constexpr U offsets[]={0x2c,0x160,0x22c,0x354,0x3d0,0x3dc,0x3e0};
 constexpr U methods[]={0x486fb0,0x498ea0,0x6f48b0,0x6f4400,0x6f4500,0x499850,0x4daea0};
 for(unsigned i=0;i<7;++i)if(!Method(actor,offsets[i],methods[i]))return false;
 U uid=0,hp=0;if(!Secure(actor+0xe8,uid)||uid>65535||!Secure(actor+0x1204,hp)||!hp||hp>INT_MAX)return false;
 t={actor,uid,hp};return true;
}
bool Observe(Observation& o){
 o={};o.state=Identity;o.state.snapshot=++Sequence;o.state.tick=GetTickCount64();
 o.state.supported=Signatures();if(!o.state.supported||!Context(o.state))return false;
 const auto begin=Read<U>(o.state.map+0x10),end=Read<U>(o.state.map+0x14);
 if(end<begin||(end-begin)%4||end-begin>4096)return false;
 for(auto at=begin;at<end;at+=4){Target target{};if(TargetFor(Read<U>(at),o.state,target)){
  if(o.count==o.targets.size())return false;
  for(unsigned i=0;i<o.count;++i)if(o.targets[i].uid==target.uid)return false;
  o.targets[o.count++]=target;
 }}o.state.valid=true;return true;
}
bool SameContext(const State&a,const State&b){return a.actor==b.actor&&a.map==b.map&&a.stage==b.stage&&a.uid==b.uid;}
const char* Apply(const Request&r,const Observation& before,unsigned& applied){
 const auto validation=Validate(r,before.state,Consumed,GetTickCount64());if(validation)return validation;
 Consumed=r.id;State live=Identity;if(!Signatures()||!Context(live)||!SameContext(live,before.state))return "context_changed";
 const auto begin=Read<U>(live.map+0x10),end=Read<U>(live.map+0x14);
 if(end<begin||(end-begin)%4||end-begin>4096)return "map_vector";
 Target found{};
 for(auto at=begin;at<end;at+=4){Target target{};if(TargetFor(Read<U>(at),live,target)&&target.uid==r.target){if(found.actor)return "duplicate_target";found=target;}}
 if(!found.actor)return "target_not_live_local";
 using Authority=bool(__thiscall*)(void*);using SetHp=void(__thiscall*)(void*,int,bool,void*);
 auto* actor=reinterpret_cast<void*>(found.actor);
 if(!reinterpret_cast<Authority>(0x489bc0)(actor))return "not_local_authority";
 // The actual C2 virtualized setter handles its native update/death flow.
 // No persisted reward, HP field, or generated clear result is edited here.
 reinterpret_cast<SetHp>(0x499850)(actor,0,true,nullptr);applied=1;return "applied_native_setter";
}
void TickSafe(){
 Observation next{};Request request{};bool have=false;
 AcquireSRWLockExclusive(&Lock);const auto before=Out.observation;
 if(Pending){request=Inbox;Pending=false;have=true;}ReleaseSRWLockExclusive(&Lock);
 unsigned applied=0;const char* reason=nullptr;
 __try {
  if(have)reason=Apply(request,before,applied);
  Observe(next);
 }__except(EXCEPTION_EXECUTE_HANDLER){next={};next.state=Identity;next.state.tick=GetTickCount64();next.state.snapshot=++Sequence;reason="native_context_exception";}
 AcquireSRWLockExclusive(&Lock);Out.observation=next;
 if(have){Out.request=request.id;Out.applied=applied;strcpy_s(Out.reason,reason?reason:"rejected");}
 ReleaseSRWLockExclusive(&Lock);
}
int __fastcall DrawFrame(void* state,void* extra){
 const int result=PreviousDraw(state,extra);
 const auto now=GetTickCount64();if(GetCurrentThreadId()==GameThread&&now-LastFrame>=100){LastFrame=now;TickSafe();}return result;
}
void Save(const Published&p){
 wchar_t pending[MAX_PATH]{},destination[MAX_PATH]{};
 swprintf_s(pending,L"%s\\state.pending",Directory);swprintf_s(destination,L"%s\\state.json",Directory);
 FILE*f=nullptr;if(_wfopen_s(&f,pending,L"wb")||!f)return;
 const auto&s=p.observation.state;
 std::fprintf(f,"{\"testOnly\":true,\"pid\":%u,\"processStarted\":\"%llu\",\"nonce\":\"%llu\",\"snapshot\":\"%llu\",\"tick\":\"%llu\",\"enabled\":%s,\"supported\":%s,\"valid\":%s,\"actor\":%u,\"map\":%u,\"stage\":%u,\"uid\":%u,\"requestId\":\"%llu\",\"applied\":%u,\"reason\":\"%s\",\"targets\":[",s.pid,s.processStarted,s.nonce,s.snapshot,s.tick,s.enabled?"true":"false",s.supported?"true":"false",s.valid?"true":"false",s.actor,s.map,s.stage,s.uid,p.request,p.applied,p.reason);
 for(unsigned i=0;i<p.observation.count;++i){const auto&t=p.observation.targets[i];std::fprintf(f,"%s{\"uid\":%u,\"hp\":%u}",i?",":"",t.uid,t.hp);}
 std::fprintf(f,"]}\n");std::fclose(f);MoveFileExW(pending,destination,MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH);
}
DWORD WINAPI Worker(void*){
 Request previous{};std::uint64_t exported=0;
 for(;;){Published copy{};AcquireSRWLockShared(&Lock);copy=Out;ReleaseSRWLockShared(&Lock);
  if(copy.observation.state.snapshot!=exported){Save(copy);exported=copy.observation.state.snapshot;}
  wchar_t path[MAX_PATH]{};swprintf_s(path,L"%s\\request.bin",Directory);FILE*f=nullptr;
  if(!_wfopen_s(&f,path,L"rb")&&f){Request q{};const auto bytes=std::fread(&q,1,sizeof(q),f);const auto tail=std::fgetc(f);std::fclose(f);
   if(bytes==sizeof(q)&&tail==EOF&&std::memcmp(&q,&previous,sizeof(q))){AcquireSRWLockExclusive(&Lock);if(!Pending){Inbox=q;Pending=true;previous=q;}ReleaseSRWLockExclusive(&Lock);}}
  Sleep(25);
 } // Worker lifetime is the isolated host process lifetime.
}
} // namespace
void Install(unsigned char* image,std::size_t size) noexcept {
 wchar_t root[MAX_PATH]{},nonceText[32]{},module[MAX_PATH]{};
 if(!GetEnvironmentVariableW(L"DFL_C2_TEST_CONTROL_ROOT",root,MAX_PATH)||!GetEnvironmentVariableW(L"DFL_C2_TEST_CONTROL_NONCE",nonceText,32))return;
 GetModuleFileNameW(nullptr,module,MAX_PATH);const auto*name=std::wcsrchr(module,L'\\');name=name?name+1:module;
 constexpr wchar_t prefix[]=L"s1c2_chapter2_testcontrol-";
 if(std::wcsncmp(name,prefix,(sizeof(prefix)/sizeof(wchar_t))-1))return;
 wchar_t*end=nullptr;const auto nonce=std::wcstoull(nonceText,&end,16);
 if(!nonce||!end||*end||std::wcslen(root)>180||reinterpret_cast<U>(image)!=0x400000||size!=0xe57000)return;
 __try {if(!Signatures()||image[0x51e2cb]!=0xe8||image[0x51e2c7]!=0x90||image[0x51e2c8]!=0x90)return;}
 __except(EXCEPTION_EXECUTE_HANDLER){return;}
 FILETIME created{},exit{},kernel{},user{};if(!GetProcessTimes(GetCurrentProcess(),&created,&exit,&kernel,&user))return;
 Identity.enabled=true;Identity.supported=true;Identity.pid=GetCurrentProcessId();Identity.processStarted=(static_cast<std::uint64_t>(created.dwHighDateTime)<<32)|created.dwLowDateTime;Identity.nonce=nonce;
 swprintf_s(Directory,L"%s\\%u",root,Identity.pid);if(!CreateDirectoryW(Directory,nullptr)&&GetLastError()!=ERROR_ALREADY_EXISTS)return;
 GameThread=GetCurrentThreadId();const auto oldTarget=reinterpret_cast<std::uintptr_t>(image+0x51e2d0)+Read<std::int32_t>(reinterpret_cast<U>(image+0x51e2cc));
 PreviousDraw=reinterpret_cast<Draw>(oldTarget);
 const auto delta=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&DrawFrame)-reinterpret_cast<std::uintptr_t>(image+0x51e2d0));
 std::memcpy(image+0x51e2cc,&delta,4);FlushInstructionCache(GetCurrentProcess(),image+0x51e2cb,5);
 const auto thread=CreateThread(nullptr,0,&Worker,nullptr,0,nullptr);if(thread)CloseHandle(thread);
}
} // namespace
