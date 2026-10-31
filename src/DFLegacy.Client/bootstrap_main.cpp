#include <windows.h>
#include <bcrypt.h>
#include <detours.h>
#include "bootstrap_protocol.h"
#include <array>
#include <cstdio>
#include <cwchar>
#include <string>
#include <vector>

namespace
{
    using namespace dfl::client_bootstrap;
    bool protocolLineEmitted = false;
    struct Handle
    {
        HANDLE value = nullptr;
        Handle() = default;
        explicit Handle(HANDLE h) : value(h) {}
        ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
        Handle(const Handle&) = delete;
        Handle& operator=(const Handle&) = delete;
    };
    struct Child
    {
        Handle process, thread, job;
        bool committed = false;
        ~Child()
        {
            if (process.value && !committed)
            { TerminateProcess(process.value, ERROR_CANCELLED); WaitForSingleObject(process.value, 3000); }
        }
    };
    struct View
    {
        SharedState* state = nullptr;
        ~View() { if (state) UnmapViewOfFile(state); }
    };
    struct Attributes
    {
        std::vector<unsigned char> storage;
        LPPROC_THREAD_ATTRIBUTE_LIST list = nullptr;
        HANDLE creationJob = nullptr;
        ~Attributes() { if (list) DeleteProcThreadAttributeList(list); }
        bool Init(HANDLE* handles, SIZE_T count, HANDLE job)
        {
            SIZE_T bytes = 0;
            InitializeProcThreadAttributeList(nullptr, 2, 0, &bytes);
            storage.resize(bytes);
            auto candidate = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(storage.data());
            if (!InitializeProcThreadAttributeList(candidate, 2, 0, &bytes)) return false;
            list = candidate;
            creationJob = job;
            return UpdateProcThreadAttribute(list, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                handles, count * sizeof(HANDLE), nullptr, nullptr)
                && UpdateProcThreadAttribute(list, 0, PROC_THREAD_ATTRIBUTE_JOB_LIST,
                    &creationJob, sizeof(creationJob), nullptr, nullptr);
        }
    };
    bool Hex(const wchar_t* text, size_t length)
    {
        if (wcslen(text) != length) return false;
        for (size_t i = 0; i < length; ++i)
            if (!((text[i] >= L'0' && text[i] <= L'9') || (text[i] >= L'a' && text[i] <= L'f'))) return false;
        return true;
    }
    std::string Utf8(const std::wstring& text)
    {
        const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.c_str(),
            static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
        if (size <= 0) return {};
        std::string value(size, '\0');
        WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.c_str(), static_cast<int>(text.size()), value.data(), size, nullptr, nullptr);
        return value;
    }
    std::string Json(const std::wstring& text)
    {
        std::string result;
        for (unsigned char c : Utf8(text))
        {
            if (c == '"' || c == '\\') { result += '\\'; result += static_cast<char>(c); }
            else if (c < 32) { char escaped[7]{}; sprintf_s(escaped, "\\u%04x", c); result += escaped; }
            else result += static_cast<char>(c);
        }
        return result;
    }
    bool Emit(const std::string& text)
    {
        if (protocolLineEmitted) return false;
        protocolLineEmitted = true;
        const std::string line = text + "\n";
        if (line.size() > 8192) return false;
        DWORD written = 0;
        return WriteFile(GetStdHandle(STD_OUTPUT_HANDLE), line.data(), static_cast<DWORD>(line.size()), &written, nullptr)
            && written == line.size();
    }
    int Error(const std::wstring& nonce, const char* code, const char* message)
    {
        Emit("{\"schemaVersion\":1,\"event\":\"error\",\"nonce\":\"" + Json(nonce)
            + "\",\"code\":\"" + code + "\",\"message\":\"" + message + "\"}");
        return 1;
    }
    std::wstring Canonical(HANDLE file)
    {
        wchar_t path[PathCapacity]{};
        const DWORD size = GetFinalPathNameByHandleW(file, path, PathCapacity, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        if (!size || size >= PathCapacity) return {};
        std::wstring value(path);
        if (value.rfind(L"\\\\?\\UNC\\", 0) == 0) value = L"\\\\" + value.substr(8);
        else if (value.rfind(L"\\\\?\\", 0) == 0) value = value.substr(4);
        return value;
    }
    bool Absolute(const wchar_t* path)
    {
        const size_t len = wcslen(path);
        return len > 3 && len < PathCapacity
            && ((path[1] == L':' && (path[2] == L'\\' || path[2] == L'/'))
                || (path[0] == L'\\' && path[1] == L'\\'));
    }
    bool ReadAt(HANDLE file, LONGLONG offset, void* output, DWORD size)
    {
        LARGE_INTEGER pos{}; pos.QuadPart = offset;
        DWORD count = 0;
        return SetFilePointerEx(file, pos, nullptr, FILE_BEGIN)
            && ReadFile(file, output, size, &count, nullptr) && count == size;
    }
    bool Image(HANDLE file, bool dll)
    {
        LARGE_INTEGER size{}; IMAGE_DOS_HEADER dos{}; IMAGE_NT_HEADERS32 nt{};
        if (!GetFileSizeEx(file, &size) || !ReadAt(file, 0, &dos, sizeof(dos))
            || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < sizeof(dos)
            || dos.e_lfanew > size.QuadPart - sizeof(nt)
            || !ReadAt(file, dos.e_lfanew, &nt, sizeof(nt))) return false;
        return nt.Signature == IMAGE_NT_SIGNATURE && nt.FileHeader.Machine == IMAGE_FILE_MACHINE_I386
            && nt.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC
            && nt.FileHeader.SizeOfOptionalHeader == sizeof(IMAGE_OPTIONAL_HEADER32)
            && (nt.FileHeader.Characteristics & IMAGE_FILE_EXECUTABLE_IMAGE)
            && !!(nt.FileHeader.Characteristics & IMAGE_FILE_DLL) == dll;
    }
    std::wstring Hash(HANDLE file)
    {
        BCRYPT_HASH_HANDLE hash = nullptr;
        if (BCryptCreateHash(BCRYPT_SHA256_ALG_HANDLE, &hash, nullptr, 0, nullptr, 0, 0) != 0) return {};
        LARGE_INTEGER start{}; bool ok = SetFilePointerEx(file, start, nullptr, FILE_BEGIN) != FALSE;
        std::array<unsigned char, 65536> buffer{}; DWORD count = 0;
        while (ok)
        {
            if (!ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()), &count, nullptr)) { ok = false; break; }
            if (!count) break;
            ok = BCryptHashData(hash, buffer.data(), count, 0) == 0;
        }
        unsigned char digest[32]{};
        ok = ok && BCryptFinishHash(hash, digest, sizeof(digest), 0) == 0;
        BCryptDestroyHash(hash);
        if (!ok) return {};
        std::wstring result; constexpr wchar_t digits[] = L"0123456789abcdef";
        for (auto b : digest) { result += digits[b >> 4]; result += digits[b & 15]; }
        return result;
    }
    std::wstring Quote(const std::wstring& value)
    {
        std::wstring result = L"\""; size_t slashes = 0;
        for (wchar_t c : value)
        {
            if (c == L'\\') { ++slashes; continue; }
            result.append(c == L'"' ? slashes * 2 + 1 : slashes, L'\\');
            result += c; slashes = 0;
        }
        result.append(slashes * 2, L'\\'); result += L'"'; return result;
    }
    bool Commit(HANDLE child, const std::wstring& nonce)
    {
        const HANDLE input = GetStdHandle(STD_INPUT_HANDLE);
        if (GetFileType(input) != FILE_TYPE_PIPE) return false;
        const std::string expected = "commit " + Utf8(nonce) + "\n";
        std::string received;
        const ULONGLONG deadline = GetTickCount64() + CommitTimeout;
        while (GetTickCount64() < deadline)
        {
            if (WaitForSingleObject(child, 0) != WAIT_TIMEOUT) return false;
            DWORD available = 0;
            if (!PeekNamedPipe(input, nullptr, 0, nullptr, &available, nullptr)) return false;
            if (!available) { Sleep(10); continue; }
            char byte = 0; DWORD count = 0;
            if (!ReadFile(input, &byte, 1, &count, nullptr) || count != 1) return false;
            received += byte;
            if (received.size() > expected.size() || expected.compare(0, received.size(), received) != 0) return false;
            if (byte == '\n')
            {
                // This is a single-line handoff, not a stream of commands.
                // Reject data already queued after the complete line without
                // requiring the launcher to close its writer to commit.
                DWORD trailing = 0;
                if (PeekNamedPipe(input, nullptr, 0, nullptr, &trailing, nullptr))
                    return trailing == 0;
                // A writer may close immediately after writing the exact ack.
                // Once all its bytes were read, EOF is still a valid handoff.
                return GetLastError() == ERROR_BROKEN_PIPE;
            }
        }
        return false;
    }
    int Run(int argc, wchar_t** argv)
    {
        std::wstring nonce;
        if (argc == 13 && Hex(argv[10], 32)) nonce = argv[10];
        if (argc != 13 || wcscmp(argv[1], L"--client") || wcscmp(argv[3], L"--patch")
            || wcscmp(argv[5], L"--sha256") || wcscmp(argv[7], L"--mode") || wcscmp(argv[9], L"--nonce")
            || wcscmp(argv[11], L"--") || nonce.empty() || !Hex(argv[6], 64)
            || (wcscmp(argv[8], L"normal") && wcscmp(argv[8], L"test"))
            || !Absolute(argv[2]) || !Absolute(argv[4]) || wcslen(argv[12]) > 16384)
            return Error(nonce, "invalid_arguments", "Invalid bootstrap arguments.");
        if (GetFileType(GetStdHandle(STD_INPUT_HANDLE)) != FILE_TYPE_PIPE)
            return Error(nonce, "invalid_protocol", "Bootstrap requires a launcher input pipe.");
        Handle exe(CreateFileW(argv[2], GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
        Handle dll(CreateFileW(argv[4], GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
        if (exe.value == INVALID_HANDLE_VALUE || dll.value == INVALID_HANDLE_VALUE)
            return Error(nonce, "file_unavailable", "Client or patch is unavailable or being changed.");
        const std::wstring exePath = Canonical(exe.value), dllPath = Canonical(dll.value);
        const auto separator = exePath.find_last_of(L'\\');
        if (exePath.empty() || dllPath.empty() || separator == std::wstring::npos
            || _wcsicmp(dllPath.c_str(), (exePath.substr(0, separator + 1) + L"DFLegacy.Client.dll").c_str()))
            return Error(nonce, "patch_location", "Patch must be DFLegacy.Client.dll beside the selected client.");
        if (Hash(dll.value) != argv[6]) return Error(nonce, "hash_mismatch", "Patch integrity check failed.");
        if (!Image(exe.value, false) || !Image(dll.value, true))
            return Error(nonce, "invalid_image", "Client and patch must be valid x86 images.");
        BY_HANDLE_FILE_INFORMATION expected{};
        if (!GetFileInformationByHandle(dll.value, &expected)) return Error(nonce, "file_identity", "Cannot verify patch identity.");
        SECURITY_ATTRIBUTES sa{ sizeof(sa), nullptr, TRUE };
        Handle mapping(CreateFileMappingW(INVALID_HANDLE_VALUE, &sa, PAGE_READWRITE, 0, sizeof(SharedState), nullptr));
        Handle ready(CreateEventW(&sa, TRUE, FALSE, nullptr)), release(CreateEventW(&sa, TRUE, FALSE, nullptr));
        if (!mapping.value || !ready.value || !release.value) return Error(nonce, "handshake_failed", "Cannot create private handshake.");
        View view; view.state = static_cast<SharedState*>(MapViewOfFile(mapping.value, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedState)));
        if (!view.state) return Error(nonce, "handshake_failed", "Cannot map private handshake.");
        auto& state = *view.state; state.magic = ProtocolMagic; state.size = sizeof(SharedState);
        state.mode = wcscmp(argv[8], L"test") == 0 ? 1 : 0;
        const std::string nonceUtf8 = Utf8(nonce); memcpy(state.nonce, nonceUtf8.c_str(), nonceUtf8.size() + 1);
        HANDLE inherited[] = { mapping.value, ready.value, release.value };
        Child child;
        child.job.value = CreateJobObjectW(nullptr, nullptr);
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{}; limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!child.job.value || !SetInformationJobObject(child.job.value, JobObjectExtendedLimitInformation, &limits, sizeof(limits)))
            return Error(nonce, "ownership_failed", "Cannot establish child ownership.");
        // Atomic job assignment closes the create-suspended/assign-job crash window.
        // This requires Windows 10+, the supported launcher platform.
        Attributes attributes;
        if (!attributes.Init(inherited, _countof(inherited), child.job.value))
            return Error(nonce, "handshake_failed", "Cannot restrict child handles or ownership.");
        STARTUPINFOEXW startup{}; startup.StartupInfo.cb = sizeof(startup); startup.lpAttributeList = attributes.list;
        PROCESS_INFORMATION pi{};
        // The supported game's WinMain parses the raw tail, including its first
        // numeric '?' field. Preserve whitespace-free launcher data verbatim;
        // CRT quoting is needed only for an empty argument, whitespace or quotes.
        const std::wstring data = argv[12];
        const bool quoteData = data.empty() || data.find_first_of(L" \t\n\r\v\"") != std::wstring::npos;
        std::wstring command = Quote(exePath) + L" " + (quoteData ? Quote(data) : data);
        const std::wstring directory = exePath.substr(0, separator);
        if (!CreateProcessW(exePath.c_str(), command.data(), nullptr, nullptr, TRUE,
            CREATE_SUSPENDED | EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
            nullptr, directory.c_str(), &startup.StartupInfo, &pi))
            return Error(nonce, "create_failed", "Cannot create the selected client.");
        child.process.value = pi.hProcess; child.thread.value = pi.hThread; state.pid = pi.dwProcessId;
        Payload payload{ ProtocolMagic, sizeof(Payload), reinterpret_cast<std::uint32_t>(mapping.value),
            reinterpret_cast<std::uint32_t>(ready.value), reinterpret_cast<std::uint32_t>(release.value) };
        const char* imports[] = { "DFLegacy.Client.dll" };
        if (!DetourUpdateProcessWithDll(pi.hProcess, imports, 1)
            || !DetourCopyPayloadToProcess(pi.hProcess, PayloadId, &payload, sizeof(payload))
            || ResumeThread(pi.hThread) == static_cast<DWORD>(-1))
            return Error(nonce, "injection_failed", "Cannot prepare client patch initialization.");
        HANDLE wait[] = { ready.value, pi.hProcess };
        DWORD status = WAIT_TIMEOUT;
        const ULONGLONG initializationDeadline = GetTickCount64() + InitializationTimeout;
        while (GetTickCount64() < initializationDeadline)
        {
            DWORD available = 0;
            if (!PeekNamedPipe(GetStdHandle(STD_INPUT_HANDLE), nullptr, 0, nullptr, &available, nullptr))
                return Error(nonce, "parent_disconnected", "Launcher disconnected before initialization completed.");
            status = WaitForMultipleObjects(2, wait, FALSE, 10);
            if (status != WAIT_TIMEOUT) break;
        }
        if (status != WAIT_OBJECT_0 || state.initialized != 1)
        {
#ifdef FIXTURE_BOOTSTRAP
            DWORD childExit=0; GetExitCodeProcess(pi.hProcess,&childExit);
            char detail[240]{};
            sprintf_s(detail,"Fixture init failed: wait=%lu initialized=%ld child_exit=%lu child_pid=%lu module_path_set=%u volume=%lu",
                status,state.initialized,childExit,pi.dwProcessId,state.modulePath[0]?1u:0u,state.volume);
            return Error(nonce, status == WAIT_TIMEOUT ? "initialization_timeout" : "initialization_failed", detail);
#else
            return Error(nonce, status == WAIT_TIMEOUT ? "initialization_timeout" : "initialization_failed",
                "Client patch initialization failed; this client image may be unsupported.");
#endif
        }
        if (state.volume != expected.dwVolumeSerialNumber || state.fileIndexHigh != expected.nFileIndexHigh
            || state.fileIndexLow != expected.nFileIndexLow || state.modulePath[PathCapacity - 1] != 0
            || _wcsicmp(state.modulePath, dllPath.c_str()))
            return Error(nonce, "module_identity", "Loaded patch identity does not match the selected file.");
        FILETIME created{}, ended{}, kernel{}, user{};
        if (!GetProcessTimes(pi.hProcess, &created, &ended, &kernel, &user)) return Error(nonce, "child_identity", "Cannot verify child identity.");
        ULARGE_INTEGER timestamp{}; timestamp.LowPart = created.dwLowDateTime; timestamp.HighPart = created.dwHighDateTime;
        if (!Emit("{\"schemaVersion\":1,\"event\":\"ready\",\"nonce\":\"" + nonceUtf8 + "\",\"pid\":" + std::to_string(pi.dwProcessId)
            + ",\"createdFileTime\":\"" + std::to_string(timestamp.QuadPart) + "\",\"patchPath\":\"" + Json(state.modulePath) + "\"}")) return 1;
        // Exactly one protocol line: post-ready cancellation is signaled by nonzero exit.
        if (!Commit(pi.hProcess, nonce)) return 1;
        limits.BasicLimitInformation.LimitFlags = 0;
        if (!SetInformationJobObject(child.job.value, JobObjectExtendedLimitInformation, &limits, sizeof(limits))
            || !SetEvent(release.value)) return 1;
        child.committed = true;
        return 0;
    }
}
int wmain(int argc, wchar_t** argv)
{
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    try { return Run(argc, argv); }
    catch (...) { return Error(L"", "bootstrap_failed", "Bootstrap could not complete."); }
}
