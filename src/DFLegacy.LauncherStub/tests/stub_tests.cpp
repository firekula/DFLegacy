#include "../layout.hpp"
#include "../launch.hpp"
#include <windows.h>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <string>

static void Check(bool condition, const char* what) {
    if (condition) return;
    std::fprintf(stderr, "FAIL: %s\n", what);
    std::exit(1);
}

int main(int argc, char** argv) {
    namespace fs = std::filesystem;
    std::error_code ignored;

    auto root = fs::temp_directory_path() / "dfo-layout-test";
    fs::remove_all(root, ignored);
    fs::create_directories(root);

    Check(dfo::Classify(root) == dfo::Layout::Invalid, "empty dir is invalid");

    std::ofstream(root / "DFLegacy.Launcher.exe") << "x";
    Check(dfo::Classify(root) == dfo::Layout::Invalid, "entry alone is invalid");

    std::ofstream(root / "DFLegacy.Launcher.App.exe") << "x";
    std::ofstream(root / "DFLegacy.Updater.exe") << "x";
    std::ofstream(root / "DFLegacy.Chapter.json") << "{}";
    Check(dfo::Classify(root) == dfo::Layout::Legacy, "legacy layout is detected");

    fs::create_directories(root / "client");
    std::ofstream(root / "client" / "DFLegacy.Launcher.exe") << "x";
    Check(dfo::Classify(root) == dfo::Layout::Migrated, "client entry wins over legacy files");
    Check(!dfo::ClientComplete(root / "client"), "client without required files is incomplete");

    for (const auto* name : {"DFLegacy.Launcher.App.exe", "DFLegacy.Updater.exe", "DFLegacy.Bootstrap.ini",
                             "DFLegacy.Chapter.json", "DNF.exe", "Script.pvf"}) {
        std::ofstream(root / "client" / name) << "x";
    }
    Check(dfo::ClientComplete(root / "client"), "complete client is recognized");

    fs::remove_all(root, ignored);

    Check(argc >= 2, "fixture path argument is required");
    const fs::path fixture = fs::absolute(argv[1]);
    auto launchRoot = fs::temp_directory_path() / ("dfo-launch-test-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(launchRoot, ignored);
    auto client = launchRoot / "client";
    fs::create_directories(client);
    fs::copy_file(fixture, client / "DFLegacy.Launcher.exe", fs::copy_options::overwrite_existing);

    const auto completed = dfo::LaunchClient(launchRoot, L"--result 0 --sleep-ms 100", 5000);
    Check(completed.started && !completed.earlyFailure && completed.exitCode == 0, "clean launch succeeds");

    const auto failed = dfo::LaunchClient(launchRoot, L"--result 3", 5000);
    Check(failed.started && failed.earlyFailure && failed.exitCode == 3, "nonzero early exit is a failure");

    const auto running = dfo::LaunchClient(launchRoot, L"--sleep-ms 1000", 250);
    Check(running.started && !running.earlyFailure, "timeout means the client is still starting");

    fs::remove_all(launchRoot, ignored);
    return 0;
}
