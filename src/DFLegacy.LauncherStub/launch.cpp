#include "launch.hpp"
#include <windows.h>

namespace dfo {
LaunchResult LaunchClient(const std::filesystem::path& root, const std::wstring& arguments, DWORD timeoutMs) {
    const auto client = root / "client";
    const auto executable = client / "DFLegacy.Launcher.exe";
    std::wstring commandLine = L"\"" + executable.wstring() + L"\"";
    if (!arguments.empty()) commandLine += L" " + arguments;

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), commandLine.data(), nullptr, nullptr, FALSE, 0, nullptr,
                        client.c_str(), &startup, &process)) {
        return {false, 0, false};
    }
    CloseHandle(process.hThread);

    const auto wait = WaitForSingleObject(process.hProcess, timeoutMs);
    DWORD exitCode = 0;
    if (wait == WAIT_OBJECT_0) GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hProcess);
    return {true, exitCode, wait == WAIT_OBJECT_0 && exitCode != 0};
}
}
