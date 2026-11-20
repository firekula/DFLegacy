#pragma once
#include <filesystem>
#include <string>

namespace dfo {
enum class Layout { Migrated, Legacy, Invalid };
Layout Classify(const std::filesystem::path& dir);
bool ClientComplete(const std::filesystem::path& client);
// Launcher binaries and their state belong in client\; game files stay in the root.
bool IsLauncherEntry(const std::wstring& name);
bool IsLegacySplit(const std::filesystem::path& root);
}
