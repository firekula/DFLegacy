#include <array>
#include <limits>

#include "mage_skill_contact.h"

bool TestMageSkillContactPrerequisite()
{
    using dfl::mage_skill_contact::detail::CanEvaluateContactSkill;
    int templateObject = 0;
    // Reproduce the failed remote contact: the lazily created template has
    // level 0. The stock parameter lookups both return 0; parameter8-100
    // becomes -100 and the following ratio has denominator -100+100 == 0.
    const int missingLevel = 0;
    const int stockParameter8 = 0;
    const int stockDenominator = stockParameter8 - 100 + 100;
    if (stockDenominator != 0 || CanEvaluateContactSkill(&templateObject, missingLevel)
        || CanEvaluateContactSkill(nullptr, 10)
        || CanEvaluateContactSkill(nullptr, 0)
        || CanEvaluateContactSkill(&templateObject, -1)
        || CanEvaluateContactSkill(&templateObject, (std::numeric_limits<int>::min)()))
        return false;

    // The local B capture had skill20 level 10. All positive learned levels
    // must take the untouched native path, even the highest byte wire value.
    for (const int level : std::array<int, 5>{1, 2, 10, 20, 255})
    {
        if (!CanEvaluateContactSkill(&templateObject, level)) return false;
    }
    return true;
}
