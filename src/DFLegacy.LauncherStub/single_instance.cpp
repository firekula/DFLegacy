#include "single_instance.hpp"
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cwctype>
#include <string>

namespace {
HANDLE instanceMutex = nullptr;

std::wstring MutexName(const std::filesystem::path& root) {
    std::uint64_t hash = 1469598103934665603ULL;
    for (wchar_t character : root.wstring()) {
        const auto folded = static_cast<std::uint64_t>(std::towlower(character));
        hash ^= folded;
        hash *= 1099511628211ULL;
    }
    wchar_t buffer[40]{};
    std::swprintf(buffer, 40, L"Local\\DFOLauncher-%016llx", static_cast<unsigned long long>(hash));
    return buffer;
}
}

namespace dfo {
bool AcquireSingleInstance(const std::filesystem::path& root) {
    if (instanceMutex) return false;
    instanceMutex = CreateMutexW(nullptr, TRUE, MutexName(root).c_str());
    if (!instanceMutex) return false;
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        CloseHandle(instanceMutex);
        instanceMutex = nullptr;
        return false;
    }
    return true;
}

void ReleaseSingleInstance() {
    if (!instanceMutex) return;
    ReleaseMutex(instanceMutex);
    CloseHandle(instanceMutex);
    instanceMutex = nullptr;
}
}
