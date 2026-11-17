#pragma once
#include <cstddef>
#include <filesystem>
#include <string>

namespace dfo {
std::size_t RetargetShortcutsIn(const std::filesystem::path& directory, const std::wstring& oldTarget,
                                const std::filesystem::path& newTarget);
std::size_t RetargetShortcuts(const std::wstring& oldTarget, const std::filesystem::path& newTarget);
}
