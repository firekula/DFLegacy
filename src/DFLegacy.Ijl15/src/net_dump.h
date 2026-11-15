#pragma once

#include <cstddef>

namespace dfl::net_dump
{
    // IAT-hook the client's own ws2_32 send/sendto/recv/recvfrom/WSARecvFrom so
    // that every TCP segment and UDP datagram the game exchanges is written to
    // netdump-<pid>.log beside the client executable.
    //
    // Why a hook instead of a packet capture: with two clients on the same
    // machine the party/dungeon traffic is Windows loopback, which a packet
    // capture does not see at all -- measured 2026-10-04: a 184 MB pktmon
    // capture contained zero loopback UDP packets, while the gameplay data
    // between the two clients is exactly loopback UDP.
    // Hooking the client's own socket calls has no such blind spot, needs no
    // administrator rights, and sees the payloads before any encryption or
    // framing the capture would have to reconstruct.
    //
    // The five hooked functions are exactly the ws2_32 imports DNF.exe
    // statically declares (checked 2026-10-04 with pefile): WSAAsyncSelect,
    // WSACleanup, WSACloseEvent, WSACreateEvent, WSAEnumNetworkEvents,
    // WSAEventSelect, WSAGetLastError, WSARecvFrom, WSAResetEvent, WSAStartup,
    // WSAWaitForMultipleEvents, accept, bind, closesocket, connect,
    // gethostbyname, gethostname, getsockname, htonl, htons, inet_addr,
    // inet_ntoa, ioctlsocket, listen, recv, recvfrom, select, send, sendto,
    // setsockopt, shutdown, socket. There is no WSASend/WSARecv/mswsock import,
    // so hooking these five leaves no gap on this client.
    //
    // Non-fatal by design: a client whose import table does not match simply
    // runs without the dump. Returns false in that case.
    bool Install() noexcept;

    // Everything Install() needs, with the pieces a test has to control exposed
    // explicitly. Production uses the defaults: settings from Config.ini, log
    // beside the running executable.
    struct InstallOptions
    {
        // Payload bytes written per packet; 0 dumps the whole payload.
        int PayloadBytes = 0;
        // Stop writing once the log reaches this size (0 disables the cap).
        int MaxMegabytes = 0;
        // Log file to create, or nullptr for netdump-<pid>.log beside the
        // running executable. Tests pass their own path so they do not have to
        // be copied next to the module they hook.
        const char* ExplicitLogPath = nullptr;
    };

    // Hook the ws2_32 imports of one module. Install() is this with the module
    // resolved from Config.ini; the split exists so a test can hook its own
    // import table and drive a real socket through the wrappers.
    bool InstallForModule(void* module, const InstallOptions& options) noexcept;

    // Release the current log so the next InstallForModule can open another
    // one, and restore the imports of the last hooked module. Leaving a hooked
    // import table pointing at an unloaded module would crash the process, so
    // tests must call this before they unload us.
    void UninstallForTest() noexcept;

    // Which of the five hooks the last InstallForModule managed to place, for
    // tests and for diagnosing a client whose import table does not match.
    struct InstallReport
    {
        bool Send = false;
        bool SendTo = false;
        bool Recv = false;
        bool RecvFrom = false;
        // connect() is the one call that records which address the client chose
        // to dial, including an attempt that failed and never sent a byte.
        bool Connect = false;
        bool WSARecvFrom = false;
        // Non-null when InstallForModule bailed out before hooking anything.
        const char* Failure = nullptr;
        // How many times a wrapper reached the writer, and how many lines it
        // actually emitted. Calls > Lines means a line was rejected; Calls == 0
        // means the wrapped call never ran.
        int WritePacketCalls = 0;
        int WrittenLines = 0;
    };

    InstallReport LastInstallReport() noexcept;
}
