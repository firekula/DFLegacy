#include "../src/client_test_control_runtime.cpp"
#include <cstdio>
#include <fstream>
#include <string>
#include <unordered_map>
namespace
{
    std::uint64_t ControlTick(void*) noexcept{return 10000;}
    std::uint64_t ControlFt(void*) noexcept{return 100000000;}
    bool ControlUnsupported(void*) noexcept{return false;}
    bool ControlSupported(void*) noexcept{return true;}
    bool ControlRead(void*,std::uint32_t,void*,std::size_t) noexcept{return false;}
    struct ControlMemory
    {
        std::unordered_map<std::uint32_t,unsigned char> bytes;
        template<class T> void Put(std::uint32_t at,T v){const auto* p=reinterpret_cast<const unsigned char*>(&v);for(std::size_t i=0;i<sizeof(T);++i)bytes[at+static_cast<std::uint32_t>(i)]=p[i];}
        void Hex(std::uint32_t at,const char* hex){for(std::uint32_t i=0;hex[i*2];++i){unsigned value=0;sscanf_s(hex+i*2,"%2x",&value);Put(at+i,static_cast<unsigned char>(value));}}
        void Secure(std::uint32_t at,std::uint32_t value,std::uint32_t index)
        {constexpr std::uint32_t key=0x12345678;Put(0xCFD458U,0x80000U);Put(0xCFD45CU,key);Put(0x80004U,100U);Put(0x80044U,0x90000U);Put(0x80048U,0xA0000U);Put(at,(at+index)^key);Put(at+4,index);Put(0x90000+index*4,value^(0x90000+index*4)^key);Put(0xA0000+index*4,value^(0xA0000+index*4)^key);}
        static bool Read(void* ctx,std::uint32_t at,void* out,std::size_t n) noexcept
        {auto& m=*static_cast<ControlMemory*>(ctx);auto* p=static_cast<unsigned char*>(out);for(std::size_t i=0;i<n;++i){auto found=m.bytes.find(at+static_cast<std::uint32_t>(i));if(found==m.bytes.end())return false;p[i]=found->second;}return true;}
    };
}
int RunClientTestControlRuntimeTests()
{
    using namespace dfl::client_test_control;
    int failures=0;auto check=[&](bool b,const char* label){if(!b){++failures;std::fprintf(stderr,"FAIL test-control IO: %s\n",label);}};
    {
        ControlMemory memory;Native.context=&memory;Native.read=ControlMemory::Read;
        memory.Hex(0x4715F0,"558bec8b89e00000005de951870500");memory.Hex(0x471610,"558bec8b89e00000005de9d1850500");
        memory.Hex(0x4C9D50,"558bec8b0185c0c6818c000000017416");memory.Hex(0x4736B0,"558becd9450c8b4508d95c8104");
        memory.Hex(0x4CAC90,"558bec53568b75088b54f169578b3d58");
        check(PositionSignatures(),"original PE coordinate signatures accepted");
        memory.Put(0x4715FBU,static_cast<unsigned char>(0x90));check(!PositionSignatures(),"changed tail-jump destination rejected");memory.Put(0x4715FBU,static_cast<unsigned char>(0x51));
        memory.Put(0x4715F0U,static_cast<unsigned char>(0x90));check(!PositionSignatures(),"patched coordinate entry rejected");
        Native={};
    }
    {
        ControlMemory m;Native.context=&m;Native.read=ControlMemory::Read;Native.supported=ControlSupported;Native.tickMs=ControlTick;Native.fileTime=ControlFt;
        Command c;auto& q=c.request;q.pid=42;q.uid=77;q.processStartedFileTime=123;q.requestId=5;q.issuedTickMs=q.snapshotTickMs=10000;q.snapshotSequence=20;q.generation=7;
        q.actorToken=0x10000;q.mapToken=0x20000;q.userToken=0x30000;q.operation=1;q.targetId=77;q.sessionNonce=999;q.runId=888;
        c.lease.nonce=999;c.lease.runId=888;c.lease.uid=77;c.lease.issuedFileTime=100000000;
        dfl::client_state::Snapshot s{};s.valid=s.layoutSupported=s.haveMe=true;s.scene={true,3};s.pid=42;s.processStartedFileTime=123;s.capturedTickMs=10000;s.sequence=20;s.generation=7;
        s.me.uid={true,77};s.me.actorToken=0x10000;s.me.mapToken=0x20000;s.me.userToken=0x30000;s.me.dead={true,false};s.me.sameMap={true,true};s.me.position={true,100,200,0};s.room.stageToken=0xB0000;
        m.Put(0xB5D6F0U,0xD0000U);m.Secure(0xD0010,3,1);m.Put(0xB75A14U,0x10000U);m.Put(0x100D0U,0x111U);m.Secure(0x100D4,77,2);m.Put(0x100E4U,0x20000U);
        m.Put(0x30110U,0x10000U);m.Put(0x30004U,static_cast<std::uint16_t>(77));m.Put(0xB5DCE0U,0xE0000U);m.Put(0x2E0010U,0xB0000U);m.Put(0xB00B0U,0x20000U);
        m.Put(0x10000U,0xC0000U);m.Put(0xC0318U,0x652A00U);m.Secure(0x1142C,0,3);
        check(LiveContext(c,s),"native dungeon owner map and secure identity revalidated");
        m.Put(0x100E4U,0x21000U);check(!LiveContext(c,s),"live cross-map change rejected despite old valid snapshot");m.Put(0x100E4U,0x20000U);
        m.Put(0x30110U,0x11000U);check(!LiveContext(c,s),"live user binding replacement rejected");m.Put(0x30110U,0x10000U);
        m.Secure(0x1142C,1,3);check(!LiveContext(c,s),"live death rejected despite old alive snapshot");m.Secure(0x1142C,0,3);
        c.lease.issuedFileTime=1;check(!LiveContext(c,s),"stale live lease rejected before native calls");
        c.lease.issuedFileTime=100000000;LiveLease=c.lease;s.truncated=true;PublishedState={};LastStateCapture=0;
        CaptureTestState(s);check(PublishedState.serial&&!PublishedState.valid&&PublishedState.truncated,"fresh truncated SDK publishes explicit unusable ground state");
        check(!LiveContext(c,s),"observation of truncation never authorizes a mutation");
        Native={};
    }
    {
        ControlMemory m;Native.context=&m;Native.read=ControlMemory::Read;
        m.Put(0x20010U,0x40000U);m.Put(0x20014U,0x40004U);m.Put(0x40000U,0x50000U);
        m.Put(0x50000U,0x60000U);m.Put(0x500D0U,529U);m.Put(0x500E4U,0x20000U);m.Put(0x50128U,static_cast<unsigned char>(1));m.Put(0x50424U,100);m.Put(0x51864U,0x10000U);
        m.Secure(0x500D4,21,1);m.Secure(0x51168,4000,2);m.Secure(0x5142C,0,3);
        const std::uint32_t offsets[]={0x34,0x138,0x18C,0x314,0x318,0x328,0x32C,0x330};
        const std::uint32_t functions[]={0x4073D0,0x47F480,0x42C990,0x47FFB0,0x652A00,0x481000,0x4BCA70,0x652A20};
        for(std::size_t i=0;i<8;++i)m.Put(0x60000+offsets[i],functions[i]);
        std::uint32_t found=0;check(FindKillTarget(0x20000,21,0x10000,found)&&found==0x50000,"alive local-authority monster selected by native UID");
        m.Put(0x51864U,0x11000U);check(!FindKillTarget(0x20000,21,0x10000,found),"remote authority monster rejected");m.Put(0x51864U,0x10000U);
        m.Secure(0x5142C,1,3);check(!FindKillTarget(0x20000,21,0x10000,found),"already dead monster rejected");m.Secure(0x5142C,0,3);
        m.Put(0x60328U,0x402030U);check(!FindKillTarget(0x20000,21,0x10000,found),"wrong native HP setter rejected");m.Put(0x60328U,0x481000U);
        m.Put(0x20014U,0x40008U);m.Put(0x40004U,0x50000U);check(!FindKillTarget(0x20000,21,0x10000,found),"duplicate monster target rejected");
        Native={};
    }
    {
        ControlMemory m;Native.context=&m;Native.read=ControlMemory::Read;
        m.Put(0x20010U,0x40000U);m.Put(0x20014U,0x40004U);m.Put(0x40000U,0x50000U);
        m.Put(0x50000U,0x999574U);m.Put(0x500D0U,0x121U);m.Put(0x500E4U,0x20000U);m.Put(0x50128U,static_cast<unsigned char>(1));m.Put(0x507A5U,static_cast<unsigned char>(0));m.Put(0x507A0U,0xD0000U);m.Secure(0x500D4,7,1);
        m.Put(0x9995F4U,0x471610U);m.Put(0x9995E8U,0x4715F0U);m.Put(0x500E0U,0x60000U);m.Put(0x60078U,0U);m.Put(0x6007CU,static_cast<unsigned char>(0));
        m.Put(0x60000U,0x70000U);m.Put(0x70000U,0x71000U);m.Put(0x71000U,0x4736A0U);m.Put(0x71004U,0x4736B0U);m.Put(0x70004U,123.5F);m.Put(0x70008U,-20.0F);m.Put(0x7000CU,3.0F);
        TestState state;check(ReadGroundState(0x20000,state)&&state.count==1&&state.ground[0].ground.id==7&&state.ground[0].position.x==123.5F&&state.ground[0].position.y==-20.0F,"ground observation reads independent native coordinates");
        m.Put(0x507A0U,0U);check(ReadGroundState(0x20000,state)&&state.count==1&&state.ground[0].ground.descriptor==0,"gold ground actor with null descriptor remains observable");
        m.Put(0x20014U,0x40404U);check(!ReadGroundState(0x20000,state)&&state.count==0&&!state.valid,"ground observation refuses map vector exceeding256 objects");
        Native={};
    }
    wchar_t tmp[MAX_PATH]{},dir[MAX_PATH]{},ini[MAX_PATH]{},cmd[MAX_PATH]{},leaseFile[MAX_PATH]{},result[MAX_PATH]{};
    GetTempPathW(MAX_PATH,tmp);swprintf_s(dir,L"%scontrol-io-%lu",tmp,GetCurrentProcessId());CreateDirectoryW(dir,nullptr);
    swprintf_s(ini,L"%s\\test.ini",dir);swprintf_s(cmd,L"%s\\dfdebug-test-control-42.bin",dir);
    swprintf_s(result,L"%s\\dfdebug-test-control-42.json",dir);swprintf_s(leaseFile,L"%s\\dfdebug-test-lease-77.bin",dir);
    auto save=[&](const wchar_t* path,const void* ptr,DWORD n){auto f=CreateFileW(path,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);DWORD done=0;check(f!=INVALID_HANDLE_VALUE&&WriteFile(f,ptr,n,&done,nullptr)&&done==n,"fixture write complete");if(f!=INVALID_HANDLE_VALUE)CloseHandle(f);};
    auto text=[&](){std::ifstream f(result,std::ios::binary);return std::string(std::istreambuf_iterator<char>(f),{});};
    dfl::client_state::Reader reader{};reader.read=ControlRead;reader.supported=ControlUnsupported;reader.tickMs=ControlTick;reader.fileTime=ControlFt;reader.pid=42;reader.processStartedFileTime=123;
    dfl::client_state::Snapshot s{};s.valid=s.layoutSupported=s.haveMe=true;s.scene={true,3};s.pid=42;s.processStartedFileTime=123;s.capturedTickMs=10000;s.sequence=20;s.generation=7;
    s.me.uid={true,77};s.me.actorToken=0x10000;s.me.mapToken=0x20000;s.me.userToken=0x30000;s.me.dead={true,false};s.me.sameMap={true,true};s.me.position={true,100,200,0};
    Request r{};r.pid=42;r.uid=77;r.processStartedFileTime=123;r.requestId=5;r.issuedTickMs=r.snapshotTickMs=10000;r.snapshotSequence=20;r.generation=7;
    r.actorToken=0x10000;r.mapToken=0x20000;r.userToken=0x30000;r.operation=1;r.targetId=77;r.x=350;r.y=250;r.sessionNonce=999;r.runId=888;
    Lease l{};l.nonce=999;l.runId=888;l.uid=77;l.issuedFileTime=100000000;save(cmd,&r,sizeof(r));save(leaseFile,&l,sizeof(l));
    Initialize(ini,dir,reader);PumpWorker();OnFrame(s);PumpWorker();check(text().empty(),"default off ignores request files");Shutdown();
    WritePrivateProfileStringW(L"DebugOverlay",L"EnableTestControl",L"1",ini);
    Initialize(ini,dir,reader);PumpWorker();OnFrame(s);PumpWorker();const auto first=text();
    check(first.find("\"request_id\":5")!=std::string::npos&&first.find("unsupported_layout")!=std::string::npos,"enabled IO consumes request but rejects unsupported original image");
    check(first.find("\"server_committed\":false")!=std::string::npos&&first.find("\"applied_count\":0")!=std::string::npos,"client result cannot claim server economic commit");
    // Replacing the file with the same request must not invoke a native operation twice.
    save(cmd,&r,sizeof(r));PumpWorker();OnFrame(s);PumpWorker();check(text().find("replayed_request")!=std::string::npos,"replaced file with consumed ID reports replay");
    r.requestId=6;r.sessionNonce=1000;save(cmd,&r,sizeof(r));PumpWorker();OnFrame(s);PumpWorker();check(text().find("server_lease_unavailable")!=std::string::npos,"wrong server nonce rejected through real file IO");
    Shutdown();DeleteFileW(cmd);DeleteFileW(leaseFile);DeleteFileW(result);DeleteFileW(ini);RemoveDirectoryW(dir);
    std::printf("Client test control IO tests: %d failures\n",failures);return failures;
}
