#include "../src/native_loop_profile.h"
#include <cstdio>
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <fstream>
#include <string>

int RunNativeLoopProfileTests()
{
    using namespace dfl;using namespace native_loop_profile;
    int failures=0;auto check=[&](bool ok,const char* label){if(!ok){++failures;std::printf("FAIL native loop: %s\n",label);}};
    const auto sample=[](std::uint64_t sequence,std::uint64_t wall,std::uint64_t cpu)
    {
        debug_api::NativeLoopSample s{};s.size=sizeof(s);s.version=1;s.thread=7;s.sequence=sequence;
        s.sceneBegin=s.sceneEnd=1;s.foregroundKnown=1;s.pointMask=s.cpuMask=255;s.tickMs=wall/1000;s.endTickMs=s.tickMs+7;
        for(unsigned i=0;i<8;++i){s.wallUs[i]=wall+i*1000;s.cpuUs[i]=cpu+i*100;}
        return s;
    };
    Model m;auto first=sample(1,1000000,1000);check(m.Add(first),"first complete native loop accepted");
    auto second=sample(2,3500000,2000);check(m.Add(second),"large outside-loop interval accepted while background");
    check(m.value.total.frames==2&&m.value.total.backgroundFrames==2&&m.value.total.maxBetween==2493000,
        "background stalls are recorded and outside time excludes previous native loop");
    check(m.value.eventCount==1&&m.value.events[0].interval&&m.value.events[0].cpuInterval
        &&m.value.events[0].sample.cpuUs[0]-m.value.events[0].previous.cpuUs[7]==300,
        "wall outside interval and thread CPU outside interval stay independently attributable");
    auto gap=sample(4,3510000,3000);check(m.Add(gap)&&!m.value.last.interval&&m.value.discontinuities==1,
        "a missing producer sequence never becomes measured outside-loop latency");
    auto noCpu=sample(5,3520000,4000);noCpu.cpuMask=127;
    check(m.Add(noCpu)&&!m.value.last.cpu&&!m.value.last.cpuInterval,"partial CPU data remains unknown");
    auto bad=sample(6,3530000,5000);bad.wallUs[3]=bad.wallUs[2]-1;
    check(!m.Add(bad)&&m.value.invalid==1,"backward boundary is discarded");
    bad=sample(7,3540000,6000);bad.flags=1;
    check(!m.Add(bad)&&m.value.nested==1,"nested loop measurements are discarded explicitly");
    auto busy=sample(8,3550000,7000);for(unsigned i=2;i<8;++i){busy.wallUs[i]+=2000000;busy.cpuUs[i]+=1900000;}busy.endTickMs+=2000;
    check(m.Add(busy)&&m.value.last.sample.wallUs[2]-m.value.last.sample.wallUs[1]==2001000,
        "a busy native dispatch is attributed inside its own segment");
    wchar_t temp[MAX_PATH]{},dir[MAX_PATH]{},ini[MAX_PATH]{},path[MAX_PATH]{};
    GetTempPathW(MAX_PATH,temp);swprintf_s(dir,L"%snative-loop-%lu-%llu",temp,GetCurrentProcessId(),GetTickCount64());
    swprintf_s(ini,L"%s.ini",dir);swprintf_s(path,L"%s\\dfdebug-native-loop-%lu.json",dir,GetCurrentProcessId());
    client_state::Reader reader{};reader.pid=GetCurrentProcessId();
    Initialize(ini,dir,reader);Record(first);Export();check(!Enabled()&&GetFileAttributesW(path)==INVALID_FILE_ATTRIBUTES,"default off does not write a file");
    WritePrivateProfileStringW(L"DebugOverlay",L"ExportNativeLoopProfile",L"1",ini);Initialize(ini,dir,reader);
    check(Enabled(),"standalone loop profile captures its own process identity");
    const auto base=GetTickCount64()-3000;
    first=sample(1,base*1000,1000);second=sample(2,base*1000+2500000,2000);gap=sample(4,base*1000+2510000,3000);gap.cpuMask=127;
    Record(first);Record(second);Record(gap);bad=sample(5,base*1000+2520000,4000);bad.flags=1;Record(bad);Export();
    std::ifstream file(path,std::ios::binary);const std::string json(std::istreambuf_iterator<char>(file),{});file.close();
    check(json.find("\"max_outside_us\":2493000")!=std::string::npos&&json.find("\"invalid_records\":1")!=std::string::npos,
        "actual worker JSON retains the measured outside gap and discarded nested record");
    check(json.find("\"cpu_known\":false,\"cpu_loop_us\":null")!=std::string::npos,"unknown CPU measurements are serialized as null");
    wchar_t fixture[MAX_PATH]{};
    if(GetEnvironmentVariableW(L"DFLEGACY_NATIVE_LOOP_FIXTURE",fixture,MAX_PATH))
    {
        const auto output=CreateFileW(fixture,GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
        check(output!=INVALID_HANDLE_VALUE,"native loop fixture uses a fresh explicit path");
        if(output!=INVALID_HANDLE_VALUE){DWORD written=0;check(WriteFile(output,json.data(),static_cast<DWORD>(json.size()),&written,nullptr)&&written==json.size(),"actual native JSON fixture written");CloseHandle(output);}
    }
    Shutdown();DeleteFileW(path);RemoveDirectoryW(dir);DeleteFileW(ini);
    std::printf("Native loop model: %d failures\n",failures);return failures;
}
