#pragma once
#include <windows.h>
#include <cstdint>

namespace dfl::client_bootstrap
{
    inline constexpr GUID PayloadId = { 0xb4ec92af, 0x816a, 0x44a7, {0x82, 0x12, 0xdc, 0xa6, 0x1e, 0xb7, 0xc0, 0x32} };
    inline constexpr std::uint32_t ProtocolMagic = 0x314c4644;
    inline constexpr DWORD InitializationTimeout = 10000;
    inline constexpr DWORD CommitTimeout = 15000;
    inline constexpr DWORD PathCapacity = 1024;
    struct Payload
    {
        std::uint32_t magic, size, mapping, ready, release;
    };
    struct SharedState
    {
        std::uint32_t magic, size, pid, mode;
        char nonce[33];
        volatile LONG initialized;
        DWORD volume, fileIndexHigh, fileIndexLow;
        wchar_t modulePath[PathCapacity];
    };
}
