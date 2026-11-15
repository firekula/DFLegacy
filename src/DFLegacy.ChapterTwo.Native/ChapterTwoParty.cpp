#include <winsock2.h>
#include <cstring>
#include "ChapterTwoParty.h"
#include "ChapterTwoRemoteEndpoint.h"

namespace
{
    struct Ipv4Host
    {
        char name[16];
        char address[4];
        char* aliases[1]{nullptr};
        char* addresses[2]{address, nullptr};
        hostent entry{name, aliases, AF_INET, 4, addresses};
    };
}

hostent* dfl::chapter_two::ResolveLocalPartyRelayHost(const char* name) noexcept
{
    // The peer constructor resolves the advertised relay even if it later connects directly.
    static Ipv4Host packaged{"127.0.0.2", {127, 0, 0, 2}};
    static Ipv4Host isolated{"127.0.0.3", {127, 0, 0, 3}};

    if (!name) return nullptr;
    if (std::strcmp(name, packaged.name) == 0) return &packaged.entry;
    if (std::strcmp(name, isolated.name) == 0) return &isolated.entry;
    thread_local Ipv4Host remote{};
    in_addr address{};
    if (ReadRemoteIpv4(remote.name, address) && std::strcmp(name, remote.name) == 0)
    {
        std::memcpy(remote.address, &address.s_addr, sizeof(remote.address));
        return &remote.entry;
    }
    return nullptr;
}

hostent* dfl::chapter_two::ResolveGamePeerHost(const char* name, std::uintptr_t caller) noexcept
{
    // Return addresses of the original NOTI 11 local and public peer lookups.
    // The third call in the constructor resolves the relay with its own allowlist.
    if (caller != 0xA4F493 && caller != 0xA4F4E3) return nullptr;

    in_addr address{};
    if (!name || InetPtonA(AF_INET, name, &address) != 1) return nullptr;
    const auto firstOctet = ntohl(address.s_addr) >> 24;
    if (firstOctet == 0 || firstOctet >= 224) return nullptr;

    thread_local Ipv4Host peer{};
    strcpy_s(peer.name, name);
    std::memcpy(peer.address, &address.s_addr, sizeof(peer.address));
    return &peer.entry;
}
