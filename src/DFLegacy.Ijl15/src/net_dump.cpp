#include "../../DFLegacy.Client/client_paths.h"
// Network dump for the 2008DF client: log every TCP segment and UDP datagram the
// game itself sends or receives.
//
// The two clients of a party talk over Windows loopback. A packet capture cannot
// see that traffic on this machine (2026-10-04: a 184 MB pktmon capture held zero
// loopback UDP packets), which is exactly why the "mages casts a skill -> the
// party member's client crashes" packet could not be identified. The client's own
// socket calls have no such blind spot, so we hook them instead.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

#include <array>
#include <climits>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <new>
#include <vector>

#include "ijl15_config.h"
#include "memory_patch.h"
#include "net_dump.h"

namespace
{
    constexpr char LogFileNamePrefix[] = "netdump-";
    constexpr int MaximumPayloadBytes = 65536;
    constexpr std::size_t AddressTextLength = 32;

    using SendFn = int(WSAAPI*)(SOCKET, const char*, int, int);
    using SendToFn = int(WSAAPI*)(SOCKET, const char*, int, int, const struct sockaddr*, int);
    using RecvFn = int(WSAAPI*)(SOCKET, char*, int, int);
    using RecvFromFn = int(WSAAPI*)(SOCKET, char*, int, int, struct sockaddr*, int*);
    using ConnectFn = int(WSAAPI*)(SOCKET, const struct sockaddr*, int);
    using WSARecvFromFn = int(WSAAPI*)(
        SOCKET,
        LPWSABUF,
        DWORD,
        LPDWORD,
        LPDWORD,
        struct sockaddr*,
        LPINT,
        LPWSAOVERLAPPED,
        LPWSAOVERLAPPED_COMPLETION_ROUTINE);

    SendFn OriginalSend = nullptr;
    SendToFn OriginalSendTo = nullptr;
    RecvFn OriginalRecv = nullptr;
    RecvFromFn OriginalRecvFrom = nullptr;
    ConnectFn OriginalConnect = nullptr;
    WSARecvFromFn OriginalWSARecvFrom = nullptr;

    CRITICAL_SECTION LogLock;
    bool LogLockReady = false;
    HANDLE LogFile = INVALID_HANDLE_VALUE;
    std::int64_t WrittenBytes = 0;
    std::int64_t WrittenLimit = 0;
    int PayloadLimit = 0;
    bool TruncationReported = false;

    // Installed imports, so a test can put the original back before it unloads
    // this module. A hooked import left pointing at a freed image is an access
    // violation on the next socket call.
    struct PatchedImport
    {
        void** Thunk;
        void* Original;
    };
    constexpr std::size_t MaximumPatchedImports = 16;
    PatchedImport PatchedImports[MaximumPatchedImports]{};
    std::size_t PatchedImportCount = 0;
    void* HookedModule = nullptr;

    dfl::net_dump::InstallReport LastReport{};
    // How many times a wrapper reached the writer, and how many lines it
    // emitted: the difference between the two says whether a missing packet is
    // an unhooked call or a rejected line.
    int WritePacketCalls = 0;
    int WrittenLines = 0;

    // The dump line is built here, once per thread, instead of on the stack: a
    // 64 KB payload needs 128 KB of hex, and the client's network threads do not
    // all have room for that on a 1 MB stack. Heap-backed and stable across
    // calls, so the steady state does no allocation.
    thread_local std::vector<char> LineBuffer;

    // "127.0.0.1:1234" / "192.168.3.100:52108"; IPv6 is printed as [addr]:port.
    void FormatEndpoint(
        const struct sockaddr* address,
        int addressLength,
        char* output,
        std::size_t capacity) noexcept
    {
        output[0] = '\0';
        if (address == nullptr || capacity == 0)
        {
            return;
        }

        char host[NI_MAXHOST]{};
        char service[NI_MAXSERV]{};
        const int length = addressLength > 0
            ? addressLength
            : (address->sa_family == AF_INET6
                ? static_cast<int>(sizeof(struct sockaddr_in6))
                : static_cast<int>(sizeof(struct sockaddr_in)));
        if (getnameinfo(
                address,
                length,
                host,
                static_cast<DWORD>(sizeof(host)),
                service,
                static_cast<DWORD>(sizeof(service)),
                NI_NUMERICHOST | NI_NUMERICSERV) != 0)
        {
            return;
        }

        if (address->sa_family == AF_INET6)
        {
            _snprintf_s(output, capacity, _TRUNCATE, "[%s]:%s", host, service);
        }
        else
        {
            _snprintf_s(output, capacity, _TRUNCATE, "%s:%s", host, service);
        }
    }

    // The TCP side does not carry the peer address in the call, so ask the socket.
    void FormatPeerOfSocket(SOCKET socket, char* output, std::size_t capacity) noexcept
    {
        output[0] = '\0';

        struct sockaddr_storage address{};
        int length = static_cast<int>(sizeof(address));
        if (getpeername(socket, reinterpret_cast<struct sockaddr*>(&address), &length) == 0)
        {
            FormatEndpoint(reinterpret_cast<const struct sockaddr*>(&address), length, output, capacity);
            return;
        }

        // Not connected (a plain UDP socket): the bound local address is still
        // more useful than nothing when it comes to telling two clients apart.
        length = static_cast<int>(sizeof(address));
        if (getsockname(socket, reinterpret_cast<struct sockaddr*>(&address), &length) == 0)
        {
            FormatEndpoint(reinterpret_cast<const struct sockaddr*>(&address), length, output, capacity);
        }
    }

    void FormatLocalOfSocket(SOCKET socket, char* output, std::size_t capacity) noexcept
    {
        output[0] = '\0';

        struct sockaddr_storage address{};
        int length = static_cast<int>(sizeof(address));
        if (getsockname(socket, reinterpret_cast<struct sockaddr*>(&address), &length) == 0)
        {
            FormatEndpoint(reinterpret_cast<const struct sockaddr*>(&address), length, output, capacity);
        }
    }

    // Room for "YYYY-MM-DD HH:MM:SS.mmm T=<tid> TX TCP local -> remote
    // len=<n> (truncated)". Both addresses are bounded by AddressTextLength, and
    // the whole line measures under 120 bytes for IPv4 as well as IPv6; the
    // margin is headroom, not slack to rely on. This is zero-initialised per
    // packet, so it is kept small on purpose.
    constexpr std::size_t MaximumHeaderBytes = 192;
    constexpr char HexPrefix[] = " hex=";
    constexpr std::size_t HexPrefixLength = sizeof(HexPrefix) - 1;

    void AppendHex(char* output, std::size_t byteCount, const char* data) noexcept
    {
        static constexpr char Digits[] = "0123456789ABCDEF";
        for (std::size_t index = 0; index < byteCount; ++index)
        {
            const unsigned char value = static_cast<unsigned char>(data[index]);
            output[index * 2] = Digits[value >> 4];
            output[index * 2 + 1] = Digits[value & 0x0F];
        }
    }

    // One line per packet, flushed immediately: the client we are investigating
    // crashes, and a buffered log would lose exactly the packets that matter.
    void WritePacket(
        const char* direction,
        const char* protocol,
        const char* local,
        const char* remote,
        const char* data,
        int length) noexcept
    {
        ++WritePacketCalls;
        if (!LogLockReady || LogFile == INVALID_HANDLE_VALUE || length <= 0
            || data == nullptr)
        {
            return;
        }

        const int dumped = PayloadLimit > 0 && length > PayloadLimit
            ? PayloadLimit
            : length;

        // Header first, then " hex=" and the payload. The buffer is sized per
        // packet, and every offset is checked before it is written.
        const std::size_t headerCapacity = MaximumHeaderBytes;
        std::size_t capacity = headerCapacity
            + static_cast<std::size_t>(dumped) * 2
            + HexPrefixLength
            + 2;

        // Reused across calls, so the steady state does not allocate on the
        // socket's own thread.
        if (LineBuffer.size() < capacity)
        {
            try
            {
                LineBuffer.resize(capacity);
            }
            catch (...)
            {
                return;
            }
        }
        char* const buffer = LineBuffer.data();

        SYSTEMTIME now{};
        GetLocalTime(&now);

        // Format the header into its own small buffer so its length is known
        // exactly: _snprintf_s reports truncation only as -1, and %n is refused
        // by this compiler (/sdl). The header can only be over-long if the
        // addresses did not fit, which is an internal inconsistency, so the line
        // is dropped rather than written with a wrong boundary.
        char header[MaximumHeaderBytes]{};
        const int written = _snprintf_s(
            header,
            sizeof(header),
            _TRUNCATE,
            "%04u-%02u-%02u %02u:%02u:%02u.%03u T=%lu %s %s %s -> %s len=%d%s",
            now.wYear,
            now.wMonth,
            now.wDay,
            now.wHour,
            now.wMinute,
            now.wSecond,
            now.wMilliseconds,
            GetCurrentThreadId(),
            direction,
            protocol,
            local[0] != '\0' ? local : "-",
            remote[0] != '\0' ? remote : "-",
            length,
            dumped < length ? " (truncated)" : "");

        const std::size_t headerLength = written > 0
            ? static_cast<std::size_t>(written)
            : 0;
        if (headerLength == 0
            || headerLength + HexPrefixLength >= headerCapacity
            || headerLength + HexPrefixLength + static_cast<std::size_t>(dumped) * 2 + 2
                > capacity)
        {
            return;
        }

        std::memcpy(buffer, header, headerLength);
        std::size_t offset = headerLength;
        std::memcpy(buffer + offset, HexPrefix, HexPrefixLength);
        offset += HexPrefixLength;

        const std::size_t payloadBytes = static_cast<std::size_t>(dumped);
        AppendHex(buffer + offset, payloadBytes, data);
        offset += payloadBytes * 2;
        buffer[offset] = '\r';
        buffer[offset + 1] = '\n';
        const std::size_t lineLength = offset + 2;

        EnterCriticalSection(&LogLock);
        if (WrittenLimit > 0 && WrittenBytes >= WrittenLimit)
        {
            if (!TruncationReported)
            {
                TruncationReported = true;
                static constexpr char Notice[] =
                    "netdump: size limit reached; further packets are not logged.\r\n";
                DWORD ignored = 0;
                WriteFile(LogFile, Notice, sizeof(Notice) - 1, &ignored, nullptr);
            }
            LeaveCriticalSection(&LogLock);
            return;
        }

        DWORD bytesWritten = 0;
        if (WriteFile(
                LogFile,
                buffer,
                static_cast<DWORD>(lineLength),
                &bytesWritten,
                nullptr))
        {
            WrittenBytes += bytesWritten;
            ++WrittenLines;
        }
        LeaveCriticalSection(&LogLock);
    }

    // A connect attempt is not a packet, but it is the one call that says which
    // address the client decided to dial -- and it is the only evidence
    // available when a connection never produces any traffic. Logged before the
    // call so a failure is recorded too, with the outcome in the same line.
    int WSAAPI HookedConnect(SOCKET socket, const struct sockaddr* name, int nameLength)
    {
        if (OriginalConnect == nullptr)
        {
            WSASetLastError(WSANOTINITIALISED);
            return SOCKET_ERROR;
        }

        char local[AddressTextLength]{};
        char remote[AddressTextLength]{};
        FormatLocalOfSocket(socket, local, sizeof(local));
        FormatEndpoint(name, nameLength, remote, sizeof(remote));

        // Encoded before the call so the line carries the target even when the
        // connect itself fails and no socket state is left to interrogate.
        const int result = OriginalConnect(socket, name, nameLength);
        const int error = result == SOCKET_ERROR ? WSAGetLastError() : 0;

        // "connect" as the protocol slot and the outcome as the payload keeps
        // the line shape identical to a packet line, so one parser reads both.
        char outcome[24]{};
        if (result == 0)
        {
            _snprintf_s(outcome, sizeof(outcome), _TRUNCATE, "connect=ok");
        }
        else
        {
            _snprintf_s(
                outcome,
                sizeof(outcome),
                _TRUNCATE,
                "connect=fail wsa=%d",
                error);
        }

        WritePacket("TX", "TCP-CONNECT", local, remote, outcome, static_cast<int>(strlen(outcome)));
        return result;
    }

    int WSAAPI HookedSend(SOCKET socket, const char* buffer, int length, int flags)
    {
        if (OriginalSend == nullptr)
        {
            WSASetLastError(WSANOTINITIALISED);
            return SOCKET_ERROR;
        }

        const int result = OriginalSend(socket, buffer, length, flags);
        if (result > 0)
        {
            char local[AddressTextLength]{};
            char remote[AddressTextLength]{};
            FormatLocalOfSocket(socket, local, sizeof(local));
            FormatPeerOfSocket(socket, remote, sizeof(remote));
            WritePacket("TX", "TCP", local, remote, buffer, result);
        }
        return result;
    }

    int WSAAPI HookedSendTo(
        SOCKET socket,
        const char* buffer,
        int length,
        int flags,
        const struct sockaddr* to,
        int toLength)
    {
        if (OriginalSendTo == nullptr)
        {
            WSASetLastError(WSANOTINITIALISED);
            return SOCKET_ERROR;
        }

        const int result = OriginalSendTo(socket, buffer, length, flags, to, toLength);
        if (result > 0)
        {
            char local[AddressTextLength]{};
            char remote[AddressTextLength]{};
            FormatLocalOfSocket(socket, local, sizeof(local));
            FormatEndpoint(to, toLength, remote, sizeof(remote));
            WritePacket("TX", "UDP", local, remote, buffer, result);
        }
        return result;
    }

    int WSAAPI HookedRecv(SOCKET socket, char* buffer, int length, int flags)
    {
        if (OriginalRecv == nullptr)
        {
            WSASetLastError(WSANOTINITIALISED);
            return SOCKET_ERROR;
        }

        const int result = OriginalRecv(socket, buffer, length, flags);
        if (result > 0)
        {
            char local[AddressTextLength]{};
            char remote[AddressTextLength]{};
            FormatLocalOfSocket(socket, local, sizeof(local));
            FormatPeerOfSocket(socket, remote, sizeof(remote));
            WritePacket("RX", "TCP", local, remote, buffer, result);
        }
        return result;
    }

    int WSAAPI HookedRecvFrom(
        SOCKET socket,
        char* buffer,
        int length,
        int flags,
        struct sockaddr* from,
        int* fromLength)
    {
        if (OriginalRecvFrom == nullptr)
        {
            WSASetLastError(WSANOTINITIALISED);
            return SOCKET_ERROR;
        }

        const int result = OriginalRecvFrom(socket, buffer, length, flags, from, fromLength);
        if (result > 0)
        {
            char local[AddressTextLength]{};
            char remote[AddressTextLength]{};
            FormatLocalOfSocket(socket, local, sizeof(local));
            FormatEndpoint(from, fromLength != nullptr ? *fromLength : 0, remote, sizeof(remote));
            WritePacket("RX", "UDP", local, remote, buffer, result);
        }
        return result;
    }

    int WSAAPI HookedWSARecvFrom(
        SOCKET socket,
        LPWSABUF buffers,
        DWORD bufferCount,
        LPDWORD received,
        LPDWORD flags,
        struct sockaddr* from,
        LPINT fromLength,
        LPWSAOVERLAPPED overlapped,
        LPWSAOVERLAPPED_COMPLETION_ROUTINE completion)
    {
        if (OriginalWSARecvFrom == nullptr)
        {
            WSASetLastError(WSANOTINITIALISED);
            return SOCKET_ERROR;
        }

        const int result = OriginalWSARecvFrom(
            socket,
            buffers,
            bufferCount,
            received,
            flags,
            from,
            fromLength,
            overlapped,
            completion);

        // An overlapped receive that went pending completes later, in a thread we
        // do not wrap: log only what is already finished, so the dump never claims
        // a packet was received when the data has not arrived yet.
        const bool pending = result == SOCKET_ERROR && WSAGetLastError() == WSA_IO_PENDING;
        if (!pending && result == 0 && received != nullptr && *received > 0
            && buffers != nullptr && buffers[0].buf != nullptr)
        {
            char local[AddressTextLength]{};
            char remote[AddressTextLength]{};
            FormatLocalOfSocket(socket, local, sizeof(local));
            FormatEndpoint(from, fromLength != nullptr ? *fromLength : 0, remote, sizeof(remote));
            WritePacket("RX", "UDP", local, remote, buffers[0].buf, static_cast<int>(*received));
        }
        return result;
    }

    // Find the import of `peerModuleName` that currently holds `function`, and
    // hand back the thunk to rewrite. Matching on the loaded address rather than
    // on an ordinal number means an ordinal import is resolved through the
    // loader: the ws2_32.lib shipped with current MSVC imports sendto/recvfrom
    // by ordinal (20/17, measured 2026-10-04) while DNF.exe names them, and the
    // ordinal-to-name table is a property of the peer module, not of this code.
    IMAGE_THUNK_DATA* FindImportThunk(
        HMODULE module,
        const char* peerModuleName,
        const void* function) noexcept
    {
        if (function == nullptr)
        {
            return nullptr;
        }

        const auto base = reinterpret_cast<std::uintptr_t>(module);
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE)
        {
            return nullptr;
        }

        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE)
        {
            return nullptr;
        }

        const auto& directory =
            nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (directory.VirtualAddress == 0)
        {
            return nullptr;
        }

        for (auto* descriptor = reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(
                base + directory.VirtualAddress);
            descriptor->Name != 0;
            ++descriptor)
        {
            const auto* name = reinterpret_cast<const char*>(base + descriptor->Name);
            if (_stricmp(name, peerModuleName) != 0)
            {
                continue;
            }

            auto* thunk = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->FirstThunk);
            for (; thunk->u1.Function != 0; ++thunk)
            {
                if (reinterpret_cast<const void*>(thunk->u1.Function) == function)
                {
                    return thunk;
                }
            }
            return nullptr;
        }

        return nullptr;
    }

    // Replace one import of a module with our wrapper. Writing through
    // memory_patch keeps the page protection handling in one place: the import
    // table lives in a read-only section, so this needs VirtualProtect.
    bool HookImport(
        HMODULE module,
        const char* importedModuleName,
        const char* functionName,
        void* replacement,
        void** original) noexcept
    {
        if (module == nullptr || replacement == nullptr || original == nullptr)
        {
            return false;
        }

        const auto base = reinterpret_cast<std::uintptr_t>(module);
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE)
        {
            return false;
        }

        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE)
        {
            return false;
        }

        const auto& directory =
            nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (directory.VirtualAddress == 0)
        {
            return false;
        }

        auto* thunk = static_cast<IMAGE_THUNK_DATA*>(nullptr);
        for (auto* descriptor = reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(
                base + directory.VirtualAddress);
            descriptor->Name != 0;
            ++descriptor)
        {
            const auto* name = reinterpret_cast<const char*>(base + descriptor->Name);
            if (_stricmp(name, importedModuleName) != 0)
            {
                continue;
            }

            auto* candidate = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->FirstThunk);
            const auto* nameThunk = descriptor->OriginalFirstThunk != 0
                ? reinterpret_cast<const IMAGE_THUNK_DATA*>(base + descriptor->OriginalFirstThunk)
                : nullptr;

            for (; candidate->u1.Function != 0; ++candidate)
            {
                bool matches = false;
                if (nameThunk != nullptr)
                {
                    if (!IMAGE_SNAP_BY_ORDINAL(nameThunk->u1.Ordinal))
                    {
                        const auto* import = reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(
                            base + nameThunk->u1.AddressOfData);
                        matches = strcmp(
                            reinterpret_cast<const char*>(import->Name),
                            functionName) == 0;
                    }
                    ++nameThunk;
                }

                if (!matches)
                {
                    continue;
                }

                thunk = candidate;
                break;
            }
            break;
        }

        if (thunk == nullptr)
        {
            // Not imported by name. The import may still be there by ordinal, in
            // which case the peer module can tell us which function it is.
            HMODULE peer = GetModuleHandleA(importedModuleName);
            if (peer == nullptr)
            {
                return false;
            }

            thunk = FindImportThunk(
                module,
                importedModuleName,
                reinterpret_cast<const void*>(GetProcAddress(peer, functionName)));
        }

        if (thunk == nullptr || PatchedImportCount >= MaximumPatchedImports)
        {
            return false;
        }

        *original = reinterpret_cast<void*>(thunk->u1.Function);
        const auto replacementAddress = reinterpret_cast<std::uintptr_t>(replacement);
        if (!dfl::memory_patch::WriteBytes(
                reinterpret_cast<std::uintptr_t>(&thunk->u1.Function),
                &replacementAddress,
                sizeof(replacementAddress)))
        {
            *original = nullptr;
            return false;
        }

        PatchedImports[PatchedImportCount].Thunk =
            reinterpret_cast<void**>(&thunk->u1.Function);
        PatchedImports[PatchedImportCount].Original = *original;
        ++PatchedImportCount;
        return true;
    }

    void CloseLog() noexcept
    {
        if (LogFile != INVALID_HANDLE_VALUE)
        {
            CloseHandle(LogFile);
            LogFile = INVALID_HANDLE_VALUE;
        }
    }

    bool OpenLogFile(const char* explicitPath) noexcept
    {
        wchar_t path[MAX_PATH*2]{};
        if(explicitPath && explicitPath[0])
        {if(!MultiByteToWideChar(CP_ACP,MB_ERR_INVALID_CHARS,explicitPath,-1,path,MAX_PATH*2))return false;}
        else
        {
            wchar_t leaf[100]{};
            if(swprintf_s(leaf,L"%hs%lu.log",LogFileNamePrefix,GetCurrentProcessId())<0
                ||!dfl::client_paths::BuildLogPath(leaf,path,MAX_PATH*2))return false;
        }
        LogFile = CreateFileW(
            path,
            GENERIC_WRITE,
            FILE_SHARE_READ,
            nullptr,
            CREATE_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (LogFile == INVALID_HANDLE_VALUE)
        {
            OutputDebugStringA("DFLegacy.Ijl15: net dump log could not be created.\n");
            return false;
        }

        char header[512]{};
        SYSTEMTIME now{};
        GetLocalTime(&now);
        const int written = _snprintf_s(
            header,
            sizeof(header),
            _TRUNCATE,
            "# DFLegacy.Ijl15 net dump: %04u-%02u-%02u %02u:%02u:%02u pid=%lu\r\n"
            "# columns: timestamp thread direction protocol local -> remote len hex\r\n",
            now.wYear,
            now.wMonth,
            now.wDay,
            now.wHour,
            now.wMinute,
            now.wSecond,
            GetCurrentProcessId());
        if (written > 0)
        {
            DWORD bytesWritten = 0;
            WriteFile(LogFile, header, static_cast<DWORD>(written), &bytesWritten, nullptr);
            WrittenBytes += bytesWritten;
        }
        return true;
    }

    void RestoreHooks() noexcept
    {        for (std::size_t index = 0; index < PatchedImportCount; ++index)
        {
            const std::uintptr_t target = reinterpret_cast<std::uintptr_t>(
                PatchedImports[index].Thunk);
            dfl::memory_patch::WriteBytes(
                target,
                &PatchedImports[index].Original,
                sizeof(void*));
        }
        PatchedImportCount = 0;
        HookedModule = nullptr;

        OriginalSend = nullptr;
        OriginalSendTo = nullptr;
        OriginalRecv = nullptr;
        OriginalRecvFrom = nullptr;
        OriginalConnect = nullptr;
        OriginalWSARecvFrom = nullptr;
    }
}

namespace dfl::net_dump
{
    bool InstallForModule(void* module, const InstallOptions& options) noexcept
    {
        LastReport = InstallReport{};
        if (module == nullptr)
        {
            LastReport.Failure = "no module to hook";
            return false;
        }

        if (LogFile != INVALID_HANDLE_VALUE)
        {
            LastReport.Failure = "a dump is already installed";
            return false;
        }

        // Reaching here after a previous install means a test did not clean up;
        // start from a known state instead of stacking hooks.
        if (PatchedImportCount != 0)
        {
            RestoreHooks();
        }

        PayloadLimit = options.PayloadBytes < 0
            ? 0
            : (options.PayloadBytes > MaximumPayloadBytes
                ? MaximumPayloadBytes
                : options.PayloadBytes);
        WrittenLimit = options.MaxMegabytes > 0
            ? static_cast<std::int64_t>(options.MaxMegabytes) * 1024 * 1024
            : 0;
        TruncationReported = false;
        WrittenBytes = 0;
        WritePacketCalls = 0;
        WrittenLines = 0;

        if (!LogLockReady)
        {
            InitializeCriticalSection(&LogLock);
            LogLockReady = true;
        }

        if (!OpenLogFile(options.ExplicitLogPath))
        {
            LastReport.Failure = "the log file could not be created";
            return false;
        }

        HMODULE target = static_cast<HMODULE>(module);
        const bool send = HookImport(target, "ws2_32.dll", "send", reinterpret_cast<void*>(&HookedSend), reinterpret_cast<void**>(&OriginalSend));
        const bool sendTo = HookImport(target, "ws2_32.dll", "sendto", reinterpret_cast<void*>(&HookedSendTo), reinterpret_cast<void**>(&OriginalSendTo));
        const bool recv = HookImport(target, "ws2_32.dll", "recv", reinterpret_cast<void*>(&HookedRecv), reinterpret_cast<void**>(&OriginalRecv));
        const bool recvFrom = HookImport(target, "ws2_32.dll", "recvfrom", reinterpret_cast<void*>(&HookedRecvFrom), reinterpret_cast<void**>(&OriginalRecvFrom));
        const bool connect = HookImport(target, "ws2_32.dll", "connect", reinterpret_cast<void*>(&HookedConnect), reinterpret_cast<void**>(&OriginalConnect));
        const bool wsaRecvFrom = HookImport(
            target,
            "ws2_32.dll",
            "WSARecvFrom",
            reinterpret_cast<void*>(&HookedWSARecvFrom),
            reinterpret_cast<void**>(&OriginalWSARecvFrom));

        if (!send && !sendTo && !recv && !recvFrom && !connect && !wsaRecvFrom)
        {
            LastReport.Failure = "no ws2_32 import matched";
            OutputDebugStringA("DFLegacy.Ijl15: net dump found no ws2_32 imports to hook.\n");
            CloseLog();
            return false;
        }

        HookedModule = module;

        InstallReport report{};
        report.Send = send;
        report.SendTo = sendTo;
        report.Recv = recv;
        report.RecvFrom = recvFrom;
        report.Connect = connect;
        report.WSARecvFrom = wsaRecvFrom;
        LastReport = report;

        char message[256]{};
        _snprintf_s(
            message,
            sizeof(message),
            _TRUNCATE,
            "DFLegacy.Ijl15: net dump installed (send=%d sendto=%d recv=%d recvfrom=%d connect=%d wsarecvfrom=%d).\n",
            send ? 1 : 0,
            sendTo ? 1 : 0,
            recv ? 1 : 0,
            recvFrom ? 1 : 0,
            connect ? 1 : 0,
            wsaRecvFrom ? 1 : 0);
        OutputDebugStringA(message);
        return true;
    }

    bool Install() noexcept
    {
        const auto& settings = dfl::ijl15_config::Get();
        if (!settings.DumpNetwork || LogFile != INVALID_HANDLE_VALUE)
        {
            return false;
        }

        InstallOptions options{};
        options.PayloadBytes = settings.DumpNetworkPayloadBytes;
        options.MaxMegabytes = settings.DumpNetworkMaxMegabytes;
        options.ExplicitLogPath = nullptr;

        // The client is the main module; ijl15.dll is loaded into it, so
        // GetModuleHandleA(nullptr) is DNF.exe.
        return InstallForModule(GetModuleHandleA(nullptr), options);
    }

    void UninstallForTest() noexcept
    {
        RestoreHooks();
        CloseLog();
        if (LogLockReady)
        {
            DeleteCriticalSection(&LogLock);
            LogLockReady = false;
        }
    }

    InstallReport LastInstallReport() noexcept
    {
        InstallReport report = LastReport;
        report.WritePacketCalls = WritePacketCalls;
        report.WrittenLines = WrittenLines;
        return report;
    }
}
