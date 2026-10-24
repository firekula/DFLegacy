#include <array>
#include <cstdint>

#include "ground_item_removal.h"

namespace
{
    using dfl::ground_item_removal::detail::GroundItem;
    constexpr std::uint16_t ItemType = dfl::ground_item_removal::detail::GroundItemObjectType;
    int Map = 0;
    int PreviousMap = 0;

    struct Drop
    {
        std::uint16_t Id;
        std::uint32_t ItemId;
        bool Active;
        int Deactivations;
    };

    std::array<Drop, 4> Drops{};
    bool CorruptCandidate = false;
    GroundItem Candidate;
    int Lookups = 0;

    void Reset()
    {
        // The same item id appears on two different ground actors. A third
        // ground id equals that item id, catching the old field confusion.
        Drops = {{{1, 0, true, 0}, {2, 3171, true, 0},
            {3, 3171, true, 0}, {3171, 2000, true, 0}}};
        Lookups = 0;
        CorruptCandidate = false;
        Candidate = {};
    }

    GroundItem __cdecl Find(void* map, std::uint16_t type, std::uint16_t id)
    {
        ++Lookups;
        if (CorruptCandidate) return Candidate;
        if (map != &Map || type != ItemType) return {};
        for (auto& drop : Drops)
            if (drop.Id == id)
                return {&drop, &Map, ItemType, drop.Id, drop.Active};
        return {};
    }

    void __cdecl Deactivate(void* actor)
    {
        auto& drop = *static_cast<Drop*>(actor);
        drop.Active = false;
        ++drop.Deactivations;
    }
}

bool TestGroundItemRemovalNotification()
{
    using namespace dfl::ground_item_removal::detail;
    Reset();
    const ClientFunctions functions{&Find, &Deactivate};
    // Real pre-fix NOTI86 subtype5 payload observed on the non-picker:
    // picker UID1 / item3171. Its original seven bytes retain native behavior.
    constexpr unsigned char official[]{5, 1, 0, 0x63, 0x0C, 0, 0};
    PickupRemoval removal;
    if (ReadExtension(official, sizeof(official), removal)) return false;
    constexpr unsigned char extended[]{5, 1, 0, 0x63, 0x0C, 0, 0, 2, 0};
    if (!ReadExtension(extended, sizeof(extended), removal)
        || removal.PickerUserId != 1 || removal.ItemId != 3171 || removal.GroundId != 2)
        return false;
    if (!ApplyRemoval(&Map, removal, functions) || Drops[1].Active
        || Drops[1].Deactivations != 1 || !Drops[0].Active || !Drops[2].Active || !Drops[3].Active)
        return false;
    // Duplicate notifications and the owner receiving 86 after its personal
    // 39 already deactivated the drop are both harmless.
    if (ApplyRemoval(&Map, removal, functions) || Drops[1].Deactivations != 1)
        return false;
    removal.GroundId = 99;
    if (ApplyRemoval(&Map, removal, functions)) return false;
    // Gold uses the same ground-actor lifetime; item id zero is not a sentinel.
    constexpr unsigned char gold[]{5, 1, 0, 0, 0, 0, 0, 1, 0};
    if (!ReadExtension(gold, sizeof(gold), removal)
        || !ApplyRemoval(&Map, removal, functions) || Drops[0].Active
        || Drops[0].Deactivations != 1 || !Drops[2].Active)
        return false;
    constexpr unsigned char fullWidth[]{5, 0xFE, 0xFF, 0x98, 0xBA, 0xDC, 0xFE, 0xEF, 0xBE};
    if (!ReadExtension(fullWidth, sizeof(fullWidth), removal)
        || removal.PickerUserId != 0xFFFE || removal.ItemId != 0xFEDCBA98
        || removal.GroundId != 0xBEEF)
        return false;
    for (std::size_t size = 0; size < sizeof(extended); ++size)
        if (ReadExtension(extended, size, removal)) return false;
    const auto before = removal;
    if (ReadExtension(nullptr, 9, removal)
        || ReadExtension(extended, 10, removal)
        || removal.PickerUserId != before.PickerUserId || removal.ItemId != before.ItemId
        || removal.GroundId != before.GroundId)
        return false;
    for (const unsigned char subtype : std::array<unsigned char, 5>{0, 1, 2, 4, 6})
    {
        std::array<unsigned char, 9> other{};
        other[0] = subtype;
        if (ReadExtension(other.data(), other.size(), removal)) return false;
    }
    if (!ReadExtension(extended, sizeof(extended), removal)) return false;
    const int previousLookups = Lookups;
    if (ApplyRemoval(nullptr, removal, functions) || Lookups != previousLookups
        || ApplyRemoval(&Map, removal, {nullptr, &Deactivate})
        || ApplyRemoval(&Map, removal, {&Find, nullptr}))
        return false;
    // Reject a stale/wrong object even if the lookup layer returns a candidate.
    CorruptCandidate = true;
    Candidate = {&Drops[2], &PreviousMap, ItemType, 2, true};
    if (ApplyRemoval(&Map, removal, functions)) return false;
    Candidate.Map = &Map;
    Candidate.ObjectType = 0x0111;
    if (ApplyRemoval(&Map, removal, functions)) return false;
    Candidate.ObjectType = ItemType;
    Candidate.GroundId = 3;
    if (ApplyRemoval(&Map, removal, functions)) return false;
    Candidate.GroundId = 2;
    Candidate.Actor = nullptr;
    if (ApplyRemoval(&Map, removal, functions)) return false;
    return Drops[2].Active && Drops[2].Deactivations == 0
        && Drops[3].Active && Drops[3].Deactivations == 0;
}
