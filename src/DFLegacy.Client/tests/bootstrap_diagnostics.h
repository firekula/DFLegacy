#pragma once
#include <windows.h>
#include <cstdio>
#include <cstdint>
#include <type_traits>
#include <cstring>
// Fixture only: preserve the observed return and last-error values. Never log
// a command line, nonce, account, path, or any production client data.
template<class T> T ObserveFixtureAttach(const char* stage, T result) noexcept
{
    const DWORD error = GetLastError();
    std::uintptr_t value = 0;
    if constexpr (std::is_pointer_v<T>) value = reinterpret_cast<std::uintptr_t>(result);
    else value = static_cast<std::uintptr_t>(result);
    char text[160]{};
    const int size = sprintf_s(text,"%s result=%08lX last_error=%lu\r\n",stage,static_cast<unsigned long>(value),error);
    HANDLE file = CreateFileW(L"fixture-attach-status.txt",FILE_APPEND_DATA,FILE_SHARE_READ,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if (file != INVALID_HANDLE_VALUE)
    {
        DWORD written = 0; if(size>0) WriteFile(file,text,static_cast<DWORD>(size),&written,nullptr);
        if (!strcmp(stage,"entry-point") || (!strcmp(stage,"detour-attach") && value != 0))
        {
            for (std::uintptr_t address=0x10000;address<0x100000;address+=0x10000)
            {
                MEMORY_BASIC_INFORMATION info{};
                if (!VirtualQuery(reinterpret_cast<void*>(address),&info,sizeof(info))) continue;
                const int n=sprintf_s(text,"region=%08lX base=%p size=%08lX state=%08lX type=%08lX protect=%08lX\r\n",
                    static_cast<unsigned long>(address),info.BaseAddress,static_cast<unsigned long>(info.RegionSize),info.State,info.Type,info.Protect);
                if(n>0) WriteFile(file,text,static_cast<DWORD>(n),&written,nullptr);
            }
        }
        CloseHandle(file);
    }
    SetLastError(error);
    return result;
}
#define DFL_FIXTURE_OBSERVE(stage, expression) ObserveFixtureAttach(stage, expression)
