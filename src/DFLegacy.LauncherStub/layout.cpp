#include "layout.hpp"
#include <windows.h>

namespace fs = std::filesystem;

namespace dfo {
namespace {
bool RegularFile(const fs::path& path) {
    const auto attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES &&
           (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0 &&
           (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0;
}
}

Layout Classify(const fs::path& dir) {
    if (RegularFile(dir / "client" / "DFLegacy.Launcher.exe")) return Layout::Migrated;
    if (RegularFile(dir / "DFLegacy.Launcher.exe") && RegularFile(dir / "DFLegacy.Launcher.App.exe") &&
        RegularFile(dir / "DFLegacy.Updater.exe") && RegularFile(dir / "DFLegacy.Chapter.json")) {
        return Layout::Legacy;
    }
    return Layout::Invalid;
}

bool ClientComplete(const fs::path& client) {
    // The split layout keeps DNF.exe/Script.pvf beside client\ instead of inside it.
    const auto game = client.parent_path();
    const auto file = [&](const wchar_t* name) {
        return RegularFile(client / name) || RegularFile(game / name);
    };
    return RegularFile(client / "DFLegacy.Launcher.exe") && RegularFile(client / "DFLegacy.Launcher.App.exe") &&
           RegularFile(client / "DFLegacy.Updater.exe") && RegularFile(client / "DFLegacy.Bootstrap.ini") &&
           RegularFile(client / "DFLegacy.Chapter.json") && file(L"DNF.exe") && file(L"Script.pvf");
}

bool IsLauncherEntry(const std::wstring& name) {
    static const wchar_t* const entries[] = {
        L"DFLegacy.Launcher.exe", L"DFLegacy.Launcher.App.exe", L"DFLegacy.Updater.exe",
        L"DFLegacy.Diagnostics.exe", L"DFLegacy.CrashCapture.exe", L"DFLegacy.Bootstrap.ini",
        L"DFLegacy.Chapter.json", L".dflegacy"
    };
    for (const auto* entry : entries) {
        if (CompareStringOrdinal(name.c_str(), static_cast<int>(name.size()), entry, -1, TRUE) == CSTR_EQUAL)
            return true;
    }
    return false;
}

// An installation made by the previous single-launcher package keeps the game files
// inside client\. Lifting them to the root restores the split layout without touching
// the launcher binaries or their state.
bool IsLegacySplit(const fs::path& root) {
    return RegularFile(root / "client" / "DNF.exe") && !RegularFile(root / "DNF.exe");
}
}
