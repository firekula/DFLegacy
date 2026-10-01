using DFLegacy.Server;

internal static class PartyCardSelectionSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        var instance = new DungeonInstance(1, Guid.Empty, null!, null!);
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        instance.AddMember(new DungeonInstanceMember(a, 1, 2, "A", _ => ValueTask.CompletedTask));
        instance.AddMember(new DungeonInstanceMember(b, 2, 0, "B", _ => ValueTask.CompletedTask));
        instance.AddMember(new DungeonInstanceMember(c, 3, 3, "C", _ => ValueTask.CompletedTask));
        var generation = instance.BeginPartyCardSettlement();
        check(instance.TryReservePartyCard(a, PartyCardType.Free, 0, generation).Status == PartyCardSelectionStatus.Reserved, "manual reservation accepted");
        check(instance.TryReservePartyCard(a, PartyCardType.Free, 0, generation).Status == PartyCardSelectionStatus.AlreadyReserved, "duplicate reservation idempotent");
        check(instance.TryReservePartyCard(b, PartyCardType.Free, 0, generation).Status == PartyCardSelectionStatus.Rejected, "column uniqueness enforced");
        check(instance.CompletePartyCardSelection(generation).Status == PartyCardSelectionStatus.Reserved, "auto completion runs");
        check(instance.CompletePartyCardSelection(generation).Status == PartyCardSelectionStatus.AlreadyReserved, "auto completion once per generation");
        var snapshot = instance.SnapshotPartyCards(); snapshot.FreeOwners[0] = 99;
        check(instance.SnapshotPartyCards().FreeOwners[0] != 99, "snapshot ownership is copied");
        check(instance.TryReservePartyCard(b, PartyCardType.Free, 1, generation + 1).Status == PartyCardSelectionStatus.Rejected, "stale generation rejected");
        check(instance.IsParty, "party instance remains isolated");
    }
}
