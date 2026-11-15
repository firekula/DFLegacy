#include "../src/client_party.h"
#include <cstdio>
#include <cstring>
#include <unordered_map>
#include <vector>

namespace
{
    struct PartyMemory
    {
        std::unordered_map<std::uint32_t,unsigned char> bytes;
        std::uint32_t index=0;
        template<class T> void Put(std::uint32_t at,T value)
        {const auto* p=reinterpret_cast<const unsigned char*>(&value);for(std::size_t i=0;i<sizeof(T);++i)bytes[at+static_cast<std::uint32_t>(i)]=p[i];}
        void U(std::uint32_t at,std::uint32_t value) {Put(at,value);}
        void Secure(std::uint32_t at,std::uint32_t value)
        {
            constexpr std::uint32_t key=0x12345678;const auto slot=index++;U(at,(at+slot)^key);U(at+4,slot);
            const auto a=0x2000000+slot*4,b=0x2100000+slot*4;U(a,value^a^key);U(b,value^b^key);
        }
        static bool Read(void* context,std::uint32_t at,void* output,std::size_t size) noexcept
        {
            auto& m=*static_cast<PartyMemory*>(context);auto* p=static_cast<unsigned char*>(output);
            for(std::size_t i=0;i<size;++i){auto it=m.bytes.find(at+static_cast<std::uint32_t>(i));if(it==m.bytes.end())return false;p[i]=it->second;}return true;
        }
        PartyMemory()
        {
            U(0xCFD458,0x1000000);U(0xCFD45C,0x12345678);U(0x1000004,1024);U(0x1000044,0x2000000);U(0x1000048,0x2100000);
            U(0x500328,0x600000);U(0x50032C,0x600004);U(0x500330,0x600004);U(0x600000,0x700000);
            U(0x500EF8,0);U(0x500EFC,0);U(0x700000,0xA7E2F4);U(0x700004,0x25);U(0x700010,0x500000);
            U(0x700008,0x800000);Put<unsigned char>(0x800010,1);U(0x700038,1);U(0x70003C,0);
            Secure(0x700058,1);Secure(0x700060,1);Secure(0x700068,0);Secure(0x700050,0);
            Secure(0x700070,0U-1000);Secure(0x700080,10000);Put<unsigned char>(0xCFD44C,1);
            U(0xCFD278,0x900000);Secure(0x900000,6000);
        }
    };
}

int RunClientPartyTests()
{
    using namespace dfl::client_party;
    int failures=0;
    auto check=[&](bool condition,const char* label) {if(!condition){++failures;std::fprintf(stderr,"FAIL party: %s\n",label);}};
    dfl::client_state::Snapshot s;
    s.pid=7;s.processStartedFileTime=999;s.sequence=20;s.generation=3;s.capturedTickMs=1000;
    s.valid=s.layoutSupported=s.haveMe=true;s.scene={true,1};s.me.uid={true,2};
    s.me.actorToken=0x100000;s.me.mapToken=0x200000;s.me.userToken=0x300000;
    s.town.present=true;s.town.contextToken=0x400000;s.town.mapToken=s.me.mapToken;
    s.town.townId=1;s.town.areaId=0;s.town.position={true,100,200,0};
    Request r;r.pid=7;r.uid=2;r.processStartedFileTime=999;r.requestId=40;r.issuedTickMs=1050;r.snapshotTickMs=1000;
    r.generation=3;r.snapshotSequence=20;r.actorToken=s.me.actorToken;r.mapToken=s.me.mapToken;
    r.userToken=s.me.userToken;r.contextToken=s.town.contextToken;r.townId=1;r.areaId=0;r.operation=1;r.targetUid=1;
    Pending pending;pending.known=true;
    check(ValidateRequest(r,s,pending,1100,0,0)==nullptr,"fresh same-area request permitted");
    check(std::strcmp(ValidateRequest(r,s,pending,1100,0,0,1050),"previous_session")==0,
          "reinitialize floor rejects old command even within freshness window");
    struct Calls
    {
        int live=0,request=0,accept=0,reject=0;bool ready=true;
        static bool Live(void* p,const Request&) noexcept {auto& c=*static_cast<Calls*>(p);++c.live;return c.ready;}
        static bool RequestCall(void* p,const Request&) noexcept {++static_cast<Calls*>(p)->request;return true;}
        static bool Accept(void* p,const Request&) noexcept {++static_cast<Calls*>(p)->accept;return true;}
        static bool Reject(void* p,const Request&) noexcept {++static_cast<Calls*>(p)->reject;return true;}
    } calls;
    Operations ops{&calls,&Calls::Live,&Calls::RequestCall,&Calls::Accept,&Calls::Reject};
    std::uint64_t consumed=0,prompt=0;
    check(Perform(r,s,pending,1100,consumed,prompt,ops)==nullptr && calls.request==1 && consumed==40,"one native request");
    check(Perform(r,s,pending,1100,consumed,prompt,ops)!=nullptr && calls.request==1,"replayed ID never sends twice");
    const auto base=r;
    const char* expected[]={"expired_request","identity_mismatch","invalid_target","invalid_operation","snapshot_mismatch","town_unavailable"};
    for(unsigned i=0;i<6;++i)
    {
        r=base;auto changed=s;r.requestId=41+i;
        if(i==0)r.issuedTickMs=1;
        if(i==1)r.generation++;
        if(i==2)r.targetUid=r.uid;
        if(i==3)r.operation=4;
        if(i==4)r.snapshotSequence=s.sequence+1;
        if(i==5)changed.scene.value=3;
        const auto* error=Perform(r,changed,pending,1100,consumed,prompt,ops);
        check(error && std::strcmp(error,expected[i])==0,"invalid command has precise rejection");
    }
    check(calls.live==1 && calls.request==1,"invalid contexts never reach native leaf");
    pending.present=pending.canReply=true;pending.windowToken=0x500000;pending.requesterUid=1;
    pending.remainingMs=5000;pending.serial=4;pending.reason="ready";
    r=base;r.requestId=50;r.operation=2;r.windowToken=pending.windowToken;r.invitationSerial=4;
    check(Perform(r,s,pending,1100,consumed,prompt,ops)==nullptr && calls.accept==1 && prompt==4,"accept consumes exact prompt");
    r.requestId++;
    check(Perform(r,s,pending,1100,consumed,prompt,ops)!=nullptr && calls.accept==1,"new ID cannot consume same prompt again");
    prompt=0;pending.peerType=1;r.requestId++;
    check(Perform(r,s,pending,1100,consumed,prompt,ops)!=nullptr && calls.accept==1,"trade prompt cannot be accepted by party API");
    pending.peerType=0;pending.remainingMs=0;r.requestId++;
    check(Perform(r,s,pending,1100,consumed,prompt,ops)!=nullptr && calls.accept==1,"expired prompt never accepted");
    pending.remainingMs=5000;pending.serial=5;r.requestId++;
    check(Perform(r,s,pending,1100,consumed,prompt,ops)!=nullptr,"reused pointer/new invitation serial rejected");
    r.invitationSerial=5;r.operation=3;r.requestId++;calls.ready=false;
    check(Perform(r,s,pending,1100,consumed,prompt,ops)!=nullptr && calls.reject==0,"live peer/context revalidation required");
    calls.ready=true;r.requestId++;
    check(Perform(r,s,pending,1100,consumed,prompt,ops)==nullptr && calls.reject==1 && prompt==5,"reject uses exactly one native action");
    PartyMemory memory;dfl::client_state::Reader reader;reader.context=&memory;reader.read=&PartyMemory::Read;
    memory.U(0xB5D6F0,0xA00000);memory.Secure(0xA00010,1);memory.U(0xA0001C,base.contextToken);
    memory.Put<std::int32_t>(base.contextToken+0x88,base.townId);memory.U(base.contextToken+0x8C,0xA10000);
    memory.Put<std::int32_t>(0xA10000,base.areaId);memory.U(0xB75A18,base.actorToken);memory.U(base.actorToken,0x9988DC);
    memory.Secure(base.actorToken+0xD4,base.uid);memory.U(base.actorToken+0xE4,base.mapToken);
    memory.Put<std::uint16_t>(base.userToken+4,static_cast<std::uint16_t>(base.uid));memory.U(base.userToken+0x10C,base.actorToken);
    memory.Put<std::int32_t>(base.userToken+0x2C,0);memory.U(0xB5DCE8,0x500000);memory.Put<std::int32_t>(0x500FC4,0);
    memory.U(0xB5DCB8,0xB00000);memory.U(0xB0000C,0xB10000);memory.U(0xB10000,0xB20000);
    memory.U(0xB20000,0xB10000);memory.U(0xB2000C,0xC00000);memory.Put<std::uint16_t>(0xC00004,1);
    memory.Put<std::int32_t>(0xC0002C,0);memory.U(0xC0010C,0xC10000);memory.U(0xC10000,0x9988DC);
    memory.Secure(0xC100D4,1);memory.U(0xC100E4,base.mapToken);
    std::uint32_t liveManager=0;
    check(ReadLiveContext(reader,base,liveManager,true)&&liveManager==0x500000,"live self and target actor bindings verified");
    memory.U(base.userToken+0xFC,0xD00000);memory.U(0xC000FC,0xD10000);
    const auto* bothParties=ReadRequestFailure(reader,base);
    check(bothParties&&std::strcmp(bothParties,"both_already_in_parties")==0,
          "retained singleton plus newly formed party explains native invite rejection");
    memory.U(0xC000FC,0);
    check(ReadRequestFailure(reader,base)==nullptr,"explicit singleton disband removes both-party rejection");
    memory.U(base.userToken+0xFC,0);memory.U(0xC000FC,0xD10000);
    check(ReadRequestFailure(reader,base)==nullptr,"unpartied requester can apply to existing party");
    memory.U(base.userToken+0xFC,0xD00000);memory.U(0xC100E4,base.mapToken+4);
    check(ReadRequestFailure(reader,base)==nullptr,"changed peer context cannot produce a stale diagnosis");
    memory.U(0xC100E4,base.mapToken);memory.bytes.erase(0xC000FC);
    check(ReadRequestFailure(reader,base)==nullptr,"unreadable membership remains unknown");
    memory.U(0xC000FC,0);
    memory.U(0xC100E4,base.mapToken+4);
    check(!ReadLiveContext(reader,base,liveManager,true),"peer from another area/map cannot receive stale command");
    memory.U(0xC100E4,base.mapToken);memory.Secure(base.actorToken+0xD4,3);
    check(!ReadLiveContext(reader,base,liveManager,true),"live changed UID rejected even with old matching snapshot");
    Pending observed;
    check(ReadPending(reader,0x500000,observed) && observed.known && observed.present && observed.canReply
          && observed.remainingMs==5000 && observed.requesterUid==1,"read original class 25 timer without native calls");
    memory.Secure(0x900000,11000);
    check(ReadPending(reader,0x500000,observed) && observed.present && !observed.canReply && observed.remainingMs==0,"native deadline clamps at zero");
    memory.Secure(0x900000,6000);memory.U(0x70003C,1);
    check(ReadPending(reader,0x500000,observed) && observed.present && !observed.canReply,"non-party peer prompt observable but cannot reply");
    memory.U(0x70003C,0);memory.U(0x500EF8,0xA00000);memory.U(0x500EFC,0xA00004);memory.U(0xA00000,0x700000);
    check(ReadPending(reader,0x500000,observed) && observed.known && !observed.present,"deferred-close window cannot be reused");
    memory.U(0x500EF8,0);memory.U(0x500EFC,0);memory.U(0x700000,0xA7E044);
    check(!ReadPending(reader,0x500000,observed),"wrong window RTTI fails closed");
    memory.U(0x50032C,0x600000);
    check(ReadPending(reader,0x500000,observed) && observed.known && !observed.present,"empty registry is known no invitation");
    struct Writer
    {
        std::uint32_t command=0;std::vector<unsigned char> body;unsigned writes=0,sends=0,failAt=0;
        static std::int32_t Begin(void* p,std::uint32_t command) noexcept {static_cast<Writer*>(p)->command=command;return 1;}
        static std::int32_t Write(void* p,const void* bytes,std::uint32_t length) noexcept
        {auto& w=*static_cast<Writer*>(p);if(++w.writes==w.failAt)return -1;const auto* b=static_cast<const unsigned char*>(bytes);w.body.insert(w.body.end(),b,b+length);return 1;}
        static std::int32_t Send(void* p) noexcept {++static_cast<Writer*>(p)->sends;return 1;}
    } writer;
    RejectionWriter encoder{&writer,&Writer::Begin,&Writer::Write,&Writer::Send};
    check(SendRejection(0x1234,encoder)&&writer.command==11&&writer.writes==4&&writer.sends==1
        &&writer.body==std::vector<unsigned char>({0x34,0x12,0,0,0,0,0,100,0}),"original ACT1 reject error100 golden body");
    writer={};writer.failAt=2;
    check(!SendRejection(1,encoder)&&writer.writes==2&&writer.sends==0,"failed builder never sends a partial reply");
    writer={};
    check(!SendRejection(65536,encoder)&&writer.command==0&&writer.sends==0,"UID cannot truncate to a different peer");
    Pending last,episode=pending;std::uint64_t next=0;AssignInvitationSerial(episode,last,next);
    check(episode.serial==1,"first observed episode gets a nonce");
    Pending gap;AssignInvitationSerial(gap,last,next);gap.known=true;AssignInvitationSerial(gap,last,next);
    episode.serial=0;AssignInvitationSerial(episode,last,next);
    check(episode.serial==1&&next==1,"unknown and empty gaps cannot renew consumed invitation");
    r=base;r.operation=2;r.windowToken=episode.windowToken;r.invitationSerial=episode.serial;
    check(ValidateRequest(r,s,episode,1100,0,1)!=nullptr,"same episode stays consumed after observation gap");
    episode.timerOffset++;AssignInvitationSerial(episode,last,next);
    check(episode.serial==2&&next==2,"new native timer episode gets a new nonce");
    std::printf("party tests: %d failure(s)\n",failures);return failures;
}
