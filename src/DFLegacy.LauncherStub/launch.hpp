#pragma once
#include <windows.h>
#include <filesystem>
#include <string>

namespace dfo {
struct LaunchResult {
    bool started;
    DWORD exitCode;
    bool earlyFailure;
};

LaunchResult LaunchClient(const std::filesystem::path& root, const std::wstring& arguments, DWORD timeoutMs);
}
