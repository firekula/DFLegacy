#include "bootstrap.h"
#include <windows.h>
#include <bcrypt.h>
#include <wincrypt.h>
#include <algorithm>
#include <array>
#include <map>
#include <stdexcept>
#include <utility>

namespace dflegacy::startup {
namespace fs = std::filesystem;
namespace {
constexpr DWORD descriptorLimit = 4096;
constexpr DWORD logLimit = 16384;
struct Failure : std::runtime_error { using std::runtime_error::runtime_error; };
void Check(bool condition) { if (!condition) throw Failure("bootstrap operation failed"); }
class Handle {
    HANDLE value_ = INVALID_HANDLE_VALUE;
public:
    Handle() = default;
    explicit Handle(HANDLE value) : value_(value) {}
    ~Handle() { reset(); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    Handle(Handle&& other) noexcept : value_(std::exchange(other.value_, INVALID_HANDLE_VALUE)) {}
    Handle& operator=(Handle&& other) noexcept {
        if (this != &other) { reset(); value_ = std::exchange(other.value_, INVALID_HANDLE_VALUE); } return *this;
    }
    explicit operator bool() const { return value_ != INVALID_HANDLE_VALUE && value_ != nullptr; }
    HANDLE get() const { return value_; }
    void reset() { if (*this) CloseHandle(value_); value_ = INVALID_HANDLE_VALUE; }
};
BY_HANDLE_FILE_INFORMATION Information(const Handle& file) {
    BY_HANDLE_FILE_INFORMATION info{};
    Check(GetFileInformationByHandle(file.get(), &info) != FALSE);
    return info;
}
void Regular(const Handle& file) {
    const auto info = Information(file);
    Check(!(info.dwFileAttributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) && info.nNumberOfLinks == 1);
}
Handle ReadFileHandle(const fs::path& path) {
    Handle file(CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
    Check(static_cast<bool>(file)); Regular(file); return file;
}
fs::path Absolute(const fs::path& input) {
    const auto& text = input.native();
    Check(!text.empty() && text.find(L'\0') == std::wstring::npos);
    // A bootstrap installation is a local absolute drive path. Device namespaces and
    // UNC paths are not accepted, including extended paths that bypass Win32 parsing.
    Check(text.size() >= 3 && ((text[0] >= L'A' && text[0] <= L'Z') || (text[0] >= L'a' && text[0] <= L'z')) &&
        text[1] == L':' && (text[2] == L'\\' || text[2] == L'/'));
    for (const auto& part : input.relative_path()) {
        auto component = part.native();
        Check(component.empty() || (component != L"." && component != L".." && component.back() != L'.' &&
            component.back() != L' ' && component.find(L':') == std::wstring::npos));
    }
    std::array<wchar_t, 32768> buffer{};
    auto length = GetFullPathNameW(text.c_str(), static_cast<DWORD>(buffer.size()), buffer.data(), nullptr);
    Check(length > 0 && length < buffer.size());
    return fs::path(std::wstring(buffer.data(), length));
}
class Directories {
    std::vector<Handle> handles_;
public:
    void Hold(const fs::path& path) {
        Handle directory(CreateFileW(path.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
        Check(static_cast<bool>(directory));
        const auto info = Information(directory);
        Check((info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && !(info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT));
        handles_.push_back(std::move(directory));
    }
    void Tree(const fs::path& path) {
        fs::path current = path.root_path(); Hold(current);
        for (const auto& component : path.relative_path()) {
            if (component.empty()) continue;
            current /= component; Hold(current);
        }
    }
    void Create(const fs::path& path) {
        if (!CreateDirectoryW(path.c_str(), nullptr)) Check(GetLastError() == ERROR_ALREADY_EXISTS);
        Hold(path);
    }
    void Clear() { handles_.clear(); }
};
void Seek(const Handle& file, LONGLONG position) {
    LARGE_INTEGER offset{}; offset.QuadPart = position;
    Check(SetFilePointerEx(file.get(), offset, nullptr, FILE_BEGIN) != FALSE);
}
void ReadExact(const Handle& file, void* buffer, DWORD size) {
    DWORD count = 0; Check(ReadFile(file.get(), buffer, size, &count, nullptr) && count == size);
}
void WriteExact(const Handle& file, const void* buffer, DWORD size) {
    DWORD count = 0; Check(WriteFile(file.get(), buffer, size, &count, nullptr) && count == size);
}
std::string Hex(const unsigned char* data, size_t length) {
    constexpr char digits[] = "0123456789abcdef";
    std::string value; value.reserve(length * 2);
    for (size_t i = 0; i < length; ++i) { value += digits[data[i] >> 4]; value += digits[data[i] & 15]; }
    return value;
}
std::string Hash(const Handle& file) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    Check(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0);
    struct CloseHash {
        BCRYPT_ALG_HANDLE& algorithm; BCRYPT_HASH_HANDLE& hash;
        ~CloseHash() { if (hash) BCryptDestroyHash(hash); if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0); }
    } cleanup{algorithm, hash};
    Check(BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) >= 0);
    Seek(file, 0);
    std::array<unsigned char, 65536> bytes{};
    for (;;) {
        DWORD count = 0; Check(ReadFile(file.get(), bytes.data(), static_cast<DWORD>(bytes.size()), &count, nullptr) != FALSE);
        if (!count) break;
        Check(BCryptHashData(hash, bytes.data(), count, 0) >= 0);
    }
    std::array<unsigned char, 32> digest{};
    Check(BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) >= 0);
    return Hex(digest.data(), digest.size());
}
void Verify(const Handle& file, const std::string& expected) {
    Check(Hash(file) == expected);
    IMAGE_DOS_HEADER dos{}; Seek(file, 0); ReadExact(file, &dos, sizeof(dos));
    Check(dos.e_magic == IMAGE_DOS_SIGNATURE && dos.e_lfanew >= sizeof(dos));
    Seek(file, dos.e_lfanew);
    DWORD signature = 0; ReadExact(file, &signature, sizeof(signature));
    IMAGE_FILE_HEADER header{}; ReadExact(file, &header, sizeof(header));
    WORD magic = 0; ReadExact(file, &magic, sizeof(magic));
    Check(signature == IMAGE_NT_SIGNATURE && header.Machine == (sizeof(void*) == 8 ? IMAGE_FILE_MACHINE_AMD64 : IMAGE_FILE_MACHINE_I386) &&
        magic == (sizeof(void*) == 8 ? IMAGE_NT_OPTIONAL_HDR64_MAGIC : IMAGE_NT_OPTIONAL_HDR32_MAGIC) &&
        (header.Characteristics & IMAGE_FILE_EXECUTABLE_IMAGE) && !(header.Characteristics & IMAGE_FILE_DLL));
}
Descriptor ReadDescriptor(const fs::path& path) {
    auto file = ReadFileHandle(path);
    LARGE_INTEGER size{}; Check(GetFileSizeEx(file.get(), &size) && size.QuadPart > 0 && size.QuadPart <= descriptorLimit);
    std::string bytes(static_cast<size_t>(size.QuadPart), '\0');
    ReadExact(file, bytes.data(), static_cast<DWORD>(bytes.size()));
    auto result = ParseDescriptor(bytes, Architecture()); Check(result.has_value()); return *result;
}
std::string Encode(const Descriptor& descriptor) {
    return "[Bootstrap]\r\nSchemaVersion=1\r\nArchitecture=" + descriptor.architecture + "\r\nLauncherSha256=" +
        descriptor.launcherSha256 + "\r\nUpdaterSha256=" + descriptor.updaterSha256 + "\r\n";
}
std::string Nonce() {
    std::array<unsigned char, 16> bytes{};
    Check(BCryptGenRandom(nullptr, bytes.data(), static_cast<ULONG>(bytes.size()), BCRYPT_USE_SYSTEM_PREFERRED_RNG) >= 0);
    return Hex(bytes.data(), bytes.size());
}
Handle NewFile(const fs::path& path) {
    Handle file(CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    Check(static_cast<bool>(file)); Regular(file); return file;
}
void ValidateReplacement(const fs::path& path) {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES) {
        const auto error = GetLastError(); Check(error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND); return;
    }
    auto existing = ReadFileHandle(path); // Reject directory, reparse or hardlink targets before replacement.
}
struct PendingFile {
    fs::path path;
    ~PendingFile() { if (!path.empty()) DeleteFileW(path.c_str()); }
};
void CopyVerified(const Handle& source, const fs::path& target, const std::string& hash) {
    auto output = NewFile(target);
    Seek(source, 0); std::array<unsigned char, 65536> bytes{};
    for (;;) {
        DWORD count = 0; Check(ReadFile(source.get(), bytes.data(), static_cast<DWORD>(bytes.size()), &count, nullptr) != FALSE);
        if (!count) break;
        WriteExact(output, bytes.data(), count);
    }
    Check(FlushFileBuffers(output.get()) != FALSE); Verify(output, hash);
}
void Cache(const Handle& source, const fs::path& state, const Descriptor& descriptor) {
    const auto target = state / (descriptor.updaterSha256 + ".exe");
    ValidateReplacement(target);
    try { auto existing = ReadFileHandle(target); Verify(existing, descriptor.updaterSha256); return; }
    catch (const Failure&) { /* Missing/corrupt regular cache is repaired from the verified held source. */ }
    PendingFile pending{state / ("cache." + Nonce() + ".tmp")};
    CopyVerified(source, pending.path, descriptor.updaterSha256);
    Check(MoveFileExW(pending.path.c_str(), target.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE);
    pending.path.clear();
}
void CommitRecovery(const fs::path& state, const Descriptor& descriptor) {
    const auto target = state / L"recovery.ini"; ValidateReplacement(target);
    PendingFile pending{state / ("recovery." + Nonce() + ".tmp")};
    {
        auto output = NewFile(pending.path); const auto text = Encode(descriptor);
        WriteExact(output, text.data(), static_cast<DWORD>(text.size())); Check(FlushFileBuffers(output.get()) != FALSE);
    }
    Check(MoveFileExW(pending.path.c_str(), target.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE);
    pending.path.clear();
}
Handle Lock(const fs::path& path, bool& busy) {
    Handle lock(CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    if (!lock) {
        const auto error = GetLastError();
        if (error == ERROR_SHARING_VIOLATION || error == ERROR_LOCK_VIOLATION) { busy = true; return {}; }
        throw Failure("lock unavailable");
    }
    Regular(lock); return lock;
}
void Log(const fs::path& state, const char* message) noexcept {
    // Fixed event labels only: never write paths, the command line, account names or secrets.
    try {
        const auto path = state / L"bootstrap.log";
        Handle log(CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ, nullptr, OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
        Check(static_cast<bool>(log)); Regular(log);
        LARGE_INTEGER size{}; Check(GetFileSizeEx(log.get(), &size) != FALSE);
        const auto length = static_cast<DWORD>(strlen(message));
        if (size.QuadPart > logLimit - length) { Seek(log, 0); Check(SetEndOfFile(log.get()) != FALSE); }
        else Seek(log, size.QuadPart);
        WriteExact(log, message, length);
    } catch (...) { }
}
struct Candidate { Descriptor descriptor; fs::path path; };
std::optional<Candidate> Primary(const fs::path& root) {
    try {
        auto descriptor = ReadDescriptor(root / L"DFLegacy.Bootstrap.ini");
        auto path = root / L"DFLegacy.Updater.exe";
        auto file = ReadFileHandle(path); Verify(file, descriptor.updaterSha256);
        return Candidate{std::move(descriptor), std::move(path)};
    } catch (const Failure&) { return std::nullopt; }
}
std::optional<Candidate> Recovery(const fs::path& state) {
    try {
        auto descriptor = ReadDescriptor(state / L"recovery.ini");
        auto path = state / (descriptor.updaterSha256 + ".exe");
        auto file = ReadFileHandle(path); Verify(file, descriptor.updaterSha256);
        return Candidate{std::move(descriptor), std::move(path)};
    } catch (const Failure&) { return std::nullopt; }
}
class TemporaryUpdater {
    Directories parent_, owned_;
    fs::path directory_, executable_;
public:
    TemporaryUpdater() {
        std::array<wchar_t, 32768> text{};
        auto length = GetTempPathW(static_cast<DWORD>(text.size()), text.data());
        Check(length > 0 && length < text.size());
        auto temp = Absolute(fs::path(std::wstring(text.data(), length))); parent_.Tree(temp);
        directory_ = temp / ("DFLegacy-bootstrap-" + Nonce());
        Check(CreateDirectoryW(directory_.c_str(), nullptr) != FALSE);
        try { owned_.Hold(directory_); executable_ = directory_ / L"DFLegacy.Updater.exe"; }
        catch (...) { RemoveDirectoryW(directory_.c_str()); throw; }
    }
    ~TemporaryUpdater() {
        // Never recursively traverse a temp directory. Remove only our fixed child,
        // and then our own empty random directory; preserve unexpected contents.
        if (!executable_.empty()) DeleteFileW(executable_.c_str());
        owned_.Clear(); if (!directory_.empty()) RemoveDirectoryW(directory_.c_str());
    }
    const fs::path& executable() const { return executable_; }
};
struct Attempt { int exitCode; bool mayRetry; };
// This user-protected receipt is issued only after the verified worker succeeds.
// Attribute-only identities avoid reading executable or game resource contents.
std::string StartupIdentity(const fs::path& root) {
    for (const auto* pending : {L".dflegacy/v4/pending.json", L".dflegacy/pending.json"})
        Check(GetFileAttributesW((root / pending).c_str()) == INVALID_FILE_ATTRIBUTES &&
            (GetLastError() == ERROR_FILE_NOT_FOUND || GetLastError() == ERROR_PATH_NOT_FOUND));
    std::string bytes = "DFLegacy completed startup receipt 1";
    const auto text = root.native();
    bytes.append(reinterpret_cast<const char*>(text.data()), text.size() * sizeof(wchar_t));
    const auto append = [&](DWORD value) { bytes.append(reinterpret_cast<const char*>(&value), sizeof(value)); };
    append(sizeof(void*));
    for (const auto* name : {L"DFLegacy.Launcher.App.exe", L"DFLegacy.Updater.exe", L"DFLegacy.Bootstrap.ini",
        L".dflegacy/v4/installed.json", L".dflegacy/v4/validated.json"}) {
        Handle file(CreateFileW((root / name).c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
        Check(static_cast<bool>(file)); Regular(file); const auto info = Information(file);
        append(info.dwVolumeSerialNumber); append(info.nFileIndexHigh); append(info.nFileIndexLow);
        append(info.nFileSizeHigh); append(info.nFileSizeLow);
        append(info.ftLastWriteTime.dwHighDateTime); append(info.ftLastWriteTime.dwLowDateTime);
        append(info.ftCreationTime.dwHighDateTime); append(info.ftCreationTime.dwLowDateTime);
    }
    return bytes;
}
class ProtectedBytes {
public:
    DATA_BLOB value{};
    ~ProtectedBytes() { if (value.pbData) LocalFree(value.pbData); }
};
void CommitStartup(const fs::path& root, const fs::path& state) {
    // Re-read current metadata: the worker may have upgraded its own installed binaries.
    const auto descriptor = ReadDescriptor(root / L"DFLegacy.Bootstrap.ini");
    { auto app = ReadFileHandle(root / L"DFLegacy.Launcher.App.exe"); Verify(app, descriptor.launcherSha256); }
    const auto identity = StartupIdentity(root);
    DATA_BLOB plain{static_cast<DWORD>(identity.size()), reinterpret_cast<BYTE*>(const_cast<char*>(identity.data()))};
    ProtectedBytes encrypted;
    Check(CryptProtectData(&plain, L"DFLegacy completed startup", nullptr, nullptr, nullptr, CRYPTPROTECT_UI_FORBIDDEN, &encrypted.value) != FALSE);
    const auto target = state / L"startup.dat"; ValidateReplacement(target);
    PendingFile pending{state / ("startup." + Nonce() + ".tmp")};
    { auto file = NewFile(pending.path); WriteExact(file, encrypted.value.pbData, encrypted.value.cbData); Check(FlushFileBuffers(file.get()) != FALSE); }
    Check(MoveFileExW(pending.path.c_str(), target.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE);
    pending.path.clear();
}
std::optional<Attempt> FastStartup(const fs::path& root, const fs::path& state, const std::vector<std::wstring>& arguments) {
    try {
        // Explicit recheck/repair requests must execute the worker, including its parent wait.
        for (const auto& argument : arguments) if (argument.starts_with(L"--bootstrap-") || argument == L"--repair-client") return std::nullopt;
        auto receipt = ReadFileHandle(state / L"startup.dat");
        LARGE_INTEGER size{}; Check(GetFileSizeEx(receipt.get(), &size) && size.QuadPart > 0 && size.QuadPart <= 65536);
        std::vector<BYTE> bytes(static_cast<size_t>(size.QuadPart)); ReadExact(receipt, bytes.data(), static_cast<DWORD>(bytes.size()));
        DATA_BLOB encrypted{static_cast<DWORD>(bytes.size()), bytes.data()}; ProtectedBytes plain;
        Check(CryptUnprotectData(&encrypted, nullptr, nullptr, nullptr, nullptr, CRYPTPROTECT_UI_FORBIDDEN, &plain.value) != FALSE);
        const auto identity = StartupIdentity(root);
        Check(plain.value.cbData == identity.size() && memcmp(plain.value.pbData, identity.data(), identity.size()) == 0);
        const auto app = root / L"DFLegacy.Launcher.App.exe";
        Handle heldApp(CreateFileW(app.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
        Check(static_cast<bool>(heldApp)); Regular(heldApp);
        std::wstring command = QuoteArgument(app.wstring()) + L" --bootstrap-verified --";
        for (const auto& argument : arguments) { Check(argument.find(L'\0') == std::wstring::npos); command += L' '; command += QuoteArgument(argument); }
        Check(command.size() < 32767);
        const auto nonce = Nonce();
        const auto eventName = L"Local\\DFLegacy.Startup." + std::wstring(nonce.begin(), nonce.end());
        Handle ready(CreateEventW(nullptr, TRUE, FALSE, eventName.c_str())); Check(static_cast<bool>(ready));
        // Launches are serialized by entry.lock; do not leak the private event to later children.
        wchar_t previous[32768]{}; const auto length = GetEnvironmentVariableW(L"DFLEGACY_BOOTSTRAP_READY_EVENT", previous, 32768);
        Check(length < 32768); Check(SetEnvironmentVariableW(L"DFLEGACY_BOOTSTRAP_READY_EVENT", eventName.c_str()) != FALSE);
        STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION process{};
        const auto started = CreateProcessW(app.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &process);
        SetEnvironmentVariableW(L"DFLEGACY_BOOTSTRAP_READY_EVENT", length ? previous : nullptr);
        Check(started != FALSE); Handle child(process.hProcess), thread(process.hThread);
        bool headless = false;
        for (const auto& argument : arguments) for (const auto* flag : {L"--headless", L"--account", L"--account-index", L"--self-test", L"--install-ijl15", L"--check-d3d9", L"--no-dialog", L"--third-party-notices"})
            if (CompareStringOrdinal(argument.c_str(), -1, flag, -1, TRUE) == CSTR_EQUAL) headless = true;
        if (headless) {
            Check(WaitForSingleObject(child.get(), INFINITE) == WAIT_OBJECT_0);
            DWORD code = 1; Check(GetExitCodeProcess(child.get(), &code) != FALSE);
            // The app signals only after receipt validation and before executing
            // a command. Recover a rejected receipt, never repeat a completed command.
            if (WaitForSingleObject(ready.get(), 0) != WAIT_OBJECT_0) return std::nullopt;
            return Attempt{code == 0 ? 0 : 1, false};
        }
        HANDLE waits[]{ready.get(), child.get()}; const auto result = WaitForMultipleObjects(2, waits, FALSE, 30000);
        if (result == WAIT_OBJECT_0) return Attempt{0, false};
        // Never run recovery alongside an unconfirmed app.
        if (result != WAIT_OBJECT_0 + 1) {
            if (!TerminateProcess(child.get(), 1) || WaitForSingleObject(child.get(), 10000) != WAIT_OBJECT_0) return Attempt{1, false};
        }
        return std::nullopt;
    } catch (const Failure&) { return std::nullopt; }
}
Attempt Execute(const Candidate& candidate, bool cache, const fs::path& root, const fs::path& state,
    const std::vector<std::wstring>& arguments) {
    try {
        TemporaryUpdater temp;
        {
            auto source = ReadFileHandle(candidate.path); Verify(source, candidate.descriptor.updaterSha256);
            if (cache) Cache(source, state, candidate.descriptor);
            CopyVerified(source, temp.executable(), candidate.descriptor.updaterSha256);
        } // The installed updater may now be replaced by the temporary worker.
        auto executable = ReadFileHandle(temp.executable()); Verify(executable, candidate.descriptor.updaterSha256);
        std::wstring command = QuoteArgument(temp.executable().wstring()) + L" --bootstrap-client " + QuoteArgument(root.wstring()) + L" --";
        for (const auto& argument : arguments) {
            Check(argument.find(L'\0') == std::wstring::npos);
            command += L' '; command += QuoteArgument(argument);
        }
        Check(command.size() < 32767);
        STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION process{};
        Check(CreateProcessW(temp.executable().c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
            nullptr, root.c_str(), &startup, &process) != FALSE);
        Handle processHandle(process.hProcess), threadHandle(process.hThread);
        Check(WaitForSingleObject(processHandle.get(), INFINITE) == WAIT_OBJECT_0);
        DWORD result = 1; Check(GetExitCodeProcess(processHandle.get(), &result) != FALSE);
        executable.reset();
        if (result == 0) {
            // The successful worker may already have opened the UI. A failure to
            // persist its receipt must never start a second worker/UI via fallback.
            try { CommitRecovery(state, candidate.descriptor); }
            catch (const Failure&) { return {1, false}; }
            return {0, false};
        }
        if (result == 2) return {2, false};
        return {1, result == 1};
    } catch (const Failure&) { return {1, true}; }
}
}

std::string Architecture() { return sizeof(void*) == 8 ? "x64" : "x86"; }
std::optional<Descriptor> ParseDescriptor(std::string_view bytes, std::string_view architecture) {
    if (bytes.empty() || bytes.size() > descriptorLimit || (architecture != "x86" && architecture != "x64")) return std::nullopt;
    for (unsigned char byte : bytes) if ((byte < 32 && byte != '\r' && byte != '\n') || byte > 126) return std::nullopt;
    std::vector<std::string_view> lines;
    while (!bytes.empty()) {
        auto end = bytes.find('\n'); auto line = bytes.substr(0, end);
        if (!line.empty() && line.back() == '\r' && end != std::string_view::npos) line.remove_suffix(1);
        if (line.empty() || line.find('\r') != std::string_view::npos) return std::nullopt;
        lines.push_back(line);
        if (end == std::string_view::npos) break;
        bytes.remove_prefix(end + 1);
    }
    if (lines.size() != 5 || lines[0] != "[Bootstrap]") return std::nullopt;
    std::map<std::string_view, std::string_view> keys;
    for (size_t i = 1; i < lines.size(); ++i) {
        auto position = lines[i].find('=');
        if (position == std::string_view::npos || !keys.emplace(lines[i].substr(0, position), lines[i].substr(position + 1)).second) return std::nullopt;
    }
    if (keys.size() != 4 || keys["SchemaVersion"] != "1" || keys["Architecture"] != architecture) return std::nullopt;
    const auto validHash = [](std::string_view hash) {
        return hash.size() == 64 && std::all_of(hash.begin(), hash.end(), [](char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'); });
    };
    if (!validHash(keys["LauncherSha256"]) || !validHash(keys["UpdaterSha256"]) || keys.size() != 4) return std::nullopt;
    return Descriptor{std::string(architecture), std::string(keys["LauncherSha256"]), std::string(keys["UpdaterSha256"])};
}
std::wstring QuoteArgument(std::wstring_view argument) {
    std::wstring result = L"\""; size_t slashes = 0;
    for (wchar_t character : argument) {
        if (character == L'\\') { ++slashes; continue; }
        result.append(slashes * (character == L'\"' ? 2 : 1), L'\\'); slashes = 0;
        if (character == L'\"') result += L'\\';
        result += character;
    }
    result.append(slashes * 2, L'\\'); result += L'\"'; return result;
}
bool SuppressDialogs(const std::vector<std::wstring>& arguments) {
    constexpr std::wstring_view flags[] = {
        L"--no-dialog", L"--third-party-notices", L"--self-test", L"--install-ijl15", L"--check-d3d9"
    };
    for (const auto& argument : arguments) {
        for (const auto flag : flags) {
            if (argument.size() == flag.size() && CompareStringOrdinal(argument.data(), static_cast<int>(argument.size()),
                flag.data(), static_cast<int>(flag.size()), TRUE) == CSTR_EQUAL) return true;
        }
    }
    return false;
}
Result Run(const fs::path& input, const std::vector<std::wstring>& arguments) {
    try {
        const auto root = Absolute(input); Directories directories; directories.Tree(root);
        const auto state = root / L".dflegacy/bootstrap";
        directories.Create(root / L".dflegacy"); directories.Create(state);
        bool busy = false; auto entryLock = Lock(state / L"entry.lock", busy);
        if (busy) return {0, true, {}};
        { auto appLock = Lock(state / L"app.lock", busy); if (busy) return {0, true, {}}; }
        if (const auto fast = FastStartup(root, state, arguments)) {
            Log(state, fast->exitCode == 0 ? "Completed receipt opened app.\r\n" : "Receipt app command failed.\r\n");
            return {fast->exitCode, false, fast->exitCode == 0 ? L"" : L"登录器命令未成功完成，请查看登录器日志后重试。"};
        }
        const auto old = Recovery(state); const auto primary = Primary(root);
        if (!primary && !old) {
            Log(state, "No verified updater available.\r\n");
            return {1, false, L"无法验证更新程序或本地恢复副本。请恢复完整的启动包后重试。"};
        }
        auto attempt = Execute(primary ? *primary : *old, primary.has_value(), root, state, arguments);
        if (attempt.exitCode == 1 && attempt.mayRetry && primary && old && primary->descriptor.updaterSha256 != old->descriptor.updaterSha256) {
            Log(state, "Primary failed; trying prior verified recovery once.\r\n");
            attempt = Execute(*old, false, root, state, arguments);
        }
        if (attempt.exitCode == 0) {
            // Failure to save an optimization must never launch a second UI.
            try { CommitStartup(root, state); } catch (const Failure&) { Log(state, "Startup receipt unavailable; next start verifies.\r\n"); }
            Log(state, "Verified updater completed.\r\n"); return {0, false, {}};
        }
        if (attempt.exitCode == 2) { Log(state, "Update cancelled.\r\n"); return {2, false, {}}; }
        Log(state, "Verified updater failed.\r\n");
        return {1, false, L"更新或自动修复未完成。请查看更新程序提示后重试；本地恢复副本已保留。"};
    } catch (...) {
        return {1, false, L"无法安全启动更新程序。请检查客户端路径、目录权限和可用磁盘空间后重试。"};
    }
}
}
