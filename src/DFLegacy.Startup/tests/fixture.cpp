#include <windows.h>
#include <filesystem>
#include <fstream>
#include <string>

int wmain(int argc, wchar_t** argv) {
    const bool direct = argc >= 3 && std::wstring(argv[1]) == L"--bootstrap-verified" && std::wstring(argv[2]) == L"--";
    if (!direct && (argc < 4 || std::wstring(argv[1]) != L"--bootstrap-client" || std::wstring(argv[3]) != L"--")) return 91;
    const std::filesystem::path root = direct ? std::filesystem::current_path() : std::filesystem::path(argv[2]);
    if (!root.is_absolute()) return 92;
    wchar_t module[32768]{};
    if (!GetModuleFileNameW(nullptr, module, 32768)) return 93;
    if (!direct && std::filesystem::path(module).parent_path() == root) return 94;
    if (std::filesystem::current_path() != root) return 95;
    std::ofstream output(root / "observed.bin", std::ios::binary | std::ios::app);
    const int result = FIXTURE_RESULT;
    output.write(reinterpret_cast<const char*>(&result), sizeof(result));
    const auto write = [&](const std::wstring& value) {
        const auto size = static_cast<unsigned>(value.size());
        output.write(reinterpret_cast<const char*>(&size), sizeof(size));
        output.write(reinterpret_cast<const char*>(value.data()), size * sizeof(wchar_t));
    };
    write(module);
    write(root.wstring());
    const unsigned count = static_cast<unsigned>(argc - (direct ? 3 : 4));
    output.write(reinterpret_cast<const char*>(&count), sizeof(count));
    for (int i = direct ? 3 : 4; i < argc; ++i) write(argv[i]);
    output.close();
    if (direct) {
        if (std::filesystem::exists(root / L"reject-fast")) return 23;
        wchar_t eventName[256]{};
        if (!GetEnvironmentVariableW(L"DFLEGACY_BOOTSTRAP_READY_EVENT", eventName, 256)) return 97;
        HANDLE ready = OpenEventW(EVENT_MODIFY_STATE, FALSE, eventName);
        if (!ready) return 98;
        SetEvent(ready); CloseHandle(ready); return FIXTURE_RESULT;
    }
    if (std::filesystem::exists(root / "hold")) {
        for (int attempt = 0; attempt < 200 && !std::filesystem::exists(root / "release"); ++attempt) Sleep(25);
    }
    if (FIXTURE_RESULT == 0 && std::filesystem::exists(root / L"replacement.exe")) {
        if (!CopyFileW((root / L"replacement.exe").c_str(), (root / L"DFLegacy.Updater.exe").c_str(), FALSE) ||
            !CopyFileW((root / L"replacement.ini").c_str(), (root / L"DFLegacy.Bootstrap.ini").c_str(), FALSE)) return 96;
    }
    return FIXTURE_RESULT;
}
