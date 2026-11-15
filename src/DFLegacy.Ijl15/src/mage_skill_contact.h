#pragma once

namespace dfl::mage_skill_contact
{
    bool Install() noexcept;

    namespace detail
    {
        // The stock Mage skill-20 contact callback expects a learned skill.
        // A peer can legitimately have no skill object/level yet; it must not
        // derive a -100 modifier and divide by that modifier plus 100.
        bool CanEvaluateContactSkill(const void* skill, int learnedLevel) noexcept;
    }
}
