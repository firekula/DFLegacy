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

// Debug-client combat override: the local player deals 9,999,999 damage,
// takes no damage, and moves/attacks/casts at +100% speed. The damage and
// defense values live in the effective stat bank; the "take no damage" half
// is completed by the SetHp guard (BlocksHealthLoss).
class CombatOverride {
public:
    static constexpr std::int32_t InvincibleValue = 1000000;
    static constexpr std::int32_t DamageValue = 9999999;

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

        restore_ = before;
        Values desired{};
        if (!Desired(before, desired)) return false;
        const auto result = Change(actor, access, before, desired);
        if (result != Result::RolledBack) {
            actor_ = actor;
            applied_ = desired;
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
        for (std::size_t i = 0; i < before.size(); ++i) {
            if (before[i] != applied_[i]) restore_[i] = before[i];
        }
        Values desired{};
        if (!Desired(restore_, desired)) return false;
        const auto result = Change(actor, access, before, desired);
        if (result == Result::Complete) applied_ = desired;
        return result == Result::Complete;
    }

    bool Disable(std::uint32_t actor, CombatAccess access) noexcept
    {
        Values before{};
        if (actor == 0 || actor != actor_ || !Capture(actor, access, before)) return false;

        Values desired = restore_;
        for (std::size_t i = 0; i < before.size(); ++i) {
            if (before[i] != applied_[i]) desired[i] = before[i];
        }
        if (Change(actor, access, before, desired) != Result::Complete) return false;

        actor_ = 0;
        enabled_ = false;
        return true;
    }

private:
    // The final effective bank is actor+0xEC4 (native vtable slot +0x33C,
    // bank index 3). Each covered stat is an int base and a float factor.
    // The bank follows the combat-stat block order: max HP, max MP, physical
    // attack, physical defense, magical attack, magical defense, ...
    static constexpr std::uint32_t EffectiveBank = 0xEC4;
    static constexpr std::uint32_t OneFloat = 0x3f800000;

    // Physical attack, physical defense, magical attack, magical defense,
    // movement speed, attack speed, cast speed.
    static constexpr std::array StatOffsets{
        EffectiveBank + 0x28,
        EffectiveBank + 0x38,
        EffectiveBank + 0x48,
        EffectiveBank + 0x58,
        EffectiveBank + 0xD8,
        EffectiveBank + 0xE8,
        EffectiveBank + 0xF8
    };
    static constexpr std::size_t SpeedFirst = 4;
    static constexpr std::size_t StatCount = StatOffsets.size();
    static constexpr std::size_t ValueCount = StatCount * 2;
    using Values = std::array<std::uint32_t, ValueCount>;
    enum class Result { Complete, RolledBack, RollbackFailed };

    static constexpr std::uint32_t OffsetFor(std::size_t index) noexcept
    {
        return StatOffsets[index / 2] + (index % 2 == 0 ? 0u : 8u);
    }

    static bool Capture(std::uint32_t actor, CombatAccess access, Values& values) noexcept
    {
        if (actor < 0x10000 || !access.read || !access.write) return false;

        for (std::size_t i = 0; i < values.size(); ++i) {
            if (!access.read(access.context, actor + OffsetFor(i), values[i])) return false;
        }
        return true;
    }

    static bool Desired(const Values& original, Values& desired) noexcept
    {
        desired = original;
        for (std::size_t i = 0; i < ValueCount; i += 2) {
            const auto stat = i / 2;
            if (stat < SpeedFirst) {
                desired[i] = static_cast<std::uint32_t>(DamageValue);
                desired[i + 1] = OneFloat;
                continue;
            }

            const auto base = static_cast<std::int64_t>(original[i]);
            const auto doubled = base > std::numeric_limits<std::int32_t>::max() / 2
                ? std::numeric_limits<std::int32_t>::max()
                : base * 2;
            desired[i] = static_cast<std::uint32_t>(doubled);
            desired[i + 1] = original[i + 1];
        }
        return true;
    }

    static Result Change(std::uint32_t actor, CombatAccess access, const Values& before, const Values& desired) noexcept
    {
        for (std::size_t i = 0; i < before.size(); ++i) {
            if (before[i] == desired[i]) continue;
            if (access.write(access.context, actor + OffsetFor(i), desired[i])) continue;

            bool restored = true;
            for (std::size_t j = 0; j < i; ++j) {
                if (before[j] == desired[j]) continue;
                if (!access.write(access.context, actor + OffsetFor(j), before[j])) restored = false;
            }
            return restored ? Result::RolledBack : Result::RollbackFailed;
        }
        return Result::Complete;
    }

    std::uint32_t actor_ = 0;
    Values restore_{};
    Values applied_{};
    bool enabled_ = false;
};

}
