#pragma once
#include <windows.h>
#include <filesystem>

namespace dfo {
bool HasRunningClient(const std::filesystem::path& root, DWORD ownPid);
}
