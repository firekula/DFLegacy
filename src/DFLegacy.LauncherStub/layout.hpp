#pragma once
#include <filesystem>

namespace dfo {
enum class Layout { Migrated, Legacy, Invalid };
Layout Classify(const std::filesystem::path& dir);
bool ClientComplete(const std::filesystem::path& client);
}
