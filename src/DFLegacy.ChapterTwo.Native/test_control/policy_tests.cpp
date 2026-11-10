#include "Policy.h"
#include <cstdio>
#include <cstring>
#include <initializer_list>
using namespace dfl::chapter_two::test_control;
namespace {unsigned Checks=0,Failures=0;
void Check(bool ok,const char* why){++Checks;if(!ok){++Failures;std::printf("FAIL %s\n",why);}}
State Good(){State s{};s.enabled=s.supported=s.valid=true;s.scene=3;s.pid=5;s.actor=0x10000;s.map=0x20000;s.stage=0x30000;s.uid=7;s.processStarted=123;s.nonce=456;s.snapshot=12;s.tick=10000;return s;}
Request Command(const State&s){Request r{};r.pid=s.pid;r.actor=s.actor;r.map=s.map;r.stage=s.stage;r.uid=s.uid;r.processStarted=s.processStarted;r.nonce=s.nonce;r.snapshot=s.snapshot;r.id=19;r.issued=s.tick;r.target=9;return r;}
}
int main(){
 const auto s=Good();const auto r=Command(s);Check(Validate(r,s,18,10001)==nullptr,"fresh bound request accepted");
 auto zero=r;zero.target=0;Check(Validate(zero,s,18,10001)==nullptr,"native monster UID zero is a valid explicit target");
 const char* errors[]={"disabled","layout","context"};
 for(unsigned i=0;i<3;++i){auto bad=s;if(i==0)bad.enabled=false;else if(i==1)bad.supported=false;else bad.valid=false;Check(Validate(r,bad,18,10001)&&std::strcmp(Validate(r,bad,18,10001),errors[i])==0,"invalid enable/layout/scene refused");}
 for(unsigned i=0;i<15;++i){auto bad=r;switch(i){
 case 0:bad.magic=0;break;case 1:bad.version=1;break;case 2:bad.pid++;break;case 3:bad.processStarted++;break;case 4:bad.nonce++;break;
 case 5:bad.actor++;break;case 6:bad.map++;break;case 7:bad.stage++;break;case 8:bad.uid++;break;case 9:bad.snapshot++;break;
 case 10:bad.id=18;break;case 11:bad.operation=3;break;case 12:bad.target=UINT32_MAX;break;case 13:bad.target=65536;break;case 14:bad.reserved=1;break;}
 Check(Validate(bad,s,18,10001)!=nullptr,"mismatched or unsupported request refused");}
 Check(Validate(r,s,18,10751)!=nullptr,"expired snapshot refused");Check(Validate(r,s,18,9999)!=nullptr,"future snapshot refused");
 auto stale=r;stale.issued=9999;Check(Validate(stale,s,18,10001)!=nullptr,"request before snapshot refused");stale.issued=10002;Check(Validate(stale,s,18,10001)!=nullptr,"future request refused");
 auto invalid=s;invalid.actor=0;auto q=Command(invalid);Check(Validate(q,invalid,18,10001)!=nullptr,"zero actor refused");
 invalid=s;invalid.uid=0;q=Command(invalid);Check(Validate(q,invalid,18,10001)!=nullptr,"zero uid refused");
 invalid=s;invalid.nonce=0;q=Command(invalid);Check(Validate(q,invalid,18,10001)!=nullptr,"zero run identity refused");
 auto position=r;position.operation=2;position.target=s.uid;position.x=700;position.y=240;
 Check(Validate(position,s,18,10001)==nullptr,"fresh self-position accepted");
 for(int axis=0;axis<2;++axis)for(const auto value:{-32768,32767}){auto edge=position;if(axis==0)edge.x=value;else edge.y=value;Check(!Validate(edge,s,18,10001),"signed16 coordinate edge accepted");}
 for(int axis=0;axis<2;++axis)for(const auto value:{-32769,32768}){auto bad=position;if(axis==0)bad.x=value;else bad.y=value;Check(Validate(bad,s,18,10001)!=nullptr,"coordinate overflow refused");}
 position.target=9;Check(Validate(position,s,18,10001)!=nullptr,"position cannot target peer actor");
 position.target=0;Check(Validate(position,s,18,10001)!=nullptr,"zero player target refused");
 auto kill=r;kill.x=700;Check(Validate(kill,s,18,10001)!=nullptr,"kill cannot carry unrelated coordinates");
 auto town=s;town.scene=1;auto walk=Command(town);walk.operation=3;walk.target=town.uid;walk.x=474;walk.y=340;
 Check(!Validate(walk,town,18,10001),"bound native town navigation accepted");
 Check(Validate(walk,s,18,10001)!=nullptr,"town navigation cannot run in dungeon");
 walk.operation=2;Check(Validate(walk,town,18,10001)!=nullptr,"dungeon teleport cannot run in town");
 walk.operation=1;walk.x=walk.y=0;Check(Validate(walk,town,18,10001)!=nullptr,"monster action cannot run in town");
 for(unsigned operation:{4u,5u}){
  auto card=r;card.operation=operation;card.target=3;
  Check(!Validate(card,s,18,10001),"explicit native card column accepted in dungeon");
  card.target=4;Check(Validate(card,s,18,10001)!=nullptr,"fifth card column refused");
  card.target=0;card.x=1;Check(Validate(card,s,18,10001)!=nullptr,"card cannot carry position");
  card.x=0;Check(Validate(card,town,18,10001)!=nullptr,"card cannot run in town");
 }
 std::printf("C2 isolated test-control policy: %u checks, %u failures\n",Checks,Failures);return Failures?1:0;
}
