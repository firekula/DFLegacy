// Compile the actual production native guard, without invoking absolute game functions.
#include "../src/client_party_runtime.cpp"
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
    std::uint64_t PartyTestTick(void*) noexcept {return 1100;}
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
    std::printf("party runtime tests: %d failure(s)\n",failures);return failures;
}
