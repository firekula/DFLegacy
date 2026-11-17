#pragma once
#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <vector>

namespace dfo {
struct Journal {
    std::uint32_t version;
    std::vector<std::wstring> entries;
};

bool WriteJournal(const std::filesystem::path& root, const Journal& journal, std::wstring& error);
std::optional<Journal> ReadJournal(const std::filesystem::path& root, std::wstring& error);
void DeleteJournal(const std::filesystem::path& root);
}
