#include "ChapterTwoRelaySockets.h"

int WSAAPI dfl::chapter_two::RelayGameSendTo(SOCKET socket, const char* buffer, int length, int flags,
    const sockaddr* address, int addressLength)
{
    sockaddr_in mapped{};
    if (address && addressLength >= static_cast<int>(sizeof(mapped)) && address->sa_family == AF_INET) {
        mapped = *reinterpret_cast<const sockaddr_in*>(address);
        if (MapLocalRelayDatagram(mapped, false))
            return sendto(socket, buffer, length, flags, reinterpret_cast<const sockaddr*>(&mapped), sizeof(mapped));
    }
    return sendto(socket, buffer, length, flags, address, addressLength);
}

int WSAAPI dfl::chapter_two::RelayGameRecvFrom(SOCKET socket, char* buffer, int length, int flags,
    sockaddr* address, int* addressLength)
{
    const auto result = recvfrom(socket, buffer, length, flags, address, addressLength);
    const auto error = WSAGetLastError();
    if (result >= 0 && address && addressLength && *addressLength >= static_cast<int>(sizeof(sockaddr_in))
        && address->sa_family == AF_INET)
        MapLocalRelayDatagram(*reinterpret_cast<sockaddr_in*>(address), true);
    WSASetLastError(error);
    return result;
}
