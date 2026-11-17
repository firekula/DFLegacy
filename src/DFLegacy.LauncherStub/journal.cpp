#include "journal.hpp"
#include <windows.h>
#include <cstring>
#include <utility>
#include <vector>

namespace fs = std::filesystem;

namespace dfo {
namespace {
constexpr std::uint32_t Magic = 0x44464F4D;
constexpr std::uint32_t CurrentVersion = 1;
constexpr std::uint64_t MaxFileBytes = 1024 * 1024;
constexpr std::uint32_t MaxEntries = 10000;
constexpr std::uint32_t MaxEntryBytes = 1024;
constexpr const wchar_t* FileName = L"DFOLauncher.migrate";
constexpr const wchar_t* TempName = L"DFOLauncher.migrate.tmp";

bool WriteAll(HANDLE file, const void* data, std::uint32_t bytes) {
    auto* cursor = static_cast<const char*>(data);
    while (bytes != 0) {
        DWORD written = 0;
        if (!WriteFile(file, cursor, bytes, &written, nullptr) || written == 0) return false;
        cursor += written;
        bytes -= written;
    }
    return true;
}
}

bool WriteJournal(const fs::path& root, const Journal& journal, std::wstring& error) {
    error.clear();
    const auto temporary = root / TempName;
    const auto final = root / FileName;
    const HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                                    FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        error = L"无法创建迁移日志。";
        return false;
    }
    bool ok = true;
    const std::uint32_t version = journal.version;
    const std::uint32_t count = static_cast<std::uint32_t>(journal.entries.size());
    ok = ok && WriteAll(file, &Magic, 4) && WriteAll(file, &version, 4) && WriteAll(file, &count, 4);
    for (const auto& entry : journal.entries) {
        const auto bytes = static_cast<std::uint32_t>(entry.size() * sizeof(wchar_t));
        if (bytes > MaxEntryBytes) {
            ok = false;
            break;
        }
        ok = ok && WriteAll(file, &bytes, 4) && WriteAll(file, entry.data(), bytes);
        if (!ok) break;
    }
    if (ok) ok = FlushFileBuffers(file) != FALSE;
    CloseHandle(file);
    if (!ok) {
        DeleteFileW(temporary.c_str());
        error = L"无法写入迁移日志。";
        return false;
    }
    if (!MoveFileExW(temporary.c_str(), final.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        DeleteFileW(temporary.c_str());
        error = L"无法提交迁移日志。";
        return false;
    }
    return true;
}

std::optional<Journal> ReadJournal(const fs::path& root, std::wstring& error) {
    error.clear();
    const auto file = root / FileName;
    const HANDLE handle = CreateFileW(file.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                                      FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        const DWORD code = GetLastError();
        if (code == ERROR_FILE_NOT_FOUND || code == ERROR_PATH_NOT_FOUND) return std::nullopt;
        error = L"无法读取迁移日志。";
        return std::nullopt;
    }
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(handle, &size) || size.QuadPart < 12 ||
        static_cast<std::uint64_t>(size.QuadPart) > MaxFileBytes) {
        CloseHandle(handle);
        error = L"迁移日志已损坏。";
        return std::nullopt;
    }
    std::vector<char> data(static_cast<std::size_t>(size.QuadPart));
    DWORD read = 0;
    const BOOL readOk = ReadFile(handle, data.data(), static_cast<DWORD>(data.size()), &read, nullptr);
    CloseHandle(handle);
    if (!readOk || read != static_cast<DWORD>(data.size())) {
        error = L"迁移日志读取不完整。";
        return std::nullopt;
    }

    std::size_t offset = 0;
    const auto next = [&](std::uint32_t& value) {
        if (offset + 4 > data.size()) return false;
        std::memcpy(&value, data.data() + offset, 4);
        offset += 4;
        return true;
    };

    std::uint32_t magic = 0;
    std::uint32_t version = 0;
    std::uint32_t count = 0;
    if (!next(magic) || magic != Magic || !next(version) || version != CurrentVersion || !next(count) ||
        count > MaxEntries) {
        error = L"迁移日志已损坏。";
        return std::nullopt;
    }

    Journal journal{version, {}};
    journal.entries.reserve(count);
    for (std::uint32_t i = 0; i < count; ++i) {
        std::uint32_t bytes = 0;
        if (!next(bytes) || bytes > MaxEntryBytes || bytes % 2 != 0 || offset + bytes > data.size()) {
            error = L"迁移日志已损坏。";
            return std::nullopt;
        }
        std::wstring entry(bytes / 2, L'\0');
        std::memcpy(entry.data(), data.data() + offset, bytes);
        offset += bytes;
        journal.entries.push_back(std::move(entry));
    }
    if (offset != data.size()) {
        error = L"迁移日志已损坏。";
        return std::nullopt;
    }
    return journal;
}

void DeleteJournal(const fs::path& root) {
    DeleteFileW((root / FileName).c_str());
    DeleteFileW((root / TempName).c_str());
}
}
