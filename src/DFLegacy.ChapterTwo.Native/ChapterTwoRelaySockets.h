#pragma once
#include "ChapterTwoRemoteEndpoint.h"
#include "ChapterTwoRelayPortMap.h"

namespace dfl::chapter_two
{
    inline bool ReadLocalRelayIpv4(char (&text)[16], in_addr& address) noexcept
    {
        const DWORD length = GetEnvironmentVariableA("DFL_S1C2_LOCAL_RELAY_IPV4", text, sizeof(text));
        if (!length || length >= sizeof(text) || InetPtonA(AF_INET, text, &address) != 1) return false;
        const auto value = ntohl(address.s_addr);
        return (value >> 16) == 0x7f49 && (value & 255) > 0 && (value & 255) < 255
            && ((value >> 8) & 255) > 0 && ((value >> 8) & 255) < 255;
    }
    inline bool IsLocalGameAddress(in_addr address) noexcept
    {
        const auto value = ntohl(address.s_addr);
        return value == 0x7f000001 || value == 0x7f000002;
    }
    inline bool MapLocalRelayDatagram(sockaddr_in& endpoint, bool incoming) noexcept
    {
        if (endpoint.sin_family != AF_INET) return false;
        char host[16]{}; in_addr relay{};
        if (!ReadLocalRelayIpv4(host, relay)) return false;
        static const auto ports = RelayPortMap::Read("DFLEGACY_CH2_RELAY_UDP");
        const auto wirePort = ntohs(endpoint.sin_port);
        const auto port = incoming ? ports.ToService(wirePort) : wirePort;
        if (port != 42312 && port != 47005 && port != 47012 && port != 47013) return false;
        if (incoming) {
            if (endpoint.sin_addr.s_addr != relay.s_addr) return false;
            endpoint.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
            endpoint.sin_port = htons(port);
        } else {
            if (!IsLocalGameAddress(endpoint.sin_addr)) return false;
            const auto destination = ports.ToLocal(port);
            if (!destination) return false;
            endpoint.sin_addr = relay;
            endpoint.sin_port = htons(destination);
        }
        return true;
    }
    int WSAAPI RelayGameSendTo(SOCKET socket, const char* buffer, int length, int flags, const sockaddr* address, int addressLength);
    int WSAAPI RelayGameRecvFrom(SOCKET socket, char* buffer, int length, int flags, sockaddr* address, int* addressLength);
}
