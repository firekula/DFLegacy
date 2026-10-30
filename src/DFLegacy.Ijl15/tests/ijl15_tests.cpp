#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#define IJL15_EXPORTS
#include "auto_input.h"
#include "dnf_patches.h"
#include "external_string_table.h"
#include "ijl15.h"
#include "ijl15_config.h"
#include "memory_patch.h"
#include "net_dump.h"
#include "output_paths.h"

bool TestStageFollowRepair();
bool TestSummonOwnerLossOrdering();
bool TestPartyCombatDetailInitialization();
bool TestMageSkillContactPrerequisite();
bool TestBossRoomLateSpawnDeath();
bool TestPriestHealTargetReplay();
bool TestGroundItemRemovalNotification();
bool TestPartyCardDisplay();
bool TestClientServiceUrls();
int ClientServiceUrlProbe(int argc, char** argv);
bool TestChannelReentryMtu();
bool TestChannelSwitch();
bool TestCreatureGiftRow();
bool TestCountdownCoin();
bool TestRelaySendQueue();
bool TestDungeonTooltipCleanup();

namespace
{
struct IjlApi
{
    HMODULE Module;
    const IJLibVersion* (__stdcall* GetLibVersion)();
    IJLERR (__stdcall* Init)(JPEG_CORE_PROPERTIES*);
    IJLERR (__stdcall* Free)(JPEG_CORE_PROPERTIES*);
    IJLERR (__stdcall* Read)(JPEG_CORE_PROPERTIES*, IJLIOTYPE);
    IJLERR (__stdcall* Write)(JPEG_CORE_PROPERTIES*, IJLIOTYPE);
    const char* (__stdcall* ErrorStr)(IJLERR);
};

int Fail(const char* message)
{
    std::fprintf(stderr, "FAIL: %s\n", message);
    return 1;
}

bool IsClose(unsigned char actual, unsigned char expected, int tolerance)
{
    return std::abs(
        static_cast<int>(actual) - static_cast<int>(expected)) <= tolerance;
}

// ---------------------------------------------------------------------------
// Net dump (src/net_dump.cpp)
//
// What this proves: the IAT hook really intercepts this module's own ws2_32
// calls, the original call still happens, and the datagram that lands in the
// log carries the right direction, addresses and bytes. That is the client-side
// equivalent of "the packet capture shows it", which is not available for the
// loopback traffic this dump exists to see.
// ---------------------------------------------------------------------------

std::string ReadAllText(const std::string& path)
{
    std::ifstream stream(path.c_str(), std::ios::binary);
    std::ostringstream buffer;
    buffer << stream.rdbuf();
    return buffer.str();
}

enum class Match
{
    Exact,
    Contains,
    Absent,
};

void Check(
    std::vector<std::string>* failures,
    const std::string& text,
    const std::string& needle,
    Match mode,
    const std::string& what)
{
    const bool found = text.find(needle) != std::string::npos;
    const bool satisfied = mode == Match::Absent ? !found : found;
    if (satisfied)
    {
        return;
    }

    const char* wanted = mode == Match::Exact
        ? "containing"
        : (mode == Match::Contains ? "containing" : "not containing");
    failures->push_back(
        what + ": no line " + wanted + " [" + needle + "]");
}

// The sockets used by this test all bind to an ephemeral loopback port, so the
// only moving part in an expected line is the port number.
void FillLoopbackAddress(struct sockaddr_in* address, unsigned short port)
{
    std::memset(address, 0, sizeof(*address));
    address->sin_family = AF_INET;
    address->sin_port = htons(port);
    address->sin_addr.s_addr = htonl(INADDR_LOOPBACK);
}

// The dump renders payloads as upper-case hex, so expectations built from text
// have to go through the same encoding.
std::string ToHex(const std::string& text)
{
    static constexpr char Digits[] = "0123456789ABCDEF";
    std::string result;
    result.reserve(text.size() * 2);
    for (const unsigned char value : text)
    {
        result.push_back(Digits[value >> 4]);
        result.push_back(Digits[value & 0x0F]);
    }
    return result;
}

bool TestNetDump()
{
    WSADATA wsaData{};
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0)
    {
        return Fail("WSAStartup failed for the net dump test");
    }

    std::array<char, MAX_PATH> temporaryDirectory{};
    if (GetTempPathA(
            static_cast<DWORD>(temporaryDirectory.size()),
            temporaryDirectory.data()) == 0)
    {
        WSACleanup();
        return Fail("could not resolve the temporary directory");
    }

    const std::string logPath =
        std::string(temporaryDirectory.data()) + "dflegacy-netdump-test.log";
    DeleteFileA(logPath.c_str());

    // Hook this executable: it imports sendto/recvfrom from ws2_32 exactly the
    // way DNF.exe does, so the same import-table walk is under test.
    constexpr int PayloadLimit = 6;
    dfl::net_dump::InstallOptions options{};
    options.PayloadBytes = PayloadLimit;
    options.MaxMegabytes = 0;
    options.ExplicitLogPath = logPath.c_str();
    if (!dfl::net_dump::InstallForModule(GetModuleHandleA(nullptr), options))
    {
        const dfl::net_dump::InstallReport report = dfl::net_dump::LastInstallReport();
        std::fprintf(
            stderr,
            "net dump install failed: %s (send=%d sendto=%d recv=%d recvfrom=%d wsarecvfrom=%d)\n",
            report.Failure != nullptr ? report.Failure : "(no reason recorded)",
            report.Send ? 1 : 0,
            report.SendTo ? 1 : 0,
            report.Recv ? 1 : 0,
            report.RecvFrom ? 1 : 0,
            report.WSARecvFrom ? 1 : 0);
        WSACleanup();
        return Fail("net dump did not install on the test executable");
    }

    const dfl::net_dump::InstallReport installReport = dfl::net_dump::LastInstallReport();
    if (!installReport.SendTo || !installReport.RecvFrom)
    {
        std::fprintf(
            stderr,
            "net dump hooked sendto=%d recvfrom=%d, so the UDP path is not fully covered\n",
            installReport.SendTo ? 1 : 0,
            installReport.RecvFrom ? 1 : 0);
    }
    std::vector<std::string> failures;

    const SOCKET sender = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    const SOCKET receiver = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (sender == INVALID_SOCKET || receiver == INVALID_SOCKET)
    {
        dfl::net_dump::UninstallForTest();
        WSACleanup();
        return Fail("could not create the UDP sockets for the net dump test");
    }

    struct sockaddr_in receiverAddress{};
    FillLoopbackAddress(&receiverAddress, 0);
    if (bind(
            receiver,
            reinterpret_cast<const struct sockaddr*>(&receiverAddress),
            sizeof(receiverAddress)) == SOCKET_ERROR)
    {
        closesocket(sender);
        closesocket(receiver);
        dfl::net_dump::UninstallForTest();
        WSACleanup();
        return Fail("could not bind the receiving UDP socket");
    }

    int addressLength = sizeof(receiverAddress);
    if (getsockname(
            receiver,
            reinterpret_cast<struct sockaddr*>(&receiverAddress),
            &addressLength) == SOCKET_ERROR
        || receiverAddress.sin_port == 0)
    {
        closesocket(sender);
        closesocket(receiver);
        dfl::net_dump::UninstallForTest();
        WSACleanup();
        return Fail("could not resolve the receiving UDP port");
    }
    const unsigned short receiverPort = ntohs(receiverAddress.sin_port);

    struct sockaddr_in senderAddress{};
    FillLoopbackAddress(&senderAddress, 0);
    if (bind(
            sender,
            reinterpret_cast<const struct sockaddr*>(&senderAddress),
            sizeof(senderAddress)) == SOCKET_ERROR)
    {
        closesocket(sender);
        closesocket(receiver);
        dfl::net_dump::UninstallForTest();
        WSACleanup();
        return Fail("could not bind the sending UDP socket");
    }

    addressLength = sizeof(senderAddress);
    if (getsockname(
            sender,
            reinterpret_cast<struct sockaddr*>(&senderAddress),
            &addressLength) == SOCKET_ERROR
        || senderAddress.sin_port == 0)
    {
        closesocket(sender);
        closesocket(receiver);
        dfl::net_dump::UninstallForTest();
        WSACleanup();
        return Fail("could not resolve the sending UDP port");
    }
    const unsigned short senderPort = ntohs(senderAddress.sin_port);

    // Bytes with a NUL and high values, so the dump has to be hex rather than a
    // string that happens to survive a round trip.
    const char payload[] = {'\x11', '\x22', '\x00', '\x33', '\xFE', '\x7A', '\x0D'};
    const int payloadLength = static_cast<int>(sizeof(payload));

    const int sent = sendto(
        sender,
        payload,
        payloadLength,
        0,
        reinterpret_cast<const struct sockaddr*>(&receiverAddress),
        sizeof(receiverAddress));

    char received[64]{};
    struct sockaddr_in fromAddress{};
    int fromLength = sizeof(fromAddress);
    const int got = recvfrom(
        receiver,
        received,
        sizeof(received),
        0,
        reinterpret_cast<struct sockaddr*>(&fromAddress),
        &fromLength);

    closesocket(sender);
    closesocket(receiver);

    // Read the report before uninstalling: it carries how many times a wrapper
    // reached the writer and how many lines it emitted, which is what tells a
    // missing packet apart from an unhooked call. The hooks stay installed a
    // little longer, because the connect test below needs them.
    const dfl::net_dump::InstallReport afterTraffic = dfl::net_dump::LastInstallReport();

    if (sent != payloadLength)
    {
        failures.push_back("the hooked sendto did not report the full length");
    }
    if (got != payloadLength || std::memcmp(received, payload, payloadLength) != 0)
    {
        failures.push_back("the wrapped sendto/recvfrom did not carry the datagram");
    }

    std::string text = ReadAllText(logPath);
    if (text.empty())
    {
        WSACleanup();
        return Fail("the net dump log was not written");
    }

    const std::string senderText =
        "127.0.0.1:" + std::to_string(senderPort);
    const std::string receiverText =
        "127.0.0.1:" + std::to_string(receiverPort);

    // TX: the wrapper saw the datagram that really left, with the destination
    // from the call and the source from the socket.
    Check(
        &failures,
        text,
        " TX UDP " + senderText + " -> " + receiverText + " len="
            + std::to_string(payloadLength) + " (truncated) hex=11220033FE7A",
        Match::Exact,
        "the transmitted datagram");

    // RX: the wrapper saw the datagram that really arrived, with the peer from
    // recvfrom's own out-parameter.
    Check(
        &failures,
        text,
        " RX UDP " + receiverText + " -> " + senderText + " len="
            + std::to_string(payloadLength) + " (truncated) hex=11220033FE7A",
        Match::Exact,
        "the received datagram");

    // The bytes past the configured payload limit must not be in the dump.
    Check(
        &failures,
        text,
        "11220033FE7A0D",
        Match::Absent,
        "the payload limit");

    // A normal reception is not a truncation, so it must not be labelled one.
    Check(
        &failures,
        text,
        " RX TCP",
        Match::Absent,
        "the protocol label");

    // A dial to a closed port must still be recorded: that is the only evidence
    // left when a client picks a wrong endpoint and never sends a byte. Reserve
    // a port that nothing can be listening on by binding it and closing it.
    unsigned short closedPort = 0;
    {
        const SOCKET probe = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        if (probe != INVALID_SOCKET)
        {
            struct sockaddr_in probeAddress{};
            FillLoopbackAddress(&probeAddress, 0);
            if (bind(
                    probe,
                    reinterpret_cast<const struct sockaddr*>(&probeAddress),
                    sizeof(probeAddress)) != SOCKET_ERROR)
            {
                int probeLength = sizeof(probeAddress);
                if (getsockname(
                        probe,
                        reinterpret_cast<struct sockaddr*>(&probeAddress),
                        &probeLength) != SOCKET_ERROR)
                {
                    closedPort = ntohs(probeAddress.sin_port);
                }
            }
            closesocket(probe);
        }
    }

    int connectError = 0;
    SOCKET dialer = INVALID_SOCKET;
    if (closedPort == 0)
    {
        failures.push_back("could not reserve a closed TCP port for the connect test");
    }
    else
    {
        dialer = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        if (dialer == INVALID_SOCKET)
        {
            failures.push_back("could not create the TCP socket for the connect test");
        }
        else
        {
            struct sockaddr_in closedAddress{};
            FillLoopbackAddress(&closedAddress, closedPort);
            const int connected = connect(
                dialer,
                reinterpret_cast<const struct sockaddr*>(&closedAddress),
                sizeof(closedAddress));
            connectError = connected == SOCKET_ERROR ? WSAGetLastError() : 0;
        }
    }

    if (!installReport.Connect)
    {
        failures.push_back("the connect import was not hooked");
    }

    // The connect line is written after the UDP exchange, so the log has to be
    // re-read before anything can assert on it.
    dfl::net_dump::UninstallForTest();
    text = ReadAllText(logPath);

    if (installReport.Connect && closedPort != 0 && dialer != INVALID_SOCKET)
    {
        const std::string connectLine = "connect=fail wsa="
            + std::to_string(connectError);
        // The socket is never bound, so there is no local address to report and
        // the fallback "-" must appear rather than a made-up endpoint.
        Check(
            &failures,
            text,
            " TX TCP-CONNECT - -> 127.0.0.1:" + std::to_string(closedPort) + " ",
            Match::Contains,
            "the dialled address");

        // The outcome text is longer than the 6-byte payload limit in force for
        // this test, so the line must carry the limit marker and the first six
        // bytes of "connect=fail wsa=..." -- which is "connec".
        const std::string prefix = connectLine.substr(0, PayloadLimit);
        Check(
            &failures,
            text,
            "-> 127.0.0.1:" + std::to_string(closedPort) + " len="
                + std::to_string(connectLine.size()) + " (truncated) hex="
                + ToHex(prefix),
            Match::Contains,
            "the closed port and its failure code");
    }
    if (dialer != INVALID_SOCKET)
    {
        closesocket(dialer);
    }

    // The dial above must not have been mistaken for a data packet.
    Check(
        &failures,
        text,
        " RX TCP-CONNECT",
        Match::Absent,
        "the connect direction label");

    WSACleanup();

    if (!failures.empty())
    {
        std::fprintf(stderr, "net dump log (%s):\n", logPath.c_str());
        std::fputs(text.c_str(), stderr);
        std::fprintf(
            stderr,
            "net dump: hooked sendto=%d recvfrom=%d connect=%d; wrapper reached writer %d time(s), "
            "wrote %d line(s)\n",
            installReport.SendTo ? 1 : 0,
            installReport.RecvFrom ? 1 : 0,
            installReport.Connect ? 1 : 0,
            afterTraffic.WritePacketCalls,
            afterTraffic.WrittenLines);
        for (const std::string& failure : failures)
        {
            std::fprintf(stderr, "FAIL: net dump %s\n", failure.c_str());
        }
        return false;
    }

    return true;
}

// The path the client actually takes: Config.ini switches the dump on, Load()
// reads it, and Install() resolves the module and the log location itself. The
// test runs with its own Config.ini beside it (copied from the module's), so
// this exercises the real wiring rather than InstallForModule's seams.
bool TestNetDumpFromConfiguration()
{
    constexpr char ConfigName[] = "Config.ini";
    constexpr char DisabledConfigName[] = "Config.netdump-test-off.ini";
    constexpr char EnabledConfigName[] = "Config.netdump-test-on.ini";

    const auto writeFile = [](const char* path, const char* text) -> bool
    {
        std::ofstream stream(path, std::ios::binary | std::ios::trunc);
        stream << text;
        return stream.good();
    };

    // Keep whatever the build placed beside the tests, so a failure here does
    // not leave the test directory in a state that breaks the next run.
    const std::string original = ReadAllText(ConfigName);
    if (original.empty())
    {
        return Fail("the test directory has no Config.ini to work with");
    }

    constexpr char DisabledConfig[] =
        "[Features]\r\n"
        "DumpNetwork=0\r\n";
    constexpr char EnabledConfig[] =
        "[Features]\r\n"
        "DumpNetwork=1\r\n"
        "DumpNetworkPayloadBytes=4\r\n"
        "DumpNetworkMaxMegabytes=1\r\n";

    if (!writeFile(DisabledConfigName, DisabledConfig)
        || !writeFile(EnabledConfigName, EnabledConfig))
    {
        return Fail("could not write the net dump test configurations");
    }

    bool ok = true;

    if (!CopyFileA(DisabledConfigName, ConfigName, FALSE))
    {
        return Fail("could not install the disabled test configuration");
    }

    // Load() caches the first read, which is why each case needs its own
    // process. The test therefore only asserts the enabled case here and the
    // disabled default is covered by DumpNetwork being off in the shipped file.
    if (!CopyFileA(EnabledConfigName, ConfigName, FALSE))
    {
        return Fail("could not install the enabled test configuration");
    }

    if (!dfl::ijl15_config::Load())
    {
        ok = false;
    }

    const dfl::ijl15_config::Settings& settings = dfl::ijl15_config::Get();
    if (!settings.DumpNetwork)
    {
        std::fprintf(stderr, "net dump: Config.ini DumpNetwork=1 was not read\n");
        ok = false;
    }
    if (settings.DumpNetworkPayloadBytes != 4)
    {
        std::fprintf(
            stderr,
            "net dump: DumpNetworkPayloadBytes was read as %d, expected 4\n",
            settings.DumpNetworkPayloadBytes);
        ok = false;
    }
    if (settings.DumpNetworkMaxMegabytes != 1)
    {
        std::fprintf(
            stderr,
            "net dump: DumpNetworkMaxMegabytes was read as %d, expected 1\n",
            settings.DumpNetworkMaxMegabytes);
        ok = false;
    }

    // Install() with no explicit path puts the log beside the executable, which
    // is what the client does, and returns false only if nothing could be
    // hooked. No packets are sent here: this checks the wiring, not the wire.
    if (ok && !dfl::net_dump::Install())
    {
        const dfl::net_dump::InstallReport report = dfl::net_dump::LastInstallReport();
        std::fprintf(
            stderr,
            "net dump: Install() from Config.ini failed: %s\n",
            report.Failure != nullptr ? report.Failure : "(no reason recorded)");
        ok = false;
    }
    if (ok)
    {
        const dfl::net_dump::InstallReport report = dfl::net_dump::LastInstallReport();
        if (!report.SendTo || !report.RecvFrom)
        {
            std::fprintf(
                stderr,
                "net dump: Install() hooked sendto=%d recvfrom=%d\n",
                report.SendTo ? 1 : 0,
                report.RecvFrom ? 1 : 0);
            ok = false;
        }
    }
    dfl::net_dump::UninstallForTest();

    // The log Install() created sits beside the executable, named after the pid.
    char logName[64]{};
    std::snprintf(
        logName,
        sizeof(logName),
        "netdump-%lu.log",
        static_cast<unsigned long>(GetCurrentProcessId()));
    if (ok && !std::ifstream(logName, std::ios::binary).good())
    {
        std::fprintf(stderr, "net dump: %s was not created beside the executable\n", logName);
        ok = false;
    }
    DeleteFileA(logName);

    writeFile(ConfigName, original.c_str());
    DeleteFileA(DisabledConfigName);
    DeleteFileA(EnabledConfigName);
    return ok;
}

template<typename T>
T Resolve(HMODULE module, const char* name)
{
    return reinterpret_cast<T>(GetProcAddress(module, name));
}

bool LoadApi(IjlApi* api)
{
    api->Module = LoadLibraryW(L"ijl15.dll");
    if (api->Module == nullptr)
    {
        return false;
    }

    api->GetLibVersion = Resolve<decltype(api->GetLibVersion)>(
        api->Module,
        "ijlGetLibVersion");
    api->Init = Resolve<decltype(api->Init)>(api->Module, "ijlInit");
    api->Free = Resolve<decltype(api->Free)>(api->Module, "ijlFree");
    api->Read = Resolve<decltype(api->Read)>(api->Module, "ijlRead");
    api->Write = Resolve<decltype(api->Write)>(api->Module, "ijlWrite");
    api->ErrorStr = Resolve<decltype(api->ErrorStr)>(
        api->Module,
        "ijlErrorStr");
    return api->GetLibVersion != nullptr
        && api->Init != nullptr
        && api->Free != nullptr
        && api->Read != nullptr
        && api->Write != nullptr
        && api->ErrorStr != nullptr;
}

bool TestMemoryPatchHelpers()
{
    constexpr std::size_t RegionSize = 64;
    void* const region = VirtualAlloc(
        nullptr,
        RegionSize,
        MEM_COMMIT | MEM_RESERVE,
        PAGE_EXECUTE_READWRITE);
    if (region == nullptr)
    {
        return false;
    }

    std::memset(region, 0x5A, RegionSize);
    DWORD oldProtection = 0;
    if (!VirtualProtect(
            region,
            RegionSize,
            PAGE_EXECUTE_READ,
            &oldProtection))
    {
        VirtualFree(region, 0, MEM_RELEASE);
        return false;
    }

    const std::uintptr_t base =
        reinterpret_cast<std::uintptr_t>(region);
    const std::array<unsigned char, 4> bytes = {
        0x10,
        0x20,
        0x30,
        0x40
    };
    bool passed = dfl::memory_patch::WriteBytes(
        base,
        bytes.data(),
        bytes.size());
    passed = passed
        && std::memcmp(region, bytes.data(), bytes.size()) == 0;

    passed = passed && dfl::memory_patch::WriteNops(base + 4, 4);
    const auto* const memory =
        static_cast<const unsigned char*>(region);
    passed = passed
        && std::all_of(memory + 4, memory + 8, [](unsigned char value)
        {
            return value == 0x90;
        });

    passed = passed && dfl::memory_patch::Fill(base + 8, 0, 4);
    passed = passed
        && std::all_of(memory + 8, memory + 12, [](unsigned char value)
        {
            return value == 0;
        });

    constexpr std::size_t JumpOffset = 16;
    constexpr std::size_t CallOffset = 24;
    constexpr std::size_t JumpDestinationOffset = 48;
    constexpr std::size_t CallDestinationOffset = 52;
    passed = passed && dfl::memory_patch::WriteRelativeJump(
        base + JumpOffset,
        base + JumpDestinationOffset);
    passed = passed && memory[JumpOffset] == 0xE9;
    std::int32_t displacement = 0;
    std::memcpy(
        &displacement,
        memory + JumpOffset + 1,
        sizeof(displacement));
    passed = passed
        && base + JumpOffset + 5 + displacement
            == base + JumpDestinationOffset;

    passed = passed && dfl::memory_patch::WriteRelativeCall(
        base + CallOffset,
        base + CallDestinationOffset);
    passed = passed && memory[CallOffset] == 0xE8;
    std::memcpy(
        &displacement,
        memory + CallOffset + 1,
        sizeof(displacement));
    passed = passed
        && base + CallOffset + 5 + displacement
            == base + CallDestinationOffset;

    passed = passed
        && dfl::memory_patch::WriteBreakpoints(base + 32, 4);
    passed = passed
        && std::all_of(memory + 32, memory + 36, [](unsigned char value)
        {
            return value == 0xCC;
        });
    passed = passed
        && !dfl::memory_patch::WriteRelativeJump(0, base);
    passed = passed
        && !dfl::memory_patch::WriteRelativeCall(
            0x00010000,
            0xF0000000);

    MEMORY_BASIC_INFORMATION information{};
    passed = passed
        && VirtualQuery(
            region,
            &information,
            sizeof(information)) == sizeof(information)
        && information.Protect == PAGE_EXECUTE_READ;

    VirtualFree(region, 0, MEM_RELEASE);
    return passed;
}

bool TestCustomStackableUseModePolicy()
{
    using dfl::dnf_patches::ShouldUseStackableMode1;

    return dfl::dnf_patches::EnableCustomStackableUseMode1
        && ShouldUseStackableMode1(7'518u, 10u)
        && ShouldUseStackableMode1(7'519u, 10u)
        && ShouldUseStackableMode1(1'121u, 10u)
        && ShouldUseStackableMode1(1'122u, 10u)
        && ShouldUseStackableMode1(1'123u, 10u)
        && !ShouldUseStackableMode1(7'517u, 10u)
        && ShouldUseStackableMode1(71u, 13u)
        && ShouldUseStackableMode1(0u, 13u)
        && !ShouldUseStackableMode1(71u, 0u);
}

bool TestCustomPopupPatchConfiguration()
{
    return dfl::dnf_patches::RemoveCustomPopupAnnouncementTitle;
}

bool TestMailboxArchiveRefreshPatchConfiguration()
{
    return dfl::dnf_patches::FixMailboxArchiveRefresh;
}

bool TestCeraPurchaseGatePolicy()
{
    using Gate = dfl::dnf_patches::CeraPurchaseGate;
    using dfl::dnf_patches::ShouldBypassCeraPurchaseGate;

    return ShouldBypassCeraPurchaseGate(Gate::LegacySaleWindow)
        && ShouldBypassCeraPurchaseGate(Gate::PerCharacterLimit)
        && ShouldBypassCeraPurchaseGate(Gate::CharacterLevel)
        && !ShouldBypassCeraPurchaseGate(Gate::ClientInventoryCapacity)
        && !ShouldBypassCeraPurchaseGate(Gate::JobCompatibility)
        && !ShouldBypassCeraPurchaseGate(Gate::CurrencyBalance)
        && !ShouldBypassCeraPurchaseGate(Gate::RequiredOption)
        && !ShouldBypassCeraPurchaseGate(Gate::GiftEligibility);
}

bool TestAutoInputConfigDefaults()
{
    // Default off: must be false when Config.ini does not provide the key.
    return dfl::ijl15_config::Settings{}.EnableAutoInput == false;
}

bool TestExternalStringTableConfiguration()
{
    using Validation = dfl::external_string_table::EncodingValidation;

    constexpr unsigned char Cp936Text[] = {
        '7', '2', '3', '>',
        0xCD, 0xE6,
        0xBC, 0xD2,
        '\r', '\n'};
    constexpr unsigned char Utf8Text[] = {
        '7', '2', '3', '>',
        0xE7, 0x8E, 0xA9,
        0xE5, 0xAE, 0xB6,
        '\r', '\n'};
    constexpr unsigned char Utf8Bom[] = {
        0xEF, 0xBB, 0xBF, '7', '2', '3', '>'};
    constexpr unsigned char InvalidCp936[] = {
        '7', '2', '3', '>', 0x81, 0x7F};
    constexpr unsigned char EmbeddedNull[] = {
        '7', '2', '3', '>', 0, 'x'};

    wchar_t path[MAX_PATH]{};
    return dfl::dnf_patches::UseExternalDnfStringTable
        && std::wcscmp(
            dfl::external_string_table::RelativePath,
            L"Plugin\\dnf.str") == 0
        && dfl::external_string_table::BuildPluginPath(
            L"D:\\DFLegacy\\DF2008\\DNF.exe",
            path,
            MAX_PATH)
        && std::wcscmp(
            path,
            L"D:\\DFLegacy\\DF2008\\Plugin\\dnf.str") == 0
        && dfl::external_string_table::ValidateCp936(
            Cp936Text,
            sizeof(Cp936Text)) == Validation::ValidCp936
        && dfl::external_string_table::ValidateCp936(
            Utf8Text,
            sizeof(Utf8Text)) == Validation::Utf8
        && dfl::external_string_table::ValidateCp936(
            Utf8Bom,
            sizeof(Utf8Bom)) == Validation::UnicodeBom
        && dfl::external_string_table::ValidateCp936(
            InvalidCp936,
            sizeof(InvalidCp936)) == Validation::InvalidCp936
        && dfl::external_string_table::ValidateCp936(
            EmbeddedNull,
            sizeof(EmbeddedNull)) == Validation::EmbeddedNull;
}

int BuiltinStringCalls = 0;
int __cdecl BuiltinStringFixture(const char* name)
{
    ++BuiltinStringCalls;
    return name && std::strcmp(name, "dnf.str") == 0 ? 7 : -1;
}

bool TestExternalStringTableFallback()
{
    char temporary[MAX_PATH]{}, unique[MAX_PATH]{};
    if (!GetTempPathA(MAX_PATH, temporary)
        || !GetTempFileNameA(temporary, "dfs", 0, unique)) return false;
    DeleteFileA(unique);
    if (!CreateDirectoryA(unique, nullptr)) return false;
    const std::string source = std::string(unique) + "\\override.str";
    const std::string output = std::string(unique) + "\\staged.str";
    const int wideLength = MultiByteToWideChar(
        CP_ACP, 0, source.data(), static_cast<int>(source.size()), nullptr, 0);
    if (wideLength <= 0) { RemoveDirectoryA(unique); return false; }
    std::wstring sourceWide(static_cast<std::size_t>(wideLength), L'\0');
    if (MultiByteToWideChar(CP_ACP, 0, source.data(), static_cast<int>(source.size()),
            sourceWide.data(), wideLength) != wideLength)
    { RemoveDirectoryA(unique); return false; }
    const auto extract = [&](dfl::external_string_table::BuiltinExtractor builtin = BuiltinStringFixture)
    {
        return dfl::external_string_table::ExtractOrFallback(
            sourceWide.c_str(), output.c_str(), "dnf.str", builtin);
    };
    bool ok = true;
    BuiltinStringCalls = 0;
    ok &= extract() == 7 && BuiltinStringCalls == 1;
    ok &= extract(nullptr) == -1 && BuiltinStringCalls == 1;
    const std::string valid = "723>\xCD\xE6\xBC\xD2\r\n";
    { std::ofstream file(source, std::ios::binary); file.write(valid.data(), valid.size()); }
    ok &= extract() == 1 && BuiltinStringCalls == 1;
    { std::ifstream file(output, std::ios::binary);
      ok &= std::string(std::istreambuf_iterator<char>(file), {}) == valid; }
    HANDLE locked = CreateFileA(source.c_str(), GENERIC_READ, 0, nullptr,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    ok &= locked != INVALID_HANDLE_VALUE && extract() == -1 && BuiltinStringCalls == 1;
    if (locked != INVALID_HANDLE_VALUE) CloseHandle(locked);
    { std::ofstream file(source, std::ios::binary | std::ios::trunc); }
    ok &= extract() == -1 && BuiltinStringCalls == 1;
    { std::ofstream file(source, std::ios::binary | std::ios::trunc); file << "\xEF\xBB\xBF" "723>bad"; }
    ok &= extract() == -1 && BuiltinStringCalls == 1;
    { std::ifstream file(output, std::ios::binary);
      ok &= std::string(std::istreambuf_iterator<char>(file), {}) == valid; }
    DeleteFileA(source.c_str()); DeleteFileA(output.c_str()); RemoveDirectoryA(unique);
    return ok;
}

bool TestRelativeOutputPathPolicy()
{
    char output[128]{};
    if (!dfl::output_paths::BuildRelativePath(
            "DNF.trc",
            output,
            sizeof(output))
        || std::strcmp(output, "Logs\\DNF.trc") != 0)
    {
        return false;
    }

    std::memset(output, 0, sizeof(output));
    if (!dfl::output_paths::BuildRelativePath(
            "Low\\DNF\\NeopleEngine.LOG",
            output,
            sizeof(output))
        || std::strcmp(output, "Logs\\NeopleEngine.LOG") != 0)
    {
        return false;
    }

    std::memset(output, 0, sizeof(output));
    if (!dfl::output_paths::BuildRelativePath(
            "SomeOtherClient.LOG",
            output,
            sizeof(output))
        || std::strcmp(output, "Logs\\SomeOtherClient.LOG") != 0)
    {
        return false;
    }

    std::memset(output, 0, sizeof(output));
    if (!dfl::output_paths::BuildRelativePath(
            "ScreenShot",
            output,
            sizeof(output))
        || std::strcmp(output, "ScreenShot\\") != 0)
    {
        return false;
    }

    std::memset(output, 0, sizeof(output));
    if (!dfl::output_paths::BuildRelativePath(
            "ScreenShot\\ScreenShot00004.JPG",
            output,
            sizeof(output))
        || std::strcmp(output, "ScreenShot\\ScreenShot00004.JPG") != 0)
    {
        return false;
    }

    return !dfl::output_paths::BuildRelativePath(
        "Interface\\premiumService.img",
        output,
        sizeof(output));
}

// ---------------------------------------------------------------------------
// Synthetic input core (src/auto_input.cpp).
//
// What this proves: the auto-input.json subset parser accepts exactly the
// spec 3.1 shape and nothing else, and the timeline state machine fires key
// edges, click primitives and text expansion at the pinned times. ArmPlan's
// explicit origin pins every timeline to synthetic ticks, so none of these
// tests touch the file system.
// ---------------------------------------------------------------------------

bool TestFocusRequestIsolation()
{
    using dfl::auto_input::IsFocusRequestForProcess;
    constexpr char EmbeddedNull[] = {'1', '2', '3', '\0', '4'};
    const char* invalid[] = {"", "0", "0123", "-123", "+123", "123x", "12 3",
        "4294967296", "99999999999999999999999999999999", " \r\n"};
    for (const auto* text : invalid)
    {
        if (IsFocusRequestForProcess(text, static_cast<unsigned>(std::strlen(text)), 123))
        {
            return false;
        }
    }
    return IsFocusRequestForProcess("123", 3, 123)
        && IsFocusRequestForProcess(" \t123\r\n", 7, 123)
        && IsFocusRequestForProcess("4294967295", 10, 0xFFFFFFFFUL)
        && !IsFocusRequestForProcess("123", 3, 456)
        && !IsFocusRequestForProcess("123", 3, 0)
        && !IsFocusRequestForProcess(nullptr, 3, 123)
        && !IsFocusRequestForProcess(EmbeddedNull, sizeof(EmbeddedNull), 123)
        && !IsFocusRequestForProcess("123", 33, 123);
}

bool TestParsePlanHappyPath()
{
    using namespace dfl::auto_input;
    Step steps[kMaxSteps];
    int count = 0;
    int version = 0;
    const char* json =
        "{\"version\":1,\"steps\":[{\"t\":100,\"op\":\"click\",\"x\":320,\"y\":240},"
        "{\"t\":300,\"op\":\"key\",\"vk\":13,\"dur\":60},"
        "{\"t\":500,\"op\":\"text\",\"chars\":\"test\"}]}";
    if (!ParsePlan(json, steps, kMaxSteps, &count, &version))
    {
        return false;
    }
    return version == 1
        && count == 3
        && steps[0].t == 100
        && steps[0].op == kClick
        && steps[0].x == 320
        && steps[0].y == 240
        && steps[0].dur == 80  // omitted in the JSON: the struct default survives
        && steps[1].op == kKey
        && steps[1].vk == 13
        && steps[1].dur == 60
        && steps[2].op == kText
        && steps[2].t == 500
        && steps[2].text[0] == 't'
        && steps[2].text[1] == 'e'
        && steps[2].text[2] == 's'
        && steps[2].text[3] == 't'
        && steps[2].text[4] == '\0';
}

bool TestParsePlanRejectsGarbage()
{
    using namespace dfl::auto_input;
    Step steps[8];
    int count = 0;
    int version = 0;
    const char* bad[] = {
        "",
        "{",
        "{\"version\":2,\"steps\":[]}",                        // wrong plan version
        "{\"version\":1}",                                     // steps missing
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"fly\"}]}",
        "{\"version\":1,\"steps\":[]} tail",                   // trailing garbage
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"key\",\"vk\":13},\"x\"]}",
        "{\"version\":1,\"steps\":[{\"t\":-1,\"op\":\"key\"}]}",
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"key\",\"dur\":-5}]}",
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"text\",\"chars\":\"a\\tb\"}]}",
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"key\",\"vk\":13,\"vk\":14}]}",
        "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"key\",\"who\":3}]}",
        "{\"version\":1,\"steps\":["
        "{\"t\":1,\"op\":\"move\"},{\"t\":2,\"op\":\"move\"},{\"t\":3,\"op\":\"move\"},"
        "{\"t\":4,\"op\":\"move\"},{\"t\":5,\"op\":\"move\"},{\"t\":6,\"op\":\"move\"},"
        "{\"t\":7,\"op\":\"move\"},{\"t\":8,\"op\":\"move\"},{\"t\":9,\"op\":\"move\"}]}"};
    for (const char* sample : bad)
    {
        if (ParsePlan(sample, steps, 8, &count, &version))
        {
            return false;
        }
        if (count != 0)
        {
            return false;  // a rejected plan must not touch the caller's buffer
        }
    }
    return true;
}

bool TestTextReturnCharacters()
{
    using namespace dfl::auto_input;
    Step step{};int count=0,version=0;
    if(!ParsePlan("{\"version\":1,\"steps\":[{\"t\":100,\"op\":\"text\",\"chars\":\"\\r\\n\"}]}",&step,1,&count,&version)
        || count!=1 || step.text[0]!='\r' || step.text[1]!='\n' || step.text[2]!=0) return false;
    if(!ArmPlan(&step,count,version,0))return false;
    Advance(100);if(!(QueryKey(13,0)&0x8000))return false;
    Advance(140);if(!(QueryKey(13,0)&0x8000))return false;
    Advance(180);if(QueryKey(13,0)!=0 || !IsFinished())return false;
    const char* bad[]={
        "{\"version\":1,\"steps\":[{\"t\":0,\"op\":\"text\",\"chars\":\"\r\"}]}",
        "{\"version\":1,\"steps\":[{\"t\":0,\"op\":\"text\",\"chars\":\"\\u000d\"}]}",
        "{\"version\":1,\"steps\":[{\"t\":0,\"op\":\"text\",\"chars\":\"\\0\"}]}"};
    for(const auto* json:bad)if(ParsePlan(json,&step,1,&count,&version))return false;
    return true;
}

bool TestAdvanceKeyEdgeAndFinish()
{
    using namespace dfl::auto_input;
    ClearInjectionLog();
    Step steps[kMaxSteps];
    int count = 0;
    int version = 0;

    // The brief's key-edge assertions and the injection-log assertions on one
    // timeline: a key press at t=100 held for 60 ms, then a click at t=200.
    const char* json =
        "{\"version\":1,\"steps\":["
        "{\"t\":100,\"op\":\"key\",\"vk\":13,\"dur\":60},"
        "{\"t\":200,\"op\":\"click\",\"x\":320,\"y\":240,\"dur\":80}]}";
    if (!ParsePlan(json, steps, kMaxSteps, &count, &version) || count != 2)
    {
        return false;
    }
    if (!ArmPlan(steps, count, version, 0) || IsFinished())
    {
        return false;
    }

    // t<100: nothing fires and the key forwards untouched.
    if (Advance(50) != 0 || QueryKey(13, 0x00AB) != 0x00AB)
    {
        return false;
    }
    // t=100: the down edge; the synth table reports the press.
    if (Advance(100) != 1 || QueryKey(13, 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    // 100<t<160: still held, no further edges.
    if (Advance(130) != 0 || QueryKey(13, 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    // t=160: the up edge; the key forwards again.
    if (Advance(160) != 1 || QueryKey(13, 0x00AB) != 0x00AB)
    {
        return false;
    }
    // t=200: the click's move and down land in the injection log; the up is
    // due only at t+dur. Mouse primitives are not keyboard edges.
    if (Advance(200) != 0)
    {
        return false;
    }
    InjectionRecord records[64] = {};
    if (GetInjectionLog(records, 64) != 2)
    {
        return false;
    }
    if (records[0].op != kMouseMove || records[0].x != 320 || records[0].y != 240
        || records[1].op != kMouseDown || records[1].x != 320 || records[1].y != 240)
    {
        return false;
    }
    // t=280: the click's up completes the plan -> Finished.
    if (Advance(280) != 0)
    {
        return false;
    }
    if (GetInjectionLog(records, 64) != 3)
    {
        return false;
    }
    if (records[2].op != kMouseUp || records[2].x != 320 || records[2].y != 240)
    {
        return false;
    }
    if (!IsFinished() || QueryKey(13, 0x00AB) != 0x00AB || Advance(400) != 0)
    {
        return false;
    }

    // The brief's key-only timeline: t<100 no key, 100<=t<160 down, t>=160
    // released and Finished.
    const char* keyOnly =
        "{\"version\":1,\"steps\":[{\"t\":100,\"op\":\"key\",\"vk\":13,\"dur\":60}]}";
    if (!ParsePlan(keyOnly, steps, kMaxSteps, &count, &version) || count != 1)
    {
        return false;
    }
    if (!ArmPlan(steps, count, version, 0))
    {
        return false;
    }
    if (Advance(99) != 0 || Advance(100) != 1 || Advance(159) != 0
        || QueryKey(13, 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    if (Advance(160) != 1 || !IsFinished() || QueryKey(13, 0x00AB) != 0x00AB)
    {
        return false;
    }

    // Text expands serially: downs 40 ms apart, each char held until the
    // next char's down ('a' -> VK 'A', 'b' -> VK 'B').
    const char* textPlan =
        "{\"version\":1,\"steps\":[{\"t\":100,\"op\":\"text\",\"chars\":\"ab\"}]}";
    if (!ParsePlan(textPlan, steps, kMaxSteps, &count, &version) || count != 1)
    {
        return false;
    }
    if (!ArmPlan(steps, count, version, 0))
    {
        return false;
    }
    if (Advance(99) != 0 || Advance(100) != 1
        || QueryKey('A', 0x00AB) != static_cast<short>(0x8000)
        || QueryKey('B', 0x00AB) != 0x00AB)
    {
        return false;
    }
    // t=140: 'A' releases and 'B' presses inside the same pump (ups first).
    if (Advance(140) != 2
        || QueryKey('A', 0x00AB) != 0x00AB
        || QueryKey('B', 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    if (Advance(180) != 1 || !IsFinished())
    {
        return false;
    }

    // Catch-up regression: a pump gap that skips whole 40 ms slots (loading
    // screens stall the frame hook) must never release a char before its own
    // down fired, and must never end with a key stuck down.
    const char* catchUpPlan =
        "{\"version\":1,\"steps\":[{\"t\":100,\"op\":\"text\",\"chars\":\"ab\"}]}";
    if (!ParsePlan(catchUpPlan, steps, kMaxSteps, &count, &version) || count != 1)
    {
        return false;
    }
    if (!ArmPlan(steps, count, version, 0))
    {
        return false;
    }
    if (Advance(139) != 1 || QueryKey('A', 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    // One pump 80 ms after the previous one skips 'B's down slot entirely.
    if (Advance(220) != 2
        || QueryKey('A', 0x00AB) != 0x00AB
        || QueryKey('B', 0x00AB) != static_cast<short>(0x8000))
    {
        return false;
    }
    // The next pump releases what was already pressed ('B') and finishes
    // with an all-clear table.
    if (Advance(260) != 1
        || QueryKey('A', 0x00AB) != 0x00AB
        || QueryKey('B', 0x00AB) != 0x00AB
        || !IsFinished())
    {
        return false;
    }
    return true;
}

bool TestQueryKeyForwardsOriginal()
{
    // With an empty synth table every query must forward the original value
    // untouched: the hook may never swallow the real keyboard.
    using namespace dfl::auto_input;
    return QueryKey(0x41, 0x0001) == 0x0001
        && QueryKey(0x41, static_cast<short>(0x8000)) == static_cast<short>(0x8000);
}

bool TestRightClickPrimitive()
{
    // DNF forms a party from a right click on the other player's character, so
    // the timeline needs an rclick op whose edges reach the game as right-button
    // mouse messages (WM_RBUTTONDOWN/UP), not left ones. Pins both halves:
    // the parser word and the primitive the up/down passes emit.
    using namespace dfl::auto_input;

    const char* plan =
        "{\"version\":1,\"steps\":[{\"t\":0,\"op\":\"rclick\",\"x\":40,\"y\":50,\"dur\":40}]}";
    Step steps[4] = {};
    int count = 0;
    int version = 0;
    if (!ParsePlan(plan, steps, 4, &count, &version) || count != 1)
    {
        return false;
    }
    if (steps[0].op != kRClick || steps[0].x != 40 || steps[0].y != 50)
    {
        return false;
    }

    ClearInjectionLog();
    if (!ArmPlan(steps, 1, version, 0))
    {
        return false;
    }
    Advance(10);   // the down edge is due at t=0
    InjectionRecord records[8] = {};
    const int downCount = GetInjectionLog(records, 8);
    bool sawRightDown = false;
    for (int i = 0; i < downCount; ++i)
    {
        if (records[i].op == kMouseRightDown && records[i].x == 40 && records[i].y == 50)
        {
            sawRightDown = true;
        }
        if (records[i].op == kMouseDown)
        {
            return false;  // a right click must never emit a left button edge
        }
    }

    ClearInjectionLog();
    Advance(400);  // past t+dur: the up edge
    const int upCount = GetInjectionLog(records, 8);
    bool sawRightUp = false;
    for (int i = 0; i < upCount; ++i)
    {
        if (records[i].op == kMouseRightUp)
        {
            sawRightUp = true;
        }
        if (records[i].op == kMouseUp)
        {
            return false;
        }
    }
    return sawRightDown && sawRightUp;
}

bool TestKeyboardDikTranslation()
{
    // DirectInput hands the keyboard state back as 256 bytes indexed by DIK
    // code: the Set-1 scan code, with bit 7 set for the E0-prefixed keys.
    // MapVirtualKeyA reports only the raw scan code on this host (measured
    // 2026-10-23: MAPVK_VK_TO_VSC_EX answers 0x004B for VK_LEFT), so the
    // extended set is enumerated in auto_input.cpp. A regression here writes
    // the arrows to 0x48/0x4B/0x4D/0x50 -- slots the client never reads --
    // which is exactly the bug that kept the character standing still.
    using namespace dfl::auto_input;
    struct Case
    {
        unsigned vk;
        unsigned char dik;
    };
    const Case cases[] = {
        {0x25, 0xCB},  // VK_LEFT     -> DIK_LEFT (extended)
        {0x26, 0xC8},  // VK_UP
        {0x27, 0xCD},  // VK_RIGHT
        {0x28, 0xD0},  // VK_DOWN
        {0x2D, 0xD2},  // VK_INSERT
        {0x2E, 0xD3},  // VK_DELETE
        {0x24, 0xC7},  // VK_HOME
        {0x23, 0xCF},  // VK_END
        {0x21, 0xC9},  // VK_PRIOR (PageUp)
        {0x22, 0xD1},  // VK_NEXT  (PageDown)
        {0xA3, 0x9D},  // VK_RCONTROL
        {0xA5, 0xB8},  // VK_RMENU
        {0x41, 0x1E},  // 'A' -- plain scan code, bit 7 clear
        {0x20, 0x39},  // VK_SPACE
        {0x1B, 0x01},  // VK_ESCAPE
        {0x0D, 0x1C},  // VK_RETURN
    };
    for (const Case& entry : cases)
    {
        if (MapVkToDik(entry.vk) != entry.dik)
        {
            return false;
        }
    }
    return true;
}

bool TestSyntheticKeyboardStateMerge()
{
    // The synthetic device's GetDeviceState (auto_input.cpp) is what the game
    // reads every frame; its wiring -- plan edge -> synth table -> DIK slot in
    // the 256-byte buffer -- is what actually moved the character on
    // 2026-10-23, and it had no automated coverage until here.
    //
    // VK_F13 is used as the probe key because no physical keyboard held during
    // a test run carries it: the merge also ORs in the real keyboard state, so
    // a common key would make the "released" assertion depend on the machine.
    using namespace dfl::auto_input;

    constexpr unsigned kProbeVk = 0x7C;  // VK_F13
    Step steps[1] = {};
    steps[0].t = 0;
    steps[0].op = kKey;
    steps[0].vk = kProbeVk;
    steps[0].dur = 100;
    if (!ArmPlan(steps, 1, 1, 0))
    {
        return false;
    }

    const unsigned char dik = MapVkToDik(kProbeVk);
    unsigned char keys[256] = {};

    // The down edge is due at t=0: the buffer must carry that DIK slot.
    Advance(10);
    BuildKeyboardState(keys);
    if ((keys[dik] & 0x80) == 0)
    {
        return false;
    }

    // Past t+dur the up edge fires and the buffer must be clean again.
    Advance(200);
    for (int i = 0; i < 256; ++i)
    {
        keys[i] = 0;
    }
    BuildKeyboardState(keys);
    return (keys[dik] & 0x80) == 0;
}
}

bool TestGroundItemRemovalContext();
bool TestMotionHorizon();
bool TestNativeLoopReturnStack();
bool TestAutoInputDrag();

int main(int argc, char** argv)
{
    const int probe = ClientServiceUrlProbe(argc, argv);
    if (probe >= 0) return probe;
    if (!TestClientServiceUrls()) return Fail("client service URL configuration failed");
    if(argc==2&&std::strcmp(argv[1],"--auto-input-text-only")==0)return TestTextReturnCharacters()?0:Fail("text Return escapes could not reach the native input timeline");
    if(argc==2&&std::strcmp(argv[1],"--native-loop-only")==0)return TestNativeLoopReturnStack()?0:1;
    if(!TestNativeLoopReturnStack())return Fail("native loop return stack failed");
    if(argc==2&&std::strcmp(argv[1],"--motion-horizon-only")==0)return TestMotionHorizon()?0:1;
    if(!TestMotionHorizon())return Fail("motion horizon repair failed");
    if (argc == 2 && std::strcmp(argv[1], "--ground-item-removal-only") == 0)
        return TestGroundItemRemovalNotification() && TestGroundItemRemovalContext() ? 0 : 1;
    if (argc == 2 && std::strcmp(argv[1], "--auto-input-drag-only") == 0) return TestAutoInputDrag() ? 0 : 1;
    if (!TestAutoInputDrag()) return Fail("auto-input drag failed");
    if (argc == 2 && std::strcmp(argv[1], "--dungeon-tooltip-only") == 0) return TestDungeonTooltipCleanup()?0:1;
    if (!TestDungeonTooltipCleanup()) return Fail("dungeon tooltip cleanup failed");
    if (argc == 2 && std::strcmp(argv[1], "--countdown-coin-only") == 0)
    {
        if (!TestCountdownCoin()) return 1;
        std::puts("countdown coin tests passed");
        return 0;
    }
    if (argc == 2 && std::strcmp(argv[1], "--relay-send-queue-only") == 0)
    {
        if (!TestRelaySendQueue()) return 1;
        std::puts("relay send queue tests passed");
        return 0;
    }
    if (argc == 2 && std::strcmp(argv[1], "--creature-gift-row-only") == 0)
    {
        if (!TestCreatureGiftRow()) return 1;
        std::puts("creature gift row tests passed");
        return 0;
    }
    if (argc == 2 && std::strcmp(argv[1], "--channel-switch-only") == 0)
    {
        if (!TestChannelSwitch()) return 1;
        std::puts("channel switch transition tests passed");
        return 0;
    }
    // First: it hooks this executable's own import table and puts it back, so
    // later tests get the imports they expect.
    if (!TestNetDump())
    {
        return Fail("net dump hook failed");
    }
    if (!TestNetDumpFromConfiguration())
    {
        return Fail("net dump Config.ini wiring failed");
    }

    if (!TestMemoryPatchHelpers())
    {
        return Fail("memory patch helpers failed");
    }
    if (!TestStageFollowRepair())
    {
        return Fail("removed actor stage follow repair failed");
    }
    if (!TestSummonOwnerLossOrdering())
    {
        return Fail("summon owner-loss ordering failed");
    }
    if (!TestPartyCombatDetailInitialization())
    {
        return Fail("party combat detail initialization failed");
    }
    if (!TestMageSkillContactPrerequisite())
    {
        return Fail("Mage contact skill prerequisite failed");
    }
    if (!TestBossRoomLateSpawnDeath())
    {
        return Fail("boss room late-spawn death failed");
    }
    if (!TestPriestHealTargetReplay())
    {
        return Fail("Priest healing target replay failed");
    }
    if (!TestGroundItemRemovalContext()) return Fail("ground receiver context failed");
    if (!TestGroundItemRemovalNotification())
    {
        return Fail("ground item removal notification failed");
    }
    if (!TestRelaySendQueue()) return Fail("relay send queue serialization failed");
    if (!TestCreatureGiftRow()) return Fail("creature gift row failed");
    if (!TestCountdownCoin()) return Fail("countdown coin presentation failed");
    if (!TestChannelSwitch()) return Fail("channel switch transition failed");
    if (!TestChannelReentryMtu())
    {
        return Fail("channel reentry MTU reuse failed");
    }
    if (!TestPartyCardDisplay())
    {
        return Fail("party free-card display failed");
    }
    if (!TestCustomStackableUseModePolicy())
    {
        return Fail("[etc] stackable mode-1 list failed");
    }
    if (!TestCustomPopupPatchConfiguration())
    {
        return Fail("custom popup announcement-title patch is disabled");
    }

    if (!TestMailboxArchiveRefreshPatchConfiguration())
    {
        return Fail("mailbox archive refresh patch is disabled");
    }
    if (!TestCeraPurchaseGatePolicy())
    {
        return Fail("Cera purchase-gate policy is invalid");
    }
    if (!TestAutoInputConfigDefaults())
    {
        return Fail("auto-input config default is not disabled");
    }
    if (!TestExternalStringTableConfiguration())
    {
        return Fail("external CP936 dnf.str configuration failed");
    }
    if (!TestExternalStringTableFallback())
    {
        return Fail("missing external string table fallback or override protection failed");
    }
    if (!TestRelativeOutputPathPolicy())
    {
        return Fail("relative Logs/ScreenShot output path policy failed");
    }

    if (!TestFocusRequestIsolation())
    {
        return Fail("auto-input focus request accepted invalid or foreign process identity");
    }
    if (!TestParsePlanHappyPath())
    {
        return Fail("auto-input JSON subset happy path failed");
    }
    if (!TestTextReturnCharacters())return Fail("text Return escapes could not reach the native input timeline");
    if (!TestParsePlanRejectsGarbage())
    {
        return Fail("auto-input JSON subset parser accepted invalid input");
    }
    if (!TestAdvanceKeyEdgeAndFinish())
    {
        return Fail("auto-input timeline advance or key-edge query failed");
    }
    // Plan Task 3 step 1: this guard is expected green already (Task 2
    // forwards untouched); its value is catching a regression once the real
    // hook lands.
    if (!TestQueryKeyForwardsOriginal())
    {
        return Fail("auto-input QueryKey did not forward the original value");
    }
    if (!TestRightClickPrimitive())
    {
        return Fail("auto-input right-click primitive did not reach the injection log");
    }
    if (!TestKeyboardDikTranslation())
    {
        return Fail("keyboard DIK translation lost the extended-key bit");
    }
    if (!TestSyntheticKeyboardStateMerge())
    {
        return Fail("synthetic keyboard state did not follow the plan edges");
    }

    IjlApi api{};
    if (!LoadApi(&api))
    {
        return Fail("could not load the six IJL exports");
    }

    JPEG_CORE_PROPERTIES properties{};
    if (api.Init(&properties) != IJL_OK)
    {
        return Fail("ijlInit failed");
    }
    if (properties.DIBChannels != 3
        || properties.DIBColor != IJL_BGR
        || properties.JPGChannels != 3
        || properties.JPGColor != IJL_YCBCR
        || properties.jquality != 75)
    {
        return Fail("ijlInit defaults do not match IJL 1.5");
    }

    // A top-down RGB image with four colored quadrants. The DNF_v1.0 screenshot
    // path supplies this row order with a positive DIBHeight.
    constexpr int width = 16;
    constexpr int height = 16;
    std::vector<unsigned char> rgb(width * height * 3);
    for (int y = 0; y < height; ++y)
    {
        for (int x = 0; x < width; ++x)
        {
            unsigned char* pixel =
                rgb.data() + static_cast<std::size_t>(y * width + x) * 3;
            pixel[0] = x < width / 2 ? 230 : 30;
            pixel[1] = y < height / 2 ? 220 : 20;
            pixel[2] = x < width / 2 ? 20 : 225;
        }
    }

    std::vector<unsigned char> jpeg(width * height * 6);
    properties.DIBBytes = rgb.data();
    properties.DIBWidth = width;
    properties.DIBHeight = height;
    properties.DIBPadBytes = 0;
    properties.DIBChannels = 3;
    properties.DIBColor = IJL_RGB;
    properties.JPGBytes = jpeg.data();
    properties.JPGSizeBytes = static_cast<int>(jpeg.size());
    properties.JPGWidth = width;
    properties.JPGHeight = height;
    properties.JPGChannels = 3;
    properties.JPGColor = IJL_YCBCR;
    properties.JPGSubsampling = IJL_411;
    properties.jquality = 95;

    if (api.Write(&properties, IJL_JBUFF_WRITEWHOLEIMAGE) != IJL_OK)
    {
        return Fail("buffer JPEG encoding failed");
    }
    const int jpegSize = properties.JPGSizeBytes;
    if (jpegSize < 4
        || jpeg[0] != 0xFF
        || jpeg[1] != 0xD8
        || jpeg[jpegSize - 2] != 0xFF
        || jpeg[jpegSize - 1] != 0xD9)
    {
        return Fail("encoded output is not a complete JPEG");
    }

    JPEG_CORE_PROPERTIES decoded{};
    if (api.Init(&decoded) != IJL_OK)
    {
        return Fail("decode ijlInit failed");
    }
    decoded.JPGBytes = jpeg.data();
    decoded.JPGSizeBytes = jpegSize;
    if (api.Read(&decoded, IJL_JBUFF_READPARAMS) != IJL_OK
        || decoded.JPGWidth != width
        || decoded.JPGHeight != height
        || decoded.JPGChannels != 3)
    {
        return Fail("buffer JPEG parameter read failed");
    }

    constexpr int padding = 4;
    const int decodedStride = width * 3 + padding;
    std::vector<unsigned char> bgr(
        static_cast<std::size_t>(decodedStride * height),
        0xCD);
    decoded.DIBBytes = bgr.data();
    decoded.DIBWidth = width;
    decoded.DIBHeight = height;
    decoded.DIBPadBytes = padding;
    decoded.DIBChannels = 3;
    decoded.DIBColor = IJL_BGR;
    if (api.Read(&decoded, IJL_JBUFF_READWHOLEIMAGE) != IJL_OK)
    {
        return Fail("buffer JPEG decode failed");
    }

    // Positive decode height is bottom-up, so the first row is the image bottom.
    const unsigned char* bottomLeft = bgr.data();
    if (!IsClose(bottomLeft[0], 20, 45)
        || !IsClose(bottomLeft[1], 20, 45)
        || !IsClose(bottomLeft[2], 230, 45))
    {
        return Fail("positive-height encoding or bottom-up decoding is wrong");
    }
    if (!std::all_of(
            bottomLeft + width * 3,
            bottomLeft + decodedStride,
            [](unsigned char value) { return value == 0xCD; }))
    {
        return Fail("DIB row padding was modified");
    }

    std::array<char, MAX_PATH> temporaryDirectory{};
    std::array<char, MAX_PATH> temporaryFile{};
    if (GetTempPathA(
            static_cast<DWORD>(temporaryDirectory.size()),
            temporaryDirectory.data()) == 0
        || GetTempFileNameA(
            temporaryDirectory.data(),
            "ijl",
            0,
            temporaryFile.data()) == 0)
    {
        return Fail("could not create a temporary JPEG path");
    }

    properties.JPGFile = temporaryFile.data();
    if (api.Write(&properties, IJL_JFILE_WRITEWHOLEIMAGE) != IJL_OK)
    {
        DeleteFileA(temporaryFile.data());
        return Fail("file JPEG encoding failed");
    }

    JPEG_CORE_PROPERTIES fileDecoded{};
    api.Init(&fileDecoded);
    fileDecoded.JPGFile = temporaryFile.data();
    if (api.Read(&fileDecoded, IJL_JFILE_READPARAMS) != IJL_OK
        || fileDecoded.JPGWidth != width
        || fileDecoded.JPGHeight != height)
    {
        DeleteFileA(temporaryFile.data());
        return Fail("file JPEG parameter read failed");
    }

    std::vector<unsigned char> filePixels(width * height * 3);
    fileDecoded.DIBBytes = filePixels.data();
    fileDecoded.DIBWidth = width;
    fileDecoded.DIBHeight = -height;
    fileDecoded.DIBChannels = 3;
    fileDecoded.DIBColor = IJL_BGR;
    if (api.Read(&fileDecoded, IJL_JFILE_READWHOLEIMAGE) != IJL_OK)
    {
        DeleteFileA(temporaryFile.data());
        return Fail("file JPEG decode failed");
    }
    DeleteFileA(temporaryFile.data());

    const IJLibVersion* version = api.GetLibVersion();
    if (version == nullptr
        || version->major != 1
        || version->minor != 5
        || version->build != 4
        || version->BuildDate == nullptr
        || std::strncmp(version->BuildDate, "source:", 7) != 0)
    {
        return Fail("version export is invalid");
    }
    if (std::strcmp(api.ErrorStr(IJL_BUFFER_TOO_SMALL),
            "Output buffer too small") != 0)
    {
        return Fail("error string export is invalid");
    }
    if (api.Free(&fileDecoded) != IJL_OK
        || api.Free(&decoded) != IJL_OK
        || api.Free(&properties) != IJL_OK)
    {
        return Fail("ijlFree failed");
    }

    FreeLibrary(api.Module);
    std::puts("ijl15 compatibility tests passed");
    return 0;
}
