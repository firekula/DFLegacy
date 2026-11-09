#include <winsock2.h>
#include <cstdio>
#include <cstring>
#include <initializer_list>
#include "ChapterTwoParty.h"

int main()
{
    using dfl::chapter_two::ResolveLocalPartyRelayHost;
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
    if (passed) std::puts("Chapter Two party relay lookup checks passed");
    return passed ? 0 : 1;
}
