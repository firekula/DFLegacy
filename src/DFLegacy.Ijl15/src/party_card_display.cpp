#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <array>
#include <cstdio>
#include <cstring>

#include "memory_patch.h"
#include "party_card_display.h"

namespace
{
    constexpr std::uintptr_t SelectCardSite = 0x004158B5;
    constexpr std::array<unsigned char, 9> OriginalSelectCard{
        0x33, 0xF6, 0xC6, 0x85, 0x54, 0xFD, 0xFF, 0xFF, 0x00};
    constexpr std::array<unsigned char, 16> OriginalRecordDestructor{
        0x55, 0x8B, 0xEC, 0x6A, 0xFF, 0x68, 0xFB, 0xB7,
        0x98, 0x00, 0x64, 0xA1, 0x00, 0x00, 0x00, 0x00};
    constexpr std::array<unsigned char, 16> OriginalAddRecord{
        0x55, 0x8B, 0xEC, 0x6A, 0xFF, 0x68, 0x94, 0xB8,
        0x98, 0x00, 0x64, 0xA1, 0x00, 0x00, 0x00, 0x00};

    std::uintptr_t* FreeVector(void* screen, std::size_t ownerSlot)
    {
        // 8797F2..879A0D renders with freeOwners[column], not column.
        // Native vectors: +436 +16*(4*ownerSlot + group), allocator then
        // begin/end/capacity. Only group 0 is changed; paid/premium stay native.
        return reinterpret_cast<std::uintptr_t*>(
            static_cast<unsigned char*>(screen) + 440 + 64 * ownerSlot);
    }

    void __cdecl ReplaceNative(void* screen, std::size_t ownerSlot,
        const dfl::party_card_display::detail::FreeCard& card)
    {
        auto* vector = FreeVector(screen, ownerSlot);
        using Destroy = void (__thiscall*)(void*);
        using Add = int (__thiscall*)(void*, unsigned int, std::uint32_t, int, int);
        const auto destroy = reinterpret_cast<Destroy>(0x00873B80);
        const auto add = reinterpret_cast<Add>(0x0087A7E0);
        // Match 879D80's 56-byte record destruction, retain vector allocation.
        for (auto record = vector[0]; record != vector[1]; record += 56)
            destroy(reinterpret_cast<void*>(record));
        vector[1] = vector[0];
        add(screen, 0, card.Gold, static_cast<int>(ownerSlot), 0);
        if (card.Item != 0)
            add(screen, card.Item, 0, static_cast<int>(ownerSlot), 0);
    }

    void __stdcall RefreshFreeCards(const unsigned char* frame) noexcept
    {
        __try
        {
            if (frame == nullptr || frame[0] != 1 || frame[1] != 74) return;
            std::uint32_t size = 0;
            std::memcpy(&size, frame + 2, sizeof(size));
            if (size < 7 || size > 128) return;
            // The original frame remains encrypted; BF29E0 points into the
            // decrypted body after CMD74's success byte. Never advance it here.
            const auto body = *reinterpret_cast<const unsigned char* const*>(0x00BF29E0);
            dfl::party_card_display::detail::FreeCards cards;
            if (!dfl::party_card_display::detail::ReadExtension(body, size - 7, cards)) return;
            const auto manager = *reinterpret_cast<const unsigned char* const*>(0x00B5DCE8);
            if (manager == nullptr) return;
            auto* screen = *reinterpret_cast<unsigned char* const*>(manager + 0xF1C);
            if (screen == nullptr || *reinterpret_cast<const std::uintptr_t*>(screen) != 0x00A7D4EC) return;
            // Validate all affected vectors before changing any of them.
            for (std::size_t column = 0; column < cards.size(); ++column)
            {
                if (!cards[column].Assigned) continue;
                const auto* vector = FreeVector(screen, cards[column].Owner);
                if (vector[1] < vector[0] || vector[2] < vector[1]
                    || (vector[1] - vector[0]) % 56 != 0
                    || vector[1] - vector[0] > 56 * 2
                    || vector[2] - vector[0] > 56 * 16) return;
            }
            dfl::party_card_display::detail::Apply(screen, cards, &ReplaceNative);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            OutputDebugStringA("DFLegacy.Ijl15: free-card display refresh rejected.\n");
        }
    }

    __declspec(naked) void SelectCardHook()
    {
        __asm
        {
            pushfd
            pushad
            push dword ptr [ebp + 8]
            call RefreshFreeCards
            popad
            popfd
            xor esi, esi
            mov byte ptr [ebp - 2ACh], 0
            push 004158BEh
            ret
        }
    }
}

namespace dfl::party_card_display
{
    bool detail::ReadExtension(const unsigned char* body, std::size_t size,
        FreeCards& cards) noexcept
    {
        if (body == nullptr) return false;
        FreeCards parsed{};
        std::size_t at = 0;
        for (auto& card : parsed)
        {
            if (at > size || size - at < 3) return false;
            card.Owner = body[at];
            const auto paidOwner = body[at + 1];
            const auto count = body[at + 2];
            if ((card.Owner >= 4 && card.Owner != 255)
                || (paidOwner >= 4 && paidOwner != 255) || count > 2) return false;
            at += 3;
            const auto records = static_cast<std::size_t>(count) * 6;
            if (size - at < records) return false;
            at += records;
        }
        constexpr unsigned char Header[]{'D', 'F', 'C', 'R', 1, 4};
        constexpr std::size_t TrailerSize = sizeof(Header) + 4 * 7;
        if (size - at != TrailerSize || std::memcmp(body + at, Header, sizeof(Header)) != 0) return false;
        at += sizeof(Header);
        for (auto& card : parsed)
        {
            const auto assigned = body[at];
            if (assigned > 1 || (assigned != 0) != (card.Owner < 4)) return false;
            card.Assigned = assigned != 0;
            std::memcpy(&card.Gold, body + at + 1, sizeof(card.Gold));
            std::memcpy(&card.Item, body + at + 5, sizeof(card.Item));
            if (!card.Assigned && (card.Gold != 0 || card.Item != 0)) return false;
            at += 7;
        }
        cards = parsed;
        return true;
    }

    std::size_t detail::Apply(void* screen, const FreeCards& cards, ReplaceCard replace)
    {
        if (screen == nullptr || replace == nullptr) return 0;
        std::size_t count = 0;
        for (std::size_t column = 0; column < cards.size(); ++column)
        {
            if (!cards[column].Assigned || cards[column].Owner >= 4) continue;
            replace(screen, cards[column].Owner, cards[column]);
            ++count;
        }
        return count;
    }

    bool Install() noexcept
    {
        if (reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)) != 0x00400000
            || std::memcmp(reinterpret_cast<const void*>(SelectCardSite), OriginalSelectCard.data(), OriginalSelectCard.size()) != 0
            || std::memcmp(reinterpret_cast<const void*>(0x00873B80), OriginalRecordDestructor.data(), OriginalRecordDestructor.size()) != 0
            || std::memcmp(reinterpret_cast<const void*>(0x0087A7E0), OriginalAddRecord.data(), OriginalAddRecord.size()) != 0) return false;
        std::array<unsigned char, OriginalSelectCard.size()> patch{};
        patch.fill(0x90);
        patch[0] = 0xE9;
        const auto displacement = static_cast<std::uint32_t>(
            reinterpret_cast<std::uintptr_t>(&SelectCardHook) - SelectCardSite - 5);
        std::memcpy(patch.data() + 1, &displacement, sizeof(displacement));
        return memory_patch::WriteBytes(SelectCardSite, patch.data(), patch.size());
    }
}
