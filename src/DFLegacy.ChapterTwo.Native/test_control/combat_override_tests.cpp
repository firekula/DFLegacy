#include "CombatOverride.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>

namespace {
using dfl::chapter_two::test_control::CombatAccess;
using dfl::chapter_two::test_control::CombatOverride;

constexpr std::uint32_t Actor = 0x02000000;
constexpr std::uint32_t OtherActor = 0x02010000;
constexpr std::uint32_t OneFloat = 0x3f800000;
constexpr std::uint32_t Value = 1000000;
constexpr std::array<std::uint32_t, 14> Offsets{
    0xECC, 0xED4, 0xEDC, 0xEE4, 0xEEC, 0xEF4, 0xEFC, 0xF04,
    0xF0C, 0xF14, 0xF1C, 0xF24, 0x1204, 0x120C
};
constexpr std::array<std::uint32_t, 14> Expected{
    Value, OneFloat, Value, OneFloat, Value, OneFloat, Value, OneFloat,
    Value, OneFloat, Value, OneFloat, Value, Value
};

struct Memory {
    std::array<std::uint32_t, Offsets.size()> bits{
        470, 0x3f400000, 340, 0x3f900000, 73, 0x3fa00000, 42, OneFloat,
        56, 0x3fc00000, 64, OneFloat, 231, 104
    };
    std::uint32_t rejectRead = 0;
    std::uint32_t rejectWrite = 0;
    unsigned writes = 0;

    static std::size_t Index(std::uint32_t address) noexcept
    {
        for (std::size_t i = 0; i < Offsets.size(); ++i) {
            if (address == Actor + Offsets[i]) return i;
        }
        return Offsets.size();
    }

    static bool Read(void* context, std::uint32_t address, std::uint32_t& value) noexcept
    {
        auto& self = *static_cast<Memory*>(context);
        const auto index = Index(address);
        if (index == Offsets.size() || address == self.rejectRead) return false;
        value = self.bits[index];
        return true;
    }

    static bool Write(void* context, std::uint32_t address, std::uint32_t value) noexcept
    {
        auto& self = *static_cast<Memory*>(context);
        const auto index = Index(address);
        if (index == Offsets.size() || address == self.rejectWrite) return false;
        self.bits[index] = value;
        ++self.writes;
        return true;
    }

    CombatAccess Access() noexcept { return {this, Read, Write}; }
};

int failures = 0;
void Check(bool ok, const char* label)
{
    std::printf("%s %s\n", ok ? "PASS" : "FAIL", label);
    if (!ok) ++failures;
}
}

int main()
{
    Memory memory;
    const auto original = memory.bits;
    const auto access = memory.Access();
    CombatOverride mode;
    Check(!mode.ActiveFor(Actor) && !mode.BlocksHealthLoss(Actor, 0), "default leaves native damage enabled");
    memory.rejectRead = Actor + 0xEDC;
    Check(!mode.Enable(Actor, access) && memory.writes == 0, "a failed snapshot leaves all fields untouched");
    memory.rejectRead = 0;

    memory.rejectWrite = Actor + 0xEFC;
    Check(!mode.Enable(Actor, access) && !mode.ActiveFor(Actor) && memory.bits == original,
        "an incomplete enable restores earlier writes");
    memory.rejectWrite = 0;
    Check(mode.Enable(Actor, access) && mode.ActiveFor(Actor) && memory.bits == Expected,
        "enable overrides current HP/MP and the six proven final stat pairs");
    Check(mode.BlocksHealthLoss(Actor, 0) && mode.BlocksHealthLoss(Actor, Value - 1)
        && !mode.BlocksHealthLoss(Actor, Value) && !mode.BlocksHealthLoss(OtherActor, 0),
        "the damage hook blocks only decreases for the bound local actor");
    const auto writes = memory.writes;
    Check(!mode.Enable(OtherActor, access) && !mode.Disable(OtherActor, access)
        && !mode.Maintain(OtherActor, access) && memory.writes == writes,
        "an actor change leaves the live binding and memory untouched");
    Check(mode.Enable(Actor, access) && memory.writes == writes,
        "repeated enable retains the original snapshot");

    memory.bits[4] = 81;
    memory.bits[12] = 100;
    Check(mode.Maintain(Actor, access) && memory.bits == Expected,
        "native recalculation and HP decreases reapply the active values");

    memory.rejectWrite = Actor + 0xEFC;
    Check(!mode.Disable(Actor, access) && mode.ActiveFor(Actor) && memory.bits == Expected,
        "an incomplete disable remains active and rolls back restored fields");
    memory.rejectWrite = 0;
    auto restored = original;
    restored[4] = 81;
    Check(mode.Disable(Actor, access) && !mode.ActiveFor(Actor) && memory.bits == restored,
        "disable restores original HP/MP and the latest real recalculated stat");
    Check(!mode.BlocksHealthLoss(Actor, 0), "damage becomes available immediately after disable");
    return failures == 0 ? 0 : 1;
}
