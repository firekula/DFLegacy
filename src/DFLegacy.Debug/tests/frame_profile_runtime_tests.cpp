#include "../src/frame_profile.cpp"
#include <fstream>
#include <string>

namespace
{
    std::uint64_t FakeCounter=100000;
    std::uint64_t ProfileCounter(void*) noexcept {FakeCounter+=10000;return FakeCounter;}
}
int RunFrameProfileRuntimeTests()
{
    using namespace dfl::frame_profile;
    int failures=0;auto check=[&](bool b,const char* text){if(!b){++failures;std::printf("FAIL frame profile IO: %s\n",text);}};
    wchar_t base[MAX_PATH]{},folder[MAX_PATH]{},ini[MAX_PATH]{},output[MAX_PATH]{};
    GetTempPathW(MAX_PATH,base);swprintf_s(folder,L"%sframe-profile-%lu",base,GetCurrentProcessId());
    swprintf_s(ini,L"%sframe-profile-%lu.ini",base,GetCurrentProcessId());
    swprintf_s(output,L"%s\\dfdebug-frame-profile-%lu.json",folder,GetCurrentProcessId());
    dfl::client_state::Reader reader{};reader.counterUs=ProfileCounter;reader.pid=GetCurrentProcessId();
    Initialize(ini,folder,nullptr,reader);
    {Frame f(1);for(int i=0;i<4;++i)f.Mark();}Export();
    check(GetFileAttributesW(output)==INVALID_FILE_ATTRIBUTES,"default-off profiler does not create output");
    WritePrivateProfileStringW(L"DebugOverlay",L"ExportFrameProfile",L"1",ini);
    // Explicitly no ExportState and no process-start identity inherited from it.
    Initialize(ini,folder,nullptr,reader);
    {Frame f(1);for(int i=0;i<4;++i)f.Mark();}Export();
    std::ifstream file(output,std::ios::binary);std::string json(std::istreambuf_iterator<char>(file),{});file.close();
    check(json.find("\"schema\":\"dfl-client-frame-profile/v1\"")!=std::string::npos,"standalone profile creates a fresh output directory and captures own identity");
    check(json.find("\"unknown_foreground_frames\":1")!=std::string::npos,"unknown foreground coverage survives native JSON export");
    Sample slow;slow.beginUs=FakeCounter+10000;slow.endUs=slow.beginUs+60000;slow.tickMs=GetTickCount64();slow.scene=1;
    slow.foregroundKnown=slow.foregroundBegin=slow.foregroundEnd=true;slow.parts={0,0,0,0,60000};
    Model.Add(slow);Published=Model.value;Have=true;LastExport=0;Export();
    std::ifstream exported(output,std::ios::binary);json.assign(std::istreambuf_iterator<char>(exported),{});exported.close();
    check(json.find("\"foreground_interval_us\":null,\"interval_known\":false")!=std::string::npos,"first foreground slow event exports unknown interval rather than zero");
    if(!json.empty())
    {
        wchar_t fixture[MAX_PATH]{};
        if(GetEnvironmentVariableW(L"DFLEGACY_FRAME_PROFILE_FIXTURE",fixture,MAX_PATH))
            check(CopyFileW(output,fixture,TRUE)!=0,"native profile fixture preserves exact exported bytes");
    }
    wchar_t pipelinePath[MAX_PATH]{};
    swprintf_s(pipelinePath,L"%s\\dfdebug-frame-pipeline-%lu.json",folder,GetCurrentProcessId());
    check(PipelineEnabled(),"outer pipeline profiling follows explicit opt-in");
    Sample pipeline=slow;pipeline.beginUs+=100000;pipeline.endUs=pipeline.beginUs+60000;
    pipeline.parts={100,200,300,59000,400};
    RecordPipeline(pipeline);LastExport=0;Export();
    std::ifstream pipelineFile(pipelinePath,std::ios::binary);
    std::string pipelineJson(std::istreambuf_iterator<char>(pipelineFile),{});pipelineFile.close();
    check(pipelineJson.find("\"schema\":\"dfl-client-frame-pipeline/v1\"")!=std::string::npos,
          "outer pipeline has a separate process-bound evidence stream");
    check(pipelineJson.find("\"parts_us\":[100,200,300,59000,400]")!=std::string::npos,
          "slow memory query remains attributable outside DebugRender");
    {Frame composed(1,123,456);composed.Mark();composed.Mark();}
    check(Model.value.total.maximumParts[0]>=123&&Model.value.total.maximumParts[1]>=456,"shared core capture/navigation remain attributed in frame profiler");
    Shutdown();check(!PipelineEnabled(),"shutdown disables the optional outer producer");
    auto size=json.size();{Frame f(1);for(int i=0;i<4;++i)f.Mark();}RecordPipeline(pipeline);Export();
    std::ifstream after(output,std::ios::binary);std::string stopped(std::istreambuf_iterator<char>(after),{});after.close();
    check(stopped.size()==size&&stopped==json,"shutdown stops publication without rewriting observations");
    DeleteFileW(pipelinePath);DeleteFileW(output);RemoveDirectoryW(folder);DeleteFileW(ini);
    std::printf("Frame profile IO tests: %d failures\n",failures);return failures;
}
