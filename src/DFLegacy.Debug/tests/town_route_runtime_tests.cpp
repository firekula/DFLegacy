#include "../src/client_town_route_runtime.h"
#include <cstdio>
#include <cstring>
#include <unordered_map>
namespace
{
    unsigned Applies=0;
    bool Unsupported(void*) noexcept {return false;}
    bool Apply(const dfl::client_navigation::Request&) noexcept {++Applies;return true;}
}

int RunTownRouteRuntimeTests()
{
    using namespace dfl;
    int failures=0;
    auto check=[&](bool value,const char* label){if(!value){++failures;std::fprintf(stderr,"FAIL town route adapter: %s\n",label);}};
    client_state::Snapshot s{};
    s.valid=s.layoutSupported=s.haveMe=true;s.scene={true,1};s.pid=7;s.processStartedFileTime=999;
    s.generation=3;s.sequence=20;s.capturedTickMs=1000;
    s.me.uid={true,2};s.me.actorToken=0x100000;s.me.mapToken=0x200000;s.me.userToken=0x300000;
    s.town.present=true;s.town.contextToken=0x400000;s.town.mapToken=s.me.mapToken;
    s.town.townId=1;s.town.areaId=0;s.town.position={true,100,200,0};
    s.town.destinationX={true,100};s.town.destinationY={true,200};
    s.town.directionX={true,4};s.town.directionY={true,4};s.town.autoMoveActive={true,true};s.ui.selectionMode={true,0};
    client_navigation::Request r{};
    std::memcpy(r.magic,"DLRT",4);r.pid=7;r.uid=2;r.processStartedFileTime=999;r.requestId=50;
    r.issuedTickMs=1050;r.snapshotTickMs=1000;r.generation=3;r.snapshotSequence=20;
    r.actorToken=s.me.actorToken;r.mapToken=s.me.mapToken;r.userToken=s.me.userToken;r.contextToken=s.town.contextToken;
    r.townId=2;r.areaId=2;r.x=575;r.y=247;
    check(client_town_route::ValidateGoalRequest(r,s,1100,49)==nullptr,"cross-area final goal validates original binding");
    auto bad=r;std::memcpy(bad.magic,"DLNV",4);
    check(client_town_route::ValidateGoalRequest(bad,s,1100,49)!=nullptr,"legacy magic is not a route goal");
    bad=r;bad.actorToken++;
    check(client_town_route::ValidateGoalRequest(bad,s,1100,49)!=nullptr,"goal cannot inherit changed actor");
    bad=r;bad.townId=256;
    check(client_town_route::ValidateGoalRequest(bad,s,1100,49)!=nullptr,"invalid final town rejected before source substitution");
    check(client_town_route::ValidateGoalRequest(r,s,2200,49)!=nullptr,"native freshness bound unchanged");
    check(client_town_route::ValidateGoalRequest(r,s,1100,50)!=nullptr,"route request replay rejected");
    auto o=client_town_route::Observe(s);
    check(o.valid&&o.pid==7&&o.uid==2&&o.started==999&&o.map==0x200000&&o.context==0x400000,
          "observation retains exact source identity");
    check(o.position.x==100&&o.position.y==200&&o.destination.x==100&&o.autoKnown&&o.autoActive,
          "observation preserves coordinates and native auto state");
    s.town.autoMoveActive.known=false;o=client_town_route::Observe(s);
    check(!o.autoKnown,"unknown auto state stays unknown");s.town.autoMoveActive.known=true;
    s.town.mapToken++;check(!client_town_route::Observe(s).valid,"conflicting actor and town map fail closed");s.town.mapToken--;
    s.ui.selectionMode={true,2};check(client_town_route::Observe(s).busy,"known UI busy remains a cancellation observation");
    client_state::Reader reader{};reader.pid=7;reader.processStartedFileTime=999;reader.supported=&Unsupported;
    client_town_route::Initialize(nullptr,L"C:\\unused-native-route-fixture",reader,&Apply,nullptr);
    client_town_route::OnFrame(s);
    check(!client_town_route::BeginCanvasGoal(s,{2,2,575,247})&&Applies==0,"unsupported image never schedules a native goal");
    client_town_route::PumpWorker();client_town_route::Shutdown();
    std::printf("town route adapter tests: %d failure(s)\n",failures);
    return failures;
}
