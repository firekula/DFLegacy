#pragma once
#include <cstddef>
#include <filesystem>
#include <string>

namespace dfo {
struct MigrationObserver {
    virtual ~MigrationObserver() = default;
    virtual void OnProgress(std::size_t done, std::size_t total, const std::wstring& name) = 0;
};

struct MigrationResult {
    bool ok;
    bool rolledBack;
    std::wstring error;
};

enum class Recovery { None, Completed, RolledBack, Failed };

MigrationResult Migrate(const std::filesystem::path& root, MigrationObserver& observer);
Recovery RecoverIfInterrupted(const std::filesystem::path& root, std::wstring& error);
MigrationResult RestoreWithoutJournal(const std::filesystem::path& root, MigrationObserver& observer);
// Moves game files out of client\ for installations made by the previous single-launcher package.
MigrationResult LiftGameFiles(const std::filesystem::path& root, MigrationObserver& observer);
}
