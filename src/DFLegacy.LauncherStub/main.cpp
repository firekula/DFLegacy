#include "layout.hpp"
#include "launch.hpp"
#include "migration.hpp"
#include "process.hpp"
#include "shortcut.hpp"
#include "single_instance.hpp"
#include "ui.hpp"
#include <windows.h>
#include <shellapi.h>
#include <array>
#include <atomic>
#include <cstddef>
#include <filesystem>
#include <string>
#include <thread>

namespace {
struct ProgressObserver : dfo::MigrationObserver {
    explicit ProgressObserver(dfo::ProgressWindow& target) : window(target) {}
    dfo::ProgressWindow& window;
    void OnProgress(std::size_t done, std::size_t total, const std::wstring& name) override {
        window.Update(done, total, name);
    }
};

template <typename Operation>
dfo::MigrationResult RunWithProgress(HINSTANCE instance, Operation operation) {
    dfo::ProgressWindow progress(instance);
    ProgressObserver observer{progress};
    dfo::MigrationResult result{};
    std::atomic<bool> done = false;
    std::thread worker([&] {
        result = operation(observer);
        done = true;
    });
    MSG message{};
    while (!done) {
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        Sleep(10);
    }
    worker.join();
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    progress.Close();
    return result;
}
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
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
    std::wstring arguments;
    for (int index = 1; index < argumentCount; ++index) {
        const std::wstring argument = argv[index];
        if (argument == L"--restore") {
            restore = true;
            continue;
        }
        if (!arguments.empty()) arguments += L" ";
        arguments += argument;
    }

    if (!dfo::AcquireSingleInstance(root)) return 0;
    struct ReleaseInstance {
        ~ReleaseInstance() { dfo::ReleaseSingleInstance(); }
    } release;

    if (dfo::HasRunningClient(root, GetCurrentProcessId())) {
        dfo::Error(instance, L"检测到客户端或游戏仍在运行，请关闭后重试。");
        return 1;
    }

    if (restore) {
        if (!dfo::Confirm(instance,
                          L"将把 client\\ 中的客户端文件移回外层，恢复旧布局。\n\n是否继续？")) {
            return 0;
        }
        const auto result = RunWithProgress(instance, [&](dfo::MigrationObserver& observer) {
            return dfo::RestoreWithoutJournal(root, observer);
        });
        if (!result.ok) {
            dfo::Error(instance, result.error.c_str());
            return 1;
        }
        dfo::Error(instance, L"已恢复旧布局。请从外层入口启动客户端。");
        return 0;
    }

    std::wstring recoveryError;
    const auto recovery = dfo::RecoverIfInterrupted(root, recoveryError);
    if (recovery == dfo::Recovery::Failed) {
        dfo::Error(instance, recoveryError.c_str());
        return 1;
    }

    if (recovery == dfo::Recovery::None || recovery == dfo::Recovery::RolledBack) {
        const auto layout = dfo::Classify(root);
        if (layout == dfo::Layout::Legacy) {
            if (!dfo::Confirm(
                    instance,
                    L"将把当前客户端整理到 client\\ 子文件夹，以后请统一从 DFOLauncher.exe 启动。\n\n"
                    L"请确认客户端已关闭。是否继续？")) {
                return 0;
            }
            if (dfo::HasRunningClient(root, GetCurrentProcessId())) {
                dfo::Error(instance, L"检测到客户端或游戏仍在运行，请关闭后重试。");
                return 1;
            }
            const auto result = RunWithProgress(instance, [&](dfo::MigrationObserver& observer) {
                return dfo::Migrate(root, observer);
            });
            if (!result.ok) {
                dfo::Error(instance, result.error.c_str());
                return 1;
            }
            dfo::RetargetShortcuts((root / L"DFLegacy.Launcher.exe").wstring(), root / L"DFOLauncher.exe");
        } else if (layout == dfo::Layout::Invalid) {
            dfo::Error(instance,
                       L"客户端不完整。请重新解压完整启动包，或检查 client\\ 是否被移动或改名。");
            return 1;
        }
    }

    const auto launch = dfo::LaunchClient(root, arguments, 10000);
    if (!launch.started) {
        dfo::Error(instance, L"无法启动客户端。请检查 client\\DFLegacy.Launcher.exe 是否存在。");
        return 1;
    }
    if (launch.earlyFailure) {
        if (dfo::Confirm(instance, L"新版客户端启动失败。是否恢复旧布局？")) {
            if (dfo::HasRunningClient(root, GetCurrentProcessId())) {
                dfo::Error(instance, L"检测到客户端或游戏仍在运行，请关闭后重试。");
                return 1;
            }
            const auto result = RunWithProgress(instance, [&](dfo::MigrationObserver& observer) {
                return dfo::RestoreWithoutJournal(root, observer);
            });
            if (!result.ok) {
                dfo::Error(instance, result.error.c_str());
                return 1;
            }
            dfo::Error(instance, L"已恢复旧布局。请从外层旧入口启动客户端。");
        }
        return 1;
    }
    return 0;
}
