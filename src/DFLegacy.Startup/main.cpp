#include "bootstrap.h"
#include <windows.h>
#include <shellapi.h>
#include <array>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    bool noDialog = false;
    try {
        std::array<wchar_t, 32768> module{};
        auto count = GetModuleFileNameW(nullptr, module.data(), static_cast<DWORD>(module.size()));
        if (!count || count >= module.size()) return 1;
        int argc = 0; auto argv = CommandLineToArgvW(GetCommandLineW(), &argc);
        if (!argv) return 1;
        struct FreeArguments { LPWSTR* argv; ~FreeArguments() { LocalFree(argv); } } cleanup{argv};
        std::vector<std::wstring> arguments;
        for (int i = 1; i < argc; ++i) arguments.emplace_back(argv[i]);
        noDialog = dflegacy::startup::SuppressDialogs(arguments);
        auto result = dflegacy::startup::Run(std::filesystem::path(std::wstring(module.data(), count)).parent_path(), arguments);
        if (result.exitCode == 1 && !noDialog) MessageBoxW(nullptr, result.message.c_str(), L"DFLegacy 启动修复", MB_OK | MB_ICONERROR);
        return result.exitCode;
    } catch (...) {
        if (!noDialog) MessageBoxW(nullptr, L"启动修复失败。请检查客户端文件和可用磁盘空间。", L"DFLegacy 启动修复", MB_OK | MB_ICONERROR);
        return 1;
    }
}
