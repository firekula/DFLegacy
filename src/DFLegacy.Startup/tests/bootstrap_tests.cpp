#include "bootstrap.h"
#include <windows.h>
#include <bcrypt.h>
#include <winioctl.h>
#include <algorithm>
#include <array>
#include <fstream>
#include <functional>
#include <future>
#include <iostream>
#include <sstream>
#include <stdexcept>

namespace fs = std::filesystem;
using namespace dflegacy::startup;
namespace {
void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
void Write(const fs::path& path, const std::string& value) {
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    output << value;
    Require(static_cast<bool>(output), "fixture write failed");
}
std::string Read(const fs::path& path) {
    std::ifstream input(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(input), {}};
}
std::string Hash(const fs::path& path) {
    const auto bytes = Read(path);
    std::array<unsigned char, 32> hash{};
    Require(BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0,
        reinterpret_cast<PUCHAR>(const_cast<char*>(bytes.data())), static_cast<ULONG>(bytes.size()),
        hash.data(), static_cast<ULONG>(hash.size())) == 0, "fixture hash failed");
    const char* hex = "0123456789abcdef";
    std::string result;
    for (auto byte : hash) { result += hex[byte >> 4]; result += hex[byte & 15]; }
    return result;
}
std::string DescriptorBytes(const std::string& hash, const std::string& arch = Architecture()) {
    return "[Bootstrap]\r\nSchemaVersion=1\r\nArchitecture=" + arch + "\r\nLauncherSha256=" +
        std::string(64, 'a') + "\r\nUpdaterSha256=" + hash + "\r\n";
}
struct Root {
    fs::path path;
    explicit Root(unsigned index) {
        wchar_t temp[32768]{};
        Require(GetTempPathW(32768, temp) > 0, "no temp path");
        path = fs::path(temp) / (L"DFLegacy native tests 中文 " + std::to_wstring(GetCurrentProcessId()) + L" " + std::to_wstring(index));
        Require(!fs::exists(path), "fixture path already exists");
        fs::create_directories(path / L".dflegacy/bootstrap");
    }
    ~Root() { std::error_code error; fs::remove_all(path, error); }
    fs::path state() const { return path / L".dflegacy/bootstrap"; }
    void Primary(const fs::path& exe) {
        fs::copy_file(exe, path / L"DFLegacy.Updater.exe", fs::copy_options::overwrite_existing);
        Write(path / L"DFLegacy.Bootstrap.ini", DescriptorBytes(Hash(exe)));
    }
    void Recovery(const fs::path& exe) {
        auto hash = Hash(exe);
        fs::copy_file(exe, state() / (hash + ".exe"), fs::copy_options::overwrite_existing);
        Write(state() / L"recovery.ini", DescriptorBytes(hash));
    }
    void Completed(const fs::path& exe) {
        Primary(exe); fs::copy_file(exe, path / L"DFLegacy.Launcher.App.exe");
        auto descriptor = DescriptorBytes(Hash(exe));
        descriptor.replace(descriptor.find(std::string(64, 'a')), 64, Hash(exe));
        Write(path / L"DFLegacy.Bootstrap.ini", descriptor);
        fs::create_directories(path / L".dflegacy/v4");
        Write(path / L".dflegacy/v4/installed.json", "fixture verified installation");
        Write(path / L".dflegacy/v4/validated.json", "fixture completed verification");
    }
};
struct Observation { int result; fs::path image; fs::path root; std::vector<std::wstring> arguments; };
std::vector<Observation> Observations(const Root& root) {
    std::ifstream input(root.path / L"observed.bin", std::ios::binary);
    std::vector<Observation> results;
    const auto readString = [&]() {
        unsigned size = 0; input.read(reinterpret_cast<char*>(&size), sizeof(size));
        Require(size < 32768, "bad fixture string");
        std::wstring value(size, L'\0');
        input.read(reinterpret_cast<char*>(value.data()), size * sizeof(wchar_t));
        return value;
    };
    Observation value{};
    while (input.read(reinterpret_cast<char*>(&value.result), sizeof(value.result))) {
        value.image = readString(); value.root = readString(); value.arguments.clear();
        unsigned count = 0; input.read(reinterpret_cast<char*>(&count), sizeof(count));
        Require(count < 1024, "bad fixture count");
        while (count--) value.arguments.push_back(readString());
        Require(static_cast<bool>(input), "truncated fixture output");
        results.push_back(value);
    }
    return results;
}
class Lock {
    HANDLE handle_;
public:
    explicit Lock(const fs::path& path) : handle_(CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0,
        nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr)) { Require(handle_ != INVALID_HANDLE_VALUE, "test lock failed"); }
    ~Lock() { CloseHandle(handle_); }
};
void Junction(const fs::path& link, const fs::path& target) {
    fs::create_directory(link);
    HANDLE directory = CreateFileW(link.c_str(), GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
        FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, nullptr);
    Require(directory != INVALID_HANDLE_VALUE, "junction open failed");
    struct JunctionData {
        DWORD tag; WORD length, reserved;
        WORD substituteOffset, substituteLength, printOffset, printLength;
        wchar_t paths[16384];
    } data{};
    const auto substitute = L"\\??\\" + target.wstring();
    const auto print = target.wstring();
    data.tag = IO_REPARSE_TAG_MOUNT_POINT;
    data.substituteLength = static_cast<WORD>(substitute.size() * sizeof(wchar_t));
    data.printOffset = data.substituteLength + sizeof(wchar_t);
    data.printLength = static_cast<WORD>(print.size() * sizeof(wchar_t));
    data.length = 8 + data.printOffset + data.printLength + sizeof(wchar_t);
    memcpy(data.paths, substitute.c_str(), data.substituteLength + sizeof(wchar_t));
    memcpy(reinterpret_cast<char*>(data.paths) + data.printOffset, print.c_str(), data.printLength + sizeof(wchar_t));
    DWORD returned = 0;
    const bool ok = DeviceIoControl(directory, FSCTL_SET_REPARSE_POINT, &data, data.length + 8, nullptr, 0, &returned, nullptr) != FALSE;
    CloseHandle(directory); Require(ok, "junction creation failed");
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 7) return 99;
    const fs::path good(argv[1]), fail(argv[2]), cancel(argv[3]), unknown(argv[4]), terminal(argv[5]), launcher(argv[6]);
    unsigned passed = 0, failed = 0, index = 0;
    const auto test = [&](const char* name, const std::function<void()>& run) {
        try { run(); ++passed; std::cout << "PASS " << name << '\n'; }
        catch (const std::exception& error) { ++failed; std::cerr << "FAIL " << name << ": " << error.what() << '\n'; }
    };
    test("strict descriptor valid LF/CRLF", [&] {
        auto text = DescriptorBytes(std::string(64, 'b'));
        auto parsed = ParseDescriptor(text, Architecture());
        Require(parsed && parsed->launcherSha256 == std::string(64, 'a') && parsed->updaterSha256 == std::string(64, 'b'), "valid descriptor rejected");
        text.erase(std::remove(text.begin(), text.end(), '\r'), text.end());
        Require(ParseDescriptor(text, Architecture()).has_value(), "LF descriptor rejected");
        text.pop_back(); Require(ParseDescriptor(text, Architecture()).has_value(), "no final newline rejected");
    });
    test("strict descriptor malformed/duplicate/schema/architecture/hash/size", [&] {
        const auto valid = DescriptorBytes(std::string(64, 'b'));
        std::vector<std::string> invalid = {"", "\xef\xbb\xbf" + valid, valid + "SchemaVersion=1\n", valid + "Path=evil\n",
            valid + "[Other]\n", valid + "[Bootstrap]\n", " " + valid, valid + std::string(1, '\0'),
            DescriptorBytes(std::string(64, 'B')), DescriptorBytes(std::string(63, 'b')), DescriptorBytes(std::string(64, 'z')),
            DescriptorBytes(std::string(64, 'b'), Architecture() == "x86" ? "x64" : "x86"), std::string(4097, 'a')};
        auto schema = valid; schema.replace(schema.find("SchemaVersion=1"), 15, "SchemaVersion=2"); invalid.push_back(schema);
        auto whitespace = valid; whitespace.insert(whitespace.find("Architecture="), " "); invalid.push_back(whitespace);
        for (const auto& value : invalid) Require(!ParseDescriptor(value, Architecture()), "unsafe descriptor accepted");
    });
    test("Windows argument quoting", [&] {
        Require(QuoteArgument(L"") == L"\"\"", "empty argument lost");
        Require(QuoteArgument(L"a b\\") == L"\"a b\\\\\"", "terminal slash not doubled");
        Require(QuoteArgument(L"a\"b") == L"\"a\\\"b\"", "quote not escaped");
    });
    test("primary process Unicode args temp cache and successful receipt", [&] {
        Root root(++index); root.Primary(good);
        const std::vector<std::wstring> args = {L"", L"中文 空格", L"a\"b", L"a\\\"b", L"tail \\", L"--password=secret"};
        auto result = Run(root.path, args);
        Require(result.exitCode == 0 && !result.alreadyRunning, "primary failed");
        auto observed = Observations(root);
        Require(observed.size() == 1 && observed[0].arguments == args && observed[0].root == root.path, "argument/root mismatch");
        Require(!fs::exists(observed[0].image.parent_path()), "owned temp not cleaned");
        Require(Hash(root.state() / (Hash(good) + ".exe")) == Hash(good), "cache missing");
        Require(Read(root.state() / L"recovery.ini") == DescriptorBytes(Hash(good)), "receipt not committed");
        Require(Read(root.state() / L"bootstrap.log").find("secret") == std::string::npos, "argument logged");
    });
    test("missing or corrupt primary recovers", [&] {
        for (int mode = 0; mode < 3; ++mode) {
            Root root(++index); root.Recovery(good);
            if (mode == 1) { root.Primary(fail); Write(root.path / L"DFLegacy.Updater.exe", "corrupt"); }
            if (mode == 2) Write(root.path / L"DFLegacy.Bootstrap.ini", "broken");
            Require(Run(root.path, {}).exitCode == 0, "recovery failed");
            auto observed = Observations(root); Require(observed.size() == 1 && observed[0].result == 0, "wrong recovery process");
        }
    });
    test("completed startup receipt opens app directly and invalidates on critical change", [&] {
        Root root(++index); root.Completed(good);
        Require(Run(root.path, {}).exitCode == 0, "initial verified updater failed");
        Require(Run(root.path, {L"--self-test", L"中文 fast"}).exitCode == 0, "receipt fast launch failed");
        auto observed = Observations(root);
        Require(observed.size() == 2 && observed[1].image == root.path / L"DFLegacy.Launcher.App.exe" &&
            observed[1].arguments == std::vector<std::wstring>{L"--self-test", L"中文 fast"}, "unchanged startup reran updater instead of app");
        Write(root.path / L"reject-fast", "");
        Require(Run(root.path, {}).exitCode == 0, "unready receipt app did not enter recovery");
        observed = Observations(root);
        Require(observed.size() == 4 && observed[2].image.parent_path() == root.path && observed[3].image.parent_path() != root.path,
            "rejected app must exit before the verified worker takes over");
        Require(Run(root.path, {L"--self-test"}).exitCode == 0, "headless app rejected before readiness must recover before executing the command");
        fs::remove(root.path / L"reject-fast");
        Write(root.path / L"DFLegacy.Launcher.App.exe", "damaged app");
        Require(Run(root.path, {}).exitCode == 0 && Observations(root).back().image.parent_path() != root.path, "critical app corruption bypassed repair");
    });
    test("headless receipt command failure after readiness never repeats command", [&] {
        Root root(++index); root.Completed(good);
        fs::copy_file(terminal, root.path / L"DFLegacy.Launcher.App.exe", fs::copy_options::overwrite_existing);
        auto descriptor = DescriptorBytes(Hash(good));
        descriptor.replace(descriptor.find(std::string(64, 'a')), 64, Hash(terminal));
        Write(root.path / L"DFLegacy.Bootstrap.ini", descriptor);
        Require(Run(root.path, {}).exitCode == 0, "failed to establish terminal app receipt");
        Require(Run(root.path, {L"--self-test"}).exitCode == 1, "terminal headless command was reported as success");
        const auto observed = Observations(root);
        Require(observed.size() == 2 && observed.back().image.parent_path() == root.path && observed.back().result == 3,
            "completed headless command retried through a worker");
    });
    test("startup receipt rejects missing files tampering pending journals and another root", [&] {
        for (const auto* changed : {L".dflegacy/bootstrap/startup.dat", L"DFLegacy.Launcher.App.exe", L"DFLegacy.Updater.exe",
            L"DFLegacy.Bootstrap.ini", L".dflegacy/v4/installed.json", L".dflegacy/v4/validated.json",
            L".dflegacy/v4/pending.json", L".dflegacy/pending.json"}) {
            Root root(++index); root.Completed(good);
            Require(Run(root.path, {}).exitCode == 0, "initial completion failed");
            if (std::wstring(changed).ends_with(L".exe")) fs::remove(root.path / changed);
            else Write(root.path / changed, "changed identity");
            Require(Run(root.path, {L"--self-test"}).exitCode == 0, "receipt invalidation did not use available recovery");
            const auto observed = Observations(root);
            Require(observed.size() == 2 && observed.back().image.parent_path() != root.path, "invalid receipt or pending transaction launched app directly");
        }
        Root first(++index); first.Completed(good); Require(Run(first.path, {}).exitCode == 0, "source completion failed");
        Root other(++index); other.Completed(good);
        fs::copy_file(first.state() / L"startup.dat", other.state() / L"startup.dat");
        Require(Run(other.path, {L"--self-test"}).exitCode == 0 && Observations(other).back().image.parent_path() != other.path,
            "copied receipt authorized another installation root");
    });
    test("failed new updater preserves old receipt and retries different recovery once", [&] {
        Root root(++index); root.Primary(fail); root.Recovery(good);
        auto receipt = Read(root.state() / L"recovery.ini");
        Require(Run(root.path, {}).exitCode == 0, "fallback failed");
        auto observed = Observations(root);
        Require(observed.size() == 2 && observed[0].result == 1 && observed[1].result == 0, "fallback order/count wrong");
        Require(Read(root.state() / L"recovery.ini") == receipt, "failed primary became recovery");
        Require(fs::exists(root.state() / (Hash(fail) + ".exe")), "primary not cached before execution");
    });
    test("cancellation never falls back or advances recovery", [&] {
        Root root(++index); root.Primary(cancel); root.Recovery(good);
        auto receipt = Read(root.state() / L"recovery.ini");
        Require(Run(root.path, {}).exitCode == 2, "cancellation lost");
        Require(Observations(root).size() == 1, "cancel launched fallback");
        Require(Read(root.state() / L"recovery.ini") == receipt, "cancel advanced recovery");
    });
    test("same failed hash is not retried", [&] {
        Root root(++index); root.Primary(fail); root.Recovery(fail);
        Require(Run(root.path, {}).exitCode == 1, "failure lost");
        Require(Observations(root).size() == 1, "same hash retried");
    });
    test("unknown exit code is failure without retry", [&] {
        Root root(++index); root.Primary(unknown); root.Recovery(good);
        Require(Run(root.path, {}).exitCode == 1, "unknown exit succeeded");
        Require(Observations(root).size() == 1, "non-contract exit retried");
    });
    test("terminal CLI failure exit3 never retries recovery", [&] {
        Root root(++index); root.Primary(terminal); root.Recovery(good);
        auto receipt = Read(root.state() / L"recovery.ini");
        Require(Run(root.path, {L"--headless"}).exitCode == 1, "terminal CLI exit did not map to failure");
        const auto observed = Observations(root);
        Require(observed.size() == 1 && observed[0].result == 3, "terminal CLI command retried");
        Require(Read(root.state() / L"recovery.ini") == receipt, "terminal CLI failure advanced recovery");
    });
    test("only exact managed no-dialog flags suppress native dialogs", [&] {
        for (const auto* flag : {L"--no-dialog", L"--third-party-notices", L"--self-test", L"--install-ijl15", L"--check-d3d9", L"--No-Dialog", L"--SELF-TEST"})
            Require(SuppressDialogs({L"unrelated", flag}), "managed quiet flag not recognized");
        for (const auto* flag : {L"", L"--headless", L"--account", L"--account-index", L"--no-dialog=true", L"prefix--self-test", L"--check-d3d9x"})
            Require(!SuppressDialogs({flag}), "unrelated flag suppressed native errors");
        Require(!SuppressDialogs({}), "normal GUI startup suppressed errors");
    });
    test("actual fixed entry exits quietly for CLI failure", [&] {
        for (const auto* flag : {L"--no-dialog", L"--third-party-notices", L"--self-test", L"--install-ijl15", L"--check-d3d9", L"--No-Dialog"}) {
            Root root(++index); root.Primary(terminal); root.Recovery(good);
            const auto target = root.path / L"DFLegacy.Launcher.exe"; fs::copy_file(launcher, target);
            std::wstring command = L"\"" + target.wstring() + L"\" " + flag;
            STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION process{};
            Require(CreateProcessW(target.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr,
                root.path.c_str(), &startup, &process), "quiet launcher start failed");
            const auto waited = WaitForSingleObject(process.hProcess, 3000);
            if (waited != WAIT_OBJECT_0) { TerminateProcess(process.hProcess, 98); WaitForSingleObject(process.hProcess, 3000); }
            DWORD exitCode = 99; GetExitCodeProcess(process.hProcess, &exitCode);
            CloseHandle(process.hThread); CloseHandle(process.hProcess);
            Require(waited == WAIT_OBJECT_0 && exitCode == 1, "quiet CLI failure displayed a blocking native error dialog");
            const auto observed = Observations(root);
            Require(observed.size() == 1 && observed[0].result == 3, "quiet CLI failure was retried");
        }
    });
    test("invalid hash or machine never executes", [&] {
        Root root(++index); root.Primary(good);
        Write(root.path / L"DFLegacy.Updater.exe", "not a PE");
        Require(Run(root.path, {}).exitCode == 1 && Observations(root).empty(), "hash mismatch executed");
        Write(root.path / L"DFLegacy.Bootstrap.ini", DescriptorBytes(Hash(root.path / L"DFLegacy.Updater.exe")));
        Require(Run(root.path, {}).exitCode == 1 && Observations(root).empty(), "non-PE executed");
        root.Primary(good);
        auto bytes = Read(root.path / L"DFLegacy.Updater.exe");
        const auto offset = *reinterpret_cast<const unsigned*>(bytes.data() + 0x3c);
        const unsigned short wrong = Architecture() == "x86" ? IMAGE_FILE_MACHINE_AMD64 : IMAGE_FILE_MACHINE_I386;
        memcpy(bytes.data() + offset + 4, &wrong, sizeof(wrong));
        Write(root.path / L"DFLegacy.Updater.exe", bytes);
        Write(root.path / L"DFLegacy.Bootstrap.ini", DescriptorBytes(Hash(root.path / L"DFLegacy.Updater.exe")));
        Require(Run(root.path, {}).exitCode == 1 && Observations(root).empty(), "wrong PE architecture executed");
    });
    test("entry and app exclusive locks suppress duplicate process", [&] {
        for (const auto* name : {L"entry.lock", L"app.lock"}) {
            Root root(++index); root.Primary(good); Lock lock(root.state() / name);
            auto result = Run(root.path, {});
            Require(result.exitCode == 0 && result.alreadyRunning && Observations(root).empty(), "lock did not suppress launch");
        }
    });
    test("entry lock spans actual child and recovery changes after success", [&] {
        Root root(++index); root.Primary(good); root.Recovery(cancel); Write(root.path / L"hold", "");
        auto future = std::async(std::launch::async, [&] { return Run(root.path, {}); });
        for (int i = 0; i < 100 && !fs::exists(root.path / L"observed.bin"); ++i) Sleep(20);
        auto second = Run(root.path, {});
        const bool wasOld = Read(root.state() / L"recovery.ini") == DescriptorBytes(Hash(cancel));
        const bool wasCached = fs::exists(root.state() / (Hash(good) + ".exe"));
        Write(root.path / L"release", "");
        auto first = future.get();
        Require(second.alreadyRunning && second.exitCode == 0 && first.exitCode == 0, "concurrent child allowed");
        Require(wasOld && wasCached, "receipt advanced before exit or cache missing");
        Require(Read(root.state() / L"recovery.ini") == DescriptorBytes(Hash(good)), "successful receipt not advanced");
    });
    test("hard-linked updater rejected", [&] {
        Root root(++index); root.Primary(good);
        Require(CreateHardLinkW((root.path / L"alias.exe").c_str(), (root.path / L"DFLegacy.Updater.exe").c_str(), nullptr), "hardlink fixture failed");
        Require(Run(root.path, {}).exitCode == 1 && Observations(root).empty(), "hard-linked updater executed");
    });
    test("reparse directory traversal refused for root state and temp", [&] {
        Root root(++index), elsewhere(++index); root.Primary(good);
        auto alias = elsewhere.path / L"alias";
        Junction(alias, root.path);
        auto aliased = Run(alias, {});
        Require(RemoveDirectoryW(alias.c_str()) != FALSE, "junction cleanup failed");
        Require(aliased.exitCode == 1 && Observations(root).empty(), "root junction followed");
        fs::remove_all(root.path / L".dflegacy");
        Junction(root.path / L".dflegacy", elsewhere.path);
        auto state = Run(root.path, {});
        Require(RemoveDirectoryW((root.path / L".dflegacy").c_str()) != FALSE, "state junction cleanup failed");
        Require(state.exitCode == 1 && Observations(root).empty(), "state junction followed");
        Junction(alias, elsewhere.path);
        wchar_t saved[32768]{}; const auto length = GetEnvironmentVariableW(L"TMP", saved, 32768);
        Require(SetEnvironmentVariableW(L"TMP", alias.c_str()) != FALSE, "temp environment failed");
        auto temp = Run(root.path, {});
        SetEnvironmentVariableW(L"TMP", length ? saved : nullptr);
        Require(RemoveDirectoryW(alias.c_str()) != FALSE, "temp junction cleanup failed");
        Require(temp.exitCode == 1 && Observations(root).empty(), "temp junction followed");
    });
    test("receipt commit failure after success cannot launch fallback", [&] {
        Root root(++index); root.Primary(good); root.Recovery(cancel);
        const auto receipt = Read(root.state() / L"recovery.ini");
        HANDLE reader = CreateFileW((root.state() / L"recovery.ini").c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        Require(reader != INVALID_HANDLE_VALUE, "receipt reader failed");
        auto result = Run(root.path, {});
        CloseHandle(reader);
        Require(result.exitCode == 1, "receipt commit failure status lost");
        Require(Observations(root).size() == 1, "successful worker retried after receipt commit failure");
        Require(Read(root.state() / L"recovery.ini") == receipt, "failed atomic commit damaged prior receipt");
        for (const auto& entry : fs::directory_iterator(root.state())) Require(entry.path().extension() != L".tmp", "owned pending receipt leaked");
    });
    test("two failing distinct updaters stop after bounded fallback", [&] {
        Root root(++index); root.Primary(fail); root.Recovery(unknown);
        Require(Run(root.path, {}).exitCode == 1, "both failing workers accepted");
        auto observed = Observations(root);
        Require(observed.size() == 2 && observed[0].result == 1 && observed[1].result == 7, "fallback loop or missing fallback");
    });
    test("invalid recovery hash and descriptor cannot execute", [&] {
        Root root(++index); root.Recovery(good);
        Write(root.state() / (Hash(good) + ".exe"), "broken");
        Require(Run(root.path, {}).exitCode == 1 && Observations(root).empty(), "corrupt recovery executed");
        root.Recovery(good);
        Write(root.state() / L"recovery.ini", DescriptorBytes(Hash(good)) + "UpdaterPath=../evil.exe\n");
        Require(Run(root.path, {}).exitCode == 1 && Observations(root).empty(), "unexpected recovery path accepted");
    });
    test("preexisting corrupt cache replaced only from verified primary", [&] {
        Root root(++index); root.Primary(good);
        Write(root.state() / (Hash(good) + ".exe"), "broken");
        Require(Run(root.path, {}).exitCode == 0, "corrupt cache prevented valid primary");
        Require(Hash(root.state() / (Hash(good) + ".exe")) == Hash(good), "cache not repaired");
    });
    test("diagnostic log stays bounded and contains no arguments", [&] {
        Root root(++index); root.Primary(fail);
        Write(root.state() / L"bootstrap.log", std::string(17000, 'X'));
        Require(Run(root.path, {L"private-account-password"}).exitCode == 1, "failed fixture status lost");
        const auto log = Read(root.state() / L"bootstrap.log");
        Require(log.size() <= 16384 && log.find("private-account-password") == std::string::npos, "diagnostic log bounds/privacy failed");
    });
    test("temporary worker can replace primary while recovery retains executed hash", [&] {
        Root root(++index); root.Primary(good);
        fs::copy_file(fail, root.path / L"replacement.exe");
        Write(root.path / L"replacement.ini", DescriptorBytes(Hash(fail)));
        Require(Run(root.path, {}).exitCode == 0, "running temporary worker could not replace primary");
        Require(Hash(root.path / L"DFLegacy.Updater.exe") == Hash(fail), "new primary not installed");
        Require(Read(root.state() / L"recovery.ini") == DescriptorBytes(Hash(good)), "unexecuted replacement became known-good recovery");
        Require(Run(root.path, {}).exitCode == 0, "replaced failing primary could not recover");
        const auto observed = Observations(root);
        Require(observed.size() == 3 && observed[0].result == 0 && observed[1].result == 1 && observed[2].result == 0,
            "selfreplacement lost prior good worker or exceeded fallback bound");
    });
    test("actual fixed launcher executable runs valid updater", [&] {
        Root root(++index); root.Primary(good);
        auto target = root.path / L"DFLegacy.Launcher.exe";
        fs::copy_file(launcher, target);
        std::wstring command = L"\"" + target.wstring() + L"\" \"entry arg\"";
        STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION process{};
        Require(CreateProcessW(target.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, root.path.c_str(), &startup, &process), "launcher start failed");
        auto waited = WaitForSingleObject(process.hProcess, 10000);
        if (waited != WAIT_OBJECT_0) TerminateProcess(process.hProcess, 98);
        DWORD exitCode = 99; GetExitCodeProcess(process.hProcess, &exitCode);
        CloseHandle(process.hThread); CloseHandle(process.hProcess);
        Require(waited == WAIT_OBJECT_0 && exitCode == 0, "actual launcher failed");
        auto observed = Observations(root);
        Require(observed.size() == 1 && observed[0].arguments == std::vector<std::wstring>{L"entry arg"}, "actual entry args lost");
    });
    std::cout << passed << " passed, " << failed << " failed\n";
    return failed ? 1 : 0;
}
