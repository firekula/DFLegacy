// Production worker mailbox, compiled instead of the standalone runtime source.
#include "../src/client_town_route_runtime.cpp"
#include <cstdio>
namespace
{
    bool SupportedIo(void*) noexcept {return true;}
    std::uint64_t TickIo(void*) noexcept {return 10000;}
    bool ApplyIo(const dfl::client_navigation::Request&) noexcept {return false;}
}
int RunTownRouteIoTests()
{
    using namespace dfl::client_town_route;
    int failures=0;
    auto check=[&](bool okay,const char* label){if(!okay){++failures;std::fprintf(stderr,"FAIL town route IO: %s\n",label);}};
    wchar_t folder[MAX_PATH]{},parent[MAX_PATH]{};
    if(!GetTempPathW(MAX_PATH,parent)||!GetTempFileNameW(parent,L"dfr",0,folder))return 1;
    DeleteFileW(folder);if(!CreateDirectoryW(folder,nullptr))return 1;
    dfl::client_state::Reader reader{};reader.pid=7;reader.processStartedFileTime=999;reader.supported=&SupportedIo;reader.tickMs=&TickIo;
    Initialize(nullptr,folder,reader,&ApplyIo,nullptr);CatalogAttempted=true;LastPoll=10000;
    wchar_t perId[MAX_PATH]{},locked[MAX_PATH]{},latest[MAX_PATH]{};
    swprintf_s(perId,L"%s\\dfdebug-route-7-42.result.json",folder);swprintf_s(locked,L"%s.tmp",perId);
    swprintf_s(latest,L"%s\\dfdebug-route-7.json",folder);
    const HANDLE handle=CreateFileW(locked,GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
    check(handle!=INVALID_HANDLE_VALUE,"exclusive temporary-result lock created");
    dfl::client_navigation::Request request{};request.pid=7;request.processStartedFileTime=999;request.requestId=42;
    Publish(request,"debug","arrived","position_observed",{});PumpWorker();
    check(Read!=Written,"failed terminal write is not acknowledged or discarded");
    if(handle!=INVALID_HANDLE_VALUE)CloseHandle(handle);DeleteFileW(locked);PumpWorker();
    check(Read==Written&&GetFileAttributesW(perId)!=INVALID_FILE_ATTRIBUTES,"unlock retries and persists the same terminal");
    Written=Read=0;HavePending=false;
    for(unsigned i=0;i<64;++i){request.requestId=100+i;Publish(request,"debug","moving","native_destination_set",{});}
    request.requestId=999;Publish(request,"debug","rejected","identity_mismatch",{});
    request.requestId=123;Publish(request,"debug","moving","native_destination_set",{});
    check(HavePending&&Pending.origin.requestId==999,"progress cannot overwrite a pending rejection");
    for(unsigned initialCount : {63U,64U})
    {
        Written=Read=0;HavePending=HaveDeferred=false;
        dfl::town_route::Catalog catalog;catalog.pvfSha256="io-queue-fixture";
        dfl::town_route::Area area;area.key={1,0};area.kind="normal";area.walkable={{0,0,1000,500}};catalog.areas.push_back(area);
        dfl::town_route::Observation observation{};observation.valid=observation.autoKnown=true;observation.scene=1;
        observation.pid=7;observation.started=999;observation.uid=2;observation.user=0x300000;
        observation.actor=0x100000;observation.context=0x400000;observation.map=0x200000;
        observation.sequence=observation.generation=1;observation.capturedTick=10000;
        observation.area={1,0};observation.position={100,100};observation.destination={100,100};
        std::string error;check(Controller.Start(catalog,observation,{{1,0},{500,200}},10000,error),"queue test starts a real planned route");
        Origin=request;Origin.requestId=42;Source="debug";
        for(unsigned i=0;i<initialCount;++i){request.requestId=100+i;Publish(request,"debug","moving","native_destination_set",{});}
        Inbox=Origin;std::memcpy(Inbox.magic,"BAD!",4);Inbox.requestId=999;HaveInbox=true;
        OnFrame({});
        check(!Controller.Active(),"full ring or mid-frame rejection cancels before any automatic continuation");
    }
    Shutdown();DeleteFileW(perId);DeleteFileW(latest);RemoveDirectoryW(folder);
    std::printf("town route IO tests: %d failure(s)\n",failures);return failures;
}
