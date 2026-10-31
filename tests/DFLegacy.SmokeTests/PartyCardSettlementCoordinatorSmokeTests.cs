using DFLegacy.Server;

internal static class PartyCardSettlementCoordinatorSmokeTests
{
    private static readonly Dictionary<Guid, byte> Slots = [];

    private static bool Report(DungeonInstance instance, Guid characterId, PartyCardType type, byte column, long generation, DungeonClearRewardRoll roll, DungeonRewardLedgerStatus status)
    {
        // Legacy model transition tests call the private reducer via reflection; production cannot supply a roll.
        var method = typeof(DungeonInstance).GetMethod("ReportPartyCardCommitFromAgent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var content = new PartyCardContent(type, column, Slots[characterId], roll, PartyCardCommitStatus.Pending);
        return (bool)method.Invoke(instance, [instance.PartyCardSettlementId, characterId, type, column, generation, content, new PartyCardCommitOutcome(status, null)])!;
    }

    public static void Run(Action<bool, string> check)
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        Slots[a] = 0; Slots[b] = 1;
        var instance = new DungeonInstance(1, Guid.Empty, null!, null!);
        instance.AddMember(new(a, 1, 0, "A", _ => ValueTask.CompletedTask));
        instance.AddMember(new(b, 2, 1, "B", _ => ValueTask.CompletedTask));
        var roll = new DungeonClearRewardRoll(10, true, 50, 100, 101, 102);
        var generation = instance.BeginPartyCardSettlement();
        instance.CompletePartyCardSelection(generation);
        check(!instance.TryTakePartyCardPublication(generation, out _), "pending owners block publication");
        check(Report(instance, a, PartyCardType.Free, 0, generation, roll, DungeonRewardLedgerStatus.Committed), "first owner content committed");
        check(!Report(instance, a, PartyCardType.Free, 1, generation, roll, DungeonRewardLedgerStatus.Committed), "wrong owner content rejected");
        check(!Report(instance, a, PartyCardType.Free, 0, generation, roll, DungeonRewardLedgerStatus.Committed), "duplicate content report idempotent");
        check(Report(instance, b, PartyCardType.Free, 1, generation, roll, DungeonRewardLedgerStatus.Unknown), "unknown retains ownership");
        check(!Report(instance, b, PartyCardType.Free, 1, generation, roll, DungeonRewardLedgerStatus.Committed), "unknown cannot be overwritten by retry");
        check(instance.TryTakePartyCardPublication(generation, out var unknownSnapshot)
            && unknownSnapshot!.Selection.FreeOwners.SequenceEqual(new byte[] { 0, 255, 255, 255 }), "Unknown masks only its column while known owner publishes");
        var next = instance.BeginPartyCardSettlement();
        check(!Report(instance, a, PartyCardType.Free, 0, generation, roll, DungeonRewardLedgerStatus.Committed), "stale content cannot affect next generation");
        check(!instance.TryTakePartyCardPublication(generation, out _), "stale generation cannot publish");
        check(instance.TryReservePartyCard(a, PartyCardType.Free, 3, next).Accepted, "manual reservation retained");
        check(instance.TryReservePartyCard(a, PartyCardType.Paid, 2, next).Accepted, "paid reservation accepted");
        check(Report(instance, a, PartyCardType.Paid, 2, next, roll, DungeonRewardLedgerStatus.Committed), "paid content joins shared table");
        instance.CompletePartyCardSelection(next);
        check(Report(instance, a, PartyCardType.Free, 3, next, roll, DungeonRewardLedgerStatus.Committed), "manual owner content committed");
        instance.RemoveMember(a);
        check(instance.TryTakePartyCardPublication(next, out var leavingSnapshot)
            && leavingSnapshot!.Selection.FreeOwners[0] == 255 && leavingSnapshot.Selection.FreeOwners[3] == 0, "leave preserves committed columns and masks pending member");
        check(Report(instance, b, PartyCardType.Free, 0, next, roll, DungeonRewardLedgerStatus.Rejected), "remaining member deterministic terminal report");
        check(instance.TryTakePartyCardPublication(next, out var snapshot), "all known terminal owners publish");
        check(snapshot is not null && snapshot.Generation == next, "publication is correct generation");
        check(!instance.TryTakePartyCardPublication(next, out _), "publication once");
        instance.AddMember(new(a, 1, 0, "A", _ => ValueTask.CompletedTask));
        var lifecycle = instance.BeginPartyCardSettlement();
        instance.TryReservePartyCard(a, PartyCardType.Paid, 0, lifecycle);
        instance.CompletePartyCardSelection(lifecycle);
        Report(instance, a, PartyCardType.Free, 0, lifecycle, roll, DungeonRewardLedgerStatus.Committed);
        Report(instance, b, PartyCardType.Free, 1, lifecycle, roll, DungeonRewardLedgerStatus.Committed);
        check(instance.TryTakePartyCardPublication(lifecycle, out var paidPendingSnapshot)
            && paidPendingSnapshot!.Selection.PaidOwners[0] == 255, "pending paid column is masked while free columns publish");
        instance.StopRun();
        check(!Report(instance, a, PartyCardType.Paid, 0, lifecycle, roll, DungeonRewardLedgerStatus.Committed), "StopRun invalidates content report");
        check(!instance.TryTakePartyCardPublication(lifecycle, out _), "StopRun invalidates publication");
    }
}
