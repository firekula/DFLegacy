#pragma once

#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>

namespace dfl::chapter_two
{
    inline bool ReadRemoteIpv4(char (&text)[16], in_addr& address) noexcept
    {
        const DWORD length = GetEnvironmentVariableA("DFL_S1C2_REMOTE_IPV4", text, sizeof(text));
        if (!length || length >= sizeof(text) || InetPtonA(AF_INET, text, &address) != 1) return false;
        const auto firstOctet = ntohl(address.s_addr) >> 24;
        return firstOctet != 0 && firstOctet != 127 && firstOctet < 224;
    }

    inline bool IsPvpPort(unsigned short port) noexcept
    {
        return port == 7004 || (port >= 7110 && port <= 7112);
    }
}
