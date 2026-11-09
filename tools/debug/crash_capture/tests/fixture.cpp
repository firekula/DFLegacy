#include <windows.h>
#include <cstdio>
#include <cstring>

__declspec(noinline) void AccessFault()
{
    *reinterpret_cast<volatile int*>(0x123) = 42;
}

int main(int argc, char** argv)
{
    std::puts("fixture-ready");
    std::fflush(stdout);
    Sleep(6000);
    if (argc == 1 || !strcmp(argv[1], "handled") || !strcmp(argv[1], "unhandled"))
    {
        __try
        {
            AccessFault();
        }
        __except (argc > 1 && !strcmp(argv[1], "unhandled") ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER)
        {
            std::puts("fixture-handled-first-chance");
            std::fflush(stdout);
        }
    }
    else if (!strcmp(argv[1], "rotate"))
    {
        for (unsigned i=0;i<18;++i) {
            __try { AccessFault(); }
            __except(EXCEPTION_EXECUTE_HANDLER) {}
            Sleep(1100);
        }
    }
    Sleep(2000);
    std::puts("fixture-finished-normally");
    return 0;
}
