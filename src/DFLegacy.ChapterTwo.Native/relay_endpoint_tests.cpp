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
    SetEnvironmentVariableA("DFLEGACY_CH2_RELAY_UDP", "42312:55001,47005:55002,47012:55003,47013:55004");
    char host[16]{}; in_addr address{};
    check(ReadLocalRelayIpv4(host, address), "session-local relay is accepted");
    unsigned short localPort = 55000;
    for (const auto port : {42312, 47005, 47012, 47013}) {
        sockaddr_in endpoint{}; endpoint.sin_family = AF_INET; endpoint.sin_port = htons(static_cast<u_short>(port));
        endpoint.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        ++localPort;
        check(MapLocalRelayDatagram(endpoint, false) && ntohl(endpoint.sin_addr.s_addr) == 0x7f490a0b
            && ntohs(endpoint.sin_port) == localPort, "known destination routes to the assigned port");
        check(MapLocalRelayDatagram(endpoint, true) && ntohl(endpoint.sin_addr.s_addr) == INADDR_LOOPBACK
            && ntohs(endpoint.sin_port) == port, "reply exposes the original game address and port");
    }
    const auto emptyMap = RelayPortMap::Read("DFLEGACY_TEST_UNSET_PORT_MAP");
    check(emptyMap.ToLocal(42312) == 42312 && emptyMap.ToService(42312) == 42312,
        "a legacy session keeps its original port");
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
    for (const auto port : {47001, 7100, 7101, 7102, 7103, 7104, 7105}) {
        check(IsChannelSwitchPort(2, static_cast<unsigned short>(port)), "town channel can resume after leaving PvP");
        check(!IsChannelSwitchPort(4, static_cast<unsigned short>(port)), "town channel is not a PvP destination");
    }
    for (const auto port : {7004, 7110, 7111, 7112}) {
        check(IsChannelSwitchPort(4, static_cast<unsigned short>(port)), "PvP channel can resume on entry");
        check(!IsChannelSwitchPort(2, static_cast<unsigned short>(port)), "PvP channel is not a town destination");
    }
    for (const auto port : {7100, 7101, 7102, 7103, 7104, 7105}) {
        check(IsChannelSwitchPort(0, static_cast<unsigned short>(port)), "regional channel can resume after leaving PvP");
    }
    check(!IsChannelSwitchPort(1, 47001) && !IsChannelSwitchPort(2, 47005)
        && !IsChannelSwitchPort(0, 47001) && !IsChannelSwitchPort(0, 7004) && !IsChannelSwitchPort(4, 7113),
        "unsupported channel selections stay outside the reconnect hook");
    if (passed) std::puts("PASS native local relay endpoint mapping");
    return passed ? 0 : 1;
}
