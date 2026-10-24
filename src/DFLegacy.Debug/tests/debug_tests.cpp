#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdio>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>
#include "../include/debug_api.h"
#include "../src/debug_model.h"
#include "../../DFLegacy.Ijl15/src/client_debug_state.h"

namespace
{
    int Failures = 0;
    std::vector<std::wstring> Lines;
    std::vector<const char*> RawLines;
    std::vector<std::string> ExpectedLines;
    void Check(bool value, const char* message)
    {
        if (!value) { ++Failures; std::fprintf(stderr, "FAIL: %s\n", message); }
    }
    void __stdcall Draw(int, int, std::uint32_t, const char* gbk)
    {
        wchar_t value[512]{};
        MultiByteToWideChar(936, 0, gbk, -1, value, 512);
        Lines.emplace_back(value);
        RawLines.push_back(gbk);
        ExpectedLines.emplace_back(gbk);
    }
    bool Contains(const wchar_t* text)
    {
        for (const auto& line : Lines) if (line.find(text) != std::wstring::npos) return true;
        return false;
    }
    std::uint64_t Now()
    {
        FILETIME time{}; GetSystemTimeAsFileTime(&time);
        return (static_cast<std::uint64_t>(time.dwHighDateTime) << 32) | time.dwLowDateTime;
    }
}

int main(int argc, char** argv)
{
    using namespace dfl;
    if (argc != 2) return 2;
    std::uint32_t manager[32]{}, valuesA[4]{}, valuesB[4]{}, wrapper[2]{};
    constexpr std::uint32_t key = 0x12345678;
    manager[1] = 4;
    manager[0x44 / 4] = reinterpret_cast<std::uintptr_t>(valuesA);
    manager[0x48 / 4] = reinterpret_cast<std::uintptr_t>(valuesB);
    wrapper[0] = key ^ (reinterpret_cast<std::uintptr_t>(wrapper) + 1);
    wrapper[1] = 1;
    valuesA[1] = 2 ^ key ^ reinterpret_cast<std::uintptr_t>(&valuesA[1]);
    valuesB[1] = 2 ^ key ^ reinterpret_cast<std::uintptr_t>(&valuesB[1]);
    std::uint32_t uid = 0;
    Check(debug_module::detail::ReadProtected(reinterpret_cast<std::uintptr_t>(wrapper),
        reinterpret_cast<std::uintptr_t>(manager), key, uid) && uid == 2,
        "player UID comes from the protected value, never object kind 0x111");
    wrapper[1] = 999;
    Check(!debug_module::detail::ReadProtected(reinterpret_cast<std::uintptr_t>(wrapper),
        reinterpret_cast<std::uintptr_t>(manager), key, uid), "transitional wrappers do not call native assertion or repair routines");
    std::ifstream fixture(argv[1], std::ios::binary);
    std::vector<char> bytes((std::istreambuf_iterator<char>(fixture)), {});
    debug_model::Snapshot snapshot{};
    Check(debug_model::Decode(bytes.data(), bytes.size(), snapshot), "C# generated fixture decodes in C++");
    if (Failures) return 1;
    Check(snapshot.header.userId == 77 && snapshot.header.count == 2
        && snapshot.monsters[0].box == 2 && snapshot.monsters[0].x == 353
        && snapshot.monsters[0].y == 264 && snapshot.monsters[1].state == 3, "cross-language roster and coordinates match");
    auto bad = bytes;
    bad[38] = static_cast<char>(255);
    Check(!debug_model::Decode(bad.data(), bad.size(), snapshot), "truncated advertised roster rejected");
    bad = bytes; bad.push_back(0);
    Check(!debug_model::Decode(bad.data(), bad.size(), snapshot), "trailing garbage rejected");
    bad = bytes; bad[46] = 4;
    Check(!debug_model::Decode(bad.data(), bad.size(), snapshot), "unknown monster state rejected");
    bad = bytes; bad[52] = bad[40]; bad[53] = bad[41];
    Check(!debug_model::Decode(bad.data(), bad.size(), snapshot), "duplicate actor IDs rejected");
    debug_model::Decode(bytes.data(), bytes.size(), snapshot);
    const auto now = Now(); snapshot.header.fileTime = now;
    Check(debug_model::IsCurrent(snapshot, 77, now, now - 1), "fresh matching roster accepted");
    Check(!debug_model::IsCurrent(snapshot, 78, now, 0)
        && !debug_model::IsCurrent(snapshot, 77, now + 16000000, 0)
        && !debug_model::IsCurrent(snapshot, 77, now, now + 1), "wrong user, stale and previous-entry snapshots rejected");

    wchar_t temp[MAX_PATH]{}, directory[MAX_PATH]{}, file[MAX_PATH]{};
    GetTempPathW(MAX_PATH, temp);
    swprintf_s(directory, L"%sdfdebug-test-%lu", temp, GetCurrentProcessId());
    CreateDirectoryW(directory, nullptr);
    swprintf_s(file, L"%s\\dfdebug-77.bin", directory);
    std::memcpy(bytes.data() + 8, &now, sizeof(now));
    const HANDLE output = CreateFileW(file, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (output == INVALID_HANDLE_VALUE)
    {
        std::fwprintf(stderr, L"fixture path error=%lu path=%s\n", GetLastError(), file);
        return 2;
    }
    DWORD written = 0;
    Check(output != INVALID_HANDLE_VALUE && WriteFile(output, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr), "write own temporary diagnostic fixture");
    if (output != INVALID_HANDLE_VALUE) CloseHandle(output);
    const HMODULE module = LoadLibraryW(L"debug.dll");
    Check(module != nullptr, "standalone debug.dll loads");
    if (module == nullptr) return 1;
    const auto initialize = reinterpret_cast<debug_api::Initialize>(GetProcAddress(module, "DebugInitialize"));
    const auto render = reinterpret_cast<debug_api::Render>(GetProcAddress(module, "DebugRender"));
    const auto shutdown = reinterpret_cast<debug_api::Shutdown>(GetProcAddress(module, "DebugShutdown"));
    Check(initialize && render && shutdown, "versioned undecorated entry points exist");
    if (!initialize || !render || !shutdown) return 1;
    debug_api::Host host{sizeof(debug_api::Host), debug_api::Version, &Draw};
    Check(initialize(&host, directory) == 1, "DLL initializes outside loader lock");
    debug_api::Frame frame{sizeof(debug_api::Frame), 3, 77, 0, 0};
    for (int i = 0; i < 15; ++i)
    {
        Lines.clear(); RawLines.clear(); ExpectedLines.clear(); render(&frame);
        if (Contains(L"BOSS房箱怪：有")) break;
        Sleep(50);
    }
    Check(Contains(L"BOSS房箱怪：有") && Contains(L"容器[2] (353,264)"), "real DLL draws Chinese game overlay through host callback");
    bool stable = !RawLines.empty();
    for (std::size_t i = 0; i < RawLines.size(); ++i)
        stable = stable && std::strcmp(RawLines[i], ExpectedLines[i].c_str()) == 0;
    Check(stable, "every queued native text pointer remains valid after the render callback returns");
    Lines.clear(); frame.userId = 78; render(&frame);
    Check(!Contains(L"BOSS房箱怪：有"), "another player's snapshot is never shown");
    Lines.clear(); frame.scene = 1; render(&frame);
    Check(Lines.empty(), "overlay does not render outside dungeon scene");
    Check(shutdown() == 1, "diagnostic worker stops before DLL unload");
    FreeLibrary(module);
    DeleteFileW(file); RemoveDirectoryW(directory);
    std::printf("Debug overlay tests: %d failures\n", Failures);
    return Failures ? 1 : 0;
}
