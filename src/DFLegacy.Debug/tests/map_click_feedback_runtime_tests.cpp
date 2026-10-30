// White-box test build includes the runtime; the DLL compiles it normally.
#include "../src/client_map_feedback_runtime.cpp"
#include <cstdio>
#include <vector>
#include <unordered_map>

namespace
{
    using namespace dfl::client_map_feedback::detail;
    int Failures=0;
    void Check(bool okay,const char* label)
    {if(!okay){++Failures;std::fprintf(stderr,"FAIL map feedback runtime: %s\n",label);}}
    struct Fixture
    {
        unsigned allocs=0,ctors=0,creates=0,locks=0,unlocks=0,deletes=0,draws=0,gets=0,sets=0,patches=0;
        unsigned failAt=0;bool destroyedConstructed=false,drawOkay=true,restoreOkay=true,patchOkay=false,deleteOkay=true,shortPitch=false;
        std::array<unsigned char,96*44> pixels{};
        std::uint32_t src=17,dst=29;bool descriptorOkay=true;
        static std::uint32_t Allocate(void* p) noexcept
        {auto& f=*static_cast<Fixture*>(p);++f.allocs;return f.failAt==1?0:0x123456;}
        static bool Construct(void* p,std::uint32_t,const Graphics&) noexcept
        {auto& f=*static_cast<Fixture*>(p);++f.ctors;return f.failAt!=2;}
        static bool Create(void* p,std::uint32_t) noexcept
        {auto& f=*static_cast<Fixture*>(p);++f.creates;return f.failAt!=3;}
        static bool Lock(void* p,std::uint32_t,LockedPixels& out) noexcept
        {auto& f=*static_cast<Fixture*>(p);++f.locks;out={f.pixels.data(),f.shortPitch?87:96};return f.failAt!=4;}
        static bool Unlock(void* p,std::uint32_t) noexcept
        {auto& f=*static_cast<Fixture*>(p);++f.unlocks;return f.failAt!=5;}
        static bool Delete(void* p,std::uint32_t,bool constructed) noexcept
        {auto& f=*static_cast<Fixture*>(p);++f.deletes;f.destroyedConstructed=constructed;return f.deleteOkay;}
        static bool Get(void* p,const Graphics&,std::uint32_t state,std::uint32_t& out) noexcept
        {auto& f=*static_cast<Fixture*>(p);++f.gets;out=state==19?f.src:f.dst;return f.failAt!=6;}
        static bool Set(void* p,const Graphics&,std::uint32_t state,std::uint32_t value) noexcept
        {auto& f=*static_cast<Fixture*>(p);++f.sets;if(state==19)f.src=value;else f.dst=value;return f.restoreOkay;}
        static bool Draw(void* p,const Graphics&,dfl::map_click_feedback::Point point,const Descriptor& desc) noexcept
        {
            auto& f=*static_cast<Fixture*>(p);++f.draws;
            std::uint32_t image=0,encoded=0,color=0;std::int32_t x=0,y=0;
            std::memcpy(&image,desc.data(),4);std::memcpy(&encoded,desc.data()+0x10,4);
            std::memcpy(&color,desc.data()+0x1C,4);std::memcpy(&x,desc.data()+8,4);std::memcpy(&y,desc.data()+12,4);
            const auto field=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(desc.data()+0x10));
            const auto bits=(encoded-field)^0x11223344u;float scale=0;std::memcpy(&scale,&bits,4);
            f.descriptorOkay &= image==0x123456 && scale==.75f && x==-22 && y==-22 && color==0xBBCACACA
                && point.x==80 && point.y==90;
            f.src=5;f.dst=2; // Native LINEARDODGE mutates these before its draw.
            return f.drawOkay;
        }
        static bool Patch(void* p,const unsigned char*,const unsigned char*) noexcept
        {auto& fixture=*static_cast<Fixture*>(p);++fixture.patches;return fixture.patchOkay;}
        static bool ExceptionDraw(void* p,const Graphics&,dfl::map_click_feedback::Point,const Descriptor&) noexcept
        {
            auto& f=*static_cast<Fixture*>(p);++f.draws;f.src=5;f.dst=2;
            __try {RaiseException(0xE0001234,0,0,nullptr);}
            __except(EXCEPTION_EXECUTE_HANDLER){return false;}
            return true;
        }
        TextureOperations Texture() noexcept {return {this,Allocate,Construct,Create,Lock,Unlock,Delete};}
        RenderOperations Render() noexcept {return {this,Get,Set,Draw};}
    };
    int OriginalCalls=0,AfterCalls=0;bool Ordered=true;
    std::int32_t Original(void*) noexcept {++OriginalCalls;Ordered &= AfterCalls==0;return -7654321;}
    void After(void*) noexcept {++AfterCalls;Ordered &= OriginalCalls==1;}
    bool Unsupported(void*) noexcept {return false;}
    std::uint64_t Tick(void*) noexcept {return 1000;}
    bool DenyRead(void*,std::uint32_t,void*,std::size_t) noexcept {return false;}
    bool Supported(void*) noexcept {return true;}
    void ObserverEntersAndRestoresBeforeWorkerLock() noexcept
    {
        // Deterministically model the review's game observer entering after the worker's
        // first zero count, restoring its hook, then remaining in Publish/Activity teardown.
        InterlockedIncrement(&ActiveCalls);
        if(!TryAcquireSRWLockExclusive(&HookLock))return;
        if(RestoreHook(DrawHook,HookPatchContext,HookPatchOperation))InterlockedExchange(&HookPublished,0);
        ReleaseSRWLockExclusive(&HookLock);
    }
    void ResourceAppearsBeforeWorkerLock() noexcept {InterlockedExchange(&ResourcesPresent,1);}
    struct Memory
    {
        std::unordered_map<std::uint32_t,unsigned char> bytes;
        template<class T> void Put(std::uint32_t at,const T& value)
        {const auto* data=reinterpret_cast<const unsigned char*>(&value);for(std::size_t i=0;i<sizeof(value);++i)bytes[at+static_cast<std::uint32_t>(i)]=data[i];}
        static bool Read(void* context,std::uint32_t at,void* output,std::size_t size) noexcept
        {
            const auto& memory=*static_cast<Memory*>(context);auto* target=static_cast<unsigned char*>(output);
            for(std::size_t i=0;i<size;++i)
            {const auto found=memory.bytes.find(at+static_cast<std::uint32_t>(i));if(found==memory.bytes.end())return false;target[i]=found->second;}
            return true;
        }
        void U(std::uint32_t at,std::uint32_t value){Put(at,value);}
        Memory()
        {
            U(0xB5DCE8,0x100000);U(0x100FF0,0x300000);U(0x300000,0xA7CF3C);U(0x300004,0x310000);U(0x30000C,0x400000);
            Put<unsigned char>(0x310010,1);Put<unsigned char>(0x310011,1);U(0x400004,0);U(0x400008,0);
            U(0xB5D6F0,0x200000);U(0x20001C,0x220000);Put<std::uint16_t>(0x230004,23);
            U(0x400014,0x500000);U(0x400018,0x500004);U(0x500000,0x600000);
            U(0x600000,0xA8480C);U(0x600004,2);U(0x600014,23);U(0x600018,0x700000);
            Descriptor descriptor{};const std::uint32_t image=0x800000,effect=2;
            const std::int32_t imageX=-22,imageY=-23;
            std::memcpy(descriptor.data(),&image,4);std::memcpy(descriptor.data()+8,&imageX,4);std::memcpy(descriptor.data()+12,&imageY,4);
            std::memcpy(descriptor.data()+0x24,&effect,4);Put(0x70008C,descriptor);
            U(0x800000,0xA8EFE4);U(0x800008,14);U(0x800014,44);U(0x800018,44);U(0x80001C,0);U(0x800020,0);
            U(0xBEF7D4,0x11223344);
        }
    };
}

int RunMapClickFeedbackRuntimeTests()
{
    Failures=0;OriginalCalls=AfterCalls=0;Ordered=true;
    Check(DrawWithOriginal(nullptr,Original,After)==-7654321 && OriginalCalls==1 && AfterCalls==1 && Ordered,
        "original executes first exactly once and preserves full EAX");
    for(bool success:{true,false})
    {
        Fixture f;f.drawOkay=success;Descriptor source{};const auto saved=source;
        const std::array<dfl::map_click_feedback::RingState,2> rings={{{true,.75f,0xBBCACACA},{true,.75f,0xBBCACACA}}};
        unsigned draws=0;
        const bool result=DrawScoped(f.Render(),{}, {80,90},source,0x123456,0x11223344,rings,draws);
        Check(result==success && f.src==17 && f.dst==29 && f.gets==2 && f.sets==2,
            "render success/failure restores both original render states");
        Check(f.draws==(success?2u:1u) && draws==(success?2u:0u) && f.descriptorOkay,
            "native local descriptor substitutes private texture and reencodes scale");
        Check(source==saved,"shared source descriptor remains unchanged");
    }
    {
        Fixture f;Descriptor desc{};unsigned draws=0;auto ops=f.Render();ops.draw=Fixture::ExceptionDraw;
        const std::array<dfl::map_click_feedback::RingState,2> rings={{{true,.75f,0xBBCACACA},{false,0,0}}};
        Check(!DrawScoped(ops,{}, {},desc,0x123456,0x11223344,rings,draws) && f.src==17 && f.dst==29 && f.sets==2,
            "SEH caught in native draw leaf still restores both original states");
    }
    {
        Fixture f;f.restoreOkay=false;Descriptor desc{};unsigned draws=0;
        const std::array<dfl::map_click_feedback::RingState,2> rings={{{true,.75f,0xBBCACACA},{false,0,0}}};
        Check(!DrawScoped(f.Render(),{}, {80,90},desc,0x123456,0x11223344,rings,draws) && f.sets==2,
            "restore failure is reported and both restores are attempted");
    }
    {
        Fixture f;f.failAt=6;Descriptor desc{};unsigned draws=0;
        Check(!DrawScoped(f.Render(),{}, {},desc,0x123456,0x11223344,{},draws) && f.draws==0,
            "unknown render state suppresses drawing");
    }
    for(unsigned failure=0;failure<=5;++failure)
    {
        Fixture f;f.failAt=failure;f.pixels.fill(0xA5);std::uint32_t texture=99;
        const bool result=BuildTexture(f.Texture(),{},texture);
        Check(result==(failure==0) && texture==(failure==0?0x123456u:0u),"texture transaction publishes only completed object");
        Check(f.deletes==(failure>1?1u:0u),"failed texture construction cleans each owned allocation once");
        if(failure>1)Check(f.destroyedConstructed==(failure>2),"partial constructor uses raw allocation cleanup");
        if(failure==0)
        {
            const auto& expected=dfl::map_click_feedback::GetYellowPixels();bool pitchOkay=true;
            for(std::size_t row=0;row<44;++row)
            {
                pitchOkay &= std::memcmp(f.pixels.data()+row*96,expected.data()+row*44,88)==0;
                for(std::size_t x=88;x<96;++x)pitchOkay &= f.pixels[row*96+x]==0xA5;
            }
            Check(pitchOkay && f.unlocks==1,"texture row pitch and padding preserved");
        }
    }
    {
        Fixture f;f.shortPitch=true;std::uint32_t texture=0;
        Check(!BuildTexture(f.Texture(),{},texture) && texture==0 && f.unlocks==1 && f.deletes==1,
            "invalid native pitch still unlocks and deletes private texture");
        f={};f.failAt=3;f.deleteOkay=false;
        Check(!BuildTexture(f.Texture(),{},texture) && texture==0x123456,
            "failed native cleanup retains allocation ownership for unload gate");
    }
    {
        Fixture f;f.pixels.fill(0xA5);const auto before=f.pixels;
        Check(!CopyPixels({f.pixels.data(),87}) && f.pixels==before && !CopyPixels({nullptr,96}),
            "invalid locked mappings never write");
        Check(!CopyPixels({reinterpret_cast<void*>(std::numeric_limits<std::uintptr_t>::max()-50),96}),
            "mapping address overflow is rejected before a write");
    }
    {
        Fixture f;Hook hook;const std::array<unsigned char,5> patched={0xE8,1,2,3,4};
        Check(!InstallHook(hook,patched,&f,Fixture::Patch) && !hook.installed,
            "failed hook installation never publishes ownership");
        f.patchOkay=true;
        Check(InstallHook(hook,patched,&f,Fixture::Patch) && hook.installed && hook.patched==patched,
            "successful hook installation records exact patch bytes");
        f.patchOkay=false;
        Check(!RestoreHook(hook,&f,Fixture::Patch) && hook.installed,"failed hook restore retains unload-blocking ownership");
        f.patchOkay=true;
        Check(RestoreHook(hook,&f,Fixture::Patch) && !hook.installed,"successful retry releases hook ownership");
    }
    {
        Memory memory;
        Native={&memory,Memory::Read,Supported,nullptr,Tick,nullptr,7,900};
        Current.binding={7,23,0x230000,0x220000,0x300000,0x400000,900};Current.town=true;Current.capturedTick=1000;
        Descriptor descriptor{};std::uint32_t key=0;
        Check(ReadTemplate(Current.binding,descriptor,key) && key==0x11223344,"unique self marker supplies verified native 44x44 descriptor");
        memory.U(0x400018,0x500008);memory.U(0x500004,0x600000);
        Check(!ReadTemplate(Current.binding,descriptor,key),"duplicate matching player marker is rejected");
        memory.U(0x400018,0x500004);memory.U(0x800018,43);
        Check(!ReadTemplate(Current.binding,descriptor,key),"non-native ring dimensions are rejected");
        memory.U(0x800018,44);
        Feedback.Start(Current.binding,{80,90},1000);GameThread=GetCurrentThreadId();NavigationReady=true;
        InterlockedExchange(&Enabled,1);InterlockedExchange(&StopRequested,0);
        OriginalCalls=AfterCalls=0;Ordered=true;
        Check(DrawWithOriginal(reinterpret_cast<void*>(0x990000),Original,AfterDraw)==-7654321 && OriginalCalls==1 && Feedback.Active(),
            "unrelated UI root runs original without drawing or cancelling valid effect");
        constexpr unsigned char specificCall[]={0xE8,0x1D,0xFC,0xFF,0xFF};
        Check(DrawCallAddress==0x88187E && DrawTargetAddress==0x8814A0 && std::memcmp(OriginalCall,specificCall,5)==0,
            "SourceSpecificBinding: hook targets original minimap draw call and exact bytes");
        Reason("before_specific_callback");FunctionsVerified=false;
        DrawWithOriginal(reinterpret_cast<void*>(0x300000),Original,AfterDraw);
        Check(!Feedback.Active() && std::strcmp(Stats.reason,"native_signature_unavailable")==0,
            "SourceSpecificBinding: minimap self reaches protected render path after original");
        Feedback.Start(Current.binding,{80,90},1000);Reason("before_generic_root");
        DrawWithOriginal(reinterpret_cast<void*>(0x310000),Original,AfterDraw);
        Check(Feedback.Active() && std::strcmp(Stats.reason,"before_generic_root")==0,
            "SourceSpecificBinding: generic UI root is not accepted as minimap self");
        InterlockedExchange(&Enabled,0);Feedback.Reset();
    }
    {
        Fixture fixture;
        dfl::client_state::Reader reader{nullptr,DenyRead,Unsupported,nullptr,Tick,nullptr,7,900};
        dfl::client_map_feedback::Initialize(reader,L"");
        DrawHook.installed=true;DrawHook.patched={0xE8,1,2,3,4};InterlockedExchange(&HookPublished,1);
        HookPatchContext=&fixture;HookPatchOperation=Fixture::Patch;
        GameThread=GetCurrentThreadId()+1;
        Check(!dfl::client_map_feedback::Shutdown() && fixture.patches==0 && DrawHook.installed,
            "wrong-thread shutdown requests stop without patching or releasing hook ownership");
        Check(!NativePatch(nullptr,OriginalCall,DrawHook.patched.data()),"native patch leaf independently rejects wrong thread");
        dfl::client_state::Snapshot snapshot;
        dfl::client_map_feedback::ObserveFrame(snapshot,false);
        Check(fixture.patches==0,"wrong-thread Observe cannot perform deferred restoration");
        GameThread=GetCurrentThreadId();
        dfl::client_map_feedback::ObserveFrame(snapshot,false);
        Check(fixture.patches==1 && DrawHook.installed && !dfl::client_map_feedback::Shutdown(),
            "game-thread failed restore retains exact hook ownership and blocks unload");
        fixture.patchOkay=true;
        dfl::client_map_feedback::ObserveFrame(snapshot,false);
        Check(fixture.patches==2 && !DrawHook.installed && dfl::client_map_feedback::Shutdown(),
            "game-thread restore retry completes before worker can approve unload");
        HookPatchContext=nullptr;HookPatchOperation=NativePatch;
    }
    {
        Fixture fixture;const auto cleanup=fixture.Texture();
        dfl::client_state::Reader reader{nullptr,DenyRead,Unsupported,nullptr,Tick,nullptr,7,900};
        dfl::client_map_feedback::Initialize(reader,L"");
        Texture=0x123456;TextureConstructed=TextureReady=TextureWasDrawn=true;RetireObservations=0;CleanupOperations=&cleanup;
        InterlockedExchange(&ResourcesPresent,1);
        Check(!dfl::client_map_feedback::Shutdown(),"shutdown requests game-thread cleanup before restoring hook");
        dfl::client_state::Snapshot snapshot;
        dfl::client_map_feedback::ObserveFrame(snapshot,false);
        Check(fixture.deletes==0 && Texture==0x123456 && InterlockedCompareExchange(&ResourcesPresent,0,0)==1,
            "first stop observation may be same Sprite interval and does not delete");
        dfl::client_map_feedback::ObserveFrame(snapshot,false);
        Check(fixture.deletes==1 && !Texture && !InterlockedCompareExchange(&ResourcesPresent,0,0)
            && dfl::client_map_feedback::Shutdown(),"second stop observation releases owned texture before unload");
        CleanupOperations=&NativeTexture;
    }
    {
        dfl::client_state::Reader reader{nullptr,DenyRead,Unsupported,nullptr,Tick,nullptr,7,900};
        dfl::client_map_feedback::Initialize(reader,L"");
        dfl::client_state::Snapshot snapshot;snapshot.pid=7;snapshot.processStartedFileTime=900;
        dfl::client_map_feedback::ObserveFrame(snapshot,true);
        dfl::client_map_feedback::AcceptGoal(snapshot,{});
        LockedPixels mapping;std::uint32_t state=0;Descriptor descriptor;
        Check(!NativeAllocate(nullptr) && !NativeConstruct(nullptr,0x123456,{}) && !NativeCreate(nullptr,0x123456)
            && !NativeLock(nullptr,0x123456,mapping) && !NativeUnlock(nullptr,0x123456) && !NativeDestroy(nullptr,0x123456,true)
            && !NativeGet(nullptr,{},19,state) && !NativeSet(nullptr,{},19,5) && !NativeDraw(nullptr,{}, {},descriptor),
            "every absolute native leaf rejects unsupported image before call");
        InterlockedIncrement(&ActiveCalls);
        Check(!dfl::client_map_feedback::Shutdown(),"in-flight callback blocks DLL unload");
        InterlockedDecrement(&ActiveCalls);
        InterlockedExchange(&ResourcesPresent,1);
        Check(!dfl::client_map_feedback::Shutdown(),"pending game-thread resource cleanup blocks DLL unload");
        InterlockedExchange(&ResourcesPresent,0);
        Check(dfl::client_map_feedback::Shutdown(),"unsupported image never installs hook or calls absolute game functions");
    }
    {
        Fixture fixture;fixture.patchOkay=true;
        dfl::client_state::Reader reader{nullptr,DenyRead,Unsupported,nullptr,Tick,nullptr,7,900};
        dfl::client_map_feedback::Initialize(reader,L"");
        DrawHook.installed=true;DrawHook.patched={0xE8,1,2,3,4};InterlockedExchange(&HookPublished,1);
        HookPatchContext=&fixture;HookPatchOperation=Fixture::Patch;
        BeforeShutdownLock=ObserverEntersAndRestoresBeforeWorkerLock;
        const bool released=dfl::client_map_feedback::Shutdown();
        BeforeShutdownLock=nullptr;
        Check(!released && fixture.patches==1 && !DrawHook.installed && InterlockedCompareExchange(&ActiveCalls,0,0)==1,
            "P2: observer entering after preliminary zero and restoring hook still blocks terminal shutdown");
        InterlockedDecrement(&ActiveCalls);
        Check(dfl::client_map_feedback::Shutdown(),"P2: terminal shutdown succeeds after the final observer leaves");
        HookPatchContext=nullptr;HookPatchOperation=NativePatch;
        dfl::client_map_feedback::Initialize(reader,L"");
        BeforeShutdownLock=ResourceAppearsBeforeWorkerLock;
        const bool resourceReleased=dfl::client_map_feedback::Shutdown();
        BeforeShutdownLock=nullptr;
        Check(!resourceReleased && InterlockedCompareExchange(&ResourcesPresent,0,0)==1,
            "P2: resource ownership changing before lock is rechecked at terminal shutdown");
        InterlockedExchange(&ResourcesPresent,0);
        Check(dfl::client_map_feedback::Shutdown(),"P2: cleared ownership permits terminal shutdown");
    }
    if(!Failures)std::puts("PASS map feedback runtime: native seams, state restore, hook ownership, pitch, cleanup, P2 drain interleaving");
    return Failures;
}
