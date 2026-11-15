#pragma once
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace dflegacy::startup {
struct Descriptor {
    std::string architecture;
    std::string launcherSha256;
    std::string updaterSha256;
};
struct Result {
    int exitCode = 1;
    bool alreadyRunning = false;
    std::wstring message;
};
std::string Architecture();
std::optional<Descriptor> ParseDescriptor(std::string_view bytes, std::string_view architecture);
std::wstring QuoteArgument(std::wstring_view argument);
bool SuppressDialogs(const std::vector<std::wstring>& arguments);
Result Run(const std::filesystem::path& root, const std::vector<std::wstring>& arguments);
}
