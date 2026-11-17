#include <windows.h>
#include <cstdlib>
#include <cstring>

int main(int argc, char** argv) {
    int result = 0;
    DWORD sleep = 0;
    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--result") == 0 && i + 1 < argc) {
            result = std::atoi(argv[++i]);
        } else if (std::strcmp(argv[i], "--sleep-ms") == 0 && i + 1 < argc) {
            sleep = static_cast<DWORD>(std::strtoul(argv[++i], nullptr, 10));
        }
    }
    if (sleep) Sleep(sleep);
    return result;
}
