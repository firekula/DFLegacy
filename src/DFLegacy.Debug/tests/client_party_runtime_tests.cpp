// Compile the actual production native guard, without invoking absolute game functions.
#include "../src/client_party_runtime.cpp"
#include <fstream>
#include <unordered_map>

namespace
{
    struct PartyTransportMemory
    {
        std::unordered_map<std::uint32_t,unsigned char> bytes;
        template<class T> void Put(std::uint32_t at,T value)
        {const auto* p=reinterpret_cast<const unsigned char*>(&value);for(std::size_t i=0;i<sizeof(T);++i)bytes[at+static_cast<std::uint32_t>(i)]=p[i];}
        static bool Read(void* context,std::uint32_t at,void* output,std::size_t size) noexcept
        {
            auto& m=*static_cast<PartyTransportMemory*>(context);auto* p=static_cast<unsigned char*>(output);
            for(std::size_t i=0;i<size;++i){const auto found=m.bytes.find(at+static_cast<std::uint32_t>(i));if(found==m.bytes.end())return false;p[i]=found->second;}return true;
        }
    };
    bool UnsupportedPartyImage(void*) noexcept {return false;}
    std::uint64_t PartyClock=1100;
    std::uint64_t PartyTestTick(void*) noexcept {return PartyClock;}

    int CheckPartyExportIo()
    {
        using namespace dfl::client_party;
        int failures=0;auto check=[&](bool okay,const char* label)
        {if(!okay){++failures;std::fprintf(stderr,"FAIL party IO: %s\n",label);}};
        wchar_t temporary[MAX_PATH]{},directory[MAX_PATH]{},settings[MAX_PATH]{},state[MAX_PATH]{},result[MAX_PATH]{},locked[MAX_PATH]{};
        GetTempPathW(MAX_PATH,temporary);swprintf_s(directory,L"%sparty-io-%lu",temporary,GetCurrentProcessId());
        check(CreateDirectoryW(directory,nullptr)!=0,"create private IO test directory");
        swprintf_s(settings,L"%s\\settings.ini",directory);swprintf_s(state,L"%s\\dfdebug-party-71.json",directory);
        swprintf_s(result,L"%s\\dfdebug-party-71.result.json",directory);
        WritePrivateProfileStringW(L"DebugOverlay",L"EnablePartyCommands",L"1",settings);
        PartyTransportMemory memory;dfl::client_state::Reader reader{};reader.context=&memory;reader.read=&PartyTransportMemory::Read;
        reader.supported=&UnsupportedPartyImage;reader.tickMs=&PartyTestTick;reader.pid=71;reader.processStartedFileTime=999;
        PartyClock=1000;Initialize(settings,directory,reader);
        auto contents=[](const wchar_t* path)
        {std::ifstream input(path,std::ios::binary);return std::string(std::istreambuf_iterator<char>(input),{});};
        auto stamp=[&](const wchar_t* path)
        {
            const auto file=CreateFileW(path,FILE_WRITE_ATTRIBUTES,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
            const FILETIME marker{0xD53E8000,0x019DB1DE}; // fixed old timestamp, independent of real clock granularity
            check(file!=INVALID_HANDLE_VALUE&&SetFileTime(file,nullptr,nullptr,&marker),"mark successful file for rewrite detection");
            if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);
        };
        auto marked=[](const wchar_t* path)
        {
            WIN32_FILE_ATTRIBUTE_DATA data{};
            return GetFileAttributesExW(path,GetFileExInfoStandard,&data)
                &&data.ftLastWriteTime.dwLowDateTime==0xD53E8000&&data.ftLastWriteTime.dwHighDateTime==0x019DB1DE;
        };
        Publication p{};p.binding.pid=71;p.binding.processStartedFileTime=999;p.binding.uid=7;p.binding.generation=3;
        p.binding.actorToken=0x10000;p.binding.mapToken=0x20000;p.binding.userToken=0x30000;p.binding.contextToken=0x40000;
        p.binding.townId=1;p.binding.areaId=1;p.available=true;p.reason="ready";
        auto send=[&](std::uint64_t tick)
        {PartyClock=tick;p.tick=tick;++p.serial;++p.binding.snapshotSequence;p.binding.snapshotTickMs=tick-1;Export(p);};
        send(1000);check(contents(state).find("\"tick_ms\":1000")!=std::string::npos,"initial state publishes complete JSON");
        const auto first=contents(state);stamp(state);send(1050);
        check(marked(state)&&contents(state)==first,"50ms transient tick/sequence changes do not rewrite unchanged state");
        send(1199);check(marked(state)&&contents(state)==first,"199ms unchanged state remains the successful prior JSON");
        send(1200);check(!marked(state)&&contents(state).find("\"tick_ms\":1200")!=std::string::npos,"200ms state heartbeat contains actual latest tick");
        p.pending.present=true;p.pending.known=true;p.pending.windowToken=0x50000;p.pending.serial=11;p.pending.requesterUid=9;
        p.pending.canReply=true;p.pending.reason="ready";p.pending.remainingMs=10000;
        send(1201);check(contents(state).find("\"serial\":11")!=std::string::npos,"new invitation publishes before heartbeat");
        const auto invitation=contents(state);stamp(state);p.pending.remainingMs=9950;send(1251);
        check(marked(state)&&contents(state)==invitation,"remaining_ms alone does not force a write");
        p.pending.canReply=false;p.pending.reason="invitation_consumed";send(1252);
        check(contents(state).find("\"can_reply\":false")!=std::string::npos,"invitation reply capability changes publish immediately");
        p.pending={};p.available=false;p.reason="town_unavailable";p.binding.townId=-1;p.binding.areaId=-1;send(1253);
        check(contents(state).find("\"pending\":null")!=std::string::npos&&contents(state).find("town_unavailable")!=std::string::npos,
              "closed invitation and scene availability publish immediately");
        p.binding.generation=4;p.binding.actorToken=0x60000;send(1254);
        check(contents(state).find("\"generation\":4")!=std::string::npos&&contents(state).find("0x00060000")!=std::string::npos,
              "identity generation and actor change publish immediately");
        auto changed=[&](const char* label)
        {stamp(state);send(p.tick+1);check(!marked(state),label);};
        p.binding.uid=8;changed("wire identity change is semantic");
        p.binding.mapToken=0x70000;changed("map token change is semantic");
        p.binding.userToken=0x80000;changed("user token change is semantic");
        p.binding.contextToken=0x90000;changed("context token change is semantic");
        p.binding.areaId=2;changed("area identity change is semantic");
        p.consumedRequest=41;changed("consumed command identity change is semantic");
        p.pending.present=true;p.pending.windowToken=0x50000;p.pending.serial=12;p.pending.requesterUid=9;
        changed("new prompt serial is semantic");
        p.pending.windowToken=0x50004;changed("prompt window identity change is semantic");
        p.pending.requesterUid=10;changed("prompt requester identity change is semantic");
        p.pending.peerType=1;changed("prompt peer type change is semantic");
        p.haveResult=true;p.result.request=p.binding;p.result.request.requestId=42;p.result.request.operation=1;
        p.result.request.targetUid=9;p.result.tick=1254;p.result.status="dispatched";p.result.reason="native_request_sent";
        send(1300);const auto originalResult=contents(result);stamp(result);
        check(originalResult.find("\"request_id\":42")!=std::string::npos&&originalResult.find("\"tick_ms\":1254")!=std::string::npos,
              "new decision preserves its original result tick");
        send(1350);check(marked(result)&&contents(result)==originalResult,"identical successful result is not rewritten at 50ms");
        send(1500);check(marked(result)&&contents(result)==originalResult&&contents(state).find("\"tick_ms\":1500")!=std::string::npos,
              "state heartbeat never rewrites old result or changes result tick");
        p.result.reason="same_id_new_reason";send(1501);
        check(!marked(result)&&contents(result).find("same_id_new_reason")!=std::string::npos
                &&contents(state).find("\"tick_ms\":1501")!=std::string::npos,
              "same ID and result tick with changed reason is a new result");
        swprintf_s(locked,L"%s.tmp",result);const auto resultLock=CreateFileW(locked,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
        check(resultLock!=INVALID_HANDLE_VALUE,"acquire real exclusive result temporary-file lock");
        p.result.request.requestId=43;p.result.tick=1502;p.result.reason="retry_result";p.reason="new_state_while_result_locked";send(1502);
        const auto stateWhileLocked=contents(state);stamp(state);
        check(stateWhileLocked.find("new_state_while_result_locked")!=std::string::npos&&contents(result).find("\"request_id\":42")!=std::string::npos,
              "failed result write cannot prevent independent new state publication");
        send(1552);check(marked(state)&&contents(state)==stateWhileLocked,"failed old result does not force duplicate successful state writes");
        if(resultLock!=INVALID_HANDLE_VALUE)CloseHandle(resultLock);send(1602);
        check(contents(result).find("\"request_id\":43")!=std::string::npos&&contents(result).find("\"tick_ms\":1502")!=std::string::npos,
              "unlock retries the exact failed decision without refreshing result tick");
        stamp(result);send(1652);check(marked(result),"retried result is cached only after successful write");
        swprintf_s(locked,L"%s.tmp",state);const auto stateLock=CreateFileW(locked,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
        check(stateLock!=INVALID_HANDLE_VALUE,"acquire real exclusive state temporary-file lock");
        p.reason="retry_state";p.result.request.requestId=44;p.result.tick=1653;p.result.reason="independent_result";send(1653);
        check(contents(result).find("\"request_id\":44")!=std::string::npos&&contents(state)==stateWhileLocked,
              "state failure does not stop new result from succeeding");
        if(stateLock!=INVALID_HANDLE_VALUE)CloseHandle(stateLock);stamp(result);send(1703);
        check(contents(state).find("retry_state")!=std::string::npos&&marked(result),"failed state retries independently of already successful result");
        stamp(state);stamp(result);Initialize(settings,directory,reader);send(1753);
        check(!marked(state)&&!marked(result),"Initialize clears both successful export caches");
        auto worker=[&](std::uint64_t tick,bool publish)
        {PartyClock=tick;if(publish){p.tick=tick;++p.serial;++p.binding.snapshotSequence;p.binding.snapshotTickMs=tick-1;Published=p;}PumpWorker();};
        stamp(state);worker(1803,true);check(marked(state),"production worker suppresses unchanged intermediate publication");
        worker(1953,true);check(contents(state).find("\"tick_ms\":1953")!=std::string::npos,"production worker delivers heartbeat from a new publication");
        swprintf_s(locked,L"%s.tmp",result);const auto workerLock=CreateFileW(locked,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
        check(workerLock!=INVALID_HANDLE_VALUE,"lock real result for worker retry");
        p.result.request.requestId=45;p.result.tick=2003;worker(2003,true);stamp(state);
        check(contents(result).find("\"request_id\":44")!=std::string::npos,"worker retains previous result until failed replacement succeeds");
        if(workerLock!=INVALID_HANDLE_VALUE)CloseHandle(workerLock);worker(2053,false);
        check(contents(result).find("\"request_id\":45")!=std::string::npos&&contents(result).find("\"tick_ms\":2003")!=std::string::npos
                &&marked(state),"worker retries same failed publication without a new game frame or duplicate state write");
        worker(2253,false);check(marked(state)&&contents(state).find("\"tick_ms\":2003")!=std::string::npos,
              "stopped game publications cannot be retimestamped by worker heartbeat");
        Shutdown();DeleteFileW(state);DeleteFileW(result);DeleteFileW(settings);RemoveDirectoryW(directory);Native={};PartyClock=1100;
        std::printf("party derived IO tests: %d failure(s)\n",failures);return failures;
    }
}
int RunClientPartyRuntimeTests()
{
    using namespace dfl::client_party;
    int failures=0;auto check=[&](bool okay,const char* label){if(!okay){++failures;std::fprintf(stderr,"FAIL party runtime: %s\n",label);}};
    PartyTransportMemory memory;constexpr std::uint32_t socket=0x1000000;
    memory.Put<std::uint32_t>(0xBF0A9C,socket);memory.Put<unsigned char>(socket+4,1);memory.Put<unsigned char>(socket+8,1);
    memory.Put<unsigned char>(socket+0x15E1E8,0);
    for(const auto offset:{0x15E1C0U,0x15E1C4U,0x15E1C8U,0x15E1CCU})memory.Put<std::uint32_t>(socket+offset,0);
    Native.context=&memory;Native.read=&PartyTransportMemory::Read;Native.supported=&UnsupportedPartyImage;
    std::uint32_t observed=0;
    check(Socket(observed)&&observed==socket,"drained connected native transport");
    memory.Put<unsigned char>(socket+0x15E1E8,1);
    check(!Socket(observed),"original send-block flag prevents builder mutation");
    memory.Put<unsigned char>(socket+0x15E1E8,0);memory.Put<std::uint32_t>(socket+0x15E1C0,19);
    check(!Socket(observed),"pending ring bytes prevent header overwrite");
    memory.Put<std::uint32_t>(socket+0x15E1C4,19);
    check(Socket(observed),"drained nonzero ring indices supported");
    memory.Put<std::uint32_t>(socket+0x15E1C0,0xAF000-20);memory.Put<std::uint32_t>(socket+0x15E1C4,0xAF000-20);
    check(!Socket(observed),"wrapping native header or payload is not allowed");
    memory.Put<std::uint32_t>(socket+0x15E1C0,0xAF000-21);memory.Put<std::uint32_t>(socket+0x15E1C4,0xAF000-21);
    check(Socket(observed),"exact contiguous maximum body room");
    Request request;
    check(!Layout()&&!Live(nullptr,request),"unsupported executable cannot reach an absolute game function");
    InterlockedExchange(&Enabled,1);GameThread=GetCurrentThreadId()+1;HaveInbox=true;
    OnFrame();check(HaveInbox,"foreign thread never consumes command inbox");
    Shutdown();check(!IsEnabled(),"shutdown disables worker and frame dispatch");
    wchar_t temp[MAX_PATH]{},settings[MAX_PATH]{};GetTempPathW(MAX_PATH,temp);
    check(GetTempFileNameW(temp,L"pty",0,settings)!=0,"create own lifecycle settings");
    WritePrivateProfileStringW(L"DebugOverlay",L"EnablePartyCommands",L"1",settings);
    Native.pid=7;Native.processStartedFileTime=999;Native.tickMs=&PartyTestTick;
    ConsumedId=42;ConsumedPrompt=3;NextPrompt=5;PreviousPrompt.present=true;PreviousPrompt.serial=3;
    const auto reader=Native;Initialize(settings,temp,reader);
    check(IsEnabled()&&SessionFloor==1100&&ConsumedId==42&&ConsumedPrompt==3&&NextPrompt==5&&PreviousPrompt.serial==3,
          "same-process reinitialize retains consumed invitation and applies session floor");
    auto fresh=reader;fresh.processStartedFileTime++;
    Shutdown();Initialize(settings,temp,fresh);
    check(ConsumedId==0&&ConsumedPrompt==0&&NextPrompt==0&&!PreviousPrompt.present,"different process identity resets lifecycle");
    Shutdown();Initialize(nullptr,temp,fresh);check(!IsEnabled(),"party commands default disabled");
    DeleteFileW(settings);
    HaveInbox=false;GameThread=0;Native={};
    failures+=CheckPartyExportIo();
    std::printf("party runtime tests: %d failure(s)\n",failures);return failures;
}
