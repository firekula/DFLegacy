#pragma once
#include <cstdint>

struct hostent;

namespace dfl::chapter_two
{
    hostent* ResolveLocalPartyRelayHost(const char* name) noexcept;
    hostent* ResolveGamePeerHost(const char* name, std::uintptr_t caller) noexcept;
}
