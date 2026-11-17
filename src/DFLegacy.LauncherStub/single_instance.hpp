#pragma once
#include <filesystem>

namespace dfo {
bool AcquireSingleInstance(const std::filesystem::path& root);
void ReleaseSingleInstance();
}
