#include "../journal.hpp"
#include "../layout.hpp"
#include "../launch.hpp"
#include "../process.hpp"
#include "../single_instance.hpp"
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
    Sleep(700);

    auto processRoot = fs::temp_directory_path() / ("dfo-process-test-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(processRoot, ignored);
    fs::create_directories(processRoot);
    const fs::path probe = processRoot / "DFLegacy.Probe.exe";
    fs::copy_file(fixture, probe, fs::copy_options::overwrite_existing);
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION probeProcess{};
    std::wstring probeCommand = L"\"" + probe.wstring() + L"\" --sleep-ms 30000";
    Check(CreateProcessW(probe.c_str(), probeCommand.data(), nullptr, nullptr, FALSE, 0, nullptr,
                         processRoot.c_str(), &startup, &probeProcess) != FALSE, "probe starts");
    Sleep(300);
    Check(dfo::HasRunningClient(processRoot, GetCurrentProcessId()), "running client is detected");
    TerminateProcess(probeProcess.hProcess, 1);
    WaitForSingleObject(probeProcess.hProcess, 5000);
    CloseHandle(probeProcess.hThread);
    CloseHandle(probeProcess.hProcess);
    Sleep(200);
    Check(!dfo::HasRunningClient(processRoot, GetCurrentProcessId()), "stopped client is not detected");

    Check(dfo::AcquireSingleInstance(processRoot), "first instance acquires the mutex");
    Check(!dfo::AcquireSingleInstance(processRoot), "second instance is rejected");
    dfo::ReleaseSingleInstance();
    Check(dfo::AcquireSingleInstance(processRoot), "mutex is reusable after release");
    dfo::ReleaseSingleInstance();

    auto journalRoot = fs::temp_directory_path() / ("dfo-journal-test-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(journalRoot, ignored);
    fs::create_directories(journalRoot);
    const dfo::Journal journal{1, {L"DFLegacy.Launcher.exe", L"ImagePacks2", L".dflegacy"}};
    std::wstring journalError;
    Check(dfo::WriteJournal(journalRoot, journal, journalError) && journalError.empty(), "journal writes");
    const auto readJournal = dfo::ReadJournal(journalRoot, journalError);
    Check(readJournal.has_value() && journalError.empty(), "journal reads back");
    Check(readJournal->version == 1, "journal version round-trips");
    Check(readJournal->entries == journal.entries, "journal entries round-trip");

    {
        std::ofstream corrupt(journalRoot / "DFOLauncher.migrate", std::ios::binary | std::ios::trunc);
        corrupt.write("xxxx", 4);
    }
    const auto corruptJournal = dfo::ReadJournal(journalRoot, journalError);
    Check(!corruptJournal.has_value() && !journalError.empty(), "corrupt journal is rejected");

    dfo::DeleteJournal(journalRoot);
    Check(!fs::exists(journalRoot / "DFOLauncher.migrate"), "journal is deleted");
    const auto missingJournal = dfo::ReadJournal(journalRoot, journalError);
    Check(!missingJournal.has_value() && journalError.empty(), "missing journal is not an error");
    fs::remove_all(journalRoot, ignored);

    fs::remove_all(processRoot, ignored);
    fs::remove_all(launchRoot, ignored);
    return 0;
}
