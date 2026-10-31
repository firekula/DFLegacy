#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstring>
#include <cwchar>
#include "client_paths.h"
#include "early_log_redirect.h"
#include "../DFLegacy.Ijl15/src/pe_import.h"
#ifdef FIXTURE_DLL
#include "tests/bootstrap_diagnostics.h"
#else
#define DFL_FIXTURE_OBSERVE(stage, expression) (expression)
#endif
namespace
{
    decltype(&CreateFileA) Original=nullptr;
    wchar_t RsLog[32768]{};
    HANDLE WINAPI OpenRsLog(LPCSTR name,DWORD access,DWORD sharing,LPSECURITY_ATTRIBUTES security,DWORD disposition,DWORD flags,HANDLE model)
    {
        if(name&&std::strcmp(name,"rs_log.txt")==0)
        {
            if(!RsLog[0]){SetLastError(ERROR_PATH_NOT_FOUND);return INVALID_HANDLE_VALUE;}
            return CreateFileW(RsLog,access,sharing,security,disposition,flags,model);
        }
        return Original(name,access,sharing,security,disposition,flags,model);
    }
}
namespace dfl::client_paths
{
    bool InstallEarlyRsLogRedirect() noexcept
    {
        if(Original)return true;
        const auto module=GetModuleHandleW(L"RS.dll");if(!module)return true;
        wchar_t loaded[32768]{},expected[32768]{};
        const auto length=GetModuleFileNameW(module,loaded,32768);
        if(!length||length>=32768||!BuildClientPath(L"RS.dll",expected,32768)||_wcsicmp(loaded,expected))return false;
        // Detours prepends our import before the original game's imports. The
        // loader has mapped/fixed imports but has not called RS DllMain yet.
        // No worker, module load, wait, or game initialization occurs here.
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS32*>(reinterpret_cast<const unsigned char*>(module)+dos->e_lfanew);
        auto** slot=pe_import::FindNamed(module,nt->OptionalHeader.SizeOfImage,"KERNEL32.dll","CreateFileA");
        if(!slot)return false;
        // A failed directory creation intentionally suppresses only this log;
        // never fall back to a root file, nor redirect any unrelated filename.
        DFL_FIXTURE_OBSERVE("rs-log-path",BuildLogPath(L"rs_log.txt",RsLog,32768));
        DWORD old=0;if(!DFL_FIXTURE_OBSERVE("rs-slot-writable",VirtualProtect(slot,sizeof(*slot),PAGE_READWRITE,&old)))return false;
        Original=reinterpret_cast<decltype(&CreateFileA)>(*slot);*slot=reinterpret_cast<void*>(&OpenRsLog);
        DWORD ignored=0;return DFL_FIXTURE_OBSERVE("rs-slot-restored",VirtualProtect(slot,sizeof(*slot),old,&ignored))!=FALSE;
    }
}
