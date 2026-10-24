#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

namespace dfl::party_card_display
{
    bool Install() noexcept;

    namespace detail
    {
        struct FreeCard
        {
            bool Assigned = false;
            std::uint8_t Owner = 255;
            std::uint32_t Gold = 0;
            std::uint16_t Item = 0;
        };
        using FreeCards = std::array<FreeCard, 4>;

        // Native CMD74 cursor, after its success byte. Legacy packets and
        // malformed trailers leave the output unchanged.
        bool ReadExtension(const unsigned char* body, std::size_t size,
            FreeCards& cards) noexcept;
        using ReplaceCard = void (__cdecl*)(void* screen, std::size_t ownerSlot, const FreeCard& card);
        std::size_t Apply(void* screen, const FreeCards& cards, ReplaceCard replace);
    }
}
