#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "../src/client_test_control.h"
#include <cstdio>
#include <cstring>
#include <unordered_map>
namespace
{
    using namespace dfl::client_test_control;
    int failures=0;
    void Check(bool okay,const char* label) { if(!okay){++failures;std::fprintf(stderr,"FAIL test-control: %s\n",label);} }
    struct Fixture
    {
        Request r{};Lease l{};dfl::client_state::Snapshot s{};
        Fixture()
        {
            s.valid=s.layoutSupported=s.haveMe=true;s.scene={true,3};s.pid=42;s.processStartedFileTime=123;
            s.capturedTickMs=10000;s.sequence=20;s.generation=7;s.me.uid={true,77};
            s.me.actorToken=0x10000;s.me.mapToken=0x20000;s.me.userToken=0x30000;
            s.me.dead={true,false};s.me.sameMap={true,true};s.me.position={true,100,200,0};
            r.pid=42;r.uid=77;r.processStartedFileTime=123;r.requestId=5;r.issuedTickMs=10000;r.snapshotTickMs=10000;
            r.snapshotSequence=20;r.generation=7;r.actorToken=0x10000;r.mapToken=0x20000;r.userToken=0x30000;r.operation=1;r.x=350;r.y=250;
            r.targetId=77;r.sessionNonce=999;r.runId=888;l.nonce=999;l.runId=888;l.uid=77;l.issuedFileTime=100000000;
        }
        const char* Validate(std::uint64_t consumed=0,std::uint64_t now=10000,std::uint64_t ft=100000000)
        {return ValidateRequest(r,s,l,now,ft,consumed);}
    };
    struct Memory
    {
        std::unordered_map<std::uint32_t,unsigned char> data;
        template<class T> void Put(std::uint32_t at,T v) {const auto* p=reinterpret_cast<const unsigned char*>(&v);for(std::size_t i=0;i<sizeof(T);++i)data[at+static_cast<std::uint32_t>(i)]=p[i];}
        static bool Read(void* ctx,std::uint32_t at,void* to,std::size_t n) noexcept
        {auto& m=*static_cast<Memory*>(ctx);auto* p=static_cast<unsigned char*>(to);for(std::size_t i=0;i<n;++i){auto it=m.data.find(at+static_cast<std::uint32_t>(i));if(it==m.data.end())return false;p[i]=it->second;}return true;}
    };
}
int RunClientTestControlTests()
{
    failures=0;
    {Fixture f;Check(f.Validate()==nullptr,"fresh leased request accepted");Check(f.Validate(5)!=nullptr,"consumed ID rejected");}
    {Fixture f;f.r.sessionNonce++;Check(f.Validate()!=nullptr,"foreign lease nonce rejected");}
    {Fixture f;f.l.runId++;Check(f.Validate()!=nullptr,"foreign run rejected");}
    {Fixture f;Check(f.Validate(0,10000,115000001)!=nullptr,"lease older than 1500ms rejected");Check(f.Validate(0,10000,115000000)==nullptr,"lease exactly 1500ms accepted");}
    {Fixture f;f.l.flags=0;Check(f.Validate()!=nullptr,"revoked lease rejected");}
    {Fixture f;f.r.mapToken++;Check(f.Validate()!=nullptr,"cross-map rejected");}
    {Fixture f;f.s.me.dead.value=true;Check(f.Validate()!=nullptr,"dead local actor rejected");}
    {Fixture f;f.s.truncated=true;Check(f.Validate()!=nullptr,"partial capture rejected");}
    {Fixture f;Check(f.Validate(0,11001)!=nullptr,"stale request rejected");}
    {Fixture f;f.r.snapshotSequence=14;Check(f.Validate()!=nullptr,"old generation of snapshot rejected");}
    {Fixture f;f.r.operation=2;f.r.targetId=0;Check(f.Validate()==nullptr,"zero-based monster target accepted");f.r.targetId=-1;Check(f.Validate()!=nullptr,"negative monster target rejected");}
    {Fixture f;f.r.operation=7;Check(f.Validate()!=nullptr,"unknown operation rejected");}
    {Fixture f;f.r.operation=6;f.r.x=f.r.y=0;
     Check(!f.Validate(),"player kill accepts only bound living self with no HP operand");
     f.r.targetId=78;Check(f.Validate()!=nullptr,"player kill cannot target a peer");f.r.targetId=77;
     f.r.x=1;Check(f.Validate()!=nullptr,"player kill rejects arbitrary HP operand");f.r.x=0;
     f.r.y=1;Check(f.Validate()!=nullptr,"player kill rejects hidden second operand");f.r.y=0;
     f.r.reserved=1;Check(f.Validate()!=nullptr,"player kill rejects extension operand");f.r.reserved=0;
     f.s.me.dead.value=true;Check(f.Validate()!=nullptr,"already dead player cannot be killed again");}
    {Fixture f;f.r.operation=5;f.r.targetId=9;f.r.x=1;f.r.y=31901;f.r.reserved=0x40000;
     Check(f.Validate()==nullptr,"inventory drop binds slot item count and native object");
     f.r.targetId=0;f.r.y=0;Check(f.Validate()==nullptr,"gold slot zero is an explicit inventory target");
     f.r.x=0;Check(f.Validate()!=nullptr,"zero inventory drop quantity rejected");f.r.x=1;
     f.r.y=1;Check(f.Validate()!=nullptr,"gold slot item mismatch rejected");f.r.targetId=9;f.r.y=0;
     Check(f.Validate()!=nullptr,"ordinary slot cannot target gold item");f.r.y=31901;f.r.reserved=0;
     Check(f.Validate()!=nullptr,"inventory drop requires an observed object binding");}
    {Fixture f;f.r.operation=5;f.r.targetId=73;f.r.x=100000;f.r.y=60000;f.r.reserved=0x40000;
     Check(f.Validate()==nullptr,"inventory operands are count and item ID rather than signed16 coordinates");
     f.r.targetId=-1;Check(f.Validate()!=nullptr,"negative inventory slot rejected");f.r.targetId=512;
     Check(f.Validate()!=nullptr,"out of bounded inventory slot rejected");f.r.targetId=73;f.r.y=65536;
     Check(f.Validate()!=nullptr,"oversized inventory item ID rejected");}
    {Fixture f;f.r.operation=4;f.r.targetId=21;f.r.reserved=static_cast<std::int32_t>(0xFFECFFF6);Check(f.Validate()==nullptr,"isolate-ground accepts signed16 parking coordinates");}
    {Fixture f;f.r.reserved=1;Check(f.Validate()!=nullptr,"unknown request extension rejected");}
    {Fixture f;f.r.operation=3;f.r.targetId=21;Ground grounds[]={{0x10000,21,0x40000,true,false},{0x20000,22,0x50000,true,false}};MovePlan plan;
     Check(!PlanGroundMoves(f.r,grounds,2,plan)&&plan.count==1&&plan.moves[0].ground.actor==0x10000&&plan.moves[0].x==350&&plan.moves[0].y==250,"relocation changes only specified ground ID to explicit coordinates");
     f.r.operation=4;f.r.reserved=static_cast<std::int32_t>(0xFFECFFF6);
     Check(!PlanGroundMoves(f.r,grounds,2,plan)&&plan.count==2&&plan.moves[1].ground.id==22&&plan.moves[1].x==-10&&plan.moves[1].y==-20,"isolation decodes signed16 parking coordinates");
     grounds[1].pending=true;Check(PlanGroundMoves(f.r,grounds,2,plan)!=nullptr&&plan.count==0,"pending ground aborts entire batch");grounds[1].pending=false;
     grounds[1].active=false;Check(!PlanGroundMoves(f.r,grounds,2,plan)&&plan.count==1,"inactive unrelated ground remains untouched");grounds[1].active=true;
     grounds[1].id=21;Check(PlanGroundMoves(f.r,grounds,2,plan)!=nullptr&&plan.count==0,"duplicate IDs abort entire batch");grounds[1].id=22;
     f.r.targetId=23;Check(PlanGroundMoves(f.r,grounds,2,plan)!=nullptr&&plan.count==0,"missing target never parks unrelated ground");}
    {Fixture f;f.r.operation=3;f.r.targetId=7;Ground gold{0x10000,7,0,true,false};MovePlan plan;
     Check(!PlanGroundMoves(f.r,&gold,1,plan)&&plan.count==1&&plan.moves[0].ground.descriptor==0,"stable null gold descriptor is preserved by relocation");}
    {Memory m;dfl::client_state::Reader rd{};rd.context=&m;rd.read=Memory::Read;PositionBinding p{};
     m.Put(0x10000U,0x20000U);m.Put(0x20080U,0x471610U);m.Put(0x20074U,0x4715F0U);m.Put(0x100E0U,0x30000U);
     m.Put(0x30078U,0U);m.Put(0x3007CU,static_cast<unsigned char>(0));m.Put(0x30000U,0x40000U);m.Put(0x40000U,0x50000U);
     m.Put(0x50000U,0x4736A0U);m.Put(0x50004U,0x4736B0U);
     Check(ReadPositionBinding(rd,0x10000,p)&&p.controller==0x30000&&p.vector==0x40000,"verified normal vector binding accepted");
     m.Put(0x50004U,0x123456U);Check(!ReadPositionBinding(rd,0x10000,p),"foreign vector setter rejected before any call");
     m.Put(0x50004U,0x4736B0U);m.Put(0x30078U,0x60000U);m.Put(0x3007CU,static_cast<unsigned char>(1));Check(!ReadPositionBinding(rd,0x10000,p),"external provider coordinates rejected");}
    {
        struct NativePosition
        {
            std::uint32_t actor=0;int x=0,y=0,sets=0,publications=0;bool setOkay=true,publishOkay=true;
            static bool Set(void* p,std::uint32_t actor,int x,int y,bool& touched) noexcept
            {auto& s=*static_cast<NativePosition*>(p);++s.sets;if(!s.setOkay)return false;s.actor=actor;s.x=x;s.y=y;touched=true;return true;}
            static bool Publish(void* p,std::uint32_t actor) noexcept
            {auto& s=*static_cast<NativePosition*>(p);++s.publications;return s.publishOkay&&s.actor==actor&&s.x==700&&s.y==240;}
        } native;
        PlayerTeleportOperations ops{&native,&NativePosition::Set,&NativePosition::Publish};
        bool touched=false;std::size_t applied=0;
        Check(!CompletePlayerTeleport(ops,0x10000,700,240,touched,applied)&&native.publications==1&&touched&&applied==1,
            "player teleport publishes the confirmed new position before reporting complete");
        native={};native.publishOkay=false;touched=false;applied=0;
        const auto* error=CompletePlayerTeleport(ops,0x10000,700,240,touched,applied);
        Check(error&&std::strcmp(error,"native_position_publication_failed")==0&&touched&&applied==1,
            "publication failure retains evidence of the local mutation for partial status");
        native={};native.setOkay=false;touched=false;applied=0;
        Check(CompletePlayerTeleport(ops,0x10000,700,240,touched,applied)&&native.publications==0&&!touched&&applied==0,
            "failed setter never publishes a stale position");
        native={};ops.publish=nullptr;touched=false;applied=0;
        Check(CompletePlayerTeleport(ops,0x10000,700,240,touched,applied)&&native.sets==0&&!touched,
            "missing publication path rejects before changing the player");
    }
    std::printf("Client test control tests: %d failures\n",failures);return failures;
}
