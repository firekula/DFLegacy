#include <windows.h>
#include "dnf_patches.h"

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH
        && dfl::dnf_patches::IsTargetProcess())
    {
        return dfl::dnf_patches::Apply() ? TRUE : FALSE;
    }
    return TRUE;
}
