// Explicitly linked into isolated test hosts only. Ordinary C2 delivery does not
// call or link this entry. There is no arbitrary-address write/call interface.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "Policy.h"
#include "CombatOverride.h"
#include <array>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <climits>
#include <cmath>
namespace dfl::chapter_two::test_control { namespace {
using U=std::uint32_t;
template<class T>T Read(U a){return *reinterpret_cast<const T*>(a);}
#include "CardControl.h"
struct Target {U actor=0,uid=0,hp=0;};
struct Coordinates {bool valid=false;float x=0,y=0,z=0;};
struct Player {U uid=0,hp=0;bool dead=false;Coordinates position{};};
struct Enemy {U uid=0,hp=0;bool dead=false,local=false;};
struct Observation {State state{};Cards cards{};std::array<Target,256> targets{};unsigned count=0;
 Coordinates position{};std::array<Player,4> players{};unsigned playerCount=0;bool invincible=false;
 std::array<Enemy,256> enemies{};unsigned enemyCount=0;};
struct Published {Observation observation{};std::uint64_t request=0;unsigned applied=0;char reason[64]="none";};
SRWLOCK Lock=SRWLOCK_INIT;
Published Out{};Request Inbox{};bool Pending=false;
State Identity{};std::uint64_t Consumed=0,Sequence=0,LastFrame=0;
DWORD GameThread=0;wchar_t Directory[MAX_PATH]{};
using Draw=int(__fastcall*)(void*,void*);Draw PreviousDraw=nullptr;
using SetHp=void(__thiscall*)(void*,int,bool,void*);
SetHp OriginalSetHp=nullptr;
CombatOverride PlayerOverride{};
U OverrideActor=0,OverrideUid=0;
bool WantInvincible=false,CombatKeyDown=false,CombatTogglePending=false;
constexpr U SetHpAddress=0x499850;
constexpr unsigned char OriginalHpEntry[]={0xe9,0x93,0xb0,0xb5,0};
std::array<unsigned char,5> InstalledHpEntry{};
bool NativeTowerMode(){
 const auto game=Read<U>(0xd564ac);
 const auto mode=game?Read<U>(game+0x20):0;
 return mode&&Read<U>(mode+0x23c)==10;
}
void __fastcall SetHpGuard(void* actor,void*,int hp,bool force,void* source){
 const auto address=reinterpret_cast<U>(actor);
 if(GetCurrentThreadId()==GameThread&&address==Read<U>(0xd7a8bc)&&PlayerOverride.BlocksHealthLoss(address,hp)&&NativeTowerMode())return;
 OriginalSetHp(actor,hp,force,source);
}
bool InstallHealthGuard(){
 if(OriginalSetHp)return std::memcmp(reinterpret_cast<void*>(SetHpAddress),InstalledHpEntry.data(),InstalledHpEntry.size())==0;
 if(std::memcmp(reinterpret_cast<void*>(SetHpAddress),OriginalHpEntry,sizeof(OriginalHpEntry)))return false;
 DWORD access=0;if(!VirtualProtect(reinterpret_cast<void*>(SetHpAddress),sizeof(OriginalHpEntry),PAGE_EXECUTE_READWRITE,&access))return false;
 OriginalSetHp=reinterpret_cast<SetHp>(SetHpAddress+5+Read<std::int32_t>(SetHpAddress+1));
 InstalledHpEntry[0]=0xe9;
 const auto relative=reinterpret_cast<U>(&SetHpGuard)-(SetHpAddress+5);
 std::memcpy(InstalledHpEntry.data()+1,&relative,4);
 std::memcpy(reinterpret_cast<void*>(SetHpAddress),InstalledHpEntry.data(),InstalledHpEntry.size());
 FlushInstructionCache(GetCurrentProcess(),reinterpret_cast<void*>(SetHpAddress),InstalledHpEntry.size());
 DWORD ignored=0;VirtualProtect(reinterpret_cast<void*>(SetHpAddress),InstalledHpEntry.size(),access,&ignored);
 return true;
}
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
 constexpr unsigned char owner[]={0x8b,0x81,0xb4,0x04,0,0,0xc3};
 constexpr unsigned char hp[]={0x81,0xc1,0x04,0x12,0,0,0xe9,0xf5,0x21,0x35,0};
 constexpr unsigned char dead[]={0x8b,0x91,0x24,0x15,0,0,0x33,0xc0,0x85,0xd2,0x0f,0x95,0xc0,0xc3};
 return !std::memcmp(reinterpret_cast<void*>(0x489bc0),authority,sizeof(authority))&&
 !std::memcmp(reinterpret_cast<void*>(SetHpAddress),OriginalSetHp?InstalledHpEntry.data():OriginalHpEntry,sizeof(OriginalHpEntry))&&
 !std::memcmp(reinterpret_cast<void*>(0x498ea0),owner,sizeof(owner))&&
 !std::memcmp(reinterpret_cast<void*>(0x6f48b0),hp,sizeof(hp))&&
 !std::memcmp(reinterpret_cast<void*>(0x6f4500),dead,sizeof(dead))&&
 Read<unsigned char>(0x4dafae)==0xc2&&Read<unsigned short>(0x4dafaf)==0x0c;
}
bool CombatRead(void*,U address,U& value) noexcept{return Secure(address,value);}
bool CombatWrite(void*,U address,U value) noexcept{
 U previous=0;if(!Secure(address,previous))return false;
 using SetSecure=void(__thiscall*)(void*,const U*);
 reinterpret_cast<SetSecure>(0x42fac0)(reinterpret_cast<void*>(address),&value);
 return true;
}
constexpr CombatAccess PlayerCombatAccess{nullptr,&CombatRead,&CombatWrite};
bool RestoreCombat(){
 if(!OverrideActor){WantInvincible=false;return true;}
 U uid=0;
 if(Read<U>(0xd7a8bc)==OverrideActor&&Secure(OverrideActor+0xe8,uid)&&uid==OverrideUid&&
  !PlayerOverride.Disable(OverrideActor,PlayerCombatAccess))return false;
 PlayerOverride=CombatOverride{};
 OverrideActor=0;OverrideUid=0;WantInvincible=false;
 return true;
}
bool SetCombat(const State&s,bool enable){
 constexpr unsigned char setter[]={0x55,0x8b,0xec,0x8b,0x15,0xf0,0x5a,0xf3,0};
 if(s.scene!=10||s.actor!=Read<U>(0xd7a8bc)||!Method(s.actor,0x230,0x6f48c0)||
  !Method(s.actor,0x33c,0x6f43e0)||!Method(s.actor,0x3f0,0x6f4910)||
  std::memcmp(reinterpret_cast<void*>(0x42fac0),setter,sizeof(setter)))return false;
 if(!enable)return RestoreCombat();
 if(OverrideActor&&(OverrideActor!=s.actor||OverrideUid!=s.uid)){PlayerOverride=CombatOverride{};OverrideActor=0;OverrideUid=0;}
 if(!InstallHealthGuard())return false;
 const auto armed=PlayerOverride.Enable(s.actor,PlayerCombatAccess);
 if(!armed){PlayerOverride.Disable(s.actor,PlayerCombatAccess);return false;}
 OverrideActor=s.actor;OverrideUid=s.uid;WantInvincible=true;return true;
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
 U scene=0,hp=0,uid=0;if(!game||!registry||!ui||!Secure(game+0x10,scene))return false;
 if(scene==1){
  const auto user=Read<U>(registry+0x2c),actor=Read<U>(0xd7a8c0);
  if(!user||!actor||Read<U>(actor)!=0xaea99c||Read<U>(user+0x164)!=actor)return false;
  uid=Read<unsigned short>(user+8);if(!uid||!RegistryBinding(registry,uid,user))return false;
  const auto map=Read<U>(actor+0xfc);if(!map)return false;const auto area=Read<U>(map+0x104);
  if(!area||!Read<U>(area+0x50))return false;
  s.actor=actor;s.map=map;s.stage=area;s.uid=uid;s.scene=1;return true;
 }
 if(scene!=3&&scene!=10)return false;
 const auto actor=Read<U>(0xd7a8bc),user=Read<U>(registry+0x2c);
 if(!actor||!user||Read<U>(user+0x168)!=actor||!Secure(actor+0xe8,uid)||!uid||uid>65535||!RegistryBinding(registry,uid,user))return false;
 U globalUid=0;if(!Secure(0xd7a8e4,globalUid)||globalUid!=uid)return false;
 const auto mode=Read<U>(game+0x20);
 const bool isTower=scene==10||(mode&&Read<U>(mode+0x23c)==10);
 const auto tower=isTower?Read<U>(game+0x40):0;
 const auto map=Read<U>(actor+0xfc),stage=isTower?(tower?Read<U>(tower+0x14):0):Read<U>(ui+0x200010);
 if(!map||!stage||Read<U>(stage+0xb0)!=map||Read<U>(actor+0x1524)||!Secure(actor+0x1204,hp)||!hp||hp>INT_MAX)return false;
 if(!Method(actor,0x22c,0x6f48b0)||!Method(actor,0x3d0,0x6f4500))return false;
 s.actor=actor;s.map=map;s.stage=stage;s.uid=uid;s.scene=isTower?10:3;return true;
}
bool IsTowerApc(U actor,const State&s){
 if(!actor||s.scene!=10||actor==s.actor||(Read<U>(actor+0xe0)&0x111)!=0x111)return false;
 const auto table=Read<U>(actor);
 if(table!=0xb791cc&&table!=0xb79984&&table!=0xb7a13c&&table!=0xb7a91c&&table!=0xb7b0d4)return false;
 U ai=0;return Method(actor,0x34,0x4aa0d0)&&Secure(actor+0x2670,ai)&&ai!=0&&Read<unsigned char>(actor+0x26c8)==1;
}
bool TargetFor(U actor,const State&s,Target& t){
 if(!actor||actor==s.actor)return false;
 const bool towerApc=IsTowerApc(actor,s);
 if(!towerApc&&(Read<U>(actor+0xe0)&0x211)!=0x211)return false;
 if(Read<U>(actor+0xfc)!=s.map||Read<U>(actor+0x4b4)!=s.actor||Read<U>(actor+0x1524))return false;
 if(towerApc){
  if(!Method(actor,0x160,0x4a8830)||!Method(actor,0x1bc,0x4344c0)||!Method(actor,0x22c,0x6f48b0)||
   !Method(actor,0x3d0,0x6f4500)||!Method(actor,0x3dc,0x499850))return false;
  using GetTeam=int(__thiscall*)(void*);
  if(reinterpret_cast<GetTeam>(0x4344c0)(reinterpret_cast<void*>(actor))!=100)return false;
 }else{
  const auto table=Read<U>(actor);
  if(s.scene==10&&(table==0xbefb2c||table==0xbeab9c)&&
   (!Method(actor,0x3fc,0x74f040)||Read<unsigned char>(actor+0x2675)!=1))return false;
  if(s.scene==10&&table==0xbe5ec4&&
   (!Method(actor,0x224,0x72de50)||Read<unsigned char>(actor+0x2630)!=1))return false;
  constexpr U offsets[]={0x2c,0x160,0x22c,0x354,0x3d0,0x3dc,0x3e0};
  constexpr U methods[]={0x486fb0,0x498ea0,0x6f48b0,0x6f4400,0x6f4500,0x499850,0x4daea0};
  for(unsigned i=0;i<7;++i)if(!Method(actor,offsets[i],methods[i]))return false;
 }
 U uid=0,hp=0;if(!Secure(actor+0xe8,uid)||uid>65535||!Secure(actor+0x1204,hp)||!hp||hp>INT_MAX)return false;
 t={actor,uid,hp};return true;
}
struct PositionBinding {U controller=0,vector=0;Coordinates position{};};
bool PositionFor(U actor,PositionBinding& binding){
 if(!actor||!Method(actor,0x84,0x487050)||!Method(actor,0x90,0x487070))return false;
 const auto controller=Read<U>(actor+0xf4);if(!controller)return false;
 if(Read<U>(controller+0x78)&&Read<unsigned char>(controller+0x7c))return false;
 const auto vector=Read<U>(controller)?Read<U>(controller):controller+8;
 const auto vt=Read<U>(vector);if(!vt)return false;
 const auto getter=Read<U>(vt),setter=Read<U>(vt+4);
 const bool plain=getter==0x489be0&&setter==0x489bf0,secure=getter==0x4f4210&&setter==0x4f4230;
 if(!plain&&!secure)return false;
 float values[3]{};
 for(U axis=0;axis<3;++axis){
  if(secure){U bits=0,mirror=0;if(!Secure(vector+0x35+8*axis,bits)||!Secure(vector+0x65+8*axis,mirror))return false;std::memcpy(values+axis,&bits,4);}
  else values[axis]=Read<float>(vector+4+4*axis);
  if(!std::isfinite(values[axis])||std::fabs(values[axis])>=1000000)return false;
 }binding={controller,vector,{true,values[0],values[1],values[2]}};return true;
}
bool PositionSignatures(){
 constexpr unsigned char setter[]={0x55,0x8b,0xec,0x8b,0x89,0xf4,0,0,0,0x5d,0xe9,0x71,0xbf,0x06,0};
 constexpr unsigned char getter[]={0x55,0x8b,0xec,0x8b,0x89,0xf4,0,0,0,0x5d,0xe9,0xf1,0xbd,0x06,0};
 constexpr unsigned char publisher[]={0x55,0x8b,0xec,0x83,0xec,0x50,0x56,0x8b,0xf1,0x8b,0x4e,0x74,0x85,0xc9,0x0f,0x84,0xa2,0x01,0,0,0xe8,0xd7,0x64,0xf9,0xff,0x84,0xc0};
 constexpr unsigned char axis[]={0x55,0x8b,0xec,0x8b,0x01,0x85,0xc0,0xc6,0x81,0x8c,0,0,0,0x01};
 return !std::memcmp(reinterpret_cast<void*>(0x487050),setter,sizeof(setter))&&
 !std::memcmp(reinterpret_cast<void*>(0x487070),getter,sizeof(getter))&&
 !std::memcmp(reinterpret_cast<void*>(0x4f36d0),publisher,sizeof(publisher))&&
 !std::memcmp(reinterpret_cast<void*>(0x4f2fd0),axis,sizeof(axis))&&
 Read<unsigned char>(0x4f388a)==0xc2&&Read<unsigned short>(0x4f388b)==8;
}
bool PublicationReady(const State&s,PositionBinding& binding){
 if(!PositionSignatures()||!PositionFor(s.actor,binding)||Read<U>(binding.controller+0x74)!=s.actor||Read<U>(s.map+0x104)!=s.stage||!Read<U>(0xd68ef8))return false;
 constexpr U offsets[]={0x3c,0x40,0x48,0x5c,0x160},methods[]={0x406aa0,0x406f00,0x406ab0,0x406ad0,0x4a8830};
 for(unsigned i=0;i<5;++i)if(!Method(s.actor,offsets[i],methods[i]))return false;
 const auto network=Read<U>(0xd56d1c),vt=Read<U>(network);if(!vt)return false;
 constexpr U required[]={8,0x20,0x24,0x2c,0x34};for(const auto offset:required)if(!Read<U>(vt+offset))return false;
 return true;
}
const char* Position(const Request&r,const State& live,unsigned& applied){
 PositionBinding before{};if(!PublicationReady(live,before))return "position_publication_unavailable";
 using Set=void(__thiscall*)(void*,int,int);using Get=int(__thiscall*)(void*,int);using Publish=void(__thiscall*)(void*,bool,int);
 using Authority=bool(__thiscall*)(void*);auto*actor=reinterpret_cast<void*>(live.actor);
 if(!reinterpret_cast<Authority>(0x489bc0)(actor))return "position_not_local_authority";
 applied=1;
 reinterpret_cast<Set>(0x487050)(actor,0,r.x);reinterpret_cast<Set>(0x487050)(actor,1,r.y);
 State after=Identity;PositionBinding binding{};
 if(!Context(after)||after.actor!=live.actor||after.map!=live.map||after.stage!=live.stage||after.uid!=live.uid||
 !PublicationReady(after,binding)||binding.controller!=before.controller||binding.vector!=before.vector||
 reinterpret_cast<Get>(0x487070)(actor,0)!=r.x||reinterpret_cast<Get>(0x487070)(actor,1)!=r.y)return "position_partial";
 // Original reposition callers use (1,1): full publication and baseline update.
 // A void native return is not proof that another client observed the position.
 reinterpret_cast<Publish>(0x4f36d0)(reinterpret_cast<void*>(binding.controller),true,1);
 return "applied_native_position";
}
bool TownPoint(U definition,int x,int y){
 for(unsigned group=0;group<2;++group){const auto offset=group?0x164U:0x174U,stride=group?24U:16U;
  const auto begin=Read<U>(definition+offset),end=Read<U>(definition+offset+4);
  if(end<begin||(end-begin)%stride||end-begin>256*stride||(!begin&&end))return false;
  for(auto at=begin;at<end;at+=stride){const auto left=Read<int>(at),top=Read<int>(at+4),width=Read<int>(at+8),height=Read<int>(at+12);
   if(width<=0||height<=0||width>1000000||height>1000000)return false;
   if(x>=left&&y>=top&&static_cast<std::int64_t>(x)<static_cast<std::int64_t>(left)+width&&static_cast<std::int64_t>(y)<static_cast<std::int64_t>(top)+height)return true;
  }
 }return false;
}
const char* TownWalk(const Request&r,const State&s,unsigned&applied){
 constexpr unsigned char validate[]={0x55,0x8b,0xec,0x8b,0x45,0x10,0x85,0xc0,0x74,0x06,0xc7,0,0,0,0,0};
 constexpr unsigned char destination[]={0x55,0x8b,0xec,0x8b,0x81,0xf4,0x04,0,0,0x85,0xc0,0x74,0x0a,0x8a,0x90,0xd8};
 if(!Method(s.actor,0xac,0x492290)||!Method(s.actor,0x1a8,0x490e60)||
 std::memcmp(reinterpret_cast<void*>(0x492290),validate,sizeof(validate))||std::memcmp(reinterpret_cast<void*>(0x490e60),destination,sizeof(destination)))return "town_signatures";
 const auto transport=Read<U>(s.actor+0x4f4);
 if((transport&&Read<unsigned char>(transport+0x1d8))||Read<unsigned char>(0xd586b0)>1||!TownPoint(Read<U>(s.stage+0x50),r.x,r.y))return "town_destination";
 using ValidatePoint=bool(__thiscall*)(void*,int,int,int*);using Set=void(__thiscall*)(void*,int,int);
 auto*actor=reinterpret_cast<void*>(s.actor);if(!reinterpret_cast<ValidatePoint>(0x492290)(actor,r.x,r.y,nullptr))return "town_native_validation";
 applied=1;reinterpret_cast<Set>(0x490e60)(actor,r.x,r.y);
 State after=Identity;if(!Context(after)||after.scene!=1||after.actor!=s.actor||after.map!=s.map||Read<int>(s.actor+0x278)!=r.x||Read<int>(s.actor+0x27c)!=r.y)return "town_navigation_partial";
 // Same activation used by the existing C2 town route controller. The original
 // native town loop performs travel, publication and any portal transition.
 *reinterpret_cast<volatile unsigned char*>(0xd586b0)=1;return "applied_native_town_walk";
}
bool Observe(Observation& o){
 o={};o.state=Identity;o.state.snapshot=++Sequence;o.state.tick=GetTickCount64();
 o.state.supported=Signatures();if(!o.state.supported||!Context(o.state))return false;
 if(o.state.scene==1){PositionBinding own{};if(PositionFor(o.state.actor,own))o.position=own.position;o.players[0]={o.state.uid,0,false,o.position};o.playerCount=1;o.state.valid=true;return true;}
 const auto begin=Read<U>(o.state.map+0x10),end=Read<U>(o.state.map+0x14);
 if(end<begin||(end-begin)%4||end-begin>4096)return false;
 PositionBinding own{};if(PositionFor(o.state.actor,own))o.position=own.position;
 for(auto at=begin;at<end;at+=4){const auto actor=Read<U>(at);if(!actor)continue;
  const auto kind=Read<U>(actor+0xe0);U uid=0,hp=0;
  const bool towerApc=IsTowerApc(actor,o.state);
  if(kind==0x111&&!towerApc&&Read<U>(actor+0xfc)==o.state.map&&Secure(actor+0xe8,uid)&&uid&&uid<=65535&&Secure(actor+0x1204,hp)&&hp<=INT_MAX){
   if(o.playerCount==o.players.size())return false;
   for(unsigned i=0;i<o.playerCount;++i)if(o.players[i].uid==uid)return false;
   PositionBinding p{};PositionFor(actor,p);o.players[o.playerCount++]={uid,hp,Read<U>(actor+0x1524)!=0,p.position};
  }
  if(((kind&0x211)==0x211||towerApc)&&Read<U>(actor+0xfc)==o.state.map&&Method(actor,0x22c,0x6f48b0)&&Method(actor,0x3d0,0x6f4500)&&Secure(actor+0xe8,uid)&&uid<=65535&&Secure(actor+0x1204,hp)&&hp<=INT_MAX){
   if(o.enemyCount==o.enemies.size())return false;
   for(unsigned i=0;i<o.enemyCount;++i)if(o.enemies[i].uid==uid)return false;
   o.enemies[o.enemyCount++]={uid,hp,Read<U>(actor+0x1524)!=0,Read<U>(actor+0x4b4)==o.state.actor};
  }
  Target target{};if(TargetFor(actor,o.state,target)){
  if(o.count==o.targets.size())return false;
  for(unsigned i=0;i<o.count;++i)if(o.targets[i].uid==target.uid)return false;
  o.targets[o.count++]=target;
 }}o.cards=ReadCards(o.state);o.invincible=PlayerOverride.ActiveFor(o.state.actor);o.state.valid=true;return true;
}
bool SameContext(const State&a,const State&b){return a.actor==b.actor&&a.map==b.map&&a.stage==b.stage&&a.uid==b.uid&&a.scene==b.scene;}
const char* Apply(const Request&r,const Observation& before,unsigned& applied){
 const auto validation=Validate(r,before.state,Consumed,GetTickCount64());if(validation)return validation;
 Consumed=r.id;State live=Identity;if(!Signatures()||!Context(live)||!SameContext(live,before.state))return "context_changed";
 if(r.operation==3)return TownWalk(r,live,applied);
 if(r.operation==2)return Position(r,live,applied);
 if(r.operation==4||r.operation==5)return SelectCard(r,live,before.cards,applied);
 if(r.operation==6||r.operation==7){
  if(!SetCombat(live,r.operation==6))return "combat_override_failed";
  applied=1;return r.operation==6?"invincible_enabled":"invincible_disabled";
 }
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
  State local=Identity;
  if(Context(local)&&local.scene==10){
   if(CombatTogglePending){
    const auto enable=!WantInvincible;
    if(SetCombat(local,enable))OutputDebugStringA(enable?"C2 private invincibility enabled.\n":"C2 private invincibility disabled.\n");
   }else if(WantInvincible)SetCombat(local,true);
  }else{
   if(WantInvincible&&!NativeTowerMode())RestoreCombat();
  }
  CombatTogglePending=false;
  Observe(next);
 }__except(EXCEPTION_EXECUTE_HANDLER){next={};next.state=Identity;next.state.tick=GetTickCount64();next.state.snapshot=++Sequence;reason="native_context_exception";}
 AcquireSRWLockExclusive(&Lock);Out.observation=next;
 if(have){Out.request=request.id;Out.applied=applied;strcpy_s(Out.reason,reason?reason:"rejected");}
 ReleaseSRWLockExclusive(&Lock);
}
int __fastcall DrawFrame(void* state,void* extra){
 const int result=PreviousDraw(state,extra);
 if(GetCurrentThreadId()==GameThread){
  const bool pressed=(GetAsyncKeyState(VK_CONTROL)&0x8000)!=0&&(GetAsyncKeyState(VK_F10)&0x8000)!=0;
  if(pressed&&!CombatKeyDown){
   DWORD foreground=0;GetWindowThreadProcessId(GetForegroundWindow(),&foreground);
   CombatTogglePending=foreground==GetCurrentProcessId();
  }
  CombatKeyDown=pressed;
  const auto now=GetTickCount64();
  if(CombatTogglePending||now-LastFrame>=100){LastFrame=now;TickSafe();}
 }
 return result;
}
void Save(const Published&p){
 wchar_t pending[MAX_PATH]{},destination[MAX_PATH]{};
 swprintf_s(pending,L"%s\\state.pending",Directory);swprintf_s(destination,L"%s\\state.json",Directory);
 FILE*f=nullptr;if(_wfopen_s(&f,pending,L"wb")||!f)return;
 const auto&s=p.observation.state;
 std::fprintf(f,"{\"testOnly\":true,\"controlVersion\":2,\"pid\":%u,\"processStarted\":\"%llu\",\"nonce\":\"%llu\",\"snapshot\":\"%llu\",\"tick\":\"%llu\",\"enabled\":%s,\"supported\":%s,\"valid\":%s,\"actor\":%u,\"map\":%u,\"stage\":%u,\"uid\":%u,\"requestId\":\"%llu\",\"applied\":%u,\"reason\":\"%s\",\"targets\":[",s.pid,s.processStarted,s.nonce,s.snapshot,s.tick,s.enabled?"true":"false",s.supported?"true":"false",s.valid?"true":"false",s.actor,s.map,s.stage,s.uid,p.request,p.applied,p.reason);
 for(unsigned i=0;i<p.observation.count;++i){const auto&t=p.observation.targets[i];std::fprintf(f,"%s{\"uid\":%u,\"hp\":%u}",i?",":"",t.uid,t.hp);}
 const auto position=[&](const Coordinates&c){if(c.valid)std::fprintf(f,"{\"x\":%.6f,\"y\":%.6f,\"z\":%.6f}",c.x,c.y,c.z);else std::fprintf(f,"null");};
 std::fprintf(f,"],\"scene\":%u,\"invincible\":%s,\"position\":",s.scene,p.observation.invincible?"true":"false");position(p.observation.position);std::fprintf(f,",\"players\":[");
 for(unsigned i=0;i<p.observation.playerCount;++i){const auto&row=p.observation.players[i];std::fprintf(f,"%s{\"uid\":%u,\"hp\":",i?",":"",row.uid);if(s.scene==1)std::fprintf(f,"null,\"dead\":null");else std::fprintf(f,"%u,\"dead\":%s",row.hp,row.dead?"true":"false");std::fprintf(f,",\"position\":");position(row.position);std::fprintf(f,"}");}
 std::fprintf(f,"],\"enemies\":[");for(unsigned i=0;i<p.observation.enemyCount;++i){const auto&e=p.observation.enemies[i];std::fprintf(f,"%s{\"uid\":%u,\"hp\":%u,\"dead\":%s,\"local\":%s}",i?",":"",e.uid,e.hp,e.dead?"true":"false",e.local?"true":"false");}
 const auto&c=p.observation.cards;
 std::fprintf(f,"],\"cards\":{\"available\":%s,\"phase\":%u,\"count\":%u,\"slot\":%u,\"freeSelected\":%u,\"paidSelected\":%u,\"freePending\":%u,\"paidPending\":%u,\"columns\":[",c.screen?"true":"false",c.phase,c.count,c.slot,c.freeSelected,c.paidSelected,c.freePending,c.paidPending);
 for(unsigned i=0;i<4;++i)std::fprintf(f,"%s{\"freeOwner\":%u,\"paidOwner\":%u,\"freeVisible\":%s,\"paidVisible\":%s}",i?",":"",c.free[i],c.paid[i],c.freeVisible[i]?"true":"false",c.paidVisible[i]?"true":"false");
 std::fprintf(f,"]}}\n");std::fclose(f);MoveFileExW(pending,destination,MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH);
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
