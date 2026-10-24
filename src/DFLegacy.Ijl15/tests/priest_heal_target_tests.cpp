#include <array>
#include <cstdint>

#include "priest_heal_target.h"

namespace
{
    int Local = 0;
    int Priest = 0;
    int ReplacedPriest = 0;
    int Target = 0;
    int Map = 0;
    bool TargetInMap = true;
    bool CasterInMap = true;
    bool CasterReplaced = false;
    std::uint32_t TargetReference = 0x01110003;

    void* __cdecl Lookup(std::uint16_t userId)
    {
        if (userId == 2) return CasterReplaced ? &ReplacedPriest : &Priest;
        if (userId == 3) return &Target;
        return nullptr;
    }

    std::uint32_t __cdecl Reference(void* actor)
    {
        return actor == &Priest ? 0x01110002 : TargetReference;
    }

    bool __cdecl Contains(void* map, void* actor)
    {
        return map == &Map
            && ((actor == &Priest && CasterInMap) || (actor == &Target && TargetInMap));
    }
}

bool TestPriestHealTargetReplay()
{
    using namespace dfl::priest_heal_target::detail;
    const ClientFunctions functions{&Lookup, &Reference, &Contains};
    // Layout fixture: state10's int vector is [phase, target.v3C()]. The
    // reference preserves both the player type and the full 16-bit user id.
    const int parameters[]{1, 0x01110003};
    const auto parsed = ReadCastTarget(10, parameters, 2);
    if (!parsed.Present || parsed.Reference != 0x01110003) return false;
    for (const int skill : std::array<int, 2>{51, 55})
    {
        if (ResolveForeignTarget(parsed, &Priest, &Local, &Map, 3, skill, functions) != &Target)
            return false;
        if (!ShouldIncludeSelfTarget(&Priest, &Priest, &Priest, nullptr, 3, 1, skill))
            return false;
    }
    // The sender can normalize an omitted target only when its actual native
    // selection is self. It must not turn an unknown or teammate cast into self.
    if (ShouldIncludeSelfTarget(&Priest, &Local, &Priest, nullptr, 3, 1, 51)
        || ShouldIncludeSelfTarget(&Priest, &Priest, nullptr, nullptr, 3, 1, 51)
        || ShouldIncludeSelfTarget(&Priest, &Priest, &Target, nullptr, 3, 1, 51)
        || ShouldIncludeSelfTarget(&Priest, &Priest, &Priest, &Target, 3, 1, 51)
        || ShouldIncludeSelfTarget(&Priest, &Priest, &Priest, nullptr, 2, 1, 51)
        || ShouldIncludeSelfTarget(&Priest, &Priest, &Priest, nullptr, 3, -1, 51)
        || ShouldIncludeSelfTarget(&Priest, &Priest, &Priest, nullptr, 3, 1, 26))
        return false;
    const int selfParameters[]{1, 0x01110002};
    if (ResolveForeignTarget(ReadCastTarget(10, selfParameters, 2), &Priest,
            &Local, &Map, 3, 51, functions) != &Priest)
        return false;
    const int noTarget[]{1};
    const int badType[]{1, 0x00010003};
    const int absentUser[]{1, 0x0111FFFF};
    const int previousPhase[]{0, 0x01110003};
    if (ReadCastTarget(10, noTarget, 1).Present
        || ReadCastTarget(10, badType, 2).Present
        || ReadCastTarget(10, absentUser, 2).Present
        || ReadCastTarget(10, previousPhase, 2).Present
        || ReadCastTarget(9, parameters, 2).Present
        || ReadCastTarget(10, nullptr, 2).Present
        || ReadCastTarget(10, parameters, 3).Present)
        return false;
    if (ResolveForeignTarget(parsed, &Priest, &Priest, &Map, 3, 51, functions)
        || ResolveForeignTarget(parsed, &Priest, &Local, &Map, 2, 51, functions)
        || ResolveForeignTarget(parsed, &Priest, &Local, &Map, 3, 26, functions)
        || ResolveForeignTarget({}, &Priest, &Local, &Map, 3, 51, functions))
        return false;
    TargetInMap = false;
    const bool staleTargetRejected = ResolveForeignTarget(parsed, &Priest,
        &Local, &Map, 3, 51, functions) == nullptr;
    TargetInMap = true;
    CasterInMap = false;
    const bool staleCasterRejected = ResolveForeignTarget(parsed, &Priest,
        &Local, &Map, 3, 51, functions) == nullptr;
    CasterInMap = true;
    CasterReplaced = true;
    const bool replacedCasterRejected = ResolveForeignTarget(parsed, &Priest,
        &Local, &Map, 3, 51, functions) == nullptr;
    CasterReplaced = false;
    TargetReference = 0x01110004;
    const bool mismatchedTargetRejected = ResolveForeignTarget(parsed, &Priest,
        &Local, &Map, 3, 51, functions) == nullptr;
    TargetReference = 0x01110003;
    return staleTargetRejected && staleCasterRejected && replacedCasterRejected
        && mismatchedTargetRejected;
}
