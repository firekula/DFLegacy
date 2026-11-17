#include "shortcut.hpp"
#include <windows.h>
#include <objbase.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <filesystem>
#include <iterator>
#include <string>

namespace fs = std::filesystem;

namespace dfo {
namespace {
bool SamePath(const std::wstring& left, const std::wstring& right) {
    return CompareStringOrdinal(left.c_str(), static_cast<int>(left.size()), right.c_str(),
                                static_cast<int>(right.size()), TRUE) == CSTR_EQUAL;
}

bool IsLink(const fs::path& path) {
    const auto extension = path.extension().wstring();
    return CompareStringOrdinal(extension.c_str(), static_cast<int>(extension.size()), L".lnk", 4, TRUE) ==
           CSTR_EQUAL;
}

std::size_t RetargetFile(const fs::path& file, const std::wstring& oldTarget, const fs::path& newTarget) {
    IShellLinkW* link = nullptr;
    if (FAILED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_IShellLinkW,
                                reinterpret_cast<void**>(&link)))) {
        return 0;
    }
    std::size_t changed = 0;
    IPersistFile* persist = nullptr;
    if (SUCCEEDED(link->QueryInterface(IID_IPersistFile, reinterpret_cast<void**>(&persist))) &&
        SUCCEEDED(persist->Load(file.c_str(), STGM_READ))) {
        wchar_t path[32768]{};
        WIN32_FIND_DATAW data{};
        if (SUCCEEDED(link->GetPath(path, static_cast<UINT>(std::size(path)), &data, SLGP_RAWPATH)) &&
            SamePath(path, oldTarget)) {
            const auto directory = newTarget.parent_path();
            if (SUCCEEDED(link->SetPath(newTarget.c_str())) &&
                SUCCEEDED(link->SetWorkingDirectory(directory.c_str())) &&
                SUCCEEDED(persist->Save(file.c_str(), TRUE))) {
                changed = 1;
            }
        }
    }
    if (persist) persist->Release();
    link->Release();
    return changed;
}

std::size_t RetargetDirectory(const fs::path& directory, const std::wstring& oldTarget,
                              const fs::path& newTarget) {
    std::error_code error;
    if (!fs::is_directory(directory, error)) return 0;
    std::size_t changed = 0;
    for (fs::recursive_directory_iterator it(directory, fs::directory_options::skip_permission_denied, error), end;
         it != end;) {
        if (error) {
            error.clear();
            it.increment(error);
            continue;
        }
        if (it->is_regular_file(error) && IsLink(it->path())) {
            changed += RetargetFile(it->path(), oldTarget, newTarget);
        }
        it.increment(error);
    }
    return changed;
}

std::size_t RetargetKnownFolder(REFKNOWNFOLDERID folder, const std::wstring& oldTarget,
                                const fs::path& newTarget) {
    PWSTR raw = nullptr;
    if (FAILED(SHGetKnownFolderPath(folder, KF_FLAG_DEFAULT, nullptr, &raw))) return 0;
    const std::size_t changed = RetargetDirectory(fs::path(raw), oldTarget, newTarget);
    CoTaskMemFree(raw);
    return changed;
}
}

std::size_t RetargetShortcutsIn(const fs::path& directory, const std::wstring& oldTarget,
                                const fs::path& newTarget) {
    return RetargetDirectory(directory, oldTarget, newTarget);
}

std::size_t RetargetShortcuts(const std::wstring& oldTarget, const fs::path& newTarget) {
    const bool initialized = SUCCEEDED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED));
    std::size_t changed = 0;
    for (const auto& folder : {FOLDERID_Desktop, FOLDERID_PublicDesktop, FOLDERID_Programs, FOLDERID_CommonPrograms}) {
        changed += RetargetKnownFolder(folder, oldTarget, newTarget);
    }
    if (initialized) CoUninitialize();
    return changed;
}
}
