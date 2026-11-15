#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <array>
#include <cstdio>
#include <cstring>
#include "../src/motion_horizon.h"

bool TestMotionHorizon()
{
    int failures=0;auto check=[&](bool ok,const char* label){if(!ok){++failures;std::printf("FAIL motion horizon: %s\n",label);}};
    const std::array<unsigned char,23> original{
        0x8D,0x57,0x1C,0x85,0xD2,0xC7,0x45,0xF8,0xF4,0x01,0,0,
        0x76,0x09,0x8B,0x03,0x85,0xC0,0x7E,0x03,0x89,0x45,0xF8};
    auto* memory=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    if(!memory)return false;
    std::memcpy(memory,original.data(),original.size());
    const auto address=reinterpret_cast<std::uintptr_t>(memory);
    check(dfl::motion_horizon::detail::ApplyAt(address),"supported original instruction sequence accepted");
    check(std::memcmp(memory,original.data(),3)!=0&&std::memcmp(memory+3,original.data()+3,20)==0,
        "repair changes only the horizon condition, preserving native arithmetic and default duration");
    check(dfl::motion_horizon::detail::ApplyAt(address),"repeated installation is idempotent");
    wchar_t fixture[MAX_PATH]{};
    if(GetEnvironmentVariableW(L"DFLEGACY_MOTION_HORIZON_PATCH",fixture,MAX_PATH))
    {
        const auto out=CreateFileW(fixture,GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
        check(out!=INVALID_HANDLE_VALUE,"actual installed bytes use a fresh fixture destination");
        if(out!=INVALID_HANDLE_VALUE){DWORD written=0;check(WriteFile(out,memory,3,&written,nullptr)&&written==3,"installed bytes exported");CloseHandle(out);}
    }
    std::memcpy(memory,original.data(),original.size());memory[22]^=1;
    const auto changed=memory[22];
    check(!dfl::motion_horizon::detail::ApplyAt(address)&&memory[0]==original[0]&&memory[22]==changed,
        "a changed neighboring instruction rejects the entire repair without writes");
    check(!dfl::motion_horizon::detail::ApplyAt(0),"unreadable/null instruction address rejected");
    VirtualFree(memory,0,MEM_RELEASE);
    std::printf("Motion horizon tests: %d failures\n",failures);return failures==0;
}
