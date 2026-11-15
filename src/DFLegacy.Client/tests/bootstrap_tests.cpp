#include <windows.h>
#include <bcrypt.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>
#include <stdexcept>

namespace fs = std::filesystem;
static void Check(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }
static std::wstring Quote(const std::wstring& value) { return L"\"" + value + L"\""; }
static std::string Hash(const fs::path& file)
{
    std::ifstream input(file, std::ios::binary);
    std::vector<unsigned char> bytes((std::istreambuf_iterator<char>(input)), {});
    unsigned char hash[32]{};
    Check(BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0, bytes.data(),
        static_cast<ULONG>(bytes.size()), hash, sizeof(hash)) == 0, "hash failed");
    std::string result;
    constexpr char digits[] = "0123456789abcdef";
    for (auto b : hash) { result += digits[b >> 4]; result += digits[b & 15]; }
    return result;
}
struct Launch
{
    HANDLE process = nullptr, input = nullptr, output = nullptr, child = nullptr;
    DWORD pid = 0;
    ~Launch()
    {
        if (input) CloseHandle(input);
        if (process) { if (WaitForSingleObject(process, 3000) == WAIT_TIMEOUT) TerminateProcess(process, 91); CloseHandle(process); }
        if (child) { TerminateProcess(child, 92); WaitForSingleObject(child, 3000); CloseHandle(child); }
        if (output) CloseHandle(output);
    }
    void Start(const fs::path& helper, const fs::path& exe, const fs::path& dll,
        const fs::path& marker, const std::string& hash, bool test = false)
    {
        SECURITY_ATTRIBUTES sa{ sizeof(sa), nullptr, TRUE };
        HANDLE readIn = nullptr, writeOut = nullptr;
        Check(CreatePipe(&readIn, &input, &sa, 0) && CreatePipe(&output, &writeOut, &sa, 0), "pipes");
        SetHandleInformation(input, HANDLE_FLAG_INHERIT, 0);
        SetHandleInformation(output, HANDLE_FLAG_INHERIT, 0);
        STARTUPINFOW si{ sizeof(si) }; si.dwFlags = STARTF_USESTDHANDLES;
        si.hStdInput = readIn; si.hStdOutput = writeOut; si.hStdError = writeOut;
        PROCESS_INFORMATION pi{};
        std::wstring cmd = Quote(helper.wstring()) + L" --client " + Quote(exe.wstring())
            + L" --patch " + Quote(dll.wstring()) + L" --sha256 " + std::wstring(hash.begin(), hash.end())
            + L" --mode " + (test ? L"test" : L"normal") + L" --nonce 0123456789abcdef0123456789abcdef -- " + Quote(marker.wstring());
        const BOOL started = CreateProcessW(helper.c_str(), cmd.data(), nullptr, nullptr, TRUE,
            CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi);
        CloseHandle(readIn); CloseHandle(writeOut);
        Check(started != FALSE, "start helper"); process = pi.hProcess; CloseHandle(pi.hThread);
    }
    std::string Line()
    {
        std::string line;
        const ULONGLONG deadline = GetTickCount64() + 13000;
        while (GetTickCount64() < deadline)
        {
            DWORD available = 0;
            if (!PeekNamedPipe(output, nullptr, 0, nullptr, &available, nullptr)) break;
            if (available) { char c = 0; DWORD count = 0; Check(ReadFile(output, &c, 1, &count, nullptr) && count, "read"); if (c == '\n') return line; line += c; Check(line.size() < 8192, "bounded JSON"); }
            else Sleep(5);
        }
        throw std::runtime_error("helper did not emit bounded JSON line");
    }
    void Ready(const std::string& line)
    {
        if (line.find("\"event\":\"ready\"") == std::string::npos)
        { std::cerr << line << '\n'; Exit(); }
        Check(line.find("\"event\":\"ready\"") != std::string::npos, "expected ready JSON");
        Check(line.find("\"schemaVersion\":1") != std::string::npos && line.find("0123456789abcdef0123456789abcdef") != std::string::npos, "schema/nonce");
        const auto pos = line.find("\"pid\":"); Check(pos != std::string::npos, "pid missing");
        pid = std::stoul(line.substr(pos + 6));
        child = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE | PROCESS_TERMINATE, FALSE, pid);
        Check(child != nullptr, "child handle");
        FILETIME created{}, exit{}, kernel{}, user{}; Check(GetProcessTimes(child, &created, &exit, &kernel, &user) != FALSE, "timestamps");
        ULARGE_INTEGER time{}; time.LowPart = created.dwLowDateTime; time.HighPart = created.dwHighDateTime;
        Check(line.find("\"createdFileTime\":\"" + std::to_string(time.QuadPart) + "\"") != std::string::npos, "creation timestamp matches retained child");
        Check(line.find("\"patchPath\":\"") != std::string::npos, "loaded path");
    }
    DWORD Exit()
    {
        Check(WaitForSingleObject(process, 18000) == WAIT_OBJECT_0, "helper exit timeout");
        DWORD code = 0; GetExitCodeProcess(process, &code);
        std::cout << "helper exit=" << code << '\n';
        return code;
    }
    void Ack(const char* value = "commit 0123456789abcdef0123456789abcdef\n")
    { AckBytes(value, static_cast<DWORD>(strlen(value))); }
    void AckBytes(const char* value, DWORD length)
    { DWORD count = 0; Check(WriteFile(input, value, length, &count, nullptr) && count == length, "ack write"); }
    void NoMoreOutput()
    {
        DWORD available = 0;
        const BOOL readable = PeekNamedPipe(output, nullptr, 0, nullptr, &available, nullptr);
        Check((readable && available == 0) || (!readable && GetLastError() == ERROR_BROKEN_PIPE), "helper must not emit a second JSON line");
    }
};
int wmain(int argc, wchar_t** argv)
{
    try
    {
        Check(argc == 8, "usage: tests case helper fixture-exe fixture-dll production-dll original-ijl rs-fixture");
        const std::wstring test = argv[1];
        const fs::path root = fs::temp_directory_path() / (L"DFLegacy native \u4e2d\u6587 " + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()));
        fs::create_directories(root);
        const fs::path exe = root / L"fixture.exe", dll = root / L"DFLegacy.Client.dll";
        const fs::path marker = root / (test == L"unicode" ? L"test-mode.marker" : L"entry.marker");
        fs::copy_file(argv[3], exe, fs::copy_options::overwrite_existing);
        fs::copy_file(argv[test == L"unknown" ? 5 : 4], dll, fs::copy_options::overwrite_existing);
        const fs::path ijl = root / L"ijl15.dll", pvf = root / L"Script.pvf", npk = root / L"sprite.npk";
        fs::copy_file(argv[6], ijl);
        fs::copy_file(argv[7],root/L"RS.dll");
        { std::ofstream file(pvf); file << "untouched PVF fixture"; }
        { std::ofstream file(npk); file << "untouched NPK fixture"; }
        const auto ijlHash = Hash(ijl), pvfHash = Hash(pvf), npkHash = Hash(npk);
        const std::string exeHash = Hash(exe), dllHash = Hash(dll);
        if (test == L"raw-tail")
        {
            Launch run; run.Start(argv[2],exe,dll,L"99?127.0.0.1?2311?fixture?synthetic",dllHash);
            run.Ready(run.Line()); run.Ack(); Check(run.Exit()==0,"raw-tail commit");
            for(int i=0;i<100 && !fs::exists(root/L"raw-entry.marker");i++) Sleep(10);
            Check(fs::exists(root/L"raw-entry.marker"),"WinMain receives exact unquoted raw 99 launcher-data tail");
        }
        else if (test == L"reject")
        {
            Launch bad; bad.Start(argv[2], exe, dll, marker, std::string(64, '0'));
            Check(bad.Line().find("hash_mismatch") != std::string::npos && bad.Exit() == 1, "bad hash rejected");
            fs::path unknown = root / L"unknown.exe"; { std::ofstream f(unknown); f << "not PE"; }
            Launch arch; arch.Start(argv[2], unknown, dll, marker, dllHash);
            Check(arch.Line().find("invalid_image") != std::string::npos && arch.Exit() == 1, "unknown image rejected");
            fs::path x64 = root / L"x64.exe"; fs::copy_file(exe, x64);
            { std::fstream f(x64, std::ios::binary | std::ios::in | std::ios::out); LONG offset = 0; f.seekg(0x3c); f.read(reinterpret_cast<char*>(&offset), 4); const WORD machine = IMAGE_FILE_MACHINE_AMD64; f.seekp(offset + 4); f.write(reinterpret_cast<const char*>(&machine), 2); }
            Launch wrong; wrong.Start(argv[2], x64, dll, marker, dllHash);
            Check(wrong.Line().find("invalid_image") != std::string::npos && wrong.Exit() == 1, "x64 rejected");
        }
        else if (test == L"eof-initializing")
        {
            Launch run; run.Start(argv[2], exe, dll, root / L"timeout.marker", dllHash);
            Sleep(200); CloseHandle(run.input); run.input = nullptr;
            Check(WaitForSingleObject(run.process, 2500) == WAIT_OBJECT_0, "EOF during initialization terminates promptly");
            Check(run.Exit() == 1 && !fs::exists(root / L"timeout.marker"), "EOF never reaches entry");
        }
        else if (test == L"location")
        {
            fs::create_directories(root / L"other");
            const auto wrong = root / L"other" / L"DFLegacy.Client.dll";
            fs::copy_file(dll, wrong);
            Launch run; run.Start(argv[2], exe, wrong, marker, dllHash);
            Check(run.Line().find("patch_location") != std::string::npos && run.Exit() == 1, "non-sibling rejected");
            Launch traversal; traversal.Start(argv[2], exe, root / L"other" / L".." / L"DFLegacy.Client.dll", marker, dllHash);
            traversal.Ready(traversal.Line()); traversal.Ack(); Check(traversal.Exit() == 0, "canonical sibling traversal accepted");
        }
        else if (test == L"failure" || test == L"timeout" || test == L"unknown")
        {
            const auto failMarker = root / (test == L"timeout" ? L"timeout.marker" : L"fail.marker");
            Launch run; run.Start(argv[2], exe, dll, failMarker, dllHash);
            const auto line = run.Line();
            Check(line.find(test == L"timeout" ? "initialization_timeout" : "initialization_failed") != std::string::npos && run.Exit() == 1, "initialization failure");
            Check(!fs::exists(failMarker), "failed init never entered");
        }
        else
        {
            Launch unrelated;
            if (test == L"mismatch")
            {
                STARTUPINFOW si{ sizeof(si) }; PROCESS_INFORMATION pi{};
                auto command = Quote(exe.wstring()) + L" " + Quote((root / L"unrelated.marker").wstring());
                Check(CreateProcessW(exe.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
                    nullptr, root.c_str(), &si, &pi) != FALSE, "start unrelated child");
                unrelated.child = pi.hProcess; CloseHandle(pi.hThread);
            }
            Launch run; run.Start(argv[2], exe, dll, marker, dllHash, test == L"unicode");
            run.Ready(run.Line()); Check(!fs::exists(marker), "entry must wait for commit");
            if(test==L"rs-log")
            {
                Check(fs::exists(root/L"Logs"/L"DFLegacy"/L"rs_log.txt"),"RS DllMain log redirected before entry");
                Check(!fs::exists(root/L"rs_log.txt"),"no early RS root log");
                Check(fs::exists(root/L"unrelated.txt")&&!fs::exists(root/L"Logs"/L"DFLegacy"/L"unrelated.txt"),"RS hook never redirects unrelated TXT");
            }
            // No write/delete replacement is possible while ownership is retained.
            HANDLE writer = CreateFileW(dll.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
            Check(writer == INVALID_HANDLE_VALUE && GetLastError() == ERROR_SHARING_VIOLATION, "patch file held against replacement");
            const bool trailing = test == L"ack-trailing" || test == L"ack-trailing-closed" || test == L"ack-trailing-binary";
            if (test == L"eof" || test == L"mismatch" || test == L"uncommitted" || test == L"killed" || trailing)
            {
                if (test == L"mismatch") run.Ack("commit ffffffffffffffffffffffffffffffff\n");
                else if (trailing)
                {
                    if (test == L"ack-trailing-binary")
                    {
                        constexpr char bytes[] = "commit 0123456789abcdef0123456789abcdef\n\0\xff";
                        run.AckBytes(bytes, sizeof(bytes) - 1);
                    }
                    else run.Ack("commit 0123456789abcdef0123456789abcdef\nEXTRA\n");
                    if (test == L"ack-trailing-closed") { CloseHandle(run.input); run.input = nullptr; }
                }
                else if (test == L"killed") Check(TerminateProcess(run.process, 98) != FALSE, "cancel helper");
                else if (test == L"eof") { CloseHandle(run.input); run.input = nullptr; }
                Check(run.Exit() == (test == L"killed" ? 98u : 1u), "uncommitted helper fails with expected exit code");
                run.NoMoreOutput();
                Check(WaitForSingleObject(run.child, 1000) == WAIT_OBJECT_0, "owned child terminated");
                Check(!fs::exists(marker), "uncommitted entry never ran");
                if (unrelated.child) Check(WaitForSingleObject(unrelated.child, 0) == WAIT_TIMEOUT, "unrelated process survives rejected commit");
            }
            else
            {
                run.Ack();
                if (test == L"ack-closed") { CloseHandle(run.input); run.input = nullptr; }
                Check(run.Exit() == 0, "commit succeeds");
                run.NoMoreOutput();
                for (int i = 0; i < 200 && !fs::exists(marker); ++i) Sleep(10);
                Check(fs::exists(marker), "entry runs after initialization and commit");
                Check(WaitForSingleObject(run.child, 0) == WAIT_TIMEOUT, "committed child survives helper");
            }
        }
        Check(Hash(exe) == exeHash && Hash(dll) == dllHash, "original file hashes preserved");
        Check(Hash(ijl) == ijlHash && Hash(pvf) == pvfHash && Hash(npk) == npkHash, "original IJL/PVF/NPK file hashes preserved");
        std::cout << "PASS native bootstrap fixture\n";
        return 0;
    }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << '\n'; return 1; }
}
