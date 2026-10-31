#include "../src/town_route_controller.h"
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <limits>
#include <utility>

namespace
{
    using namespace dfl::town_route;
    int Failures = 0;
    void Check(bool okay, const char* label)
    { if (!okay) { ++Failures; std::fprintf(stderr,"FAIL route controller: %s\n",label); } }
    Area Room(Key key, std::vector<Rect> floor = {{0,0,400,140}})
    { Area a; a.key=key; a.kind="normal"; a.mapPath="fixture.twn"; a.walkable=std::move(floor); return a; }
    Catalog Rooms(bool reverse = false)
    {
        Catalog c; c.pvfSha256="synthetic-controller";
        auto first=Room({1,0}), second=Room({1,1});
        first.portals.push_back({{1,0},{1,1},{300,20,30,80},{315,60}});
        if (reverse) second.portals.push_back({{1,1},{1,0},{0,20,20,80},{10,60}});
        c.areas={first,second,Room({1,8}),Room({1,9})}; return c;
    }
    Observation Sample()
    {
        Observation o; o.valid=true;o.scene=1;o.pid=77;o.started=999;o.uid=2;
        o.user=0x100000;o.actor=0x200000;o.context=0x300000;o.map=0x400000;
        o.generation=1;o.sequence=1;o.capturedTick=1000;o.area={1,0};
        o.position={60,60};o.destination={60,60};o.autoKnown=true;return o;
    }
    Observation At(Observation o, Waypoint p, std::uint64_t tick)
    { ++o.sequence;o.capturedTick=tick;o.position={static_cast<double>(p.x),static_cast<double>(p.y)};o.destination=p;o.autoActive=true;o.directionX=o.directionY=4;return o; }
    bool Begin(Controller& c,const Catalog& cat,const Observation& o,Goal goal)
    { std::string error;const auto okay=c.Start(cat,o,goal,o.capturedTick,error);Check(okay,error.empty()?"Start succeeds":error.c_str());return okay; }
    void SameAreaAndHandshake()
    {
        auto cat=Rooms();auto o=Sample();Controller c;
        if (!Begin(c,cat,o,{{1,0},{180,70}})) return;
        Check(c.Steps()==0 && c.Status()=="queued","Start queues without native movement");
        auto action=c.Tick(cat,o,1000);Check(action.kind==Action::Kind::SetDestination && action.point==Waypoint{180,70},"Tick emits final unchanged goal");
        Check(c.Tick(cat,o,1000).kind==Action::Kind::None && c.Steps()==1,"pending action never repeats before confirmation");
        c.ConfirmDestination(o,action.point,1000,true);
        auto old=o;old.position={180,70};old.destination=action.point;old.autoActive=true;
        Check(c.Tick(cat,old,1000).kind==Action::Kind::None && c.Active(),"pre-ack same sample cannot arrive");
        auto equal=At(o,action.point,1000);
        Check(c.Tick(cat,equal,1000).kind==Action::Kind::None,"equal-tick capture cannot complete acceptance");
        auto post=At(equal,action.point,1100);
        Check(c.Tick(cat,post,1100).kind==Action::Kind::Arrived && !c.Active() && c.Status()=="arrived","fresh stationary physical endpoint completes route with auto still true");
        Check(c.Tick(cat,post,1100).kind==Action::Kind::None,"arrived action is emitted once");
    }
    void ExpectedTransitionAndBirth()
    {
        auto cat=Rooms(true);auto o=Sample();Controller c;
        if (!Begin(c,cat,o,{{1,1},{180,60}})) return;
        auto action=c.Tick(cat,o,1000);Check(action.kind==Action::Kind::SetDestination && action.point==Waypoint{315,60},"selected portal approach");
        c.ConfirmDestination(o,action.point,1000,true);
        o=At(o,{10,60},1100);o.area={1,1};o.map=0x500000;++o.generation;o.autoActive=false;
        Check(c.Tick(cat,o,1100).kind==Action::Kind::None,"expected new area/map queues its local birth baseline");
        ++o.sequence;o.capturedTick=1110;
        action=c.Tick(cat,o,1110);Check(action.kind==Action::Kind::SetDestination && action.point==Waypoint{25,60},"expected new area/map re-arms reverse birth portal before continuing");
        if (action.kind!=Action::Kind::SetDestination) return;
        c.ConfirmDestination(o,action.point,1110,true);o=At(o,action.point,1200);
        action=c.Tick(cat,o,1200);Check(action.kind==Action::Kind::SetDestination && action.point==Waypoint{180,60},"after safe birth exit continue to final coordinate");
        if (action.kind!=Action::Kind::SetDestination) return;
        c.ConfirmDestination(o,action.point,1200,true);o=At(o,action.point,1300);
        Check(c.Tick(cat,o,1300).kind==Action::Kind::Arrived,"cross-area goal reaches final physical point");
    }
    void BindingAndContextGuards()
    {
        for (unsigned field=0;field<10;++field)
        {
            auto cat=Rooms();auto o=Sample();Controller c;if (!Begin(c,cat,o,{{1,1},{180,60}})) continue;
            auto a=c.Tick(cat,o,1000);c.ConfirmDestination(o,a.point,1000,true);
            auto changed=At(o,{100,60},1100);changed.destination=a.point;
            switch(field)
            {
                case 0:++changed.pid;break;case 1:++changed.started;break;case 2:++changed.uid;break;
                case 3:++changed.user;break;case 4:++changed.actor;break;case 5:++changed.context;break;
                case 6:changed.map++;break;case 7:changed.generation++;break;
                case 8:changed.area={1,8};changed.map++;changed.generation++;break;
                default:changed.scene=3;changed.valid=false;break;
            }
            Check(c.Tick(cat,changed,1100).kind==Action::Kind::Interrupted && !c.Active(),"identity/scene/unexpected map change interrupts permanently");
            Check(c.Tick(cat,o,1200).kind==Action::Kind::None,"old context never resurrects stopped route");
        }
        for (unsigned defect=0;defect<2;++defect)
        {
            auto cat=Rooms();auto o=Sample();Controller c;if (!Begin(c,cat,o,{{1,1},{180,60}})) continue;
            auto a=c.Tick(cat,o,1000);c.ConfirmDestination(o,a.point,1000,true);
            auto changed=At(o,{60,60},1100);changed.area={1,1};changed.autoActive=false;
            if (defect==0) changed.map++;
            else changed.generation++;
            Check(c.Tick(cat,changed,1100).kind==Action::Kind::Interrupted,"expected area still needs both increasing generation and a new map");
        }
        auto cat=Rooms();auto o=Sample();Controller c;if (!Begin(c,cat,o,{{1,1},{180,60}})) return;
        (void)c.Tick(cat,o,1000);
        auto changed=At(o,{60,60},1100);changed.area={1,1};changed.map++;changed.generation++;
        Check(c.Tick(cat,changed,1100).kind==Action::Kind::Interrupted,"transition without native acceptance cannot continue");
    }
    void StrongCancellation()
    {
        for (unsigned defect=0;defect<3;++defect)
        {
            auto cat=Rooms();auto o=Sample();Controller c;if (!Begin(c,cat,o,{{1,0},{180,60}})) continue;
            auto a=c.Tick(cat,o,1000);c.ConfirmDestination(o,a.point,1000,true);
            auto post=At(o,a.point,1100);
            if (defect==0) post.autoActive=false;
            if (defect==1) post.destination={179,60};
            if (defect==2) post.busy=true;
            Check(c.Tick(cat,post,1400).kind==Action::Kind::Interrupted,"valid age300 sample cancels even at endpoint before stricter send gate");
            Check(!c.Active() && c.Steps()==1,"manual cancellation does not emit another native leg");
        }
        auto cat=Rooms();auto o=Sample();Controller c;if (!Begin(c,cat,o,{{1,0},{180,60}})) return;
        const auto action=c.Tick(cat,o,1000);c.Cancel("manual_cancel");c.ConfirmDestination(o,action.point,1001,true);
        Check(!c.Active() && c.Tick(cat,At(o,action.point,1100),1100).kind==Action::Kind::None && c.Reason()=="manual_cancel","late confirmation cannot revive cancelled goal");
    }
    void LoadingAndBudgets()
    {
        auto cat=Rooms();auto o=Sample();Controller c;if (!Begin(c,cat,o,{{1,0},{180,60}})) return;
        auto missing=o;missing.valid=false;missing.scene=-1;
        Check(c.Tick(cat,missing,1000).kind==Action::Kind::None && c.Steps()==0,"invalid loading waits without native action");
        Check(c.Tick(cat,missing,5999).kind==Action::Kind::None,"missing-data budget is bounded at five seconds");
        Check(c.Tick(cat,missing,6000).kind==Action::Kind::Interrupted,"missing data expires at five seconds");
        c=Controller();o.capturedTick=750;Begin(c,cat,o,{{1,0},{180,60}});
        auto old=o;
        Check(c.Tick(cat,old,1000).kind==Action::Kind::None && c.Steps()==0,"age250 observation cannot authorize a native send");
        o=At(o,{60,60},1100);o.autoActive=false;
        Check(c.Tick(cat,o,1100).kind==Action::Kind::SetDestination,"new fresh initial observation can send");
        c=Controller();o=Sample();Begin(c,cat,o,{{1,0},{180,60}});(void)c.Tick(cat,o,1000);
        o=At(o,{60,60},121000);
        Check(c.Tick(cat,o,121000).kind==Action::Kind::Interrupted && c.Reason()=="route_timeout","global route limit is120seconds");
        c=Controller();o=Sample();Begin(c,cat,o,{{1,0},{180,60}});auto a=c.Tick(cat,o,1000);c.ConfirmDestination(o,a.point,1000,true);
        o=At(o,{60,60},6100);o.destination=a.point;o.directionX=1;
        Check(c.Tick(cat,o,6100).kind==Action::Kind::Interrupted && c.Reason()=="native_no_progress","fresh but motionless native leg cannot wait indefinitely");
        c=Controller();o=Sample();Begin(c,cat,o,{{1,1},{180,60}});a=c.Tick(cat,o,1000);c.ConfirmDestination(o,a.point,1000,true);
        o=At(o,a.point,1100);Check(c.Tick(cat,o,1100).kind==Action::Kind::None,"portal endpoint waits for physical area transition");
        o=At(o,a.point,6100);
        Check(c.Tick(cat,o,6100).kind==Action::Kind::Interrupted && c.Reason()=="portal_not_crossed","reached portal that does not cross fails after five seconds");
    }
    void ReplanAndForeignDoors()
    {
        Catalog cat;cat.pvfSha256="corridor";cat.areas={Room({1,0},{{0,0,100,100},{90,20,110,20},{180,20,220,80}})};
        auto o=Sample();o.position={190,60};o.destination={60,60};o.autoActive=true;o.directionX=0;Controller c;
        if (!Begin(c,cat,o,{{1,0},{310,60}})) return;
        ++o.sequence;o.capturedTick=1100;o.position={99,60};
        auto action=c.Tick(cat,o,1100);Check(action.kind==Action::Kind::SetDestination && action.point==Waypoint{95,30},"replan whole native path from fresh actual position, not Start position");
        c.ConfirmDestination(o,action.point,1100,true);
        o=At(o,action.point,1200);
        Check(c.Tick(cat,o,1450).kind==Action::Kind::None,"completion sample above send budget waits before next internal leg");
        o=At(o,action.point,1300);o.autoActive=false;
        Check(c.Tick(cat,o,1600).kind==Action::Kind::Interrupted && c.Reason()=="native_auto_move_stopped","manual stop between internal legs cannot be hidden by send freshness gate");
        auto fork=Rooms();fork.areas[0].portals.push_back({{1,0},{1,8},{140,40,40,40},{160,60}});
        o=Sample();c=Controller();if (!Begin(c,fork,o,{{1,0},{250,60}})) return;
        unsigned legs=0;
        for (std::uint64_t now=1000;now<8000 && c.Active();now+=100)
        {
            const auto next=c.Tick(fork,o,now);
            if (next.kind!=Action::Kind::SetDestination) continue;
            const double left=std::fmin(o.position.x,next.point.x),right=std::fmax(o.position.x,next.point.x);
            const double top=std::fmin(o.position.y,next.point.y),bottom=std::fmax(o.position.y,next.point.y);
            Check(!(left<184 && right>=136 && top<84 && bottom>=36),"every replanned native AABB avoids expanded unselected portal");
            ++legs;c.ConfirmDestination(o,next.point,now,true);o=At(o,next.point,now+100);
        }
        Check(c.Status()=="arrived" && legs>1,"foreign portal is routed around through multiple actual legs");
    }
    void BirthSafetyAndTakeover()
    {
        for (unsigned side=0;side<4;++side)
        {
            Catalog cat;cat.pvfSha256="birth";Rect floors[]={{0,40,60,20},{40,40,60,20},{40,0,20,60},{40,40,20,60}};
            Waypoint goals[]={{10,50},{90,50},{50,10},{50,90}},exits[]={{25,50},{75,50},{50,25},{50,75}};
            auto area=Room({1,0},{floors[side]});area.portals={{{1,0},{1,8},{30,30,40,40},{50,50}}};cat.areas={area,Room({1,8})};
            auto o=Sample();o.position={50,50};o.destination={50,50};Controller c;
            if (!Begin(c,cat,o,{{1,0},goals[side]})) continue;
            const auto a=c.Tick(cat,o,1000);Check(a.kind==Action::Kind::SetDestination && a.point==exits[side],"birth exit uses native single leg and gap5 on every side");
        }
        auto o=Sample();o.position={29.6,50};o.destination={30,50};
        Catalog narrow;narrow.pvfSha256="raw-round";auto room=Room({1,0},{{29,40,70,20}});room.portals={{{1,0},{1,8},{30,30,40,40},{50,50}}};narrow.areas={room,Room({1,8})};
        Controller c;std::string error;
        Check(!c.Start(narrow,o,{{1,0},{90,50}},1000,error) && error=="portal_rearm_unavailable","raw outside birth portal cannot cross back even when native rounding enters it");
        Catalog winding;winding.pvfSha256="turning-birth";room=Room({1,0},{{46,46,29,8},{67,46,8,28},{46,66,29,8},{0,62,54,8}});room.portals={{{1,0},{1,8},{30,30,40,40},{50,50}}};winding.areas={room,Room({1,8})};
        o=Sample();o.position={50,50};c=Controller();
        Check(!c.Start(winding,o,{{1,0},{4,66}},1000,error) && error=="portal_rearm_unavailable","multiple turns while birth portal exempt are never used as an escape");
        auto cat=Rooms();o=Sample();c=Controller();if (!Begin(c,cat,o,{{1,0},{180,60}})) return;
        Check(!c.Start(cat,o,{{1,0},{315,60}},1000,error) && c.Active() && c.Target().point==Waypoint{180,60},"invalid goal near portal cannot take over valid route");
        Check(c.Start(cat,o,{{1,0},{210,60}},1000,error) && c.Target().point==Waypoint{210,60} && c.Steps()==0,"new valid goal explicitly supersedes old route");
        auto action=c.Tick(cat,o,1000);c.ConfirmDestination(o,action.point,1000,false);
        Check(!c.Active() && c.Status()=="interrupted" && c.Steps()==1,"native rejection cannot be converted to arrival");
    }
    void AdditionalLifecycleBoundaries()
    {
        auto cat=Rooms();auto o=Sample();Controller c;
        for (unsigned bad=0;bad<4;++bad)
        {
            c=Controller();o=Sample();Begin(c,cat,o,{{1,0},{180,60}});
            auto next=At(o,{60,60},1100);
            if (bad==0) next.sequence=0;
            if (bad==1) {next.sequence=o.sequence;next.capturedTick=1100;}
            if (bad==2) next.capturedTick=1200;
            if (bad==3) {next.valid=false;++next.actor;}
            Check(c.Tick(cat,next,1100).kind==Action::Kind::Interrupted && c.Steps()==0,"header regression/future/known replaced actor cannot send");
        }
        c=Controller();o=Sample();Begin(c,cat,o,{{1,0},{180,60}});
        auto action=c.Tick(cat,o,1000);o=At(o,{60,60},6000);
        Check(c.Tick(cat,o,6000).kind==Action::Kind::Interrupted && c.Reason()=="native_confirmation_timeout","unconfirmed leaf has a five-second bound without repeats");
        c=Controller();o=Sample();Begin(c,cat,o,{{1,0},{180,60}});auto changed=cat;changed.pvfSha256="different";
        Check(c.Tick(changed,o,1000).kind==Action::Kind::Interrupted && c.Steps()==0,"catalog cannot change under an owned goal");
        cat=Rooms();cat.areas[0].portals.push_back({{1,0},{1,8},{140,-10,40,160},{160,60}});
        c=Controller();o=Sample();std::string error;
        Check(!c.Start(cat,o,{{1,0},{250,60}},1000,error) && error=="no_path" && c.Steps()==0,"foreign portal wall with no path fails without movement");
        cat=Rooms();cat.areas[0].walkable={{0,0,100,100},{90,20,110,20},{180,20,220,80}};
        o=Sample();o.position={99,60};o.destination={99,60};c=Controller();Begin(c,cat,o,{{1,1},{180,60}});
        action=c.Tick(cat,o,1000);Check(action.kind==Action::Kind::SetDestination && !(action.point==cat.areas[0].portals[0].point),"portal approach has an intermediate native leg");
        c.ConfirmDestination(o,action.point,1000,true);
        o=At(o,{60,60},1100);o.area={1,1};o.map++;o.generation++;o.autoActive=false;
        Check(c.Tick(cat,o,1100).kind==Action::Kind::None,"early expected crossing records the new area before continuing");
        ++o.sequence;o.capturedTick=1110;action=c.Tick(cat,o,1110);
        Check(action.kind==Action::Kind::SetDestination && action.point==Waypoint{180,60},"selected expected portal can cross during any intermediate accepted leg");
        Catalog rounding;rounding.pvfSha256="rounded-outside";auto room=Room({1,0},{{0,40,70,20}});
        room.portals={{{1,0},{1,8},{30,30,40,40},{50,50}}};rounding.areas={room,Room({1,8})};
        o=Sample();o.position={69.8,50};o.destination={70,50};c=Controller();
        Check(!c.Start(rounding,o,{{1,0},{10,50}},1000,error) && error=="portal_rearm_unavailable","native-rounded outside origin cannot cross back through birth portal");
        cat=Rooms();cat.areas[0].portals={{{1,0},{1,8},{30,30,40,40},{50,50}},{{1,0},{1,9},{40,40,20,20},{50,50}}};
        o=Sample();o.position={50,50};c=Controller();
        Check(!c.Start(cat,o,{{1,0},{10,10}},1000,error) && error=="ambiguous_birth_portal","overlapping birth doors are never guessed");
    }
    void NativeStepBudget()
    {
        // Real geometry, not a mocked planner: every area needs birth exit plus
        // three Native AABB legs around the corridor.256 transitions exceed1024.
        const auto key=[](unsigned i){return Key{1+static_cast<int>(i/128),static_cast<int>(i%128)};};
        Catalog cat;cat.pvfSha256="step-budget";
        for (unsigned i=0;i<=256;++i)
        {
            auto area=Room(key(i),{{0,0,100,100},{90,20,110,20},{180,20,220,80}});
            area.portals.push_back({key(i),i?key(i-1):Key{9,9},{0,40,20,40},{10,60}});
            if (i<256) area.portals.push_back({key(i),key(i+1),{300,40,30,40},{315,60}});
            cat.areas.push_back(area);
        }
        cat.areas.push_back(Room({9,9}));auto o=Sample();o.area=key(0);o.position={10,60};o.destination={10,60};
        Controller c;if (!Begin(c,cat,o,{key(256),{315,60}})) return;
        std::uint64_t now=1000;unsigned region=0;
        for (unsigned iteration=0;iteration<1800 && c.Active();++iteration)
        {
            const auto action=c.Tick(cat,o,now);
            if (action.kind==Action::Kind::None && c.Active())
            {++now;++o.sequence;o.capturedTick=now;continue;}
            if (action.kind!=Action::Kind::SetDestination) break;
            c.ConfirmDestination(o,action.point,now,true);now+=50;
            o=At(o,action.point,now);
            if (action.point==Waypoint{315,60} && region<256)
            {++region;o.area=key(region);++o.map;++o.generation;o.position={10,60};o.destination={10,60};o.autoActive=false;}
        }
        Check(!c.Active() && c.Reason()=="route_step_limit" && c.Steps()==1024,"real multi-area corridor stops before a1025th Native command");
    }
    void TransactionalFirstPlan()
    {
        auto cat=Rooms();cat.areas[0].portals.push_back({{1,0},{1,8},{140,-10,40,160},{160,60}});
        auto o=Sample();Controller c;if (!Begin(c,cat,o,{{1,0},{100,60}})) return;
        std::string error;
        Check(!c.Start(cat,o,{{1,0},{250,60}},1000,error) && error=="no_path"
            && c.Active() && c.Target().point==Waypoint{100,60} && c.Steps()==0,
            "unreachable static-valid new goal cannot replace an existing valid route");
    }
    void SupportedTownKinds()
    {
        for (const char* kind:{"normal","gate","pub","dungeon_gate","fishing_place","transport","unknown"})
        {
            auto cat=Rooms();cat.areas[0].kind=kind;auto o=Sample();Controller c;std::string error;
            const bool allowed=std::string(kind)=="normal" || std::string(kind)=="gate" || std::string(kind)=="pub" || std::string(kind)=="dungeon_gate";
            const bool started=c.Start(cat,o,{{1,0},{180,60}},1000,error);
            Check(started==allowed,"ordinary Town and dungeon-gate endpoints allowed; fishing/transport/unknown refused");
            if (started) Check(c.Tick(cat,o,1000).kind==Action::Kind::SetDestination,"supported Town endpoint plans a native leg");
        }
        auto cat=Rooms();cat.areas[1].kind="fishing_place";
        cat.areas[1].portals.push_back({{1,1},{1,8},{300,20,30,80},{315,60}});
        auto o=Sample();Controller c;std::string error;
        Check(!c.Start(cat,o,{{1,8},{180,60}},1000,error) && error=="unsupported_area","unproved transit kind is rejected before taking over");
    }
    void DungeonGateSourceExit()
    {
        auto cat=Rooms(true);cat.areas[0].kind="dungeon_gate";
        auto o=Sample();Controller c;std::string error;
        const bool started=c.Start(cat,o,{{1,1},{180,60}},1000,error);
        Check(started,"already observed dungeon gate can queue an exit to an ordinary area");
        if (started)
        {
            const auto action=c.Tick(cat,o,1000);
            Check(action.kind==Action::Kind::SetDestination && action.point==Waypoint{315,60},
                "initial dungeon-gate source can approach its selected ordinary exit");
        }
        o=Sample();o.position={315,60};o.destination={315,60};c=Controller();
        const bool born=c.Start(cat,o,{{1,1},{180,60}},1000,error);
        Check(born,"dungeon-gate source already on its portal can rearm safely");
        if (born)
        {
            const auto action=c.Tick(cat,o,1000);
            Check(action.kind==Action::Kind::SetDestination && action.point==Waypoint{295,60},
                "source exception preserves the single-leg monotonic birth exit before reentry");
        }
        o=Sample();c=Controller();
        Check(!c.Start(cat,o,{{1,1},{10,60}},1000,error) && error=="goal_inside_portal",
            "source exception still rejects a final point inside an ordinary area's dangerous portal");
        Check(c.Start(cat,o,{{1,0},{180,60}},1000,error),
            "current dungeon-gate Town area can also be the final walking area");
        c=Controller();
        o.busy=true;
        Check(!c.Start(cat,o,{{1,1},{180,60}},1000,error) && error=="ui_busy" && c.Steps()==0,
            "selection or transport UI busy cannot use the source exception");
        o.busy=false;o.scene=2;
        Check(!c.Start(cat,o,{{1,1},{180,60}},1000,error) && error=="town_unavailable" && c.Steps()==0,
            "source exception requires an observed Town scene");
        auto transit=Rooms();transit.areas[1].kind="dungeon_gate";
        transit.areas[1].portals.push_back({{1,1},{1,8},{300,20,30,80},{315,60}});
        o=Sample();c=Controller();
        Check(!c.Start(transit,o,{{1,8},{180,60}},1000,error) && !c.Active() && c.Steps()==0,
            "ordinary route cannot use a dungeon gate as an intermediate area");
        Check(c.Start(transit,o,{{1,1},{180,60}},1000,error),
            "ordinary route can choose a dungeon gate as its final walking area");
        for (unsigned field=0;field<3;++field)
        {
            o=Sample();c=Controller();
            if (!c.Start(cat,o,{{1,1},{180,60}},1000,error)) continue;
            auto changed=o;++changed.sequence;changed.capturedTick=1100;
            if (field==0) ++changed.map;
            if (field==1) ++changed.generation;
            if (field==2) ++changed.actor;
            Check(c.Tick(cat,changed,1100).kind==Action::Kind::Interrupted && c.Steps()==0,
                "initial dungeon-gate departure still pins original actor map and generation");
        }
    }
    void DungeonGateFinalArrival()
    {
        auto cat=Rooms(true);cat.areas[1].kind="dungeon_gate";
        cat.areas[1].portals.push_back({{1,1},{-1,-1},{360,20,35,80},{375,60}});
        auto o=Sample();Controller c;std::string error;
        const bool started=c.Start(cat,o,{{1,1},{180,60}},1000,error);
        Check(started,"ordinary Town source queues a dungeon-gate final target");
        if(started)
        {
            auto action=c.Tick(cat,o,1000);
            Check(action.kind==Action::Kind::SetDestination&&action.point==Waypoint{315,60},"gate endpoint still approaches its original Town portal");
            c.ConfirmDestination(o,action.point,1000,true);
            o=At(o,{10,60},1100);o.area={1,1};o.map=0x500000;++o.generation;o.autoActive=false;
            Check(c.Tick(cat,o,1100).kind==Action::Kind::None,"arrival in final gate waits for a fresh birth observation");
            ++o.sequence;o.capturedTick=1110;action=c.Tick(cat,o,1110);
            Check(action.kind==Action::Kind::SetDestination&&action.point==Waypoint{25,60},"final gate re-arms its reverse Town portal safely");
            c.ConfirmDestination(o,action.point,1110,true);o=At(o,action.point,1200);
            action=c.Tick(cat,o,1200);
            Check(action.kind==Action::Kind::SetDestination&&action.point==Waypoint{180,60},"final gate plans the requested local coordinate after rearm");
            c.ConfirmDestination(o,action.point,1200,true);o=At(o,action.point,1300);
            Check(c.Tick(cat,o,1300).kind==Action::Kind::Arrived&&!c.Active(),"fresh physical position completes the gate endpoint route");
        }
        o=Sample();o.area={1,1};o.position={180,60};o.destination={180,60};c=Controller();
        const bool same=c.Start(cat,o,{{1,1},{220,60}},1000,error);
        Check(same,"same dungeon-gate area accepts another safe walking target");
        if(same)
        {
            auto action=c.Tick(cat,o,1000);
            Check(action.kind==Action::Kind::SetDestination&&action.point==Waypoint{220,60},"same-gate movement retains the final coordinate");
            c.ConfirmDestination(o,action.point,1000,true);o=At(o,action.point,1100);
            Check(c.Tick(cat,o,1100).kind==Action::Kind::Arrived,"same-gate physical arrival succeeds");
        }
        for(const auto point:{Waypoint{10,60},Waypoint{375,60},Waypoint{356,60}})
        {
            o=Sample();c=Controller();
            Check(!c.Start(cat,o,{{1,1},point},1000,error)&&error=="goal_inside_portal"&&!c.Active()&&c.Steps()==0,
                "Town, dungeon-selector and expanded selector-edge goals remain protected");
        }
        for(const auto scene:{2,3})
        {
            o=Sample();o.area={1,1};o.position={180,60};o.destination={180,60};c=Controller();
            if(!c.Start(cat,o,{{1,1},{220,60}},1000,error))continue;
            const auto action=c.Tick(cat,o,1000);c.ConfirmDestination(o,action.point,1000,true);
            o=At(o,{190,60},1100);o.scene=scene;
            Check(c.Tick(cat,o,1100).kind==Action::Kind::Interrupted&&!c.Active()&&c.Steps()==1,
                "selector or dungeon scene change cancels gate walking without another destination");
        }
    }
    bool SafePoint(const Catalog& cat,const Area& area,Waypoint& point)
    {
        for (const auto& floor:area.walkable)
        {
            const Waypoint candidate{floor.x+floor.width/2,floor.y+floor.height/2};
            bool near=false;
            for (const auto& p:area.portals)
                near=near || (candidate.x>=p.rect.x-4 && candidate.x<p.rect.x+p.rect.width+4
                    && candidate.y>=p.rect.y-4 && candidate.y<p.rect.y+p.rect.height+4);
            std::vector<Waypoint> path;std::string error;
            if (!near && cat.Waypoints(area.key,{static_cast<double>(candidate.x),static_cast<double>(candidate.y)},candidate,area.portals,path,error))
            {point=candidate;return true;}
        }
        return false;
    }
    void RealPvfDungeonGateExit(const Catalog& cat)
    {
        const auto* source=cat.Find({1,2});const auto* target=cat.Find({2,0});
        Check(source && source->kind=="dungeon_gate","actual1/2 is the observed dungeon-gate source");
        Waypoint start{},goal{};
        if (!source || !target || !SafePoint(cat,*source,start) || !SafePoint(cat,*target,goal))
        {Check(false,"actual dungeon-gate exit has safe source and ordinary final points");return;}
        std::vector<Portal> route;std::string error;
        Check(cat.Route({1,2},{2,0},route,error) && route.size()==3
            && route[0].source==Key{1,2} && route[0].target==Key{1,0}
            && route[1].source==Key{1,0} && route[1].target==Key{2,1}
            && route[2].source==Key{2,1} && route[2].target==Key{2,0},
            "actual PVF departure is1/2 to1/0 to2/1 to2/0 without dungeon transit");
        auto o=Sample();o.area={1,2};o.position={static_cast<double>(start.x),static_cast<double>(start.y)};o.destination=start;
        Controller c;const bool started=c.Start(cat,o,{{2,0},goal},1000,error);
        Check(started,"actual dungeon-gate source queues the ordinary cross-town goal");
        if (!started) return;
        std::vector<Key> crossed;std::uint64_t now=1000;
        for (unsigned iteration=0;iteration<128 && c.Active();++iteration)
        {
            const auto action=c.Tick(cat,o,now);
            if (action.kind==Action::Kind::None)
            {++now;++o.sequence;o.capturedTick=now;continue;}
            if (action.kind!=Action::Kind::SetDestination) break;
            std::vector<Portal> remaining;
            if (!cat.Route(o.area,{2,0},remaining,error)) {Check(false,"actual continuation keeps a valid portal graph");break;}
            const bool crossing=!remaining.empty() && action.point==remaining.front().point;
            c.ConfirmDestination(o,action.point,now,true);now+=100;o=At(o,action.point,now);
            if (!crossing) continue;
            const auto previous=o.area;const auto next=remaining.front().target;const auto* area=cat.Find(next);
            const Portal* birth=nullptr;
            if (area) for (const auto& portal:area->portals) if (portal.target==previous) {birth=&portal;break;}
            if (!birth) {Check(false,"actual transition has its original reverse birth portal");break;}
            o.area=next;o.map+=0x1000;++o.generation;o.autoActive=false;
            o.position={static_cast<double>(birth->point.x),static_cast<double>(birth->point.y)};o.destination=birth->point;
            crossed.push_back(next);
        }
        Check(!c.Active() && c.Status()=="arrived" && o.area==Key{2,0} && o.destination==goal,
            "actual1/2 exit completes all controller legs and ordinary final position");
        Check(crossed.size()==3 && crossed[0]==Key{1,0} && crossed[1]==Key{2,1} && crossed[2]==Key{2,0},
            "actual departure observes exactly the three expected map transitions");
    }
    void RealPvfDungeonGateFinal(const Catalog& cat)
    {
        const auto* target=cat.Find({1,2});
        Check(target&&target->kind=="dungeon_gate","reported1/2 keeps its original PVF dungeon-gate kind");
        if(!target)return;
        for(const auto from:{Key{1,0},Key{1,2}})
        {
            const auto* source=cat.Find(from);Waypoint start{};const Waypoint goal{320,247};std::string error;
            if(!source||!SafePoint(cat,*source,start)){Check(false,"actual endpoint source has a safe physical point");continue;}
            auto o=Sample();o.area=from;o.position={static_cast<double>(start.x),static_cast<double>(start.y)};o.destination=start;
            Controller c;const bool started=c.Start(cat,o,{{1,2},goal},1000,error);
            Check(started,"actual PVF permits the reported gate point from ordinary or same-area source");
            if(!started)continue;
            std::uint64_t now=1000;unsigned transitions=0;
            for(unsigned iteration=0;iteration<128&&c.Active();++iteration)
            {
                const auto action=c.Tick(cat,o,now);
                if(action.kind==Action::Kind::None){++now;++o.sequence;o.capturedTick=now;continue;}
                if(action.kind!=Action::Kind::SetDestination)break;
                std::vector<Portal> remaining;
                if(!cat.Route(o.area,{1,2},remaining,error)){Check(false,"actual final-gate graph remains valid");break;}
                const bool crossing=!remaining.empty()&&action.point==remaining.front().point;
                c.ConfirmDestination(o,action.point,now,true);now+=100;o=At(o,action.point,now);
                if(!crossing)continue;
                const auto previous=o.area;const auto next=remaining.front().target;const auto* area=cat.Find(next);
                const Portal* birth=nullptr;
                if(area)for(const auto& portal:area->portals)if(portal.target==previous){birth=&portal;break;}
                if(!birth){Check(false,"actual gate entry has the native reverse Town portal");break;}
                o.area=next;o.map+=0x1000;++o.generation;o.autoActive=false;
                o.position={static_cast<double>(birth->point.x),static_cast<double>(birth->point.y)};o.destination=birth->point;++transitions;
            }
            Check(!c.Active()&&c.Status()=="arrived"&&o.area==Key{1,2}&&o.destination==goal,
                "actual gate target completes all native legs and the final physical point");
            Check(transitions==(from==Key{1,2}?0U:1U),"actual same-area or one-Town-portal route has no dungeon transition");
        }
    }
    void RealPvfGateRegression()
    {
        wchar_t* file=nullptr;std::size_t length=0;
        if (_wdupenv_s(&file,&length,L"DFL_TOWN_ROUTE_CONTROLLER_PVF")!=0 || !file)
        { std::printf("controller real PVF Start/Tick tests: skipped (no fixture)\n");return; }
        const std::wstring path(file);std::free(file);Catalog cat;std::string error;
        if (!LoadCatalog(path.c_str(),cat,error)) {Check(false,"actual PVF loads for controller regression");return;}
        const auto* seria=cat.Find({1,1});Check(seria && seria->kind=="gate","actual Seria1/1 is ordinary gate kind");
        for (unsigned reverse=0;reverse<2;++reverse)
        {
            const Key from=reverse?Key{2,2}:Key{1,1},to=reverse?Key{1,1}:Key{2,2};
            const auto* source=cat.Find(from);const auto* target=cat.Find(to);Waypoint start{},goal{};
            if (!source || !target || !SafePoint(cat,*source,start) || !SafePoint(cat,*target,goal)) {Check(false,"actual route has safe source and final points");continue;}
            std::vector<Portal> portals;Check(cat.Route(from,to,portals,error) && portals.size()>=2,"actual Seria/Elvengard/Hendon route and reverse exist");
            bool via=false;for (const auto& p:portals) via=via || p.source==Key{1,0} || p.target==Key{1,0};
            Check(via,"actual regression crosses1/0 rather than a guessed portal");
            auto o=Sample();o.area=from;o.position={static_cast<double>(start.x),static_cast<double>(start.y)};o.destination=start;Controller c;
            const bool okay=c.Start(cat,o,{to,goal},1000,error);Check(okay,error.empty()?"actual PVF route Start succeeds":error.c_str());
            if (okay) Check(c.Tick(cat,o,1000).kind==Action::Kind::SetDestination,"actual PVF forward/reverse Start/Tick emits first native leg");
        }
        RealPvfDungeonGateExit(cat);RealPvfDungeonGateFinal(cat);
    }
    void KnownSceneAndContinuationCancellation()
    {
        auto cat=Rooms();auto o=Sample();
        for (int scene:{0,2,3})
        {
            Controller c;Begin(c,cat,o,{{1,0},{180,60}});
            auto changed=o;changed.valid=false;changed.scene=scene;
            Check(c.Tick(cat,changed,1000).kind==Action::Kind::Interrupted && c.Reason()=="unexpected_scene",
                "known nonTown scene stops even when Town payload is invalid");
        }
        for (unsigned field=0;field<2;++field)
        {
            o=Sample();o.sequence=o.generation=4;Controller c;Begin(c,cat,o,{{1,0},{180,60}});
            auto changed=At(o,{60,60},1100);
            if (field==0) changed.sequence=3;else changed.generation=3;
            Check(c.Tick(cat,changed,1100).kind==Action::Kind::Interrupted && c.Reason()=="snapshot_regressed",
                "nonzero sequence or generation regression cannot authorize input");
        }
        Catalog corridor;corridor.pvfSha256="continuation";corridor.areas={Room({1,0},{{0,0,100,100},{90,20,110,20},{180,20,220,80}})};
        for (unsigned field=0;field<3;++field)
        {
            o=Sample();o.position={99,60};o.destination={99,60};Controller c;Begin(c,corridor,o,{{1,0},{310,60}});
            const auto first=c.Tick(corridor,o,1000);c.ConfirmDestination(o,first.point,1000,true);
            o=At(o,first.point,1100);
            Check(c.Tick(corridor,o,1350).kind==Action::Kind::None && c.Steps()==1,"completed leg cannot dispatch from age250 capture");
            o=At(o,first.point,1200);const char* reason="native_auto_move_stopped";
            if (field==0) o.autoActive=false;
            if (field==1) {o.directionX=1;reason="manual_movement";}
            if (field==2) {o.destination={222,31};reason="destination_changed";}
            Check(c.Tick(corridor,o,1500).kind==Action::Kind::Interrupted && c.Reason()==reason && c.Steps()==1,
                "manual stop/direction/target change wins over send freshness between legs");
        }
        o=Sample();o.capturedTick=750;Controller c;Begin(c,cat,o,{{1,0},{180,60}});
        Check(c.Tick(cat,o,1000).kind==Action::Kind::None,"waiting for send sample starts without action");
        o=At(o,{60,60},5749);o.autoActive=false;
        Check(c.Tick(cat,o,5999).kind==Action::Kind::None,"valid age250 captures do not reset send freshness deadline");
        o=At(o,{60,60},5750);o.autoActive=false;
        Check(c.Tick(cat,o,6000).kind==Action::Kind::Interrupted && c.Reason()=="fresh_sample_unavailable","send freshness wait has its own five-second bound");
    }
    void FirstLegIntentGuard()
    {
        const auto cat=Rooms();
        for (const std::uint64_t age:{33,300})
        for (unsigned field=0;field<6;++field)
        {
            auto o=Sample();o.autoActive=field==2;Controller c;
            if (!Begin(c,cat,o,{{1,0},{180,60}})) continue;
            auto fresh=o;++fresh.sequence;fresh.capturedTick=1100;
            const char* reason="destination_changed";
            if (field==0) {fresh.destination={200,60};fresh.autoActive=true;}
            if (field==1) {fresh.directionX=0;reason="manual_movement";}
            if (field==2) {fresh.autoActive=false;reason="native_auto_move_stopped";}
            if (field==3) {fresh.autoActive=true;reason="native_auto_move_changed";}
            if (field==4) {fresh.directionY=1;reason="manual_movement";}
            if (field==5) {fresh.autoKnown=false;reason="native_auto_move_unknown";}
            const auto action=c.Tick(cat,fresh,1100+age);
            Check(action.kind==Action::Kind::Interrupted && c.Reason()==reason && c.Steps()==0 && !c.Active(),
                "first-leg intent change cancels before age200 send eligibility and consumes no native step");
            Check(c.Tick(cat,o,1500).kind==Action::Kind::None,"restored old intent cannot revive cancelled first leg");
        }
        auto o=Sample();o.autoActive=true;o.directionX=1;o.destination={250,60};Controller c;
        Begin(c,cat,o,{{1,0},{180,60}});
        auto natural=o;++natural.sequence;natural.capturedTick=1100;natural.position={80,60};
        const auto action=c.Tick(cat,natural,1100);
        Check(action.kind==Action::Kind::SetDestination && action.point==Waypoint{180,60},
            "new goal can take over unchanged previous automatic movement using its fresh actual position");
        c=Controller();Begin(c,cat,o,{{1,0},{180,60}});
        auto idle=natural;idle.directionX=4;
        Check(c.Tick(cat,idle,1350).kind==Action::Kind::None && c.Steps()==0,
            "old movement may settle idle while first leg waits for send freshness");
        auto restart=idle;++restart.sequence;restart.capturedTick=1200;restart.directionX=1;
        Check(c.Tick(cat,restart,1500).kind==Action::Kind::Interrupted && c.Reason()=="manual_movement" && c.Steps()==0,
            "settled idle baseline cannot later restart the old direction before the first native leg");
        c=Controller();Begin(c,cat,o,{{1,0},{180,60}});
        Check(c.Tick(cat,idle,1100).kind==Action::Kind::SetDestination,
            "natural old direction settling idle does not prevent an otherwise fresh new goal");
    }
    void PostTransitionIntentGuard()
    {
        const auto cat=Rooms(true);
        for (unsigned field=0;field<5;++field)
        {
            auto o=Sample();Controller c;if (!Begin(c,cat,o,{{1,1},{180,60}})) continue;
            const auto first=c.Tick(cat,o,1000);c.ConfirmDestination(o,first.point,1000,true);
            o=At(o,{10,60},1100);o.area={1,1};++o.map;++o.generation;o.autoActive=field==2;
            const auto entered=c.Tick(cat,o,1100);
            Check(entered.kind==Action::Kind::None && c.Active() && c.Steps()==1,
                "expected transition captures its new-area intent baseline without immediately moving the birth point");
            auto next=o;++next.sequence;next.capturedTick=1200;
            const char* reason="destination_changed";
            if (field==0) next.destination={60,60};
            if (field==1) {next.directionX=0;reason="manual_movement";}
            if (field==2) {next.autoActive=false;reason="native_auto_move_stopped";}
            if (field==3) {next.autoActive=true;reason="native_auto_move_changed";}
            if (field==4) {next.directionY=1;reason="manual_movement";}
            Check(c.Tick(cat,next,1500).kind==Action::Kind::Interrupted && c.Reason()==reason && c.Steps()==1,
                "fresh new-area manual intent cancels before the birth native leg without comparing old-map destination");
        }
    }
}
int RunTownRouteControllerTests()
{
    Failures=0;SameAreaAndHandshake();ExpectedTransitionAndBirth();BindingAndContextGuards();
    StrongCancellation();LoadingAndBudgets();ReplanAndForeignDoors();BirthSafetyAndTakeover();
    AdditionalLifecycleBoundaries();NativeStepBudget();TransactionalFirstPlan();SupportedTownKinds();DungeonGateSourceExit();DungeonGateFinalArrival();RealPvfGateRegression();
    KnownSceneAndContinuationCancellation();
    FirstLegIntentGuard();PostTransitionIntentGuard();
    std::printf("town route controller tests: %d failure(s)\n",Failures);return Failures;
}
