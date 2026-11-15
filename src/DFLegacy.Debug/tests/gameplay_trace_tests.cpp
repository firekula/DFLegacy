#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
namespace
{
    std::uint64_t TraceClock=0;
    void (*TraceClockAction)()=nullptr;
    ULONGLONG TestTraceTick() noexcept
    {
        if(!TraceClock)return GetTickCount64();
        const auto result=TraceClock;TraceClock+=10;
        const auto action=TraceClockAction;TraceClockAction=nullptr;
        if(action)action();
        return result;
    }
}
#define GetTickCount64 TestTraceTick
#include "../src/gameplay_trace.cpp"
#undef GetTickCount64
#include <fstream>
#include <string>
int RunGameplayTraceTests()
{
    using namespace dfl::gameplay_trace;
    int failures=0;auto check=[&](bool b,const char* s){if(!b){++failures;std::printf("FAIL gameplay trace: %s\n",s);}};
    wchar_t temp[MAX_PATH]{},dir[MAX_PATH]{},ini[MAX_PATH]{},path[MAX_PATH]{};
    GetTempPathW(MAX_PATH,temp);swprintf_s(dir,L"%sgameplay-trace-%lu",temp,GetCurrentProcessId());
    swprintf_s(ini,L"%s.ini",dir);swprintf_s(path,L"%s\\dfdebug-gameplay-trace-%lu.json",dir,GetCurrentProcessId());
    dfl::client_state::Reader reader{};reader.pid=GetCurrentProcessId();reader.processStartedFileTime=123;
    reader.counterUs=[](void*) noexcept->std::uint64_t{return 12345;};
    const unsigned char packet[]={0,4,0,0x11,0x22,0x33,0x44,9};
    Initialize(ini,dir,reader);Observe(0x100000,packet,sizeof(packet),true);Export();
    check(!Enabled()&&GetFileAttributesW(path)==INVALID_FILE_ATTRIBUTES,"default off has no output or observation");
    WritePrivateProfileStringW(L"DebugOverlay",L"ExportGameplayTrace",L"1",ini);Initialize(ini,dir,reader);
    Observe(0x100000,packet,6,false);Export();
    std::ifstream emptyFile(path,std::ios::binary);std::string emptyJson(std::istreambuf_iterator<char>(emptyFile),{});emptyFile.close();
    check(emptyJson.find("\"serial\":0,")!=std::string::npos&&emptyJson.find("\"invalid_records\":1")!=std::string::npos,
          "all-invalid session exports diagnostics even before any accepted message");
    Initialize(ini,dir,reader);
    AcquireSRWLockExclusive(&Lock);Observe(0x100000,packet,sizeof(packet),true);ReleaseSRWLockExclusive(&Lock);Export();
    std::ifstream emptyDropFile(path,std::ios::binary);emptyJson.assign(std::istreambuf_iterator<char>(emptyDropFile),{});emptyDropFile.close();
    check(emptyJson.find("\"serial\":0,")!=std::string::npos&&emptyJson.find("\"lock_drops\":1")!=std::string::npos,
          "all-dropped session exports diagnostics even before any accepted message");
    Initialize(ini,dir,reader);
    Observe(0x100000,packet,sizeof(packet),true);Export();
    std::ifstream f(path,std::ios::binary);std::string json(std::istreambuf_iterator<char>(f),{});f.close();
    check(json.find("\"hex\":\"0004001122334409\"")!=std::string::npos,"original provider result bytes retained exactly");
    check(json.find("\"provider_buffer_match\":true")!=std::string::npos,"provider buffer comparison retained without asserting a branch event");
    Observe(0x100000,packet,6,false);check(Serial==1&&Invalid==1,"short input never becomes a valid message");
    LastExport=0;Export();
    std::ifstream invalidFile(path,std::ios::binary);json.assign(std::istreambuf_iterator<char>(invalidFile),{});invalidFile.close();
    check(json.find("\"invalid_records\":1")!=std::string::npos,"a final invalid record remains observable without a later message");
    AcquireSRWLockExclusive(&Lock);Observe(0x100000,packet,sizeof(packet),true);ReleaseSRWLockExclusive(&Lock);
    LastExport=0;Export();
    std::ifstream droppedFile(path,std::ios::binary);json.assign(std::istreambuf_iterator<char>(droppedFile),{});droppedFile.close();
    check(json.find("\"lock_drops\":1")!=std::string::npos,"a final lock drop remains observable without a later message");
    for(int i=0;i<70;++i)Observe(0x100000,packet,sizeof(packet),false);
    LastExport=0;Export();
    std::ifstream bounded(path,std::ios::binary);json.assign(std::istreambuf_iterator<char>(bounded),{});bounded.close();
    check(Count==64&&json.find("\"events\":[{\"serial\":8,")!=std::string::npos
        &&json.find("\"serial\":71,")!=std::string::npos&&Overwritten==7,"fixed ring exports exact bounded serial range in order");
    Shutdown();Observe(0x100000,packet,sizeof(packet),true);check(Serial==71,"shutdown prevents new observations");
    Initialize(ini,dir,reader);
    dfl::debug_api::MovementGate gate{sizeof(gate),1,0x100000,0x200000,0x300000,0x400000,16,529,0,273,3,0,1};
    ObserveGate(gate);LastExport=0;Export();
    std::ifstream gateFile(path,std::ios::binary);json.assign(std::istreambuf_iterator<char>(gateFile),{});gateFile.close();
    check(json.find("\"event_kind\":\"gp4_movement_entered\"")!=std::string::npos
        &&json.find("\"actor_token\":3145728")!=std::string::npos&&json.find("\"movement_flags\":16")!=std::string::npos,
        "actual native GP4 gate event is distinct from CRC-passed receipt and retains exact target binding");
    Initialize(ini,dir,reader);TraceClock=10000;
    TraceClockAction=[]()
    {
        const unsigned char fresh[]={0,6,0,0,0,0,0};
        Observe(0x100000,fresh,sizeof(fresh),false);
    };
    Export();TraceClock=0;
    std::ifstream clockFile(path,std::ios::binary);json.assign(std::istreambuf_iterator<char>(clockFile),{});clockFile.close();
    const auto exportedAt=json.find("\"exported_tick_ms\":");const auto eventAt=json.find("\"tick_ms\":");
    check(exportedAt!=std::string::npos&&eventAt!=std::string::npos
        &&std::stoull(json.substr(exportedAt+19))>=std::stoull(json.substr(eventAt+10)),
        "an event arriving after the throttle clock never exceeds the exported snapshot clock");
    wchar_t fixture[MAX_PATH]{};
    if(GetEnvironmentVariableW(L"DFLEGACY_GAMEPLAY_TRACE_FIXTURE",fixture,MAX_PATH))
    {
        const auto output=CreateFileW(fixture,GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
        check(output!=INVALID_HANDLE_VALUE,"integration fixture uses a fresh explicit destination");
        if(output!=INVALID_HANDLE_VALUE)
        {
            DWORD written=0;check(WriteFile(output,json.data(),static_cast<DWORD>(json.size()),&written,nullptr)
                &&written==json.size(),"integration fixture retains actual producer JSON");CloseHandle(output);
        }
    }
    reader.processStartedFileTime=0;Initialize(ini,dir,reader);
    check(Enabled()&&Native.processStartedFileTime!=0,"standalone trace captures its own process identity without ExportState");
    Shutdown();
    DeleteFileW(path);RemoveDirectoryW(dir);DeleteFileW(ini);
    std::printf("Gameplay trace tests: %d failures\n",failures);return failures;
}
