#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <utility>
#include <vector>

#include "ijl15_config.h"

namespace
{
    bool Probe(const dfl::ijl15_config::Settings& settings, const char* base)
    {
        const std::string pay = std::string(base) + "/client/client_pay.htm";
        const std::string diamond = std::string(base) + "/client/client_diamond.htm";
        if (std::strcmp(settings.ClientPayUrl, pay.c_str()) != 0
            || std::strcmp(settings.ClientDiamondUrl, diamond.c_str()) != 0)
        {
            std::fprintf(stderr, "FAIL: unexpected client URL for base %s\n", base);
            return false;
        }
        const char* payPointer = settings.ClientPayUrl;
        const char* diamondPointer = settings.ClientDiamondUrl;
        // A second read must retain the patched pointers and their content.
        dfl::ijl15_config::Load();
        const auto& again = dfl::ijl15_config::Get();
        return payPointer == again.ClientPayUrl
            && diamondPointer == again.ClientDiamondUrl
            && std::strcmp(payPointer, pay.c_str()) == 0;
    }
}

int ClientServiceUrlProbe(int argc, char** argv)
{
    if (argc != 3 || std::strcmp(argv[1], "--client-service-url-probe") != 0)
        return -1;
    return Probe(dfl::ijl15_config::Get(), argv[2]) ? 0 : 1;
}

bool TestClientServiceUrls()
{
    char executable[MAX_PATH]{};
    if (GetModuleFileNameA(nullptr, executable, MAX_PATH) == 0)
        return false;
    const std::string executablePath(executable);
    const std::string config = executablePath.substr(0, executablePath.find_last_of("\\/"))
        + "\\Config.ini";
    std::ifstream originalFile(config, std::ios::binary);
    const bool existed = originalFile.good();
    const std::string original((std::istreambuf_iterator<char>(originalFile)), {});
    originalFile.close();
    struct Restore
    {
        std::string Path;
        std::string Content;
        bool Existed;
        ~Restore()
        {
            if (Existed)
                std::ofstream(Path, std::ios::binary | std::ios::trunc) << Content;
            else
                DeleteFileA(Path.c_str());
        }
    } restore{config, original, existed};

    constexpr char Local[] = "http://127.0.0.1:8081";
    const std::string maximum = "https://example.org/" + std::string(980, 'a'); // 1000 bytes
    const std::vector<std::pair<std::string, std::string>> cases{
        {"", Local},
        {"[Network]\r\nClientServiceUrl=\r\n", Local},
        {"[Network]\r\nClientServiceUrl=http://game.example:8081\r\n", "http://game.example:8081"},
        {"[Network]\r\nClientServiceUrl=https://game.example/api///\r\n", "https://game.example/api"},
        {"[Network]\r\nClientServiceUrl=https://[2001:db8::1]:8081\r\n", "https://[2001:db8::1]:8081"},
        {"[Network]\r\nClientServiceUrl=" + maximum + "\r\n", maximum},
        {"[Network]\r\nClientServiceUrl=" + maximum + "a\r\n", Local},
        {"[Network]\r\nClientServiceUrl=" + maximum + std::string(4000, 'a') + "\r\n", Local}
    };
    auto allCases = cases;
    for (const char* invalid : {"ftp://example.org", "https://", "https:///missing-host",
        "https://user:secret@example.org", "https://example.org?x=1", "https://example.org/#part",
        "https://example.org/%s", "https://example.org/back\\slash", "https://bad host",
        "https://example.org:70000", "javascript:alert(1)"})
        allCases.emplace_back(std::string("[Network]\r\nClientServiceUrl=") + invalid + "\r\n", Local);

    for (const auto& entry : allCases)
    {
        {
            std::ofstream file(config, std::ios::binary | std::ios::trunc);
            file << entry.first;
            if (!file.good()) return false;
        }
        std::string command = '"' + executablePath + "\" --client-service-url-probe \""
            + entry.second + '"';
        STARTUPINFOA startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};
        if (!CreateProcessA(executable, command.data(), nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process))
            return false;
        const DWORD wait = WaitForSingleObject(process.hProcess, 10000);
        DWORD exitCode = 1;
        if (wait != WAIT_OBJECT_0)
            TerminateProcess(process.hProcess, 1);
        else
            GetExitCodeProcess(process.hProcess, &exitCode);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        if (exitCode != 0)
        {
            std::fprintf(stderr, "FAIL: client URL configuration case: %.100s\n", entry.first.c_str());
            return false;
        }
    }
    std::puts("client service URL configuration tests passed (19 cases)");
    return true;
}
