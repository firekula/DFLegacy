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
    bool FakePatch(const unsigned char* expected,const unsigned char* replacement) noexcept
    { ++PatchCount;return PatchOkay && expected[0]==0xE8 && replacement[1]==0xD9; }
    void Check(bool okay, const char* label) { if (!okay) { ++Failures; std::fprintf(stderr,"FAIL navigation: %s\n",label); } }
    struct Memory
    {
        std::unordered_map<std::uint32_t, unsigned char> data;
        template<class T> void Put(std::uint32_t at, T value)
        { const auto* p=reinterpret_cast<const unsigned char*>(&value); for(std::size_t i=0;i<sizeof(value);++i)data[at+static_cast<std::uint32_t>(i)]=p[i]; }
        void U(std::uint32_t at, std::uint32_t value) { Put(at,value); }
        static bool Read(void* context,std::uint32_t at,void* output,std::size_t count) noexcept
        { auto& m=*static_cast<Memory*>(context);auto* p=static_cast<unsigned char*>(output);for(std::size_t i=0;i<count;++i){auto it=m.data.find(at+static_cast<std::uint32_t>(i));if(it==m.data.end())return false;p[i]=it->second;}return true; }
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
        void Row(std::uint32_t hash,std::uint32_t allocation,const Rect& rect)
        {
            const auto head=allocation,node=allocation+0x100,record=allocation+0x200,rectangle=allocation+0x300;
            U(hash+8,head);U(hash+12,1);U(head,node);U(head+4,node);U(node,head);U(node+4,head);
            Put<std::int32_t>(node+8,1024);U(node+12,record);Put<std::int32_t>(record,1);Put<std::int32_t>(record+4,0);U(record+8,rectangle);Put(rectangle,rect);
        }
        void Canvas()
        {
            U(0xB5DCE8,0x500000);U(0x500FF0,0x510000);U(0x510000,0xA7CF3C);U(0x510004,0x520000);U(0x51000C,0x530000);
            Put<unsigned char>(0x520010,1);Put<unsigned char>(0x520011,1);U(0x52000C,777);U(0xB55184,777);
            Put<unsigned char>(0x500FD8,1);Put<unsigned char>(0xCFD200,0);Put<unsigned char>(0xCFD20A,0);
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
}
int RunClientNavigationTests()
{
    Failures=0;auto s=Sample();auto r=Command(s);
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
