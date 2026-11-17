#include "process.hpp"
#include <tlhelp32.h>
#include <string.h>
#include <cwchar>
#include <string>

namespace fs = std::filesystem;

namespace dfo {
namespace {
std::wstring ImagePath(DWORD pid) {
    const HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!process) return {};
    std::wstring path(32768, L'\0');
    DWORD size = static_cast<DWORD>(path.size());
    const BOOL ok = QueryFullProcessImageNameW(process, 0, path.data(), &size);
    CloseHandle(process);
    if (!ok) return {};
    path.resize(size);
    return path;
}

bool IsUnder(const std::wstring& path, const fs::path& root) {
    const std::wstring prefix = root.wstring() + L"\\";
    if (path.size() <= prefix.size()) return false;
    return CompareStringOrdinal(path.c_str(), static_cast<int>(prefix.size()), prefix.c_str(),
                                static_cast<int>(prefix.size()), TRUE) == CSTR_EQUAL;
}

bool IsClientName(const std::wstring& path) {
    const auto name = fs::path(path).filename().wstring();
    return _wcsnicmp(name.c_str(), L"DFLegacy", 8) == 0 || _wcsnicmp(name.c_str(), L"DNF", 3) == 0;
}
}

bool HasRunningClient(const fs::path& root, DWORD ownPid) {
    const HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return false;
    PROCESSENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    if (Process32FirstW(snapshot, &entry)) {
        do {
            if (entry.th32ProcessID == ownPid) continue;
            const auto path = ImagePath(entry.th32ProcessID);
            if (!path.empty() && IsUnder(path, root) && IsClientName(path)) {
                CloseHandle(snapshot);
                return true;
            }
        } while (Process32NextW(snapshot, &entry));
    }
    CloseHandle(snapshot);
    return false;
}
}
