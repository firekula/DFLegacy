#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <limits>

namespace dfl::chapter_two::test_control {

// Callers bind these operations to the validated local player on the game
// thread. A failed write leaves the protected field unchanged.
struct CombatAccess {
    void* context = nullptr;
    bool (*read)(void*, std::uint32_t, std::uint32_t&) noexcept = nullptr;
    bool (*write)(void*, std::uint32_t, std::uint32_t) noexcept = nullptr;
};

class CombatOverride {
public:
    static constexpr std::int32_t InvincibleValue = 1000000;

    bool ActiveFor(std::uint32_t actor) const noexcept
    {
        return actor != 0 && actor == actor_ && enabled_;
    }

    bool BlocksHealthLoss(std::uint32_t actor, int requestedHp) const noexcept
    {
        return ActiveFor(actor) && requestedHp < InvincibleValue;
    }

    bool Enable(std::uint32_t actor, CombatAccess access) noexcept
    {
        if (actor_) return ActiveFor(actor) && Maintain(actor, access);

        Values before{};
        if (!Capture(actor, access, before)) return false;

        const auto result = Change(actor, access, before, Overrides);
        if (result != Result::RolledBack) {
            actor_ = actor;
            restore_ = before;
        }
        enabled_ = result == Result::Complete;
        return enabled_;
    }

    bool Maintain(std::uint32_t actor, CombatAccess access) noexcept
    {
        Values before{};
        if (!ActiveFor(actor) || !Capture(actor, access, before)) return false;

        // Native rebuilds the effective bank after some scene and equipment
        // changes. Keep those recalculated values available for restoration.
        for (std::size_t i = 0; i < CurrentHealthIndex; ++i) {
            if (before[i] != Overrides[i]) restore_[i] = before[i];
        }
        return Change(actor, access, before, Overrides) == Result::Complete;
    }

    bool Disable(std::uint32_t actor, CombatAccess access) noexcept
    {
        Values before{};
        if (actor == 0 || actor != actor_ || !Capture(actor, access, before)) return false;

        auto desired = restore_;
        for (std::size_t i = 0; i < CurrentHealthIndex; ++i) {
            if (before[i] != Overrides[i]) desired[i] = before[i];
        }
        if (Change(actor, access, before, desired) != Result::Complete) return false;

        actor_ = 0;
        enabled_ = false;
        return true;
    }

private:
    // The final effective bank is actor+0xEC4 (native vtable slot +0x33C,
    // bank index 3). Each covered stat is an int base and a float factor.
    static constexpr std::uint32_t EffectiveBank = 0xEC4;
    static constexpr std::uint32_t OneFloat = 0x3f800000;
    static constexpr std::array Offsets{
        EffectiveBank + 0x08, EffectiveBank + 0x10, // Max HP
        EffectiveBank + 0x18, EffectiveBank + 0x20, // Max MP
        EffectiveBank + 0x28, EffectiveBank + 0x30, // Strength
        EffectiveBank + 0x38, EffectiveBank + 0x40, // Vitality
        EffectiveBank + 0x48, EffectiveBank + 0x50, // Intelligence
        EffectiveBank + 0x58, EffectiveBank + 0x60, // Spirit
        0x1204u, 0x120cu                            // Current HP and MP
    };
    static constexpr std::size_t CurrentHealthIndex = Offsets.size() - 2;
    using Values = std::array<std::uint32_t, Offsets.size()>;
    static constexpr Values Overrides{
        InvincibleValue, OneFloat, InvincibleValue, OneFloat, InvincibleValue, OneFloat,
        InvincibleValue, OneFloat, InvincibleValue, OneFloat, InvincibleValue, OneFloat,
        InvincibleValue, InvincibleValue
    };
    enum class Result { Complete, RolledBack, RollbackFailed };

    static bool Capture(std::uint32_t actor, CombatAccess access, Values& values) noexcept
    {
        if (actor < 0x10000 || actor > std::numeric_limits<std::uint32_t>::max() - Offsets.back()
            || !access.read || !access.write) {
            return false;
        }

        for (std::size_t i = 0; i < Offsets.size(); ++i) {
            if (!access.read(access.context, actor + Offsets[i], values[i])) return false;
        }
        return true;
    }

    static Result Change(std::uint32_t actor, CombatAccess access, const Values& before, const Values& desired) noexcept
    {
        for (std::size_t i = 0; i < Offsets.size(); ++i) {
            if (before[i] == desired[i]) continue;
            if (access.write(access.context, actor + Offsets[i], desired[i])) continue;

            bool restored = true;
            while (i > 0) {
                --i;
                if (before[i] == desired[i]) continue;
                if (!access.write(access.context, actor + Offsets[i], before[i])) restored = false;
            }
            return restored ? Result::RolledBack : Result::RollbackFailed;
        }
        return Result::Complete;
    }

    std::uint32_t actor_ = 0;
    Values restore_{};
    bool enabled_ = false;
};

}
