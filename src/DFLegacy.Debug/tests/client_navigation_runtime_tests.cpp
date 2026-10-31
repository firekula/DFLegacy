// White-box compilation of the production state machine. The test target compiles this
// file instead of client_navigation_runtime.cpp; the DLL compiles the runtime normally.
#include "../src/client_navigation_runtime.cpp"
#include <unordered_map>

namespace
{
    bool UnsupportedNavigationImage(void*) noexcept { return false; }
    std::uint64_t NavigationTestTick(void*) noexcept { return 1100; }
    bool SupportedFixture(void*) noexcept { return true; }
    bool WatchNativeHit=false,HitOriginalRan=false,HitAfterRead=false,HitReadTooSoon=false;
    unsigned HitOriginalCalls=0;
    unsigned HitLeftCaptureReads=0,HitRightCaptureReads=0;
    std::uint32_t HitRelease=16;
    struct TrackingMemory
    {
        std::unordered_map<std::uint32_t,unsigned char> bytes;
        std::uint32_t index=0;
        template<class T> void Put(std::uint32_t at,T value)
        { const auto* p=reinterpret_cast<const unsigned char*>(&value);for (std::size_t i=0;i<sizeof(value);++i)bytes[at+static_cast<std::uint32_t>(i)]=p[i]; }
        void U(std::uint32_t at,std::uint32_t value) { Put(at,value); }
        template<std::size_t N> void Bytes(std::uint32_t at,const unsigned char (&data)[N])
        { for (std::uint32_t i=0;i<N;++i)bytes[at+i]=data[i]; }
        void Secure(std::uint32_t at,std::uint32_t value)
        {
            constexpr std::uint32_t key=0x12345678;const auto i=index++;U(at,(at+i)^key);U(at+4,i);
            const auto a=0x2000000+i*4,b=0x2100000+i*4;U(a,value^a^key);U(b,value^b^key);
        }
        static bool Read(void* context,std::uint32_t at,void* output,std::size_t size) noexcept
        { if(WatchNativeHit&&at==0xB5DCE8){HitAfterRead=true;if(!HitOriginalRan)HitReadTooSoon=true;}
          if(WatchNativeHit&&at==0xB55184)++HitLeftCaptureReads;
          if(WatchNativeHit&&at==0xB55188)++HitRightCaptureReads;
          auto& m=*static_cast<TrackingMemory*>(context);auto* p=static_cast<unsigned char*>(output);for(std::size_t i=0;i<size;++i){auto it=m.bytes.find(at+static_cast<std::uint32_t>(i));if(it==m.bytes.end())return false;p[i]=it->second;}return true; }
        explicit TrackingMemory(const dfl::client_navigation::Request& r)
        {
            U(0xCFD458,0x1000000);U(0xCFD45C,0x12345678);U(0x1000004,1024);U(0x1000044,0x2000000);U(0x1000048,0x2100000);
            U(0xB5D6F0,0x3000000);Secure(0x3000010,1);U(0x300001C,r.contextToken);Put(r.contextToken+0x88,r.townId);
            U(r.contextToken+0x8C,0x410000);Put(0x410000,r.areaId);U(0xB75A18,r.actorToken);U(r.actorToken,0x9988DC);
            Secure(r.actorToken+0xD4,r.uid);U(r.actorToken+0xE4,r.mapToken);U(r.userToken+0x10C,r.actorToken);
            Put<std::uint16_t>(r.userToken+4,static_cast<std::uint16_t>(r.uid));Put<std::int32_t>(r.userToken+0x2C,0);
            U(0xB5DCE8,0x500000);U(0x500FC4,0);Put<unsigned char>(0xB5F0A0,0);U(r.actorToken+0x3C8,0);
            U(0x9988DC+0x54,0x407400);U(0x9988DC+0x94,0x479EF0);U(0x9988DC+0x154,0x47A8E0);
            U(0x9988DC+0x178,0x478F70);U(0x9988DC+0x1A4,0x4795F0);
            U(r.mapToken+0xF4,0x600000);U(0x600050,0x610000);U(0x610154,0x620000);U(0x610158,0x620010);
            Put(0x620000,dfl::client_navigation::Rect{0,0,1000,500});Put(r.actorToken+0x23C,r.x);Put(r.actorToken+0x240,r.y);
            const unsigned char setter[]={0x55,0x8B,0xEC,0x8B,0x81,0xC8,0x03,0,0,0x85,0xC0,0x74,0x0A,0x8A,0x90,0xA0};
            const unsigned char point[]={0x55,0x8B,0xEC,0x8B,0x45,0x10,0x85,0xC0,0x74,0x06,0xC7,0,0,0,0,0};
            const unsigned char activate[]={0xC6,0x05,0xA0,0xF0,0xB5,0,1};
            const unsigned char update[]={0xA0,0xA0,0xF0,0xB5,0,0x84,0xC0,0x0F,0x84,0xB1,0,0,0};
            Bytes(0x478F70,setter);Bytes(0x479EF0,point);Bytes(0x79CD07,activate);Bytes(0x47B24C,update);
        }
    };
    std::int32_t SafeOriginalHit(void* self) noexcept
    {
        ++HitOriginalCalls;HitOriginalRan=true;
        auto& memory=*static_cast<TrackingMemory*>(self);
        memory.U(HitRelease==8?0xCFD1F4:0xCFD1F8,0);memory.U(HitRelease==8?0xB55184:0xB55188,777);
        return -7654321;
    }
    void SafeOriginalMinimap(void*) noexcept
    { ++HitOriginalCalls;HitOriginalRan=true; }
}
int RunClientNavigationRuntimeTests()
{
    using namespace dfl::client_navigation;
    int failures=0;
    auto check=[&](bool condition,const char* label)
    { if (!condition) { ++failures;std::fprintf(stderr,"FAIL navigation runtime: %s\n",label); } };
    // Core player navigation is enabled even with a missing/old debug setting.
    dfl::client_state::Reader ordinary{};ordinary.read=&TrackingMemory::Read;ordinary.pid=7;ordinary.processStartedFileTime=999;
    ordinary.supported=&UnsupportedNavigationImage;ordinary.tickMs=&NavigationTestTick;
    Initialize(L"C:\\missing-player-settings.ini", L"C:\\unused-player-state", ordinary);
    check(IsEnabled(), "ordinary_navigation_without_debug_or_export_state");
    Shutdown();
    for (unsigned test=0;test<3;++test)
    {
        // supported=false is unconditional: this process cannot install the hook or call game code.
        Native={nullptr,nullptr,&UnsupportedNavigationImage,nullptr,&NavigationTestTick,nullptr,7,999};
        InterlockedExchange(&Enabled,1);InterlockedExchange(&HookPublished,0);
        Installed=HitInstalled=HookAttempted=HaveInbox=HaveFile=Tracking=false;GameThread=0;
        Outbox={};GameResult={};NextSerial=0;ExportedSerial=0;ConsumedId=0;
        Current={};Current.pid=7;Current.processStartedFileTime=999;Current.sequence=20;Current.generation=3;Current.capturedTickMs=1000;
        Current.valid=Current.layoutSupported=Current.haveMe=true;Current.scene={true,1};Current.me.uid={true,2};
        Current.me.actorToken=0x100000;Current.me.mapToken=0x200000;Current.me.userToken=0x300000;
        Current.town.present=true;Current.town.contextToken=0x400000;Current.town.mapToken=0x200000;
        Current.town.townId=1;Current.town.areaId=0;Current.town.position={true,100,200,0};
        Request old;old.pid=7;old.uid=2;old.processStartedFileTime=999;old.requestId=40;old.issuedTickMs=1050;old.snapshotTickMs=1000;
        old.generation=3;old.snapshotSequence=20;old.actorToken=0x100000;old.mapToken=0x200000;old.userToken=0x300000;old.contextToken=0x400000;
        old.townId=1;old.areaId=0;old.x=150;old.y=220;
        Active=old;ActiveTick=1050;ActiveSequence=20;ActiveSource=test==2?"minimap":"debug";Tracking=true;
        auto newer=old;newer.requestId=50;
        const char* reason="expired_request";
        if (test==0) newer.issuedTickMs=1;
        if (test==1) {ConsumedId=50;reason="replayed_request";}
        if (test==2) {newer.generation=4;reason="identity_mismatch";}
        Dispatch(newer,"debug");
        check(Outbox.request.requestId==newer.requestId && std::strcmp(Outbox.status,"rejected")==0
            && std::strcmp(Outbox.reason,reason)==0,"new request has explicit terminal rejection");
        check(!Tracking,"new decision ends older result tracking without a native cancel");
        OnFrame(Current);
        check(Outbox.request.requestId==newer.requestId && std::strcmp(Outbox.status,"rejected")==0,
            "older arrival/context result cannot replace newer rejection");
        check(!Installed,"unsupported fixture never installs any hook");
        InterlockedExchange(&Enabled,0);
    }
    // Exercise tracking against fixture bytes. No Dispatch is called, the inbox is empty,
    // and HookAttempted=true prevents patching; no absolute game function is reachable.
    auto request=Active;request.requestId=100;request.generation=Current.generation;
    TrackingMemory memory(request);
    Native={&memory,&TrackingMemory::Read,&SupportedFixture,nullptr,&NavigationTestTick,nullptr,7,999};
    HookAttempted=true;HaveInbox=false;GameThread=GetCurrentThreadId();InterlockedExchange(&Enabled,1);
    Current.sequence=21;Current.capturedTickMs=1050;Current.town.directionX={true,4};Current.town.directionY={true,4};
    Active=request;ActiveSequence=20;ActiveTick=1000;Tracking=true;
    OnFrame(Current);
    check(!Tracking && std::strcmp(Outbox.status,"interrupted")==0 && std::strcmp(Outbox.reason,"native_auto_move_stopped")==0,
        "native manual stop ends tracking immediately");
    Current.sequence=22;OnFrame(Current);
    check(memory.bytes[0xB5F0A0]==0 && !Tracking,"later frames do not reactivate after native manual stop");
    Active=request;ActiveSequence=20;Tracking=true;Current.town.position={true,static_cast<float>(request.x),static_cast<float>(request.y),0};
    OnFrame(Current);
    check(!Tracking && std::strcmp(Outbox.status,"arrived")==0,"observed arrival takes priority over cleared native auto flag");
    // Exercise the production hit wrapper using an injected original. These
    // fixtures can never enter8F6C30 or any other absolute game function.
    for(const std::uint32_t release:{8U,16U})
    for(unsigned mode=0;mode<7;++mode)
    {
        Native={&memory,&TrackingMemory::Read,mode==0?&UnsupportedNavigationImage:&SupportedFixture,
            nullptr,&NavigationTestTick,nullptr,7,999};
        GameThread=GetCurrentThreadId();if(mode==2)++GameThread;
        InterlockedExchange(&Enabled,mode==1?0:1);
        WatchNativeHit=true;HitOriginalRan=HitAfterRead=HitReadTooSoon=false;HitOriginalCalls=0;
        HitRelease=release;memory.U(0xCFD1F4,0);memory.U(0xCFD1F8,0);
        const auto eventAddress=release==8?0xCFD1F4U:0xCFD1F8U;
        memory.U(eventAddress,mode==3?2:mode==6?0:release);
        if(mode==5)memory.bytes.erase(eventAddress+1);
        const auto result=HitPollWithOriginal(&memory,&SafeOriginalHit);
        check(result==-7654321&&HitOriginalCalls==1&&HitOriginalRan,"production hit wrapper always preserves the original full EAX exactly once");
        check(!HitReadTooSoon&&HitAfterRead==(release==16&&mode==4),"only supported right release attempts business reads after original traversal");
    }
    Native={&memory,&TrackingMemory::Read,&SupportedFixture,nullptr,&NavigationTestTick,nullptr,7,999};
    GameThread=GetCurrentThreadId();InterlockedExchange(&Enabled,1);
    WatchNativeHit=true;HitOriginalRan=HitAfterRead=HitReadTooSoon=false;HitOriginalCalls=0;
    memory.bytes.erase(0x500FF0); // Missing owner stops a legacy reader before any absolute leaf.
    MinimapPollWithOriginal(&memory,&SafeOriginalMinimap);
    check(HitOriginalCalls==1&&HitOriginalRan&&!HitAfterRead,"old minimap poll runs original exactly once and never attempts early goal dispatch");
    // A partial map fixture reaches the selected capture read but deliberately
    // lacks dropdown data, so no route or absolute destination leaf is reachable.
    memory.U(0x500FF0,0x510000);memory.U(0x510000,0xA7CF3C);memory.U(0x510004,0x520000);
    memory.Put<unsigned char>(0x520010,1);memory.Put<unsigned char>(0x520011,1);memory.U(0x52000C,777);
    memory.Put<unsigned char>(0xCFD200,0);memory.Put<unsigned char>(0xCFD202,0);memory.Put<unsigned char>(0xCFD20A,0);
    for(unsigned mode=0;mode<3;++mode)
    {
        memory.U(0xCFD1F4,8);memory.U(0xCFD1F8,16);memory.U(0xB55184,777);memory.U(0xB55188,777);
        HitRelease=mode==1?16:8;
        if(mode==1)memory.bytes.erase(0xCFD1F5);
        if(mode==2)memory.bytes.erase(0xCFD1F9);
        HitOriginalRan=HitAfterRead=HitReadTooSoon=false;HitOriginalCalls=HitLeftCaptureReads=HitRightCaptureReads=0;
        const auto result=HitPollWithOriginal(&memory,&SafeOriginalHit);
        check(result==-7654321&&HitOriginalCalls==1&&!HitReadTooSoon,"mixed or missing other-button fields still preserve original-first and full EAX");
        check(HitLeftCaptureReads==0&&HitRightCaptureReads==(mode==2?0U:1U),
            "production right trigger ignores left state and never reads left capture");
    }
    WatchNativeHit=false;
    // Ownership flags model retained CALLs, but the reader refuses all bytes:
    // both failure paths stop before VirtualProtect or any native instruction.
    Native={nullptr,nullptr,&UnsupportedNavigationImage,nullptr,&NavigationTestTick,nullptr,7,999};
    Installed=HitInstalled=true;
    check(!Shutdown()&&HitInstalled&&Installed,"failed right-hook restore keeps both ownership flags and refuses unload");
    HitInstalled=false;
    check(!Shutdown()&&Installed,"failed old-hook restore also refuses unload after the right hook is gone");
    Installed=false;
    InterlockedExchange(&Enabled,0);Native={};
    std::printf("navigation runtime tests: %d failure(s)\n",failures);return failures;
}
