#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <map>

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


namespace
{
    struct ReceiverMemory
    {
        std::map<std::uintptr_t, unsigned char> Bytes;
        std::map<std::uintptr_t, int> Reads;
        std::uintptr_t RaceAddress = 0;
        std::uint32_t RaceValue = 0;
        static constexpr std::uint32_t Game = 0x100000, Actor = 0x200000, Stage = 0x300000;
        static constexpr std::uint32_t Ui = 0x400000, Dispatcher = 0x500000, Owner = 0x610000;
        static constexpr std::uint32_t Manager = 0x700000, First = 0x710000, Second = 0x720000, Table = 0x800000;
        static constexpr std::uint32_t Key = 0x12345678;
        void Put(std::uintptr_t address, std::uint32_t value)
        {
            const auto* bytes = reinterpret_cast<const unsigned char*>(&value);
            for (std::size_t i = 0; i < 4; ++i) Bytes[address + i] = bytes[i];
        }
        void Secure(std::uint32_t address, std::uint32_t index, std::uint32_t value)
        {
            Put(address, (address + index) ^ Key); Put(address + 4, index);
            Put(First + index * 4, value ^ (First + index * 4) ^ Key);
            Put(Second + index * 4, value ^ (Second + index * 4) ^ Key);
        }
        ReceiverMemory(bool dead = false, void* actorMap = &Map)
        {
            Put(0xCFD458, Manager); Put(0xCFD45C, Key); Put(Manager + 4, 8);
            Put(Manager + 0x44, First); Put(Manager + 0x48, Second);
            Put(0xB5D6F0, Game); Secure(Game + 0x10, 1, 3);
            Put(Dispatcher + 0x24, Owner); Put(Owner + 0x10, Stage);
            Put(Stage + 0xB0, static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&Map)));
            Put(0xB5DCE0, Ui); Put(Ui + 0x200010, Stage);
            Put(0xB75A14, Actor); Put(Actor + 0xD0, 0x111);
            Put(Actor + 0xE4, static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(actorMap)));
            Put(Actor, Table); Put(Table + 0x318, 0x652A00); Secure(Actor + 0x142C, 2, dead ? 1 : 0);
        }
        static bool __cdecl Read(void* state, std::uintptr_t address, void* target, std::size_t size)
        {
            auto& self = *static_cast<ReceiverMemory*>(state);
            if (++self.Reads[address] == 2 && address == self.RaceAddress) self.Put(address, self.RaceValue);
            auto* output = static_cast<unsigned char*>(target);
            for (std::size_t i = 0; i < size; ++i)
            {
                const auto found = self.Bytes.find(address + i);
                if (found == self.Bytes.end()) return false;
                output[i] = found->second;
            }
            return true;
        }
    };
}

bool TestGroundItemRemovalContext()
{
    using namespace dfl::ground_item_removal::detail;
    int failures = 0, checks = 0;
    const auto check = [&](bool result, const char* text)
    {
        ++checks; if (!result) { ++failures; std::printf("FAIL ground context: %s\n", text); }
    };
    const ClientFunctions functions{&Find, &Deactivate};
    constexpr unsigned char extended[]{5, 1, 0, 0x63, 0x0C, 0, 0, 2, 0};
    const auto receive = [&](ReceiverMemory& memory, MapContext& context)
    {
        PickupRemoval removal;
        if (!ReadExtension(extended, sizeof(extended), removal)) return false;
        void* const map = ReadCurrentMap(reinterpret_cast<void*>(ReceiverMemory::Dispatcher), {&memory, &ReceiverMemory::Read}, context);
        return ApplyRemoval(map, removal, functions);
    };
    // Identical packet on four real receiver-context paths, including both
    // native death shapes. No old-map lookup or unrelated same-item removal.
    for (int role = 0; role < 4; ++role)
    {
        Reset(); ReceiverMemory memory(role >= 2, role == 2 ? nullptr : role == 3 ? &PreviousMap : &Map);
        MapContext context;
        check(receive(memory, context) && !Drops[1].Active && Drops[1].Deactivations == 1,
            role < 2 ? "living receiver removes target" : role == 2 ? "dead detached receiver removes current-stage target" : "dead old-map receiver removes current-stage target");
        check(Lookups == 1 && Drops[2].Active && Drops[2].Deactivations == 0,
            "one current-map lookup, identical-item control remains active");
        check(!receive(memory, context) && Drops[1].Deactivations == 1,
            "duplicate receive never deactivates twice");
    }
    for (int invalid = 0; invalid < 18; ++invalid)
    {
        Reset(); ReceiverMemory memory(true, nullptr);
        switch (invalid)
        {
        case 0: memory.Secure(ReceiverMemory::Actor + 0x142C, 2, 0); break; // alive detached
        case 1: memory.Put(ReceiverMemory::Table + 0x318, 0x123456); break;
        case 2: memory.Put(ReceiverMemory::Actor + 0xD0, 0x211); break;
        case 3: memory.Put(ReceiverMemory::Second + 8, 0); break; // bad death mirror
        case 4: memory.Secure(ReceiverMemory::Game + 0x10, 1, 2); break;
        case 5: memory.Put(ReceiverMemory::Dispatcher + 0x24, 0); break;
        case 6: memory.Put(ReceiverMemory::Owner + 0x10, 0); break;
        case 7: memory.Put(ReceiverMemory::Stage + 0xB0, 0); break;
        case 8: memory.Put(0xB75A14, 0); break;
        case 9: memory.Put(0xB5DCE0, 0); break;
        case 10: memory.Put(ReceiverMemory::Ui + 0x200010, ReceiverMemory::Stage + 0x1000);
            memory.Put(ReceiverMemory::Stage + 0x1000 + 0xB0, static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&Map))); break;
        case 11: memory.RaceAddress = ReceiverMemory::Stage + 0xB0; memory.RaceValue = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&PreviousMap)); break;
        case 12: memory.RaceAddress = ReceiverMemory::Ui + 0x200010; memory.RaceValue = ReceiverMemory::Stage + 0x1000; break;
        case 13: memory.RaceAddress = 0xB75A14; memory.RaceValue = ReceiverMemory::Actor + 0x1000; break;
        case 14: memory.RaceAddress = 0xCFD45C; memory.RaceValue = 7; break;
        case 15: memory.RaceAddress = ReceiverMemory::First + 8; memory.RaceValue = 5; break;
        case 16: memory.Put(ReceiverMemory::Actor + 0x142C, 0); break; // bad protected address binding
        case 17: memory.RaceAddress = ReceiverMemory::Actor + 0xE4; memory.RaceValue = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&PreviousMap)); break;
        }
        MapContext context;
        const bool refused = !receive(memory, context) && Lookups == 0 && Drops[1].Active && Drops[1].Deactivations == 0;
        if (!refused) std::printf("invalid receiver case=%d reason=%s\n", invalid, context.Reason);
        check(refused, "invalid/changed receiver context refuses before object lookup");
    }
    Reset(); ReceiverMemory memory(true, nullptr); MapContext context;
    CorruptCandidate = true; Candidate = {&Drops[1], &PreviousMap, ItemType, 2, true};
    check(!receive(memory, context) && Drops[1].Active, "dead context still rejects foreign object map");
    Candidate.Map = &Map; Candidate.GroundId = 3;
    check(!receive(memory, context) && Drops[1].Active, "dead context still rejects wrong ground id");
    std::printf("Ground receiver context: %d checks, %d failures\n", checks, failures);
    return failures == 0;
}
