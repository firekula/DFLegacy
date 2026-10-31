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
        var snapshot = instance.SnapshotPartyCards();
        check(snapshot.FreeOwners.SequenceEqual(new byte[] { 2, 0, 3, 255 }), "auto completion assigns each available slot once in column order");
        snapshot.FreeOwners[0] = 99;
        check(instance.SnapshotPartyCards().FreeOwners[0] != 99, "snapshot ownership is copied");
        check(instance.TryReservePartyCard(b, PartyCardType.Free, 1, generation + 1).Status == PartyCardSelectionStatus.Rejected, "stale generation rejected");
        var packetA = DFLegacy.Protocol.GameProtocolEngine.CreateSelectCardReply(new byte[] { 2, 0, 255, 255 }, new byte[] { 255, 255, 255, 255 }, new ushort?[] { null, null, null, null });
        var packetB = DFLegacy.Protocol.GameProtocolEngine.CreateSelectCardReply(new byte[] { 2, 0, 255, 255 }, new byte[] { 255, 255, 255, 255 }, new ushort?[] { null, null, null, null });
        check(packetA.Payload.SequenceEqual(packetB.Payload), "shared CMD74 bytes identical");
        check(packetA.Payload.Length == 13, "shared CMD74 uses four fixed columns");
        var paidAssigned = DFLegacy.Protocol.GameProtocolEngine.CreateSelectCardReply(new byte[] { 255, 255, 255, 255 }, new byte[] { 0, 255, 255, 255 }, new ushort?[] { null, null, null, null });
        check(paidAssigned.Payload[3] == 1, "assigned paid column emits one empty reward record");
        check(instance.IsParty, "party instance remains isolated");
        check(instance.TryReservePartyCard(a, PartyCardType.Free, 0, generation).Status == PartyCardSelectionStatus.AlreadyReserved, "auto completion preserves existing owner retry");
        check(instance.TryReservePartyCard(a, PartyCardType.Free, 3, generation).Status == PartyCardSelectionStatus.Rejected, "auto completion closes remaining free columns");
        check(instance.TryReservePartyCard(a, PartyCardType.Paid, 3, generation).Status == PartyCardSelectionStatus.Reserved, "paid reservations remain open after free auto completion");
        var g = instance.BeginPartyCardSettlement();
        check(instance.TryReservePartyCard(a, PartyCardType.Paid, 2, g).Accepted, "paid reservation accepted");
        check(!instance.TryReservePartyCard(b, PartyCardType.Paid, 2, g).Accepted, "paid column unique");
        check(!instance.TryReservePartyCard(a, PartyCardType.Paid, 1, g).Accepted, "paid member unique");
        check(!instance.TryReservePartyCard(a, (PartyCardType)8, 0, g).Accepted, "invalid type rejected");
        check(!instance.TryReservePartyCard(a, PartyCardType.Free, 4, g).Accepted, "invalid column rejected");
        check(!instance.TryReservePartyCard(Guid.NewGuid(), PartyCardType.Free, 0, g).Accepted, "absent member rejected");
        check(instance.SnapshotPartyCards().MemberSlots.SequenceEqual(new byte[] { 0, 2, 3 }), "slot holes preserved");
        instance.TryReservePartyCard(a, PartyCardType.Free, 3, g);
        instance.RemoveMember(a); instance.RemoveMember(b);
        instance.CompletePartyCardSelection(g);
        check(instance.SnapshotPartyCards().FreeOwners.SequenceEqual(new byte[] { 3, 255, 255, 2 }), "departed members excluded and reserved ownership retained");
        var duplicate = new DungeonInstance(2, Guid.Empty, null!, null!);
        duplicate.AddMember(new DungeonInstanceMember(a, 1, 2, "A", _ => ValueTask.CompletedTask));
        duplicate.AddMember(new DungeonInstanceMember(b, 2, 2, "B", _ => ValueTask.CompletedTask));
        var dg = duplicate.BeginPartyCardSettlement();
        check(!duplicate.TryReservePartyCard(a, PartyCardType.Free, 0, dg).Accepted && !duplicate.TryReservePartyCard(b, PartyCardType.Free, 0, dg).Accepted, "duplicate slots excluded");
        var solo = new DungeonInstance(0, a, null!, null!);
        check(solo.BeginPartyCardSettlement() == 0 && !solo.CompletePartyCardSelection(0).Accepted, "solo card model remains inactive");
        var beforeStop = instance.BeginPartyCardSettlement(); instance.StopRun();
        check(!instance.CompletePartyCardSelection(beforeStop).Accepted, "StopRun invalidates generation");
        var beforeRoom = instance.BeginPartyCardSettlement(); instance.EnterDetachedRoom(DungeonFixture.Room([]));
        check(!instance.CompletePartyCardSelection(beforeRoom).Accepted, "EnterDetachedRoom invalidates generation");
        using var fixture = new DungeonFixture();
        var running = fixture.CreatePartyInstance(9);
        var preStartGeneration = running.BeginPartyCardSettlement();
        check(running.TryStartRun(fixture.Layout, 0, 0, out _), "StartRun succeeds for lifecycle test");
        check(!running.CompletePartyCardSelection(preStartGeneration).Accepted, "StartRun invalidates generation");
        var startGeneration = running.BeginPartyCardSettlement();
        check(running.TryMoveRoom(1, 0, out _), "TryMoveRoom succeeds for lifecycle test");
        check(!running.CompletePartyCardSelection(startGeneration).Accepted, "TryMoveRoom invalidates generation");
        check(!running.TryTakePartyCardPublication(preStartGeneration, out _), "StartRun invalidates content publication");
        check(!running.TryTakePartyCardPublication(startGeneration, out _), "TryMoveRoom invalidates content publication");
        check(!instance.TryTakePartyCardPublication(beforeRoom, out _), "EnterDetachedRoom invalidates content publication");
        check(!instance.TryTakePartyCardPublication(beforeStop, out _), "StopRun invalidates content publication");
    }
}
