#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "../src/client_navigation.h"
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <fstream>
#include <limits>
#include <unordered_map>
namespace
{
    using namespace dfl::client_navigation;
    int Failures = 0;
    bool PatchOkay=false;
    unsigned PatchCount=0;
    bool RejectLive(const dfl::client_navigation::Request&) noexcept {return false;}
    bool AcceptLive(const dfl::client_navigation::Request&) noexcept {return true;}
    bool FakePatch(const unsigned char* expected,const unsigned char* replacement) noexcept
    { ++PatchCount;return PatchOkay && expected[0]==0xE8 && replacement[1]==0xD9; }
    struct HookPatches
    {
        bool failPoll=false,failHit=false,failPollRestore=false,failHitRestore=false;
        char order[8]{};unsigned count=0;
    } Hooks;
    bool PollPatch(const unsigned char* expected,const unsigned char* replacement) noexcept
    {
        const bool installing=expected[1]==0xD9;
        Hooks.order[Hooks.count++]=installing?'L':'l';
        return installing?!Hooks.failPoll:!Hooks.failPollRestore&&replacement[1]==0xD9;
    }
    bool HitPatch(const unsigned char* expected,const unsigned char* replacement) noexcept
    {
        const bool installing=expected[1]==0x7C;
        Hooks.order[Hooks.count++]=installing?'R':'r';
        constexpr unsigned char original[]={0xE8,0x7C,0xFC,0xFF,0xFF};
        return installing?!Hooks.failHit:!Hooks.failHitRestore&&std::memcmp(replacement,original,5)==0;
    }
    struct HitOrder
    {
        unsigned calls=0,after=0;bool ordered=true;std::uint32_t event=0;
        static std::int32_t Original(void* context) noexcept
        {auto& s=*static_cast<HitOrder*>(context);s.ordered&=s.calls==0&&s.after==0;++s.calls;return -1234567;}
        static void After(void* context,std::uint32_t event) noexcept
        {auto& s=*static_cast<HitOrder*>(context);s.ordered&=s.calls==1&&s.after==0;++s.after;s.event=event;}
    };
    struct Actions
    {
        unsigned sets=0,activations=0;bool accept=true,ordered=true;
        static bool Set(void* context,const Request&) noexcept { auto& a=*static_cast<Actions*>(context);++a.sets;return a.accept; }
        static bool Activate(void* context,const Request&) noexcept
        { auto& a=*static_cast<Actions*>(context);a.ordered=a.sets==1&&a.activations==0;++a.activations;return true; }
    };
    void Check(bool okay, const char* label) { if (!okay) { ++Failures; std::fprintf(stderr,"FAIL navigation: %s\n",label); } }
    struct Memory
    {
        std::unordered_map<std::uint32_t, unsigned char> data;
        std::uint32_t mutateAt=0,mutateTo=0;
        unsigned mutateOnRead=0,matchingReads=0;
        template<class T> void Put(std::uint32_t at, T value)
        { const auto* p=reinterpret_cast<const unsigned char*>(&value); for(std::size_t i=0;i<sizeof(value);++i)data[at+static_cast<std::uint32_t>(i)]=p[i]; }
        void U(std::uint32_t at, std::uint32_t value) { Put(at,value); }
        static bool Read(void* context,std::uint32_t at,void* output,std::size_t count) noexcept
        { auto& m=*static_cast<Memory*>(context);if(at==m.mutateAt && ++m.matchingReads==m.mutateOnRead)m.U(at,m.mutateTo);auto* p=static_cast<unsigned char*>(output);for(std::size_t i=0;i<count;++i){auto it=m.data.find(at+static_cast<std::uint32_t>(i));if(it==m.data.end())return false;p[i]=it->second;}return true; }
        dfl::client_state::Reader Reader() { dfl::client_state::Reader r{};r.context=this;r.read=Read;return r; }
        void Hash()
        {
            U(0x100008,0x110000);U(0x10000C,1);U(0x110000,0x120000);U(0x110004,0x120000);
            U(0x120000,0x110000);U(0x120004,0x110000);Put<std::int32_t>(0x120008,1024);U(0x12000C,0x130000);
            Put<std::int32_t>(0x130000,1);Put<std::int32_t>(0x130004,0);U(0x130008,0x140000);
            Put(0x140000,Rect{10,20,100,30});
        }
        void Marker(std::uint32_t hit=0x882B40)
        {
            U(0x200014,0x210000);U(0x200018,0x210004);U(0x210000,0x220000);U(0x220000,0x230000);U(0x230004,hit);
            Put<std::int32_t>(0x220008,50);Put<std::int32_t>(0x22000C,60);U(0x220010,0x240000);U(0x240014,10);U(0x240018,20);
        }
        void QuestMarkerOnCanvas()
        {
            U(0x530014,0x210000);U(0x530018,0x210004);U(0x210000,0x260000);
            U(0x260000,0xA84758);U(0xA8475C,0x882E40);
            Put<std::int32_t>(0x260008,60);Put<std::int32_t>(0x26000C,50);
            U(0x260010,0x250100);U(0x26001C,0x250000);U(0x250024,60);U(0x250028,60);
            Put<unsigned char>(0x260041,1);
        }
        void Row(std::uint32_t hash,std::uint32_t allocation,const Rect& rect,std::int32_t town=1,std::int32_t area=0)
        {
            const auto head=allocation,node=allocation+0x100,record=allocation+0x200,rectangle=allocation+0x300;
            U(hash+8,head);U(hash+12,1);U(head,node);U(head+4,node);U(node,head);U(node+4,head);
            Put<std::int32_t>(node+8,(town<<10)+area);U(node+12,record);Put<std::int32_t>(record,town);Put<std::int32_t>(record+4,area);U(record+8,rectangle);Put(rectangle,rect);
        }
        void SecondProject(std::int32_t town,std::int32_t area)
        {
            U(0x53002C,2);U(0x540004,0x540400);U(0x540100,0x540400);
            U(0x540400,0x540000);U(0x540404,0x540100);Put<std::int32_t>(0x540408,(town<<10)+area);U(0x54040C,0x540500);
            Put<std::int32_t>(0x540500,town);Put<std::int32_t>(0x540504,area);U(0x540508,0x540600);Put(0x540600,Rect{10,20,100,50});
        }
        void Canvas()
        {
            U(0xB5DCE8,0x500000);U(0x500FF0,0x510000);U(0x510000,0xA7CF3C);U(0x510004,0x520000);U(0x51000C,0x530000);
            Put<unsigned char>(0x520010,1);Put<unsigned char>(0x520011,1);U(0x52000C,777);U(0xB55184,777);
            Put<unsigned char>(0x500FD8,1);Put<unsigned char>(0xCFD200,0);Put<unsigned char>(0xCFD20A,0);
            U(0xCFD1F4,0);
            Put<unsigned char>(0x500FD9,0);Put<unsigned char>(0xCFD202,0);U(0xB55188,777);U(0xCFD1F8,0);
            U(0x51001C,0x560000);Put<unsigned char>(0x560010,0);U(0x510064,0x570000);Put<unsigned char>(0x570010,0);
            Put<std::int32_t>(0xBEEFC4,0);Put<std::int32_t>(0xB53C28,26);Put<std::int32_t>(0x530004,0);Put<std::int32_t>(0x530008,-26);
            Put<std::int32_t>(0xCFD1EC,60);Put<std::int32_t>(0xCFD1F0,71);U(0x530014,0);U(0x530018,0);
            Row(0x530020,0x540000,{10,20,100,50});Row(0xBEF024,0x550000,{-100,100,1000,500});
        }
    };
    dfl::client_state::Snapshot Sample()
    {
        dfl::client_state::Snapshot s{};s.pid=7;s.processStartedFileTime=999;s.sequence=20;s.generation=3;s.capturedTickMs=1000;
        s.valid=s.layoutSupported=s.haveMe=true;s.scene={true,1};s.me.uid={true,2};s.me.actorToken=0x100000;s.me.mapToken=0x200000;s.me.userToken=0x300000;
        s.town.present=true;s.town.contextToken=0x400000;s.town.mapToken=s.me.mapToken;s.town.townId=1;s.town.areaId=0;s.town.position={true,100,200,0};return s;
    }
    Request Command(const dfl::client_state::Snapshot& s)
    {
        Request r{};r.pid=s.pid;r.uid=s.me.uid.value;r.processStartedFileTime=s.processStartedFileTime;r.requestId=50;r.issuedTickMs=1050;r.snapshotTickMs=s.capturedTickMs;
        r.generation=s.generation;r.snapshotSequence=s.sequence;r.actorToken=s.me.actorToken;r.mapToken=s.me.mapToken;r.userToken=s.me.userToken;r.contextToken=s.town.contextToken;
        r.townId=s.town.townId;r.areaId=s.town.areaId;r.x=150;r.y=220;return r;
    }
    void RouteCanvasTests()
    {
        const auto s=Sample();Memory m;m.Canvas();CanvasGoal goal;
        Check(ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal,0x10)&&goal.town==1&&goal.area==0&&goal.x==400&&goal.y==350,"consumed right canvas release selects current region with FD9 already zero");
        m.Put<unsigned char>(0x500FD8,0);m.Put<unsigned char>(0xCFD200,1);m.U(0xB55184,778);
        Check(ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal,0x10),"right reader is independent of all left button fields");
        m.Row(0x530020,0x540000,{10,20,100,50},2,3);m.Row(0xBEF024,0x550000,{-100,100,1000,500},2,3);
        Check(ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal,0x10)&&goal.town==2&&goal.area==3&&goal.x==400&&goal.y==350,"right release selects another displayed town and area");
        Check(goal.mapX==60&&goal.mapY==45&&goal.minimapToken==0x510000&&goal.planeToken==0x530000,
            "feedback carries only the verified map-local point and exact owner plane");
        auto denied=[&](const char* label,std::uint32_t eventBefore=0x10)
        {
            goal={9,8,7,6};const bool accepted=ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal,eventBefore);
            Check(!accepted&&goal.town==9&&goal.area==8&&goal.x==7&&goal.y==6,label);
        };
        m.Canvas();denied("left release or stale capture alone never selects route",0);
        Check(!ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal),"default absent native event never replays cached right hit");
        m.Canvas();m.Put<unsigned char>(0xCFD202,1);denied("held right button never selects route");
        m.Canvas();denied("non-release native event rejected",2);
        m.Canvas();m.U(0xCFD1F8,0x10);m.Put<unsigned char>(0x500FD9,1);m.U(0xB55188,UINT32_MAX);
        denied("update-phase edge before native hit traversal is not a target");
        m.Put<unsigned char>(0x500FD9,0);m.U(0xCFD1F8,0);m.U(0xB55188,777);
        Check(ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal,0x10),"draw-phase native consumed event and root hit complete the two-stage release");
        m.U(0xCFD1F8,0x10);denied("unconsumed right event rejects even a previously cached root hit");
        m.Canvas();m.Put<unsigned char>(0xCFD20A,1);denied("unsupported input mode rejected");
        m.Canvas();m.U(0xB55188,778);denied("right child dropdown button close or foreign capture rejected");
        m.Canvas();m.Put<unsigned char>(0x520010,0);denied("hidden cached map rejected");
        m.Canvas();m.Put<unsigned char>(0x520011,0);denied("disabled map rejected");
        m.Canvas();m.Put<unsigned char>(0x560010,1);denied("expanded region dropdown rejects blank canvas right click");
        m.Canvas();m.Put<unsigned char>(0x570010,1);denied("expanded NPC dropdown rejects blank canvas right click");
        m.Canvas();m.data.erase(0x560010);denied("unknown dropdown visibility fails closed");
        m.Canvas();m.U(0x500FF0,0x510100);denied("obsolete map owner rejected");
        m.Canvas();m.U(0x510000,0xA7D330);denied("dungeon HUD minimap rejected");
        m.Canvas();m.Put<std::int32_t>(0x530008,0);denied("undrawn cached plane rejected");
        m.Canvas();m.Row(0xBEF024,0x550000,{-100,100,1000,500},2,3);denied("displayed region missing matching real geometry rejected");
        m.Canvas();m.SecondProject(1,1);denied("overlapping displayed regions rejected");
        m.Canvas();m.SecondProject(1,0);denied("duplicate displayed region keys rejected");
        m.Canvas();m.Put<std::int32_t>(0xCFD1EC,110);denied("project right boundary is exclusive");
        m.Canvas();m.Put<std::int32_t>(0xCFD1F0,96);denied("project bottom boundary is exclusive");
        m.Canvas();m.Marker();m.U(0x530014,0x210000);m.U(0x530018,0x210004);m.Put<std::int32_t>(0x220008,60);m.Put<std::int32_t>(0x22000C,45);
        denied("NPC marker rejects right goal");
        m.Marker(0xDEADBEEF);denied("unknown marker layout rejects right goal");
        m.Canvas();m.QuestMarkerOnCanvas();
        Check(ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal,0x10)
            &&goal.town==1&&goal.area==0&&goal.x==400&&goal.y==350&&goal.mapX==60&&goal.mapY==45,
            "right release over a quest icon projects the ground directly under the cursor");
        std::int32_t leftX=0,leftY=0;
        Check(!ReadCanvasTarget(m.Reader(),s,0x510000,leftX,leftY),"legacy left reader still excludes the quest icon");
        m.U(0xB55188,778);denied("quest icon does not bypass the root capture check");m.U(0xB55188,777);
        m.U(0x26001C,0);denied("quest icon with unreadable sprite is rejected");m.U(0x26001C,0x250000);
        m.Marker(0x882E40);m.QuestMarkerOnCanvas();m.U(0x260000,0x230000);
        denied("quest hit code without the verified quest vtable remains opaque");
        m.Marker();m.QuestMarkerOnCanvas();m.U(0x530018,0x210008);m.U(0x210004,0x220000);
        m.U(0x220000,0xA70BD0);m.U(0xA70BD4,0x882B40);m.Put<std::int32_t>(0x220008,60);
        m.U(0x240014,15);m.U(0x240018,15);m.U(0x220028,0x260000);m.U(0x26003C,0);
        denied("quest overlay does not bypass an unrelated NPC");
        m.U(0x26003C,0x220000);
        Check(ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal,0x10)
            &&goal.town==1&&goal.area==0&&goal.x==400&&goal.y==350,
            "right release also projects ground where the visible quest icon overlaps its owning NPC");
        m.Put<unsigned char>(0x260041,0);denied("hidden quest icon does not bypass its owning NPC");
        m.Put<unsigned char>(0x260041,1);m.U(0x210000,0);denied("quest icon absent from the current plane does not bypass its owning NPC");
        m.U(0x210000,0x260000);m.Put<std::int32_t>(0xCFD1F0,77);
        denied("NPC outside its quest icon bounds still rejects a right goal");
        m.Canvas();m.Put<std::int32_t>(0xCFD1EC,INT32_MAX);m.Put<std::int32_t>(0xBEEFC4,-1);m.Put<std::int32_t>(0x530004,1);denied("mouse translation overflow rejected");
        m.Canvas();m.mutateAt=0x51000C;m.mutateTo=0x530100;m.mutateOnRead=2;m.matchingReads=0;denied("plane replacement during read rejected");
        m.mutateAt=0;m.Canvas();m.mutateAt=0xB55188;m.mutateTo=778;m.mutateOnRead=2;m.matchingReads=0;denied("right capture replacement during read rejected");
        m.mutateAt=0;m.Canvas();m.mutateAt=0x520010;m.mutateTo=0;m.mutateOnRead=2;m.matchingReads=0;denied("map closes during read without publishing partial goal");
        m.mutateAt=0;m.Canvas();m.mutateAt=0xCFD1F8;m.mutateTo=0x10;m.mutateOnRead=2;m.matchingReads=0;denied("a replacement event during read cannot publish a goal");
        m.mutateAt=0;m.Canvas();auto unavailable=s;unavailable.layoutSupported=false;
        Check(!ReadRouteCanvasTarget(m.Reader(),unavailable,0x510000,goal,0x10),"unsupported snapshot rejects right goal");
        unavailable=s;unavailable.truncated=true;Check(!ReadRouteCanvasTarget(m.Reader(),unavailable,0x510000,goal,0x10),"truncated snapshot rejects right goal");
        unavailable=s;unavailable.scene.value=3;Check(!ReadRouteCanvasTarget(m.Reader(),unavailable,0x510000,goal,0x10),"non Town scene rejects right goal");
        m.Canvas();m.Put<unsigned char>(0x500FD9,0);std::int32_t x=0,y=0;
        Check(ReadCanvasTarget(m.Reader(),s,0x510000,x,y)&&x==400&&y==350,"legacy left reader still operates with no right edge");
    }
    void RightHitHookTests()
    {
        HitOrder order;
        for (std::uint32_t event:{8U,16U})
        {order={};Check(PollCanvasRelease(&order,event,&HitOrder::Original,&HitOrder::After)==-1234567
            &&order.calls==1&&order.after==(event==16?1U:0U)
            &&(event!=16||(order.ordered&&order.event==16)),"only right release invokes after-callback; both preserve original full EAX");}
        for(std::uint32_t event:{0U,2U})
        {order={};Check(PollCanvasRelease(&order,event,&HitOrder::Original,&HitOrder::After)==-1234567
            &&order.calls==1&&order.after==0,"original hit traversal always runs without a right release");}
        order={};Check(PollCanvasRelease(&order,0x10,nullptr,&HitOrder::After)==0&&order.after==0,"missing original never dispatches a canvas action");
        const std::array<unsigned char,5> left={0xE8,0x11,2,3,4},right={0xE8,0x22,2,3,4};bool poll=false,hit=false;
        Hooks={};Check(InstallCanvasHooks(poll,hit,left,right,&PollPatch,&HitPatch)&&poll&&hit&&std::strcmp(Hooks.order,"LR")==0,"both original CALL sites install in order");
        Hooks.failHitRestore=true;Check(!RestoreInstalledHitHook(hit,right,&HitPatch)&&hit,"right restore failure retains ownership and blocks unload");
        Hooks.failHitRestore=false;Check(RestoreInstalledHitHook(hit,right,&HitPatch)&&!hit,"right restore retries exact original hit CALL bytes");
        Check(RestoreInstalledHook(poll,left,&PollPatch)&&!poll,"old poll restore follows right restore");
        poll=hit=false;Hooks={};Hooks.failHit=true;
        Check(!InstallCanvasHooks(poll,hit,left,right,&PollPatch,&HitPatch)&&!poll&&!hit&&std::strcmp(Hooks.order,"LRl")==0,"failed second hook rolls back the first");
        Hooks={};Hooks.failHit=Hooks.failPollRestore=true;
        Check(!InstallCanvasHooks(poll,hit,left,right,&PollPatch,&HitPatch)&&poll&&!hit,"failed rollback preserves retained poll ownership");
        Hooks.failPollRestore=false;Check(RestoreInstalledHook(poll,left,&PollPatch)&&!poll,"retained rollback hook can later restore before unload");
        Hooks={};Hooks.failPoll=true;
        Check(!InstallCanvasHooks(poll,hit,left,right,&PollPatch,&HitPatch)&&!poll&&!hit&&Hooks.count==1,"first hook refusal never patches second site");
    }
    void LeftRouteCanvasTests()
    {
        const auto s=Sample();Memory m;m.Canvas();CanvasGoal goal{9,8,7,6};
        Check(!ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal,8)
            &&goal.town==9&&goal.area==8&&goal.x==7&&goal.y==6,
            "consumed left canvas release never submits a route or changes the goal");
        m.Put<unsigned char>(0xCFD202,1);m.U(0xCFD1F8,16);
        Check(!ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal,8),
            "left release remains inert even with right state present");
        Check(!ReadRouteCanvasTarget(m.Reader(),s,0x510000,goal),
            "missing release token cannot replay a cached capture");
    }

}
int RunClientNavigationTests()
{
    Failures=0;auto s=Sample();auto r=Command(s);
    RouteCanvasTests();
    LeftRouteCanvasTests();
    RightHitHookTests();
    Check(ValidateRequest(r,s,1100,49)==nullptr,"fresh matching Town request accepted");
    Check(ValidateRequest(r,s,1100,50)!=nullptr,"replayed request rejected");
    Check(ValidateRequest(r,s,2200,49)!=nullptr,"expired request rejected");
    Check(ValidateRequest(r,s,1049,49)!=nullptr,"future issue rejected");
    auto bad=r;bad.areaId=1;Check(ValidateRequest(bad,s,1100,0)!=nullptr,"cross area rejected");
    bad=r;bad.actorToken++;Check(ValidateRequest(bad,s,1100,0)!=nullptr,"actor changed rejected");
    bad=r;bad.generation++;Check(ValidateRequest(bad,s,1100,0)!=nullptr,"generation changed rejected");
    bad=r;bad.snapshotSequence=21;Check(ValidateRequest(bad,s,1100,0)!=nullptr,"future sequence rejected");
    s.truncated=true;Check(ValidateRequest(r,s,1100,0)!=nullptr,"truncated capture rejected");s.truncated=false;
    s.scene.value=3;Check(ValidateRequest(r,s,1100,0)!=nullptr,"dungeon rejected");
    std::int32_t x=0,y=0;
    Check(InverseProject({10,20,100,50},{-100,100,1000,500},60,45,x,y)&&x==400&&y==350,"inverse center projection");
    Check(!InverseProject({10,20,100,50},{-100,100,1000,500},110,45,x,y),"project right edge exclusive");
    Check(!InverseProject({10,20,0,50},{0,0,100,100},10,25,x,y),"zero extent rejected");
    Check(!InverseProject({0,0,2,2},{INT32_MAX-1,0,10,2},1,1,x,y),"overflow rectangle rejected");
    Memory m;m.Hash();std::array<RegionRect,MaximumRegions> rows{};std::size_t count=0;
    Check(ReadRegions(m.Reader(),0x100000,rows,count)&&count==1&&rows[0].town==1&&rows[0].rect.width==100,"native linked cache read");
    m.U(0x120000,0x120000);Check(!ReadRegions(m.Reader(),0x100000,rows,count),"cache cycle rejected");m.Hash();
    m.Put<std::int32_t>(0x120008,2048);Check(!ReadRegions(m.Reader(),0x100000,rows,count),"cache key record mismatch rejected");
    m.Marker();Check(!BlankMarkers(m.Reader(),0x200000,50,50),"NPC icon excluded");
    Check(BlankMarkers(m.Reader(),0x200000,10,10),"empty point allowed");
    m.Marker(0x882C80);Check(!BlankMarkers(m.Reader(),0x200000,50,50),"player icon excluded");
    m.Marker(0x882BB0);Check(!BlankMarkers(m.Reader(),0x200000,70,80),"dungeon marker excluded");
    m.Marker(0xDEADBEEF);Check(!BlankMarkers(m.Reader(),0x200000,10,10),"unknown marker denies canvas");
    s=Sample();m.Canvas();
    Check(ReadCanvasTarget(m.Reader(),s,0x510000,x,y)&&x==400&&y==350,"blank canvas release sets current-region target");
    m.U(0xB55184,778);Check(!ReadCanvasTarget(m.Reader(),s,0x510000,x,y),"child or overlay capture excluded");m.U(0xB55184,777);
    m.Put<unsigned char>(0x520010,0);Check(!ReadCanvasTarget(m.Reader(),s,0x510000,x,y),"close leaves no target");m.Put<unsigned char>(0x520010,1);
    m.Put<unsigned char>(0x500FD8,0);Check(!ReadCanvasTarget(m.Reader(),s,0x510000,x,y),"no release edge no target");m.Put<unsigned char>(0x500FD8,1);
    s.town.areaId=1;Check(!ReadCanvasTarget(m.Reader(),s,0x510000,x,y),"another area canvas excluded");s.town.areaId=0;
    m.Put<std::int32_t>(0x530008,0);Check(!ReadCanvasTarget(m.Reader(),s,0x510000,x,y),"undrawn cached map excluded");
    m.Marker(0x882E40);m.U(0x22001C,0x250000);m.U(0x250024,100);m.U(0x250028,100);
    Check(!BlankMarkers(m.Reader(),0x200000,50,40),"scaled sprite icon excluded");
    Check(BlankMarkers(m.Reader(),0x200000,1,1),"scaled sprite empty point allowed");
    bool installed=true;std::array<unsigned char,5> patch={0xE8,1,2,3,4};
    Check(!RestoreInstalledHook(installed,patch,&FakePatch)&&installed&&PatchCount==1,"restore failure preserves hook ownership and blocks unload");
    PatchOkay=true;Check(RestoreInstalledHook(installed,patch,&FakePatch)&&!installed&&PatchCount==2,"restore retry clears ownership only on success");
    Check(RestoreInstalledHook(installed,patch,&FakePatch)&&PatchCount==2,"inactive restore is idempotent");
    Actions actions;DestinationOperations operations{&actions,&Actions::Set,&Actions::Activate};
    Check(!SetLiveAndActivate(r,operations,&RejectLive,1100)&&actions.sets==0&&actions.activations==0,
          "invalid live context invokes zero destination callbacks");
    Check(SetLiveAndActivate(r,operations,&AcceptLive,1100)&&actions.sets==1&&actions.activations==1,
          "live guard precedes ordered destination operations");
    actions={};
    Check(!SetLiveAndActivate(r,operations,&AcceptLive,2200)&&actions.sets==0&&actions.activations==0,
          "stale source invokes zero destination callbacks");
    Check(SetAndActivate(r,operations)&&actions.sets==1&&actions.activations==1&&actions.ordered,"verified setter then native auto mode activated exactly once");
    actions={};actions.accept=false;
    Check(!SetAndActivate(r,operations)&&actions.sets==1&&actions.activations==0,"failed setter never activates auto mode");
    char fixture[MAX_PATH]{};
    if (GetEnvironmentVariableA("DFL_NAVIGATION_REQUEST_FIXTURE",fixture,MAX_PATH)>0)
    {
        std::ifstream file(fixture,std::ios::binary);Request wire{};file.read(reinterpret_cast<char*>(&wire),sizeof(wire));
        Check(file.gcount()==sizeof(wire)&&file.peek()==std::char_traits<char>::eof(),"Python fixture exact96 bytes");
        Check(std::memcmp(wire.magic,"DLNV",4)==0&&wire.pid==4567&&wire.uid==1&&wire.processStartedFileTime==134060000000000123ULL
            &&wire.issuedTickMs==4294971392ULL&&wire.actorToken==0x1812E468&&wire.contextToken==0x18001000&&wire.x==100&&wire.y==200,"Python fixture native layout");
    }
    std::printf("navigation tests: %d failure(s)\n",Failures);return Failures;
}
