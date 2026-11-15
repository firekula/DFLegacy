#include <winsock2.h>
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <initializer_list>
#include "ChapterTwoParty.h"

int main()
{
    using dfl::chapter_two::ResolveLocalPartyRelayHost;
    using dfl::chapter_two::ResolveGamePeerHost;
    bool passed = true;
    const auto check = [&](bool condition, const char* message)
    {
        if (!condition) std::fprintf(stderr, "FAIL %s\n", message);
        passed &= condition;
    };

    const auto* packaged = ResolveLocalPartyRelayHost("127.0.0.2");
    const auto* isolated = ResolveLocalPartyRelayHost("127.0.0.3");
    const auto checkAddress = [&](const hostent* entry, const char* name, char lastOctet, const char* message)
    {
        const char address[]{127, 0, 0, lastOctet};
        check(entry && std::strcmp(entry->h_name, name) == 0 && entry->h_addrtype == AF_INET &&
              entry->h_length == static_cast<short>(sizeof(address)) && entry->h_addr_list[0] && !entry->h_addr_list[1] &&
              std::memcmp(entry->h_addr_list[0], address, sizeof(address)) == 0, message);
    };
    checkAddress(packaged, "127.0.0.2", 2, "packaged relay keeps its original address");
    checkAddress(isolated, "127.0.0.3", 3, "four-client isolated relay is available to the native peer resolver");
    for (const auto* denied : {"127.0.0.30", "127.0.0.03", "127.0.0.4", "192.0.2.1", "relay.invalid", ""})
        check(!ResolveLocalPartyRelayHost(denied), "other names stay outside the relay allowlist");
    check(!ResolveLocalPartyRelayHost(nullptr), "a null name is rejected");
    SetEnvironmentVariableA("DFL_S1C2_REMOTE_IPV4", "192.0.2.20");
    const auto* remote = ResolveLocalPartyRelayHost("192.0.2.20");
    const unsigned char address[]{192, 0, 2, 20};
    check(remote && remote->h_length == sizeof(address) && remote->h_addr_list[0] &&
        std::memcmp(remote->h_addr_list[0], address, sizeof(address)) == 0,
        "an explicit remote address resolves to the exact requested server");
    check(!ResolveLocalPartyRelayHost("192.0.2.21"), "adjacent addresses remain denied");
    SetEnvironmentVariableA("DFL_S1C2_REMOTE_IPV4", nullptr);
    checkAddress(ResolveGamePeerHost("127.0.0.1", 0xA4F493), "127.0.0.1", 1,
        "the original local peer lookup preserves its literal");
    const auto* peer = ResolveGamePeerHost("192.0.2.21", 0xA4F4E3);
    const unsigned char peerAddress[]{192, 0, 2, 21};
    check(peer && peer->h_addr_list[0] && std::memcmp(peer->h_addr_list[0], peerAddress, sizeof(peerAddress)) == 0,
        "the original public peer lookup accepts a different address from the game server");
    check(!ResolveGamePeerHost("192.0.2.21", 0xA4F533), "the relay lookup keeps its own allowlist");
    for (const auto* denied : {"localhost", "192.0.2", "192.000.2.21", "224.0.0.1", "0.0.0.0", "255.255.255.255"})
        check(!ResolveGamePeerHost(denied, 0xA4F4E3), "peer lookup accepts only usable IPv4 literals");
    if (passed) std::puts("Chapter Two party relay lookup checks passed");
    return passed ? 0 : 1;
}
