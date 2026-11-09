#include <windows.h>
extern "C" __declspec(dllexport) __declspec(noinline) void __cdecl FixtureFault()
{
    *reinterpret_cast<volatile DWORD*>(1)=0x1234;
}
