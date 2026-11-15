#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "relay_send_queue.h"
#include "memory_patch.h"
#include <algorithm>
#include <atomic>
#include <cstring>

namespace
{
    using namespace dfl::relay_send_queue::detail;
    struct Site {std::uintptr_t Offset;const char* Hex;};
    // Original ACT1 machine bytes, independently checked against DNF.exe.
    // The first four blocks include the FIRST owner lookup and virtual call.
    // Remaining prologues end on whole instructions and contain no PC-relative
    // instructions, so their original bodies can run through a small trampoline.
    constexpr Site Sites[]{
        {0x5072DC,"8B89100400008B110FB7C3508D8588DBFFFF50FF12"},
        {0x50769C,"8B8A100400008B010FB7D3528D95F8D6FFFF52FF10"},
        {0x50790E,"8B8A100400008B110FB7C3508D8598DBFFFF50FF12"},
        {0x50858E,"8B8E100400003BCB74058B01FF5004"},
        {0x507DE0,"558BEC8B4508"},{0x38D060,"568BF18B8E64052000"},
        {0x506070,"558BEC5356"},{0x505FC0,"C681FC09000001"},
        {0x506290,"558BEC6AFF688C069900"}};
    static_assert(std::size(Sites)==SiteCount);
    unsigned Nibble(char c) noexcept{return c<='9'?static_cast<unsigned>(c-'0'):static_cast<unsigned>(c-'A'+10);}
    std::vector<unsigned char> Bytes(const char* hex)
    {
        std::vector<unsigned char> bytes;
        for(std::size_t n=0;n<std::strlen(hex);n+=2)
            bytes.push_back(static_cast<unsigned char>(Nibble(hex[n])*16+Nibble(hex[n+1])));
        return bytes;
    }
    void Branch(std::vector<unsigned char>& code,unsigned char opcode,
        std::uintptr_t address,std::uintptr_t target)
    {
        const auto relative=static_cast<std::uint32_t>(target-address-code.size()-5);
        code.push_back(opcode);
        const auto offset=code.size();code.resize(offset+4);
        std::memcpy(code.data()+offset,&relative,4);
    }
    bool Matches(std::uintptr_t address,const std::vector<unsigned char>& code) noexcept
    {return std::memcmp(reinterpret_cast<const void*>(address),code.data(),code.size())==0;}
    Gate NativeGate;
    std::atomic<bool> Activated{false};
    template<class F> auto NativeCall(F&& body) -> decltype(body())
    {return Conditional(NativeGate,Activated.load(std::memory_order_acquire),body);}
    using NativeBind=int (__thiscall*)(void*,void*);
    using NativeNoArgs=int (__thiscall*)(void*);
    using NativeConfig=int (__thiscall*)(void*,const char*,int,int);
    using NativeReconnect=char (__thiscall*)(void*);
    NativeBind BindOriginal{};
    NativeNoArgs StopOriginal{},CloseOriginal{};
    NativeConfig ConfigOriginal{};
    NativeReconnect ReconnectOriginal{};
    void* ReadCurrent(void* core)
    {return *reinterpret_cast<void**>(static_cast<unsigned char*>(core)+0x410);}
    int SendCurrent(void* relay,const void* data,unsigned size)
    {
        using Send=int (__thiscall*)(void*,const void*,unsigned);
        const auto table=*static_cast<const std::uintptr_t* const*>(relay);
        return reinterpret_cast<Send>(table[0])(relay,data,size);
    }
    int TickCurrent(void* relay)
    {
        const auto table=*static_cast<const std::uintptr_t* const*>(relay);
        return reinterpret_cast<NativeNoArgs>(table[1])(relay);
    }
    const Operations NativeOperations{ReadCurrent,SendCurrent,TickCurrent};
    int __fastcall SendHook(void* core,void*,const void* data,unsigned size)
    {return Dispatch(NativeGate,core,data,size,NativeOperations,Activated.load(std::memory_order_acquire));}
    int __fastcall TickHook(void* core,void*)
    {return Tick(NativeGate,core,NativeOperations,Activated.load(std::memory_order_acquire));}
    int __fastcall BindHook(void* core,void*,void* relay)
    {return NativeCall([&]{return BindOriginal(core,relay);});}
    int __fastcall StopHook(void* manager,void*)
    {
        // 78D060 deletes relay, clears manager+200564 and then clears core+410.
        // 905560/907DE0 only set that field; there is no thread Join under this
        // lock. A waiting producer re-reads the cleared binding in our helper.
        return NativeCall([&]{return StopOriginal(manager);});
    }
    int __fastcall ConfigHook(void* owner,void*,const char* host,int tcp,int udp)
    {return NativeCall([&]{return ConfigOriginal(owner,host,tcp,udp);});}
    int __fastcall CloseHook(void* owner,void*)
    {return NativeCall([&]{return CloseOriginal(owner);});}
    char __fastcall ReconnectHook(void* owner,void*)
    {return NativeCall([&]{return ReconnectOriginal(owner);});}
}

namespace dfl::relay_send_queue::detail
{
    int Dispatch(Gate& gate, void* core, const void* data, unsigned size, const Operations& ops, bool enabled)
    {return Conditional(gate,enabled,[&]{void* relay=ops.ReadCurrent(core);return relay?ops.Send(relay,data,size):0;});}
    int Tick(Gate& gate, void* core, const Operations& ops, bool enabled)
    {return Conditional(gate,enabled,[&]{void* relay=ops.ReadCurrent(core);return relay?ops.Tick(relay):0;});}
    std::vector<unsigned char> BuildStub(std::size_t site,std::uintptr_t address,
        std::uintptr_t image,std::uintptr_t send,std::uintptr_t tick)
    {
        if(site>=SiteCount)return {};
        const auto original=Bytes(Sites[site].Hex);
        std::vector<unsigned char> code;
        if(site<3)
        {
            // At producers 1 and 2 the stable CORE is still in EDX. No owner
            // or vtable may be read until the C++ helper owns NativeGate.
            if(site!=0)code={0x8B,0xCA}; // mov ecx,edx
            code.insert(code.end(),original.begin()+8,original.end()-2);
            Branch(code,0xE8,address,send);
        }
        else if(site==3)
        {
            code={0x8B,0xCE}; // mov ecx,esi (stable core, not relay)
            Branch(code,0xE8,address,tick);
        }
        else code=original;
        Branch(code,0xE9,address,image+Sites[site].Offset+original.size());
        return code;
    }
    bool InstallImage(std::uintptr_t image,const Targets& targets,PatchWriter write) noexcept
    {
        if(!image||!write||std::any_of(targets.begin(),targets.end(),[](auto value){return value==0;}))return false;
        try
        {
            const auto owner=Bytes("50669000205F9000305F9000405F9000505F9000605F9000E0769100705F9000805F9000");
            if(!Matches(image+0x690870,owner))return false;
            const auto dispatch=Bytes("00659000705E9000"); // A90868 secondary send/tick table
            if(!Matches(image+0x690868,dispatch))return false;
            std::array<std::vector<unsigned char>,SiteCount> before{},patch{};
            std::array<bool,SiteCount> changed{};
            for(std::size_t n=0;n<SiteCount;++n)
            {
                const auto original=Bytes(Sites[n].Hex);auto& replacement=patch[n];
                Branch(replacement,0xE9,image+Sites[n].Offset,targets[n]);
                replacement.resize(original.size(),0x90);
                const auto at=image+Sites[n].Offset;
                if(!Matches(at,original)&&!Matches(at,replacement))return false;
                const auto* bytes=reinterpret_cast<const unsigned char*>(at);
                before[n].assign(bytes,bytes+original.size());
            }
            for(std::size_t n=0;n<SiteCount;++n)
            {
                if(before[n]==patch[n])continue;
                changed[n]=true; // The writer may fail AFTER changing the site.
                if(!write(image+Sites[n].Offset,patch[n].data(),patch[n].size()))
                {
                    for(std::size_t i=n+1;i-->0;)
                        if(changed[i])write(image+Sites[i].Offset,before[i].data(),before[i].size());
                    return false;
                }
            }
            return true;
        }
        catch(...){return false;}
    }
}

namespace dfl::relay_send_queue
{
    bool Install() noexcept
    {
        if(Activated.load(std::memory_order_acquire))return true;
        const auto image=reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
        if(image!=0x00400000)return false;
        // Initialization precedes native producer threads. Keep one allocation
        // alive after any attempted patch, including uncertain rollback.
        static unsigned char* stubs=nullptr;
        if(!stubs)
        {
            auto* allocated=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
            if(!allocated)return false;
            try
            {
                for(std::size_t n=0;n<SiteCount;++n)
                {
                    const auto code=detail::BuildStub(n,reinterpret_cast<std::uintptr_t>(allocated+n*64),image,
                        reinterpret_cast<std::uintptr_t>(&SendHook),reinterpret_cast<std::uintptr_t>(&TickHook));
                    if(code.empty()||code.size()>64){VirtualFree(allocated,0,MEM_RELEASE);return false;}
                    std::memcpy(allocated+n*64,code.data(),code.size());
                }
            }
            catch(...){VirtualFree(allocated,0,MEM_RELEASE);return false;}
            DWORD old{};
            if(!VirtualProtect(allocated,4096,PAGE_EXECUTE_READ,&old)
                ||!FlushInstructionCache(GetCurrentProcess(),allocated,4096))
            {VirtualFree(allocated,0,MEM_RELEASE);return false;}
            stubs=allocated;
            BindOriginal=reinterpret_cast<NativeBind>(stubs+4*64);
            StopOriginal=reinterpret_cast<NativeNoArgs>(stubs+5*64);
            ConfigOriginal=reinterpret_cast<NativeConfig>(stubs+6*64);
            CloseOriginal=reinterpret_cast<NativeNoArgs>(stubs+7*64);
            ReconnectOriginal=reinterpret_cast<NativeReconnect>(stubs+8*64);
        }
        detail::Targets targets{};
        for(std::size_t n=0;n<4;++n)targets[n]=reinterpret_cast<std::uintptr_t>(stubs+n*64);
        targets[4]=reinterpret_cast<std::uintptr_t>(&BindHook);
        targets[5]=reinterpret_cast<std::uintptr_t>(&StopHook);
        targets[6]=reinterpret_cast<std::uintptr_t>(&ConfigHook);
        targets[7]=reinterpret_cast<std::uintptr_t>(&CloseHook);
        targets[8]=reinterpret_cast<std::uintptr_t>(&ReconnectHook);
        const bool complete=detail::InstallImage(image,targets,memory_patch::WriteBytes);
        // On uncertain rollback, retained stubs keep every already-written
        // jump valid, but ALL wrappers retain the original ungated policy.
        // Never activate a subset of the send/lifetime synchronization group.
        Activated.store(complete,std::memory_order_release);
        return complete;
    }
}
