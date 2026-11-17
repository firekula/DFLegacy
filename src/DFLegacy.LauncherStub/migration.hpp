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

MigrationResult Migrate(const std::filesystem::path& root, MigrationObserver& observer);
}
