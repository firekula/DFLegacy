#include "../journal.hpp"
#include "../layout.hpp"
#include "../launch.hpp"
#include "../migration.hpp"
#include "../process.hpp"
#include "../shortcut.hpp"
#include "../single_instance.hpp"
#include <windows.h>
#include <objbase.h>
#include <shobjidl.h>
#include <cstddef>
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

struct CountObserver : dfo::MigrationObserver {
    std::size_t done = 0;
    std::size_t total = 0;

    void OnProgress(std::size_t doneValue, std::size_t totalValue, const std::wstring&) override {
        done = doneValue;
        total = totalValue;
    }
};

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

    auto migrationRoot = fs::temp_directory_path() / ("dfo-migration-test-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(migrationRoot, ignored);
    fs::create_directories(migrationRoot / "ImagePacks2");
    fs::create_directories(migrationRoot / ".dflegacy" / "v4");
    for (const auto* name : {"DFOLauncher.exe", "DFLegacy.Launcher.exe", "DFLegacy.Launcher.App.exe",
                             "DFLegacy.Updater.exe", "DFLegacy.Bootstrap.ini", "DFLegacy.Chapter.json",
                             "DNF.exe", "Script.pvf"}) {
        std::ofstream(migrationRoot / name) << "x";
    }
    std::ofstream(migrationRoot / "ImagePacks2" / "a.npk") << "x";
    std::ofstream(migrationRoot / ".dflegacy" / "v4" / "installed.json") << "{}";

    CountObserver observer;
    const auto migrated = dfo::Migrate(migrationRoot, observer);
    Check(migrated.ok && !migrated.rolledBack && migrated.error.empty(), "migration succeeds");
    Check(observer.total > 0 && observer.done == observer.total, "migration reports completion");
    Check(fs::exists(migrationRoot / "DFOLauncher.exe"), "stub stays outside the client");
    Check(!fs::exists(migrationRoot / "DFLegacy.Launcher.exe"), "entry moved out of the root");
    Check(dfo::ClientComplete(migrationRoot / "client"), "migrated client is complete");
    Check(!fs::exists(migrationRoot / "DFOLauncher.migrate"), "journal removed after success");

    auto failureRoot = fs::temp_directory_path() / ("dfo-migration-failure-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(failureRoot, ignored);
    fs::create_directories(failureRoot / "ImagePacks2");
    fs::create_directories(failureRoot / ".dflegacy" / "v4");
    for (const auto* name : {"DFOLauncher.exe", "DFLegacy.Launcher.exe", "DFLegacy.Launcher.App.exe",
                             "DFLegacy.Updater.exe", "DFLegacy.Bootstrap.ini", "DFLegacy.Chapter.json",
                             "DNF.exe", "Script.pvf"}) {
        std::ofstream(failureRoot / name) << "x";
    }
    std::ofstream(failureRoot / "ImagePacks2" / "a.npk") << "x";
    std::ofstream(failureRoot / ".dflegacy" / "v4" / "installed.json") << "{}";

    const HANDLE lock = CreateFileW((failureRoot / "DNF.exe").c_str(), GENERIC_READ, 0, nullptr, OPEN_EXISTING,
                                    FILE_ATTRIBUTE_NORMAL, nullptr);
    Check(lock != INVALID_HANDLE_VALUE, "failure fixture locks DNF.exe");
    CountObserver failureObserver;
    const auto migrationFailure = dfo::Migrate(failureRoot, failureObserver);
    CloseHandle(lock);
    Check(!migrationFailure.ok && migrationFailure.rolledBack, "failed migration rolls back");
    Check(fs::exists(failureRoot / "DFLegacy.Launcher.exe") && fs::exists(failureRoot / "DNF.exe"),
          "original layout is restored");
    Check(!fs::exists(failureRoot / "client"), "client removed after rollback");
    Check(!fs::exists(failureRoot / "DFOLauncher.migrate"), "journal removed after rollback");

    fs::remove_all(migrationRoot, ignored);
    fs::remove_all(failureRoot, ignored);

    auto recoveryRoot = fs::temp_directory_path() / ("dfo-recovery-test-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(recoveryRoot, ignored);
    fs::create_directories(recoveryRoot / "client");
    for (const auto* name : {"DFLegacy.Launcher.exe", "DFLegacy.Launcher.App.exe", "DFLegacy.Updater.exe",
                             "DFLegacy.Bootstrap.ini", "DFLegacy.Chapter.json", "DNF.exe", "Script.pvf"}) {
        std::ofstream(recoveryRoot / "client" / name) << "x";
    }
    const dfo::Journal recoveryJournal{1, {L"DFLegacy.Launcher.exe", L"DFLegacy.Launcher.App.exe",
                                          L"DFLegacy.Updater.exe", L"DFLegacy.Bootstrap.ini",
                                          L"DFLegacy.Chapter.json", L"DNF.exe", L"Script.pvf"}};
    std::wstring recoveryError;
    Check(dfo::WriteJournal(recoveryRoot, recoveryJournal, recoveryError), "recovery journal writes");
    Check(dfo::RecoverIfInterrupted(recoveryRoot, recoveryError) == dfo::Recovery::Completed,
          "complete interrupted migration is completed");
    Check(!fs::exists(recoveryRoot / "DFOLauncher.migrate"), "completed journal is removed");
    Check(dfo::ClientComplete(recoveryRoot / "client"), "completed client stays");
    fs::remove_all(recoveryRoot, ignored);

    auto partialRoot = fs::temp_directory_path() / ("dfo-recovery-partial-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(partialRoot, ignored);
    fs::create_directories(partialRoot / "ImagePacks2");
    std::ofstream(partialRoot / "ImagePacks2" / "a.npk") << "x";
    for (const auto* name : {"DFLegacy.Launcher.exe", "DFLegacy.Updater.exe", "DNF.exe"}) {
        std::ofstream(partialRoot / name) << "x";
    }
    const dfo::Journal partialJournal{1, {L"DFLegacy.Launcher.exe", L"DFLegacy.Updater.exe", L"ImagePacks2", L"DNF.exe"}};
    Check(dfo::WriteJournal(partialRoot, partialJournal, recoveryError), "partial journal writes");
    fs::create_directories(partialRoot / "client");
    fs::rename(partialRoot / "DFLegacy.Launcher.exe", partialRoot / "client" / "DFLegacy.Launcher.exe");
    fs::rename(partialRoot / "ImagePacks2", partialRoot / "client" / "ImagePacks2");
    Check(dfo::RecoverIfInterrupted(partialRoot, recoveryError) == dfo::Recovery::RolledBack,
          "partial interrupted migration is rolled back");
    Check(fs::exists(partialRoot / "DFLegacy.Launcher.exe") && fs::exists(partialRoot / "ImagePacks2" / "a.npk"),
          "partial entries are back at the root");
    Check(!fs::exists(partialRoot / "client"), "partial client directory is removed");
    Check(!fs::exists(partialRoot / "DFOLauncher.migrate"), "partial journal is removed");
    fs::remove_all(partialRoot, ignored);

    auto corruptRoot = fs::temp_directory_path() / ("dfo-recovery-corrupt-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(corruptRoot, ignored);
    fs::create_directories(corruptRoot);
    {
        std::ofstream corrupt(corruptRoot / "DFOLauncher.migrate", std::ios::binary | std::ios::trunc);
        corrupt.write("xxxx", 4);
    }
    Check(dfo::RecoverIfInterrupted(corruptRoot, recoveryError) == dfo::Recovery::Failed,
          "corrupt journal recovery fails");
    Check(!recoveryError.empty(), "corrupt journal reports an error");
    Check(fs::exists(corruptRoot / "DFOLauncher.migrate"), "corrupt journal is kept for inspection");
    fs::remove_all(corruptRoot, ignored);

    auto restoreRoot = fs::temp_directory_path() / ("dfo-restore-test-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(restoreRoot, ignored);
    fs::create_directories(restoreRoot / "client" / "ImagePacks2");
    std::ofstream(restoreRoot / "DFOLauncher.exe") << "x";
    for (const auto* name : {"DFLegacy.Launcher.exe", "DFLegacy.Launcher.App.exe", "DFLegacy.Updater.exe",
                             "DFLegacy.Bootstrap.ini", "DFLegacy.Chapter.json", "DNF.exe", "Script.pvf"}) {
        std::ofstream(restoreRoot / "client" / name) << "x";
    }
    std::ofstream(restoreRoot / "client" / "ImagePacks2" / "a.npk") << "x";
    CountObserver restoreObserver;
    const auto restored = dfo::RestoreWithoutJournal(restoreRoot, restoreObserver);
    Check(restored.ok && !restored.rolledBack, "restore succeeds");
    Check(fs::exists(restoreRoot / "DFLegacy.Launcher.exe") && fs::exists(restoreRoot / "ImagePacks2" / "a.npk"),
          "restored entries are back at the root");
    Check(!fs::exists(restoreRoot / "client"), "client removed after restore");
    Check(fs::exists(restoreRoot / "DFOLauncher.exe"), "stub survives restore");
    fs::remove_all(restoreRoot, ignored);

    auto restoreFailureRoot =
        fs::temp_directory_path() / ("dfo-restore-failure-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(restoreFailureRoot, ignored);
    fs::create_directories(restoreFailureRoot / "client");
    std::ofstream(restoreFailureRoot / "DFOLauncher.exe") << "x";
    for (const auto* name : {"DFLegacy.Launcher.exe", "DFLegacy.Launcher.App.exe", "DFLegacy.Updater.exe",
                             "DFLegacy.Bootstrap.ini", "DFLegacy.Chapter.json", "DNF.exe", "Script.pvf"}) {
        std::ofstream(restoreFailureRoot / "client" / name) << "x";
    }
    const HANDLE restoreLock = CreateFileW((restoreFailureRoot / "client" / "DNF.exe").c_str(), GENERIC_READ, 0,
                                            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    Check(restoreLock != INVALID_HANDLE_VALUE, "restore fixture locks DNF.exe");
    CountObserver restoreFailureObserver;
    const auto restoreFailure = dfo::RestoreWithoutJournal(restoreFailureRoot, restoreFailureObserver);
    CloseHandle(restoreLock);
    Check(!restoreFailure.ok && restoreFailure.rolledBack, "restore failure rolls back into the client");
    Check(fs::exists(restoreFailureRoot / "client" / "DFLegacy.Launcher.exe") &&
              fs::exists(restoreFailureRoot / "client" / "DNF.exe"),
          "client entries remain after a failed restore");
    Check(!fs::exists(restoreFailureRoot / "DFLegacy.Launcher.exe"),
          "nothing is left at the root after a failed restore");
    fs::remove_all(restoreFailureRoot, ignored);

    auto shortcutRoot = fs::temp_directory_path() / ("dfo-shortcut-test-" + std::to_string(GetCurrentProcessId()));
    fs::remove_all(shortcutRoot, ignored);
    fs::create_directories(shortcutRoot);
    const auto oldEntry = shortcutRoot / "DFLegacy.Launcher.exe";
    const auto newEntry = shortcutRoot / "DFOLauncher.exe";
    std::ofstream(oldEntry) << "x";
    std::ofstream(newEntry) << "x";
    const auto linkPath = shortcutRoot / "old.lnk";
    Check(SUCCEEDED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED)), "COM initializes");
    {
        IShellLinkW* link = nullptr;
        Check(SUCCEEDED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_IShellLinkW,
                                         reinterpret_cast<void**>(&link))), "shell link creates");
        Check(SUCCEEDED(link->SetPath(oldEntry.c_str())), "old target sets");
        IPersistFile* persist = nullptr;
        Check(SUCCEEDED(link->QueryInterface(IID_IPersistFile, reinterpret_cast<void**>(&persist))),
              "shell link persists");
        Check(SUCCEEDED(persist->Save(linkPath.c_str(), TRUE)), "shortcut saves");
        persist->Release();
        link->Release();
    }
    Check(dfo::RetargetShortcutsIn(shortcutRoot, oldEntry.wstring(), newEntry) == 1, "shortcut is retargeted");
    {
        IShellLinkW* link = nullptr;
        Check(SUCCEEDED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_IShellLinkW,
                                         reinterpret_cast<void**>(&link))), "shell link reopens");
        IPersistFile* persist = nullptr;
        Check(SUCCEEDED(link->QueryInterface(IID_IPersistFile, reinterpret_cast<void**>(&persist))),
              "reopened link persists");
        Check(SUCCEEDED(persist->Load(linkPath.c_str(), STGM_READ)), "retargeted shortcut loads");
        wchar_t target[32768]{};
        WIN32_FIND_DATAW data{};
        Check(SUCCEEDED(link->GetPath(target, 32768, &data, SLGP_RAWPATH)), "retargeted path reads");
        Check(std::wstring(target) == newEntry.wstring(), "shortcut points to DFOLauncher");
        persist->Release();
        link->Release();
    }
    CoUninitialize();
    fs::remove_all(shortcutRoot, ignored);

    fs::remove_all(processRoot, ignored);
    fs::remove_all(launchRoot, ignored);
    return 0;
}
