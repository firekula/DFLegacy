#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "relay_send_queue.h"
#include <algorithm>
#include <array>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <stdexcept>
#include <thread>
#include <vector>
#include "memory_patch.h"

namespace
{
    using namespace dfl::relay_send_queue::detail;
    bool Check(bool value, const char* message)
    {
        if (!value) std::fprintf(stderr, "FAIL: relay send queue: %s\n", message);
        return value;
    }
    struct Queue
    {
        Gate Guard;
        std::mutex Data;
        // Independent literal relay frames, without borrowing any serializer.
        const std::vector<unsigned char> A{1,0,13,0,1,0,0,0,2,0,0,0,0xA1};
        const std::vector<unsigned char> B{1,0,13,0,2,0,0,0,1,0,0,0,0xB2};
        std::vector<std::vector<unsigned char>> Pending{A}, Delivered;
        HANDLE First{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
        HANDLE Release{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
        HANDLE Attempt{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
        HANDLE Appended{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
        bool Pause{true};
        int Calls{};
        ~Queue(){CloseHandle(First);CloseHandle(Release);CloseHandle(Attempt);CloseHandle(Appended);}
    };
    int AppendPacket(void* value, const void* data, unsigned size)
    {
        auto& q=*static_cast<Queue*>(value);
        const auto* bytes=static_cast<const unsigned char*>(data);
        {std::lock_guard<std::mutex> lock(q.Data);q.Pending.emplace_back(bytes,bytes+size);}
        SetEvent(q.Appended);
        return 0x1357;
    }
    int FlushPackets(void* value)
    {
        auto& q=*static_cast<Queue*>(value);
        std::vector<std::vector<unsigned char>> snapshot;
        bool first;
        {std::lock_guard<std::mutex> lock(q.Data);snapshot=q.Pending;first=++q.Calls==1;}
        if(first&&q.Pause)
        {
            SetEvent(q.First);
            if(WaitForSingleObject(q.Release,5000)!=WAIT_OBJECT_0) return -99;
        }
        // Exactly the dangerous read/send/consume boundary, with each individual
        // fixture access synchronized so the test itself has no C++ data race.
        {std::lock_guard<std::mutex> lock(q.Data);
            q.Delivered.insert(q.Delivered.end(),snapshot.begin(),snapshot.end());
            q.Pending.erase(q.Pending.begin(),q.Pending.begin()+std::min(snapshot.size(),q.Pending.size()));}
        return 0x2468;
    }
    bool Interleaving(bool sameGate)
    {
        Queue q;int firstResult{},appendResult{},secondResult{};
        Gate other;
        std::thread first([&]{firstResult=Guarded(q.Guard,[&]{return FlushPackets(&q);});});
        const bool paused=WaitForSingleObject(q.First,2000)==WAIT_OBJECT_0;
        std::thread second([&]{SetEvent(q.Attempt);
            secondResult=Guarded(sameGate?q.Guard:other,[&]{
                appendResult=AppendPacket(&q,q.B.data(),static_cast<unsigned>(q.B.size()));
                return FlushPackets(&q);});});
        const bool attempted=WaitForSingleObject(q.Attempt,2000)==WAIT_OBJECT_0;
        const bool overlapped=WaitForSingleObject(q.Appended,200)==WAIT_OBJECT_0;
        SetEvent(q.Release);first.join();second.join();
        bool ok=Check(paused&&attempted,"two threads reached the controlled native queue boundary");
        ok&=Check(firstResult==0x2468&&secondResult==0x2468&&appendResult==0x1357,"original return values preserved");
        if(sameGate)
        {
            ok&=Check(!overlapped,"relay append waits for the whole in-flight flush");
            ok&=Check(q.Delivered==std::vector<std::vector<unsigned char>>{q.A,q.B}&&q.Pending.empty(),
                "each complete original frame delivered exactly once and in order");
        }
        else ok&=Check(overlapped,"independent test gate stays live");
        return ok;
    }
    int RecursiveFlush(void* value)
    {
        auto& q=*static_cast<Queue*>(value);
        return Guarded(q.Guard,[&]{return AppendPacket(&q,q.B.data(),static_cast<unsigned>(q.B.size()));});
    }
    int ThrowingFlush(void*) {throw std::runtime_error("controlled native callback failure");}
    struct Core
    {
        Gate Guard;
        int Old{},Replacement{};
        void* Current{&Old};
        HANDLE Locked{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
        HANDLE Release{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
        HANDLE Read{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
        HANDLE Attempt{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
        ~Core(){CloseHandle(Locked);CloseHandle(Release);CloseHandle(Read);CloseHandle(Attempt);}
    };
    void* ReadCurrent(void* value){auto& c=*static_cast<Core*>(value);void* current=c.Current;SetEvent(c.Read);return current;}
    int Send(void* value,const void*,unsigned){return ++*static_cast<int*>(value);}
    int Tick(void* value){return ++*static_cast<int*>(value);}
    const Operations Ops{ReadCurrent,Send,Tick};
    bool ReplacementBoundary(bool replacement,bool tick,bool enabled=true)
    {
        Core c;int result=-1;
        std::thread stop([&]{Guarded(c.Guard,[&]{SetEvent(c.Locked);WaitForSingleObject(c.Release,3000);
            c.Current=replacement?&c.Replacement:nullptr;return 0;});});
        bool ok=Check(WaitForSingleObject(c.Locked,2000)==WAIT_OBJECT_0,"stop owns the entire replacement boundary");
        std::thread producer([&]{SetEvent(c.Attempt);result=tick?Tick(c.Guard,&c,Ops,enabled):Dispatch(c.Guard,&c,nullptr,0,Ops,enabled);});
        ok&=Check(WaitForSingleObject(c.Attempt,2000)==WAIT_OBJECT_0,"producer entered before pointer lookup");
        const bool readEarly=WaitForSingleObject(c.Read,200)==WAIT_OBJECT_0;
        SetEvent(c.Release);stop.join();producer.join();
        if(enabled)
        {
            ok&=Check(!readEarly&&c.Old==0,"old relay pointer is never read or called during retirement");
            ok&=Check(result==(replacement?1:0)&&c.Replacement==(replacement?1:0),"lookup uses only current binding after stop/replacement");
        }
        else ok&=Check(readEarly&&c.Old==1&&result==1,"disabled patch policy never enables only part of the native gate");
        return ok;
    }
    struct Site {std::uintptr_t Offset;const char* Hex;};
    const Site Sites[]{
        {0x5072DC,"8B89100400008B110FB7C3508D8588DBFFFF50FF12"},
        {0x50769C,"8B8A100400008B010FB7D3528D95F8D6FFFF52FF10"},
        {0x50790E,"8B8A100400008B110FB7C3508D8598DBFFFF50FF12"},
        {0x50858E,"8B8E100400003BCB74058B01FF5004"},
        {0x507DE0,"558BEC8B4508"},{0x38D060,"568BF18B8E64052000"},
        {0x506070,"558BEC5356"},{0x505FC0,"C681FC09000001"},
        {0x506290,"558BEC6AFF688C069900"}};
    unsigned Nibble(char c){return c<='9'?static_cast<unsigned>(c-'0'):static_cast<unsigned>(c-'A'+10);}
    std::vector<unsigned char> Literal(const char* hex)
    {
        std::vector<unsigned char> result;
        for(std::size_t n=0;n<std::strlen(hex);n+=2)result.push_back(static_cast<unsigned char>(Nibble(hex[n])*16+Nibble(hex[n+1])));
        return result;
    }
    int Writes{},FailWrite{};bool RejectRollback{};
    bool Write(std::uintptr_t at,const void* data,std::size_t size) noexcept
    {
        ++Writes;if(RejectRollback&&Writes>FailWrite)return false;
        std::memcpy(reinterpret_cast<void*>(at),data,size);return Writes!=FailWrite;
    }
    bool PatchContracts()
    {
        std::vector<unsigned char> image(0x6A0000);
        for(const auto& site:Sites){const auto b=Literal(site.Hex);std::memcpy(image.data()+site.Offset,b.data(),b.size());}
        const auto table=Literal("50669000205F9000305F9000405F9000505F9000605F9000E0769100705F9000805F9000");
        std::memcpy(image.data()+0x690870,table.data(),table.size());
        const auto secondary=Literal("00659000705E9000");
        std::memcpy(image.data()+0x690868,secondary.data(),secondary.size());
        const auto baseline=image;const auto base=reinterpret_cast<std::uintptr_t>(image.data());
        Targets targets{};for(std::size_t n=0;n<targets.size();++n)targets[n]=base+0x1000+n*64;
        Writes=FailWrite=0;
        bool ok=Check(InstallImage(base,targets,Write),"all nine original sites install together");
        ok&=Check(Writes==9,"installer mutates exactly the nine selected native blocks");
        if(Writes==9)
        {
            for(std::size_t n=0;n<targets.size();++n)
            {
                std::uint32_t delta{};std::memcpy(&delta,image.data()+Sites[n].Offset+1,4);
                ok&=Check(image[Sites[n].Offset]==0xE9&&base+Sites[n].Offset+5+delta==targets[n],"native jump target and relative encoding");
            }
            Writes=0;ok&=Check(InstallImage(base,targets,Write)&&Writes==0,"installation is idempotent");
        }
        image=baseline;
        for(int fail=1;fail<=9;++fail)
        {
            Writes=0;FailWrite=fail;
            ok&=Check(!InstallImage(base,targets,Write)&&image==baseline,"failure after a write rolls back every touched block");
        }
        Writes=0;FailWrite=2;RejectRollback=true;
        const bool complete=InstallImage(base,targets,Write);
        ok&=Check(!complete&&image!=baseline,"uncertain rollback cannot report a complete installed group");
        ok&=ReplacementBoundary(false,false,complete);ok&=ReplacementBoundary(false,true,complete);
        image=baseline;RejectRollback=false;
        FailWrite=0;
        for(const auto& site:Sites)
        {
            image[site.Offset]^=1;Writes=0;
            ok&=Check(!InstallImage(base,targets,Write)&&Writes==0,"unknown native instruction bytes fail before any mutation");
            image=baseline;
        }
        image[0x690870]^=1;Writes=0;
        ok&=Check(!InstallImage(base,targets,Write)&&Writes==0,"unknown relay owner vtable rejected");image=baseline;
        for(auto offset:{0x690868,0x69086C})
        {
            image[offset]^=1;Writes=0;
            ok&=Check(!InstallImage(base,targets,Write)&&Writes==0,"unknown secondary send/tick vtable rejected");image=baseline;
        }
        targets[0]=0;Writes=0;
        ok&=Check(!InstallImage(base,targets,Write)&&Writes==0,"missing target rejected");
        return ok;
    }
    void* StubEntry{};void* ExpectedCore{};void* ObservedCore{};const void* ObservedData{};
    unsigned ObservedLength{},ObservedEax{},BeforeEsp{},AfterEsp{},AfterEbx{},AfterEsi{},AfterEbp{};
    void* FakeEbp{};
    int __fastcall ObserveSend(void* core,void*,const void* data,unsigned size)
    {ObservedCore=core;ObservedData=data;ObservedLength=size;return 0x13572468;}
    int __fastcall ObserveTick(void* core,void*) {ObservedCore=core;return 0x24681357;}
    __declspec(naked) void RunStub()
    {
        __asm {
            pushfd
            pushad
            mov BeforeEsp,esp
            mov ecx,ExpectedCore
            mov edx,ecx
            mov esi,ecx
            mov ebx,0CAFE1234h
            mov ebp,FakeEbp
            call StubEntry
            mov AfterEsp,esp
            mov ObservedEax,eax
            mov AfterEbx,ebx
            mov AfterEsi,esi
            mov AfterEbp,ebp
            popad
            popfd
            ret
        }
    }
    bool StubContracts()
    {
        auto* memory=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        if(!memory)return Check(false,"stub executable fixture allocation");
        std::vector<unsigned char> frame(0x4000);FakeEbp=frame.data()+0x3000;
        // This address is deliberately not an owner object. A stub must pass the
        // core through without dereferencing a cached relay before the helper.
        ExpectedCore=reinterpret_cast<void*>(0x12345000);
        bool ok=true;
        for(std::size_t n=0;n<4;++n)
        {
            DWORD old{};VirtualProtect(memory,4096,PAGE_READWRITE,&old);
            const auto base=reinterpret_cast<std::uintptr_t>(memory+2048)-Sites[n].Offset-std::strlen(Sites[n].Hex)/2;
            const auto code=BuildStub(n,reinterpret_cast<std::uintptr_t>(memory),base,
                reinterpret_cast<std::uintptr_t>(&ObserveSend),reinterpret_cast<std::uintptr_t>(&ObserveTick));
            ok&=Check(!code.empty()&&code.size()<64,"production dispatch stub exists and is bounded");
            if(code.empty())continue;
            std::memcpy(memory,code.data(),code.size());memory[2048]=0xC3;
            if(!VirtualProtect(memory,4096,PAGE_EXECUTE_READ,&old)||!FlushInstructionCache(GetCurrentProcess(),memory,4096))
            {ok&=Check(false,"stub execute protection");continue;}
            StubEntry=memory;ObservedCore=nullptr;ObservedData=nullptr;ObservedLength=0;
            RunStub();
            ok&=Check(ObservedCore==ExpectedCore&&BeforeEsp==AfterEsp&&AfterEbx==0xCAFE1234
                &&AfterEsi==reinterpret_cast<unsigned>(ExpectedCore)&&AfterEbp==reinterpret_cast<unsigned>(FakeEbp),
                "real x86 dispatch stub preserves callee registers and stack and never dereferences owner outside helper");
            if(n<3)
            {
                const unsigned offsets[]{0x2478,0x2908,0x2468};
                ok&=Check(ObservedData==static_cast<unsigned char*>(FakeEbp)-offsets[n]
                    &&ObservedLength==0x1234&&ObservedEax==0x13572468,"native original frame pointer/length/return ABI");
            }
            else ok&=Check(ObservedEax==0x24681357,"tick return ABI");
        }
        VirtualFree(memory,0,MEM_RELEASE);return ok;
    }
}

bool TestRelaySendQueue()
{
    bool ok=Interleaving(true);
    ok&=Interleaving(false);
    Queue q;q.Pause=false;
    ok&=ReplacementBoundary(false,false);ok&=ReplacementBoundary(true,false);
    ok&=ReplacementBoundary(false,true);ok&=ReplacementBoundary(true,true);
    ok&=ReplacementBoundary(false,false,false);ok&=ReplacementBoundary(false,true,false);
    ok&=Check(Guarded(q.Guard,[&]{return RecursiveFlush(&q);})==0x1357,"same-thread native callback can reenter");
    int (*volatile throwing)(void*)=ThrowingFlush;
    bool threw=false;
    try {Guarded(q.Guard,[&]{return throwing(&q);});}
    catch(const std::runtime_error&){threw=true;}
    ok&=Check(threw,"exception propagated");
    ok&=Check(Guarded(q.Guard,[&]{return FlushPackets(&q);})==0x2468,"exception releases the relay gate");
    ok&=PatchContracts();ok&=StubContracts();
    return ok;
}
