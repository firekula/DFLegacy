#include "migration.hpp"
#include "journal.hpp"
#include "layout.hpp"
#include <windows.h>
#include <algorithm>
#include <string>
#include <vector>

namespace fs = std::filesystem;

namespace dfo {
namespace {
constexpr const wchar_t* StubName = L"DFOLauncher.exe";
constexpr const wchar_t* JournalName = L"DFOLauncher.migrate";
constexpr const wchar_t* TempName = L"DFOLauncher.migrate.tmp";

bool IsReparse(const fs::path& path) {
    const auto attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
}

bool Excluded(const std::wstring& name) {
    return name == StubName || name == JournalName || name == TempName || name == L"client";
}

std::wstring ErrorText(const wchar_t* action, const std::wstring& name, DWORD code) {
    return std::wstring(action) + L" " + name + L" 失败（错误 " + std::to_wstring(code) + L"）。";
}

bool Enumerate(const fs::path& root, std::vector<std::wstring>& entries, std::wstring& error) {
    WIN32_FIND_DATAW data{};
    const HANDLE find = FindFirstFileW((root / L"*").c_str(), &data);
    if (find == INVALID_HANDLE_VALUE) {
        error = L"无法枚举安装目录。";
        return false;
    }
    do {
        const std::wstring name = data.cFileName;
        if (name == L"." || name == L".." || Excluded(name)) continue;
        entries.push_back(name);
    } while (FindNextFileW(find, &data));
    FindClose(find);
    return true;
}

bool RollBack(const fs::path& root, const fs::path& client, const std::vector<std::wstring>& entries,
              std::size_t moved) {
    bool ok = true;
    for (std::size_t index = moved; index > 0; --index) {
        const auto& name = entries[index - 1];
        if (!MoveFileW((client / name).c_str(), (root / name).c_str())) ok = false;
    }
    if (ok) RemoveDirectoryW(client.c_str());
    return ok && GetFileAttributesW(client.c_str()) == INVALID_FILE_ATTRIBUTES;
}
}

MigrationResult Migrate(const fs::path& root, MigrationObserver& observer) {
    if (IsReparse(root)) return {false, false, L"安装目录是重解析点，拒绝迁移。"};
    const auto client = root / L"client";
    if (GetFileAttributesW(client.c_str()) != INVALID_FILE_ATTRIBUTES) {
        return {false, false, L"client 目录已存在，请先恢复或手动处理。"};
    }

    std::vector<std::wstring> scanned;
    std::wstring error;
    if (!Enumerate(root, scanned, error)) return {false, false, error};
    // Only the launcher and its state move into client\; game files stay in the root.
    std::vector<std::wstring> entries;
    for (const auto& name : scanned) if (IsLauncherEntry(name)) entries.push_back(name);
    if (entries.empty()) return {false, false, L"安装目录中没有可迁移的启动器文件。"};
    for (const auto& name : entries) {
        if (IsReparse(root / name)) return {false, false, L"客户端包含重解析点，拒绝迁移：" + name};
    }

    const Journal journal{1, entries};
    if (!WriteJournal(root, journal, error)) return {false, false, error};
    if (!CreateDirectoryW(client.c_str(), nullptr)) {
        DeleteJournal(root);
        return {false, false, L"无法创建 client 目录。"};
    }

    std::size_t moved = 0;
    const std::size_t total = entries.size();
    for (const auto& name : entries) {
        observer.OnProgress(moved, total, name);
        if (!MoveFileW((root / name).c_str(), (client / name).c_str())) {
            const DWORD code = GetLastError();
            const bool rolledBack = RollBack(root, client, entries, moved);
            if (rolledBack) DeleteJournal(root);
            return {false, rolledBack,
                    ErrorText(L"移动", name, code) +
                        (rolledBack ? L"" : L" 回滚不完整，下次启动会重试恢复。")};
        }
        ++moved;
    }
    observer.OnProgress(total, total, L"");

    if (!ClientComplete(client)) {
        const bool rolledBack = RollBack(root, client, entries, moved);
        if (rolledBack) DeleteJournal(root);
        return {false, rolledBack, L"迁移后客户端不完整，已回滚。"};
    }
    DeleteJournal(root);
    return {true, false, L""};
}

Recovery RecoverIfInterrupted(const fs::path& root, std::wstring& error) {
    error.clear();
    const auto journal = ReadJournal(root, error);
    if (!journal) return error.empty() ? Recovery::None : Recovery::Failed;

    const auto client = root / L"client";
    if (ClientComplete(client)) {
        DeleteJournal(root);
        return Recovery::Completed;
    }

    bool ok = true;
    DWORD firstError = 0;
    for (auto entry = journal->entries.rbegin(); entry != journal->entries.rend(); ++entry) {
        const auto source = client / *entry;
        if (GetFileAttributesW(source.c_str()) == INVALID_FILE_ATTRIBUTES) continue;
        if (!MoveFileW(source.c_str(), (root / *entry).c_str())) {
            ok = false;
            if (firstError == 0) firstError = GetLastError();
            break;
        }
    }
    if (!ok) {
        error = L"恢复旧布局失败（错误 " + std::to_wstring(firstError) + L"），下次启动会重试。";
        return Recovery::Failed;
    }
    RemoveDirectoryW(client.c_str());
    DeleteJournal(root);
    return Recovery::RolledBack;
}

MigrationResult LiftGameFiles(const fs::path& root, MigrationObserver& observer) {
    const auto client = root / L"client";
    if (!IsLegacySplit(root)) return {true, false, L""};

    std::vector<std::wstring> entries;
    WIN32_FIND_DATAW data{};
    const HANDLE find = FindFirstFileW((client / L"*").c_str(), &data);
    if (find == INVALID_HANDLE_VALUE) return {false, false, L"无法枚举 client 目录。"};
    do {
        const std::wstring name = data.cFileName;
        if (name == L"." || name == L"..") continue;
        if (IsLauncherEntry(name)) continue;
        entries.push_back(name);
    } while (FindNextFileW(find, &data));
    FindClose(find);
    // DNF.exe moves last, so an interrupted lift is still detected and resumed next start.
    std::stable_sort(entries.begin(), entries.end(), [](const std::wstring& left, const std::wstring& right) {
        return left != L"DNF.exe" && right == L"DNF.exe";
    });

    std::vector<std::wstring> moved;
    for (const auto& name : entries) {
        observer.OnProgress(moved.size(), entries.size(), name);
        const auto target = root / name;
        if (GetFileAttributesW(target.c_str()) != INVALID_FILE_ATTRIBUTES) continue;
        if (IsReparse(client / name)) return {false, false, L"客户端包含重解析点，拒绝整理：" + name};
        if (!MoveFileW((client / name).c_str(), target.c_str())) {
            const DWORD code = GetLastError();
            bool rollbackOk = true;
            for (std::size_t index = moved.size(); index > 0; --index) {
                const auto& back = moved[index - 1];
                if (!MoveFileW((root / back).c_str(), (client / back).c_str())) rollbackOk = false;
            }
            return {false, rollbackOk,
                    ErrorText(L"移动", name, code) + (rollbackOk ? L"" : L" 回滚不完整，请手动检查。")};
        }
        moved.push_back(name);
    }
    observer.OnProgress(entries.size(), entries.size(), L"");
    return {true, false, L""};
}

MigrationResult RestoreWithoutJournal(const fs::path& root, MigrationObserver& observer) {
    const auto client = root / L"client";
    if (GetFileAttributesW(client.c_str()) == INVALID_FILE_ATTRIBUTES) {
        return {false, false, L"client 目录不存在。"};
    }

    std::vector<std::wstring> entries;
    WIN32_FIND_DATAW data{};
    const HANDLE find = FindFirstFileW((client / L"*").c_str(), &data);
    if (find == INVALID_HANDLE_VALUE) return {false, false, L"无法枚举 client 目录。"};
    do {
        const std::wstring name = data.cFileName;
        if (name == L"." || name == L"..") continue;
        entries.push_back(name);
    } while (FindNextFileW(find, &data));
    FindClose(find);

    std::size_t moved = 0;
    const std::size_t total = entries.size();
    for (const auto& name : entries) {
        observer.OnProgress(moved, total, name);
        if (!MoveFileW((client / name).c_str(), (root / name).c_str())) {
            const DWORD code = GetLastError();
            bool rollbackOk = true;
            for (std::size_t index = moved; index > 0; --index) {
                const auto& back = entries[index - 1];
                if (!MoveFileW((root / back).c_str(), (client / back).c_str())) rollbackOk = false;
            }
            return {false, rollbackOk,
                    ErrorText(L"恢复", name, code) +
                        (rollbackOk ? L"" : L" 回滚不完整，请手动检查。")};
        }
        ++moved;
    }
    observer.OnProgress(total, total, L"");
    if (!RemoveDirectoryW(client.c_str())) return {false, false, L"client 目录非空，无法完成恢复。"};
    return {true, false, L""};
}
}
