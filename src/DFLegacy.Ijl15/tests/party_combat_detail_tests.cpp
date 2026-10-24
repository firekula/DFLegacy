#include <array>

#include "party_combat_detail.h"

namespace
{
    using namespace dfl::party_combat_detail::detail;
    struct Actor
    {
        int BaseMaximum = 1440;
        int ItemBonus = 0;
        int Maximum = 1440;
        int Current = 121;
        int MaximumMp = 1960;
        int CurrentMp = 117;
        int Recovery = 46;
        int Calls = 0;
        int Initializations = 0;
    };

    void __cdecl Recalculate(void* value, bool initialize)
    {
        auto& actor = *static_cast<Actor*>(value);
        ++actor.Calls;
        actor.Maximum = actor.BaseMaximum + actor.ItemBonus;
        if (initialize)
        {
            ++actor.Initializations;
            actor.Current = actor.Maximum * actor.Recovery / 100;
            actor.CurrentMp = actor.MaximumMp * actor.Recovery / 100;
        }
    }
    const ClientFunctions Functions{&Recalculate};
    void* RegisteredActor = nullptr;
    void* RegisteredUser = nullptr;
    std::uint16_t RegisteredUserId = 0;

    bool __cdecl CurrentUser(const TransitionInitialization& entry)
    {
        return entry.Actor == RegisteredActor && entry.User == RegisteredUser
            && entry.UserId == RegisteredUserId;
    }
}

bool TestPartyCombatDetailInitialization()
{
    int parserFrame;
    // The minute tick heals the owning selection actor to the same recovery
    // percentage used by a complete peer detail, including both HP and MP.
    Actor selectionOwner;
    selectionOwner.ItemBonus = 77;
    selectionOwner.Current = 697;
    selectionOwner.CurrentMp = 901;
    selectionOwner.Recovery = 55;
    if (!ApplySelectionRecovery(&selectionOwner, 2, true, Functions)
        || selectionOwner.Current != 834 || selectionOwner.CurrentMp != 1078
        || selectionOwner.Initializations != 1)
        return false;
    for (const unsigned char mode : std::array<unsigned char, 2>{1, 3})
    {
        Actor peer;
        peer.Current = 697;
        peer.CurrentMp = 901;
        peer.Recovery = 55;
        PendingInitialization pending;
        BeginDetail(pending, &peer, &parserFrame, 2, mode, true, Functions, true);
        if (peer.Current != 697 || peer.CurrentMp != 901 || peer.Initializations != 0)
            return false;
        peer.ItemBonus = 77;
        if (!CompleteDetail(pending, &peer, &parserFrame, 2, mode, Functions, true)
            || peer.Current != selectionOwner.Current
            || peer.CurrentMp != selectionOwner.CurrentMp
            || peer.Initializations != 1)
            return false;
        if (CompleteDetail(pending, &peer, &parserFrame, 2, mode, Functions, true))
            return false;
    }

    // NOTI30 attaches a real dungeon stage before scene2 changes to scene3.
    // Recovery and even the native mode1 initFlag1 must not heal this handoff.
    Actor handoff;
    handoff.Current = 13;
    handoff.CurrentMp = 27;
    handoff.Recovery = 55;
    PendingInitialization handoffPending;
    if (ApplySelectionRecovery(&handoff, 2, false, Functions)) return false;
    BeginDetail(handoffPending, &handoff, &parserFrame, 2, 1, true, Functions, false);
    if (handoff.Current != 13 || handoff.CurrentMp != 27
        || handoff.Initializations != 0 || handoffPending.Actor != nullptr
        || CompleteDetail(handoffPending, &handoff, &parserFrame, 2, 1, Functions, false))
        return false;
    BeginDetail(handoffPending, &handoff, &parserFrame, 2, 1, true, Functions, true);
    if (CompleteDetail(handoffPending, &handoff, &parserFrame, 2, 1, Functions, false)
        || handoff.Current != 13 || handoff.CurrentMp != 27)
        return false;
    BeginDetail(handoffPending, &handoff, &parserFrame, 2, 1, true, Functions, true);
    if (CompleteDetail(handoffPending, &handoff, &parserFrame, 3, 1, Functions)
        || handoff.Current != 13 || handoff.CurrentMp != 27)
        return false;
    // A pending selection record cannot be consumed as a different Town record.
    BeginDetail(handoffPending, &handoff, &parserFrame, 2, 1, true, Functions, true);
    if (CompleteDetail(handoffPending, &handoff, &parserFrame, 1, 1, Functions)) return false;
    BeginDetail(handoffPending, &handoff, &parserFrame, 1, 1, true, Functions);
    if (CompleteDetail(handoffPending, &handoff, &parserFrame, 2, 1, Functions, true)) return false;
    for (const int scene : {0, 1, 3, 4, 5, 6, 7, 8, 9, 10})
    {
        if (ApplySelectionRecovery(&handoff, scene, true, Functions)
            || handoff.Current != 13 || handoff.CurrentMp != 27
            || handoff.Initializations != 0)
            return false;
    }

    // Each record initializes once, after equipment and passives have changed
    // maximum HP. A subsequent packet recomputes from base, not previous max.
    for (const unsigned char mode : std::array<unsigned char, 2>{1, 3})
    {
        Actor actor;
        PendingInitialization pending;
        BeginDetail(pending, &actor, &parserFrame, 1, mode, true, Functions);
        if (actor.Current != 121 || actor.Initializations != 0 || actor.Calls != 1)
            return false;
        actor.ItemBonus = 77;
        if (!CompleteDetail(pending, &actor, &parserFrame, 1, mode, Functions)
            || actor.Maximum != 1517 || actor.Current != 697
            || actor.Initializations != 1 || actor.Calls != 2)
            return false;
        if (CompleteDetail(pending, &actor, &parserFrame, 1, mode, Functions)
            || actor.Initializations != 1)
            return false;
        BeginDetail(pending, &actor, &parserFrame, 1, mode, true, Functions);
        actor.ItemBonus = 120;
        if (!CompleteDetail(pending, &actor, &parserFrame, 1, mode, Functions)
            || actor.Maximum != 1560 || actor.Current != 717
            || actor.Initializations != 2)
            return false;
    }

    // Dungeon details update equipment/stats but preserve battle damage.
    for (const int scene : {0, 2, 3, 4, 6, 7, 9})
    {
        Actor actor;
        PendingInitialization pending;
        BeginDetail(pending, &actor, &parserFrame, scene, 1, false, Functions);
        actor.ItemBonus = 77;
        if (CompleteDetail(pending, &actor, &parserFrame, scene, 1, Functions)
            || actor.Current != 121 || actor.Initializations != 0)
            return false;
    }

    // Preserve the native initialization behavior outside Town, and never
    // defer an unrelated packet mode or an explicitly non-resetting record.
    for (const auto [scene, mode, initialize] : std::array<std::array<int, 3>, 4>{
            {{3, 1, 1}, {1, 0, 1}, {1, 2, 1}, {1, 1, 0}}})
    {
        Actor actor;
        PendingInitialization pending;
        BeginDetail(pending, &actor, &parserFrame, scene,
            static_cast<unsigned char>(mode), initialize != 0, Functions);
        if (pending.Actor != nullptr || actor.Initializations != initialize
            || CompleteDetail(pending, &actor, &parserFrame, scene,
                static_cast<unsigned char>(mode), Functions))
            return false;
    }

    // An aborted/mismatched record must not initialize a different actor or
    // carry a delayed Town heal into a scene transition or a later record.
    Actor first;
    Actor other;
    int otherFrame;
    PendingInitialization pending;
    BeginDetail(pending, &first, &parserFrame, 1, 1, true, Functions);
    if (CompleteDetail(pending, &other, &parserFrame, 1, 1, Functions)
        || first.Initializations != 0 || other.Initializations != 0)
        return false;
    BeginDetail(pending, &first, &parserFrame, 1, 1, true, Functions);
    if (CompleteDetail(pending, &first, &otherFrame, 1, 1, Functions)) return false;
    BeginDetail(pending, &first, &parserFrame, 1, 1, true, Functions);
    if (CompleteDetail(pending, &first, &parserFrame, 3, 1, Functions)) return false;
    BeginDetail(pending, &first, &parserFrame, 1, 1, true, Functions);
    BeginDetail(pending, &other, &parserFrame, 3, 1, false, Functions);
    if (CompleteDetail(pending, &first, &parserFrame, 1, 1, Functions)
        || pending.Actor != nullptr || first.Initializations != 0)
        return false;

    // A real return fade still reports scene 3 when the complete equipment
    // record arrives. It must retain dead HP until the native Town setup ends.
    int user;
    RegisteredUser = &user;
    RegisteredUserId = 2;
    for (const unsigned char mode : std::array<unsigned char, 2>{1, 3})
    {
        Actor actor;
        actor.Current = -1;
        RegisteredActor = &actor;
        TransitionQueue queue;
        BeginDetail(pending, &actor, &parserFrame, 3, mode, false, Functions);
        actor.ItemBonus = 77;
        if (CompleteDetail(pending, &actor, &parserFrame, 3, mode, Functions))
            return false;
        const TransitionInitialization entry{&actor, &user, 2, mode, 100};
        if (!QueueTownTransition(queue, entry, 3, 4) || actor.Current != -1
            || actor.Initializations != 0)
            return false;
        // Latest completed record for the same uid replaces, never appends.
        if (!QueueTownTransition(queue, entry, 3, 4) || queue.Count != 1)
            return false;
        if (CompleteTownTransition(queue, 3, 1, 1, 650, &CurrentUser, Functions) != 1
            || actor.Current != 697 || actor.Maximum != 1517
            || actor.Initializations != 1 || queue.Count != 0
            || CompleteTownTransition(queue, 3, 1, 1, 651, &CurrentUser, Functions) != 0)
            return false;
    }

    Actor actor;
    actor.Current = -1;
    RegisteredActor = &actor;
    const TransitionInitialization entry{&actor, &user, 2, 1, 100};
    TransitionQueue queue;
    // An ordinary dungeon, other scene, or non-detail mode is never queued.
    for (const auto [scene, module, mode] : std::array<std::array<int, 3>, 7>{
            {{3, 0, 1}, {3, 1, 1}, {3, 3, 1}, {1, 4, 1},
             {2, 4, 1}, {3, 4, 0}, {3, 4, 2}}})
    {
        auto ignored = entry;
        ignored.Mode = static_cast<unsigned char>(mode);
        if (QueueTownTransition(queue, ignored, scene, module) || queue.Count != 0)
            return false;
    }
    // Aborted/other scene transitions discard the token without healing.
    for (const auto [oldScene, newScene, actualScene] : std::array<std::array<int, 3>, 4>{
            {{3, 3, 3}, {3, 2, 2}, {1, 1, 1}, {3, 1, 3}}})
    {
        QueueTownTransition(queue, entry, 3, 4);
        if (CompleteTownTransition(queue, oldScene, newScene, actualScene, 650,
                &CurrentUser, Functions) != 0 || queue.Count != 0
            || actor.Initializations != 0)
            return false;
    }
    // Reused uid, replaced actor, disconnected user and expired records fail
    // registration validation; none can heal a different character lifetime.
    for (int replacement = 0; replacement < 3; ++replacement)
    {
        RegisteredActor = replacement == 0 ? &other : &actor;
        RegisteredUser = replacement == 1 ? nullptr : &user;
        RegisteredUserId = replacement == 2 ? 3 : 2;
        QueueTownTransition(queue, entry, 3, 4);
        if (CompleteTownTransition(queue, 3, 1, 1, 650, &CurrentUser, Functions) != 0
            || actor.Initializations != 0 || queue.Count != 0)
            return false;
    }
    RegisteredActor = &actor;
    RegisteredUser = &user;
    RegisteredUserId = 2;
    QueueTownTransition(queue, entry, 3, 4);
    if (CompleteTownTransition(queue, 3, 1, 1, 10101, &CurrentUser, Functions) != 0
        || actor.Initializations != 0 || queue.Count != 0)
        return false;
    // GetTickCount wraparound still recognizes a recent valid transition.
    auto wrapped = entry;
    wrapped.QueuedAtMs = 0xFFFFFF00u;
    QueueTownTransition(queue, wrapped, 3, 4);
    if (CompleteTownTransition(queue, 3, 1, 1, 300, &CurrentUser, Functions) != 1
        || actor.Initializations != 1)
        return false;
    // Fixed storage fails closed when full, without replacing another uid.
    queue = {};
    for (std::size_t index = 0; index < queue.Entries.size(); ++index)
    {
        auto next = entry;
        next.UserId = static_cast<std::uint16_t>(index);
        if (!QueueTownTransition(queue, next, 3, 4)) return false;
    }
    auto overflow = entry;
    overflow.UserId = 500;
    return !QueueTownTransition(queue, overflow, 3, 4)
        && queue.Count == queue.Entries.size();
}
