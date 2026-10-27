using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class OfficialLootContractSmokeTests
{
    internal static ItemDefinition Item(ItemScriptKind kind = ItemScriptKind.Stackable,
        ItemInventoryCategory category = ItemInventoryCategory.Consumable, string type = "waste") =>
        new(1000, "test", "test", kind, category, type, ItemAttachType.Free,
            Grade: null, Rarity: 1, Weight: 999_999, MinimumLevel: 99, MaximumLevel: null,
            CreationRate: null, StackLimit: 100, MaximumHavingCount: 1,
            Cash: null, Price: null, Value: null, Durability: null);

    public static void Run(Action<bool, string> check)
    {
        check(GameProtocolEngine.TryParseGetItemCommand(Convert.FromHexString("4100100001049F20"), out var request)
                && request.GroundId == 16 && request.RouteRequested && request.ClientRarity == 4
                && request.CheckToken == 0x209F,
            "ACT1 confirmation uses an eight-byte GET_ITEM with its own routing byte and opaque token");
        check(GameProtocolEngine.TryParseGetItemCommand(Convert.FromHexString("4D0002000004970B"), out request)
                && !request.RouteRequested && request.GroundId == 2 && request.CheckToken == 0x0B97
                && !GameProtocolEngine.TryParseGetItemCommand(new byte[7], out _),
            "ACT1 direct confirmation remains distinct and short requests never enter pickup");

        var item = Item();
        var ground = new DungeonGroundItem(1, 1000, 20, false, 7, 1, 0, 0);
        check(!LootRouting.IsRoutableGroundItem(ground, item with { Rarity = 4 }, false)
                && LootRouting.IsRoutableGroundItem(ground, item with { Rarity = 4 }, true),
            "rarity above three is controlled by the native confirmation routing byte");
        check(!LootRouting.IsRoutableGroundItem(ground with { RoutingSource = 0 }, item, true)
                && LootRouting.IsRoutableGroundItem(ground with { RoutingSource = 0 }, item with { Rarity = 4 }, true),
            "the official rarity override follows the source gate instead of being blocked by a source boolean");
        check(!LootRouting.IsRoutableGroundItem(ground with { RoutingSource = 2 }, item, true)
                && LootRouting.IsRoutableGroundItem(ground with { RoutingSource = 2 }, item with { IsEpicRoutingItem = true }, true),
            "a nonzero source other than one is distinct from both generated and player-discarded sources");
        check(!LootRouting.IsRoutableGroundItem(ground, item with { IsEpicRoutingItem = true }, false)
                && LootRouting.IsRoutableGroundItem(ground, item with { IsEpicRoutingItem = true }, true)
                && LootRouting.IsRoutableGroundItem(ground, item with { Rarity = 0, InventoryCategory = ItemInventoryCategory.Avatar }, false),
            "epic routing also consumes confirmation, while a white avatar has its independent ordinary gate");

        var inventory = new Dictionary<ushort, CharacterItemRecord> { [3] = new(3, 1000, 90), [4] = new(4, 1000, 1) };
        check(!NativeLootInventory.CanInsert(inventory, ground, item),
            "the first same-id quick stack overflowing its limit excludes the candidate even with another stack and empty slots");
        inventory[3] = new(3, 1000, 80);
        check(NativeLootInventory.CanInsert(inventory, ground, item),
            "native candidate preflight accepts a single exact-limit stack without max-having, weight or level filtering");
        check(!NativeLootInventory.CanInsert(new Dictionary<ushort, CharacterItemRecord>(), ground with { CountOrValue = 101 }, item)
                && NativeLootInventory.CanInsert(new Dictionary<ushort, CharacterItemRecord>(), ground with { CountOrValue = 9_999_999 }, item with { StackLimit = null })
                && !NativeLootInventory.CanInsert(new Dictionary<ushort, CharacterItemRecord>(), ground with { CountOrValue = 10_000_000 }, item with { StackLimit = null }),
            "candidate preflight never splits the incoming stack and uses the native default limit 9999999");
        var weapon = Item(ItemScriptKind.Equipment, ItemInventoryCategory.Equipment, "weapon") with { UsableJobMask = 1 << 4 };
        check(LootRouting.HasJobBonus(weapon, 4) && !LootRouting.HasJobBonus(weapon, 0)
                && !LootRouting.HasJobBonus(weapon with { TypeTag = "coat" }, 4)
                && !LootRouting.HasJobBonus(weapon with { UsableJobMask = 0 }, 4),
            "the official job bonus applies only to weapons with the base job bit set");

        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var candidates = new[] { new LootRoutingCandidate(0, a, 7), new LootRoutingCandidate(3, b, 9) };
        var decision = LootRouting.Decide(true, false, true, a, candidates, 1, _ => 0, out var next,
            partyMemberCount: 4);
        check(decision.RoutingScores.SequenceEqual(new byte[] { 1, 0, 0, 0 }) && next == 1 && decision.WinnerCharacterId == a,
            "rotation uses total party count and remains unchanged when the compressed candidate ordinal is absent");
        decision = LootRouting.Decide(true, false, true, a, [candidates[1]], 0, _ => 0, out _, partyMemberCount: 2);
        check(decision.Outcome == LootPickupOutcome.Routed && decision.RoutingScores[3] == 1,
            "a single surviving candidate in a multiplayer party still rolls");
        decision = LootRouting.Decide(true, false, true, a, [], 0, _ => 0, out next, partyMemberCount: 2);
        check(decision.Outcome == LootPickupOutcome.NoEligibleRecipient && decision.ErrorCode == 22 && next == 0,
            "no eligible candidate returns official error22 instead of giving the item to the picker");
        var random = new Queue<int>(new[] { 10, 0, 0 });
        decision = LootRouting.Decide(true, false, true, a, candidates, 0, _ => random.Dequeue(), out _,
            partyMemberCount: 2, forcedOwnerCharacterId: a);
        check(decision.RoutingScores[0] == 99 && decision.WinnerCharacterId == a && random.Count == 0,
            "the native forced owner draws 89..99 first and still consumes the normal per-candidate random calls");
        random = new Queue<int>(new[] { 0, 98, 58 });
        decision = LootRouting.Decide(true, false, true, a, candidates, 0, _ => random.Dequeue(), out _,
            partyMemberCount: 2, forcedOwnerCharacterId: Guid.NewGuid());
        check(decision.RoutingScores.SequenceEqual(new byte[] { 88, 0, 0, 87 }) && decision.WinnerCharacterId == a,
            "an absent forced owner still caps other scores before the native tie adjustment");
        CheckReservation(check);
    }

    private static void CheckReservation(Action<bool, string> check)
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var instance = new DungeonInstance(1, Guid.Empty, null!, null!);
        instance.AddMember(new(a, 7, 0, "a", _ => ValueTask.CompletedTask));
        instance.AddMember(new(b, 9, 1, "b", _ => ValueTask.CompletedTask));
        var ground = instance.AddGroundItem(new(false, 1000, 1), 7, 1, 0, 0, 0, 0);
        var candidates = new[] { new LootRoutingCandidate(0, a, 7), new LootRoutingCandidate(1, b, 9) };
        var decisions = new System.Collections.Concurrent.ConcurrentBag<LootRoutingDecision>();
        Parallel.For(0, 2, _ =>
        {
            if (instance.TryBeginLootPickup(a, ground, true, candidates, _ => 0, out var decision)) decisions.Add(decision);
        });
        check(decisions.Count == 1 && instance.RoutingRoundRobinIndex == 1,
            "concurrent requests for the same item produce exactly one dice decision and one rotation advance");
        var first = decisions.Single();
        check(!instance.TryBeginLootPickup(b, ground, true, candidates, _ => 0, out var busy)
                && busy is { ErrorCode: 21 },
            "a second pending pickup receives a native rollback error instead of a silent wait");
        instance.ReleaseLootPickup(ground, first.PickupId);
        instance.TryBeginLootPickup(a, ground, true, candidates, _ => 0, out var second);
        instance.ReleaseLootPickup(ground, first.PickupId);
        var oldMail = new DungeonLootPickupAttempt(first.WinnerCharacterId, ground.GroundId, a, 7, 0,
            first.RoutingScores, instance, ground, first.PickupId);
        check(second.PickupId != first.PickupId
                && !instance.TryGetGroundItemForLootAttempt(oldMail, 0, 0, out _)
                && !instance.TryClaimGroundItem(ground.GroundId, 0, 0, out _, ground, first.PickupId)
                && !instance.TryBeginLootPickup(a, ground, true, candidates, _ => 0, out _),
            "old mail and old cancellation cannot consume or release a later reservation of the same object");
        instance.RemoveMember(second.WinnerCharacterId);
        var remaining = second.WinnerCharacterId == a ? b : a;
        check(instance.TryBeginLootPickup(remaining, ground, true, candidates, _ => 0, out _),
            "a winner leaving releases the pending item so it can be picked up again");

        var cancelled = new DungeonInstance(2, Guid.Empty, null!, null!);
        cancelled.AddMember(new(a, 7, 0, "a", _ => ValueTask.CompletedTask));
        cancelled.AddMember(new(b, 9, 1, "b", _ => ValueTask.CompletedTask));
        var drop = cancelled.AddGroundItem(new(false, 1000, 1), 7, 1, 0, 0, 0, 0);
        cancelled.TryBeginLootPickup(a, drop, true, candidates, _ => 0, out _);
        var registry = new DungeonInstanceRegistry();
        registry.BindCharacter(a, cancelled);
        cancelled.RemoveMember(b);
        check(registry.HasPendingMail(a)
                && cancelled.TryTakeCancelledLootPickup(a, out var error) && error.Payload.SequenceEqual(new byte[] { 0, 21 })
                && !cancelled.TryTakeCancelledLootPickup(a, out _),
            "a routed winner leaving wakes the original picker with exactly one native failure completion");

        cancelled.AddMember(new(b, 9, 1, "b", _ => ValueTask.CompletedTask));
        cancelled.TrySetPartyMemberAlive(a, false, DateTimeOffset.UtcNow);
        check(cancelled.TryBeginLootPickup(a, drop, false, candidates, _ => 0, out var direct)
                && direct.Outcome == LootPickupOutcome.Granted && direct.WinnerCharacterId == a,
            "a dead picker is not a dice candidate, but the official direct-pickup entry is not blanket rejected");
        cancelled.ReleaseLootPickup(drop, direct?.PickupId ?? Guid.Empty);
    }
}
