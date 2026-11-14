#include <winsock2.h>
#include <cstring>
#include "ChapterTwoParty.h"
#include "ChapterTwoRemoteEndpoint.h"

hostent* dfl::chapter_two::ResolveLocalPartyRelayHost(const char* name) noexcept
{
    // The peer constructor resolves the advertised relay even if it later connects directly.
    struct RelayHost
    {
        char name[16];
        char address[4];
        char* aliases[1]{nullptr};
        char* addresses[2]{address, nullptr};
        hostent entry{name, aliases, AF_INET, 4, addresses};
    };
    static RelayHost packaged{"127.0.0.2", {127, 0, 0, 2}};
    static RelayHost isolated{"127.0.0.3", {127, 0, 0, 3}};

    if (!name) return nullptr;
    if (std::strcmp(name, packaged.name) == 0) return &packaged.entry;
    if (std::strcmp(name, isolated.name) == 0) return &isolated.entry;
    thread_local RelayHost remote{};
    in_addr address{};
    if (ReadRemoteIpv4(remote.name, address) && std::strcmp(name, remote.name) == 0)
    {
        std::memcpy(remote.address, &address.s_addr, sizeof(remote.address));
        return &remote.entry;
    }
    return nullptr;
}
