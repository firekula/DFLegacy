#pragma once

struct hostent;

namespace dfl::chapter_two
{
    hostent* ResolveLocalPartyRelayHost(const char* name) noexcept;
}
