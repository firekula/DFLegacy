#include "ChapterTwoRelaySockets.h"
#include <cstdio>
#include <initializer_list>

int main()
{
    using namespace dfl::chapter_two;
    bool passed = true;
    const auto check = [&](bool value, const char* description) {
        if (!value) std::fprintf(stderr, "FAIL %s\n", description);
        passed &= value;
    };
    SetEnvironmentVariableA("DFL_S1C2_LOCAL_RELAY_IPV4", "127.73.10.11");
    char host[16]{}; in_addr address{};
    check(ReadLocalRelayIpv4(host, address), "session-local relay is accepted");
    for (const auto port : {42312, 47005, 47012, 47013}) {
        sockaddr_in endpoint{}; endpoint.sin_family = AF_INET; endpoint.sin_port = htons(static_cast<u_short>(port));
        endpoint.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        check(MapLocalRelayDatagram(endpoint, false) && ntohl(endpoint.sin_addr.s_addr) == 0x7f490a0b, "known destination routes to this session");
        check(MapLocalRelayDatagram(endpoint, true) && ntohl(endpoint.sin_addr.s_addr) == INADDR_LOOPBACK, "reply exposes stable game address");
    }
    sockaddr_in unrelated{}; unrelated.sin_family = AF_INET; unrelated.sin_port = htons(9999); unrelated.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    check(!MapLocalRelayDatagram(unrelated, false), "unrelated local port stays unchanged");
    unrelated.sin_port = htons(47012); unrelated.sin_addr.s_addr = htonl(0xc0000214);
    check(!MapLocalRelayDatagram(unrelated, false), "unrelated remote host stays unchanged");
    for (const auto* invalid : {"127.0.0.1", "127.73.0.0", "192.0.2.20", "bad", "127.73.10.11.extra"}) {
        SetEnvironmentVariableA("DFL_S1C2_LOCAL_RELAY_IPV4", invalid);
        check(!ReadLocalRelayIpv4(host, address), "invalid session address is rejected");
    }
    SetEnvironmentVariableA("DFL_S1C2_LOCAL_RELAY_IPV4", nullptr);
    check(!ReadLocalRelayIpv4(host, address), "legacy mode remains unmodified");
    if (passed) std::puts("PASS native local relay endpoint mapping");
    return passed ? 0 : 1;
}
