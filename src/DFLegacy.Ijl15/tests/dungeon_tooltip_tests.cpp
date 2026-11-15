#include "dungeon_tooltip.h"
#include <cstdio>
#include <unordered_map>
#include <vector>

namespace
{
    struct Memory
    {
        std::unordered_map<std::uintptr_t,unsigned char> bytes;
        template<class T> void Put(std::uintptr_t at,T value)
        {const auto* p=reinterpret_cast<const unsigned char*>(&value);for(std::size_t i=0;i<sizeof(T);++i)bytes[at+i]=p[i];}
        void U(std::uintptr_t at,std::uint32_t value) {Put(at,value);}
        static bool Read(void* context,std::uintptr_t at,void* output,std::size_t size) noexcept
        {
            auto& m=*static_cast<Memory*>(context);auto* p=static_cast<unsigned char*>(output);
            for(std::size_t i=0;i<size;++i){const auto it=m.bytes.find(at+i);if(it==m.bytes.end())return false;p[i]=it->second;}return true;
        }
        Memory()
        {
            U(0xB5DCE8,0x100000);U(0x1004C8,0x200000);U(0x1004CC,0x200004);U(0x1004D0,0x200014);
            U(0x200000,0x300000);U(0x300000,0xA7EBF4);U(0x300004,0x3F);U(0x300010,0x100000);
            U(0x100EF8,0);U(0x100EFC,0);U(0x100F00,0);
        }
    };
    struct Actions
    {
        std::vector<std::uintptr_t> closed;
        static bool Close(void* context,std::uintptr_t window) noexcept
        {static_cast<Actions*>(context)->closed.push_back(window);return true;}
    };
}
bool TestDungeonTooltipCleanup()
{
    using namespace dfl::dungeon_tooltip::detail;
    int failures=0;auto check=[&](bool okay,const char* why){if(!okay){++failures;std::fprintf(stderr,"FAIL dungeon tooltip: %s\n",why);}};
    Memory memory;Reader reader{&memory,&Memory::Read};Actions actions;
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::CloseRequested
        &&actions.closed==std::vector<std::uintptr_t>{0x300000},"selector timeout requests original window close");
    actions.closed.clear();
    for(const auto states:{std::array<int,2>{1,1},{1,2},{2,2},{2,3},{2,0},{3,1}})
        check(Cleanup(states[0],states[1],0x100000,reader,&actions,&Actions::Close)==Result::NotApplicable,"other transitions preserved");
    check(actions.closed.empty(),"normal dungeon-entry cleanup is never duplicated");
    memory.U(0x100EF8,0x400000);memory.U(0x100EFC,0x400004);memory.U(0x100F00,0x400100);memory.U(0x400000,0x300000);
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::AlreadyQueued&&actions.closed.empty(),"existing deferred close is never queued twice");
    memory.U(0x400000,0x300100);
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::CloseRequested
        &&actions.closed==std::vector<std::uintptr_t>{0x300000},"unrelated deferred window is preserved");
    actions.closed.clear();memory.U(0x300000,0xA7E2F4);
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::Unavailable&&actions.closed.empty(),"foreign window type prevents all mutation");
    memory.U(0x300000,0xA7EBF4);memory.U(0x300010,0x100004);
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::Unavailable&&actions.closed.empty(),"wrong owning manager rejected");
    memory.U(0x300010,0x100000);memory.U(0x1004CC,0x200008);memory.U(0x200004,0x300000);
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::Unavailable&&actions.closed.empty(),"duplicate active entry rejected");
    memory.U(0x1004CC,0x200000);
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::NoWindows,"no hover is an ordinary empty case");
    memory.U(0x1004CC,0x200003);
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::Unavailable,"misaligned active vector fails closed");
    memory.U(0x1004CC,0x200004);memory.U(0xB5DCE8,0x100004);
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::Unavailable,"stale interface pointer rejected");
    memory.U(0xB5DCE8,0x100000);memory.U(0x100EFC,0x400104);memory.U(0x100F00,0x400104);
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::Unavailable,"unbounded deletion queue rejected");
    memory.U(0x100EFC,0x400004);memory.bytes.erase(0x400000);
    check(Cleanup(2,1,0x100000,reader,&actions,&Actions::Close)==Result::Unavailable,"unreadable deletion queue never becomes empty");
    check(actions.closed.empty(),"malformed contexts never invoke native close");
    std::printf("Dungeon tooltip tests: %d failures\n",failures);return failures==0;
}
