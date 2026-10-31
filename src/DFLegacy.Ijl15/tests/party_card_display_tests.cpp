#include <array>
#include <cstdlib>
#include <fstream>
#include <iterator>
#include <vector>

#include "party_card_display.h"

namespace
{
    using namespace dfl::party_card_display::detail;
    struct Screen
    {
        FreeCards Free{};
        int Paid = 71;
        int Premium = 82;
    };
    void __cdecl Replace(void* screen, std::size_t column, const FreeCard& card)
    {
        static_cast<Screen*>(screen)->Free[column] = card;
    }
}

bool TestPartyCardDisplay()
{
    using namespace dfl::party_card_display::detail;
    // CMD74 body after success: nontrivial owner/column mapping and a paid
    // equipment row before the trailer. Same fixture as the C# encoder test.
    const std::vector<unsigned char> wire{
        2,255,0, 255,255,0, 255,1,2, 0,0,0,0,0,0, 0x89,0x13,0,0,0,0, 0,255,0,
        'D','F','C','R',1,4,
        1,111,0,0,0,0,0, 0,0,0,0,0,0,0, 0,0,0,0,0,0,0,
        1,222,0,0,0,0xC8,0x7D};
    FreeCards cards{};
    if (!ReadExtension(wire.data(), wire.size(), cards)
        || !cards[0].Assigned || cards[0].Owner != 2 || cards[0].Gold != 111
        || cards[0].Item != 0 || cards[1].Assigned || cards[2].Assigned
        || !cards[3].Assigned || cards[3].Owner != 0
        || cards[3].Gold != 222 || cards[3].Item != 32200) return false;
    Screen a, b;
    for (auto& card : a.Free) card.Gold = 19;
    for (auto& card : b.Free) {card.Gold = 20; card.Item = 32200;}
    if (Apply(&a, cards, &Replace) != 2 || Apply(&b, cards, &Replace) != 2
        || a.Free[2].Gold != 111 || b.Free[2].Gold != 111
        || a.Free[2].Item != 0 || b.Free[2].Item != 0
        || a.Free[0].Gold != 222 || b.Free[0].Gold != 222
        || a.Free[0].Item != 32200 || b.Free[0].Item != 32200
        || a.Free[1].Gold != 19 || b.Free[1].Gold != 20
        || a.Paid != 71 || b.Premium != 82) return false;
    if (Apply(&a, cards, &Replace) != 2 || a.Free[0].Gold != 222
        || Apply(nullptr, cards, &Replace) != 0 || Apply(&a, cards, nullptr) != 0) return false;
    const auto previous = cards;
    for (std::size_t size = 0; size < wire.size(); ++size)
        if (ReadExtension(wire.data(), size, cards)) return false;
    if (ReadExtension(nullptr, wire.size(), cards)) return false;
    auto bad = wire;
    bad[24] = 'X';
    if (ReadExtension(bad.data(), bad.size(), cards)) return false;
    bad = wire; bad[28] = 2;
    if (ReadExtension(bad.data(), bad.size(), cards)) return false;
    bad = wire; bad[1] = 4;
    if (ReadExtension(bad.data(), bad.size(), cards)) return false;
    bad = wire; bad[2] = 3;
    if (ReadExtension(bad.data(), bad.size(), cards)) return false;
    bad = wire; bad[37] = 1; // unassigned free owner cannot carry a reward
    if (ReadExtension(bad.data(), bad.size(), cards)) return false;
    bad = wire; bad.push_back(0);
    if (ReadExtension(bad.data(), bad.size(), cards)
        || cards[0].Gold != previous[0].Gold || cards[3].Item != previous[3].Item) return false;
    // Optional actual C# encoder output, supplied by the verification runner.
    char* fixture = nullptr;
    std::size_t fixtureSize = 0;
    if (_dupenv_s(&fixture, &fixtureSize, "DFLEGACY_CARD_DISPLAY_FIXTURE") != 0) return false;
    if (fixture != nullptr)
    {
        std::ifstream input(fixture, std::ios::binary);
        std::free(fixture);
        const std::vector<unsigned char> encoded((std::istreambuf_iterator<char>(input)), {});
        if (encoded.empty() || encoded[0] != 1 || encoded.size() != wire.size() + 1
            || !ReadExtension(encoded.data() + 1, encoded.size() - 1, cards)
            || cards[0].Gold != 111 || cards[3].Item != 32200) return false;
    }
    return true;
}
