#include "../src/native_loop_probe.h"
#include <cstdio>

bool TestNativeLoopReturnStack()
{
    using namespace dfl;using native_loop_probe::detail::ReturnStack;
    int failures=0;auto check=[&](bool ok,const char* label){if(!ok){++failures;std::printf("FAIL native loop stack: %s\n",label);}};
    ReturnStack stack;debug_api::NativeLoopSample s{};s.size=sizeof(s);s.version=1;s.pointMask=1;s.cpuMask=1;s.wallUs[0]=100;s.cpuUs[0]=10;s.tickMs=1;
    check(stack.Enter(0x12345678,s),"original native return target retained");
    for(unsigned i=1;i<8;++i)stack.Mark(i,100+i*10,10+i,true,3,true,true,2);
    std::uint32_t target=0;debug_api::NativeLoopSample out{};
    check(stack.Leave(target,out)&&target==0x12345678&&out.pointMask==255&&out.sequence==1&&out.endTickMs==2,
        "complete marker order restores native return target and complete sample");
    check(stack.Enter(0x10000,s)&&stack.Enter(0x20000,s),"nested native returns are retained");
    check(stack.Leave(target,out)&&target==0x20000&&(out.flags&1),"nested child is unmeasurable but restores its own return");
    check(stack.Leave(target,out)&&target==0x10000&&(out.flags&1),"nested parent is discarded without losing return");
    for(unsigned i=0;i<16;++i)check(stack.Enter(0x10000+i,s),"bounded return stack entry");
    check(!stack.Enter(0x99999,s),"overflow does not redirect an untracked native return");
    for(unsigned i=16;i>0;--i)check(stack.Leave(target,out)&&target==0x10000+i-1&&(out.flags&1),"overflow preserves every tracked return");
    check(stack.Depth()==0&&!stack.Leave(target,out),"empty stack remains empty");
    check(stack.Enter(0x30000,s),"sampling can resume after nested unwinding");stack.Mark(4,120,12,true,3,true,true,2);
    check(stack.Leave(target,out)&&target==0x30000&&(out.flags&2),"out of order markers never produce a valid loop");
    std::printf("Native loop return stack: %d failures\n",failures);return failures==0;
}
