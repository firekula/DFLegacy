#include "migration.hpp"
#include <windows.h>
#include <shellapi.h>
#include <array>
#include <cstddef>
#include <filesystem>
#include <string>

namespace {
struct SilentObserver : dfo::MigrationObserver {
    void OnProgress(std::size_t, std::size_t, const std::wstring&) override {}
};
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    std::array<wchar_t, 32768> module{};
    const auto count = GetModuleFileNameW(nullptr, module.data(), static_cast<DWORD>(module.size()));
    if (count == 0 || count >= module.size()) return 1;
    const auto root = std::filesystem::path(std::wstring(module.data(), count)).parent_path();

    int argumentCount = 0;
    auto* argv = CommandLineToArgvW(GetCommandLineW(), &argumentCount);
    if (!argv) return 1;
    struct FreeArguments {
        LPWSTR* argv;
        ~FreeArguments() { LocalFree(argv); }
    } cleanup{argv};

    bool restore = false;
    for (int index = 1; index < argumentCount; ++index) {
        if (std::wstring(argv[index]) == L"--restore") restore = true;
    }
    if (restore) {
        SilentObserver observer;
        const auto result = dfo::RestoreWithoutJournal(root, observer);
        return result.ok ? 0 : 1;
    }
    return 0;
}
