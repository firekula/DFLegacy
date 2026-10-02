using DFLegacy.Server;

internal static class PartyCardSettlementCoordinatorSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var instance = new DungeonInstance(1, Guid.Empty, null!, null!);
        instance.AddMember(new(a, 1, 0, "A", _ => ValueTask.CompletedTask));
        instance.AddMember(new(b, 2, 1, "B", _ => ValueTask.CompletedTask));
        var roll = new DungeonClearRewardRoll(10, true, 50, 100, 101, 102);
        var generation = instance.BeginPartyCardSettlement();
        instance.CompletePartyCardSelection(generation);
        check(!instance.TryTakePartyCardPublication(generation, out _), "pending owners block publication");
        check(instance.ReportPartyCardCommit(a, PartyCardType.Free, 0, generation, roll, DungeonRewardLedgerStatus.Committed), "first owner content committed");
        check(!instance.ReportPartyCardCommit(a, PartyCardType.Free, 1, generation, roll, DungeonRewardLedgerStatus.Committed), "wrong owner content rejected");
        check(!instance.ReportPartyCardCommit(a, PartyCardType.Free, 0, generation, roll, DungeonRewardLedgerStatus.Committed), "duplicate content report idempotent");
        check(instance.ReportPartyCardCommit(b, PartyCardType.Free, 1, generation, roll, DungeonRewardLedgerStatus.Unknown), "unknown retains ownership");
        check(!instance.ReportPartyCardCommit(b, PartyCardType.Free, 1, generation, roll, DungeonRewardLedgerStatus.Committed), "unknown cannot be overwritten by retry");
        check(instance.TryTakePartyCardPublication(generation, out var unknownSnapshot)
            && unknownSnapshot!.Selection.FreeOwners.SequenceEqual(new byte[] { 0, 255, 255, 255 }), "Unknown masks only its column while known owner publishes");
        var next = instance.BeginPartyCardSettlement();
        check(!instance.ReportPartyCardCommit(a, PartyCardType.Free, 0, generation, roll, DungeonRewardLedgerStatus.Committed), "stale content cannot affect next generation");
        check(!instance.TryTakePartyCardPublication(generation, out _), "stale generation cannot publish");
        check(instance.TryReservePartyCard(a, PartyCardType.Free, 3, next).Accepted, "manual reservation retained");
        check(instance.TryReservePartyCard(a, PartyCardType.Paid, 2, next).Accepted, "paid reservation accepted");
        check(instance.ReportPartyCardCommit(a, PartyCardType.Paid, 2, next, roll, DungeonRewardLedgerStatus.Committed), "paid content joins shared table");
        instance.CompletePartyCardSelection(next);
        check(instance.ReportPartyCardCommit(a, PartyCardType.Free, 3, next, roll, DungeonRewardLedgerStatus.Committed), "manual owner content committed");
        instance.RemoveMember(a);
        check(instance.TryTakePartyCardPublication(next, out var leavingSnapshot)
            && leavingSnapshot!.Selection.FreeOwners[0] == 255 && leavingSnapshot.Selection.FreeOwners[3] == 0, "leave preserves committed columns and masks pending member");
        check(instance.ReportPartyCardCommit(b, PartyCardType.Free, 0, next, roll, DungeonRewardLedgerStatus.Rejected), "remaining member deterministic terminal report");
        check(instance.TryTakePartyCardPublication(next, out var snapshot), "all known terminal owners publish");
        check(snapshot is not null && snapshot.Generation == next, "publication is correct generation");
        check(!instance.TryTakePartyCardPublication(next, out _), "publication once");
        instance.AddMember(new(a, 1, 0, "A", _ => ValueTask.CompletedTask));
        var lifecycle = instance.BeginPartyCardSettlement();
        instance.TryReservePartyCard(a, PartyCardType.Paid, 0, lifecycle);
        instance.CompletePartyCardSelection(lifecycle);
        instance.ReportPartyCardCommit(a, PartyCardType.Free, 0, lifecycle, roll, DungeonRewardLedgerStatus.Committed);
        instance.ReportPartyCardCommit(b, PartyCardType.Free, 1, lifecycle, roll, DungeonRewardLedgerStatus.Committed);
        check(instance.TryTakePartyCardPublication(lifecycle, out var paidPendingSnapshot)
            && paidPendingSnapshot!.Selection.PaidOwners[0] == 255, "pending paid column is masked while free columns publish");
        instance.StopRun();
        check(!instance.ReportPartyCardCommit(a, PartyCardType.Paid, 0, lifecycle, roll, DungeonRewardLedgerStatus.Committed), "StopRun invalidates content report");
        check(!instance.TryTakePartyCardPublication(lifecycle, out _), "StopRun invalidates publication");
    }
}
