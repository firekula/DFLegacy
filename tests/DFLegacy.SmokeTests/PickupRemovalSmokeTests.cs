using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PickupRemovalSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = new DungeonFixture();
        await using var harness = await PartyHarness.CreateAsync();
        using var service = new DungeonPartyService(
            harness.Service, harness.Directory, new DungeonInstanceRegistry(),
            TimeProvider.System, NullLogger<DungeonPartyService>.Instance);
        var instance = fixture.CreatePartyInstance(2300);
        var picker = Guid.NewGuid();
        var winner = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var inboxes = new Dictionary<Guid, List<GameServerPacket>>
        {
            [picker] = [], [winner] = [], [peer] = []
        };
        foreach (var (id, uid, slot) in new[] { (picker, (ushort)7, (byte)0), (winner, (ushort)9, (byte)1), (peer, (ushort)12, (byte)2) })
        {
            instance.AddMember(new DungeonInstanceMember(id, uid, slot, "拾取测试", packet =>
            {
                inboxes[id].Add(packet);
                return ValueTask.CompletedTask;
            }));
        }
        var soloCalls = 0;
        ValueTask SendToSolo(GameServerPacket packet)
        {
            soloCalls++;
            return ValueTask.CompletedTask;
        }

        // The actual successful-pickup production entry builds and broadcasts
        // the removal. The requester can differ from the routed item winner.
        await service.BroadcastPickupRemovalAsync(instance, 401, 7, 3171, SendToSolo);
        check(soloCalls == 0 && inboxes.Values.All(inbox => inbox.Count == 1),
            "the picker, routed winner and other peers each receive exactly one successful-pickup removal");
        foreach (var inbox in inboxes.Values)
        {
            var packet = inbox[0];
            check(packet.ProtocolId == GameProtocolEngine.PickupRemovedNotification
                    && packet.Payload.Length == 9
                    && packet.Payload[0] == 5
                    && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(1, 2)) == 7
                    && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(3, 4)) == 3171
                    && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(7, 2)) == 401,
                "the removal preserves the actual requester/item prefix and appends the exact ground id for every recipient");
        }
        check(inboxes[picker][0].Payload.SequenceEqual(inboxes[winner][0].Payload)
                && inboxes[winner][0].Payload.SequenceEqual(inboxes[peer][0].Payload),
            "routed pickup removal never impersonates a recipient or rewrites the winner's inventory/currency notification");

        await service.BroadcastPickupRemovalAsync(instance, 402, 9, 3171, SendToSolo);
        check(inboxes.Values.All(inbox => inbox.Count == 2
                && BinaryPrimitives.ReadUInt16LittleEndian(inbox[0].Payload.AsSpan(7, 2)) == 401
                && BinaryPrimitives.ReadUInt16LittleEndian(inbox[1].Payload.AsSpan(7, 2)) == 402),
            "two drops with the same item id retain distinct ground ids instead of removing an arbitrary same-item drop");

        var solo = fixture.CreateSoloInstance(Guid.NewGuid());
        var soloInbox = new List<GameServerPacket>();
        await service.BroadcastPickupRemovalAsync(solo, 403, 19, 0, packet =>
        {
            soloInbox.Add(packet);
            return ValueTask.CompletedTask;
        });
        check(soloInbox.Count == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(soloInbox[0].Payload.AsSpan(7, 2)) == 403
                && BinaryPrimitives.ReadUInt32LittleEndian(soloInbox[0].Payload.AsSpan(3, 4)) == 0,
            "a solo gold pickup retains its normal session send path and exact ground identity");

        foreach (var inbox in inboxes.Values) inbox.Clear();
        await service.BroadcastInventoryItemPickupAsync(instance, 410, 7, 9, 12,
            new byte[] { 13, 81, 42, 0 }, SendToSolo);
        check(soloCalls == 0 && inboxes.Values.All(inbox => inbox.Count == 1),
            "all members receive the native dice results when a teammate wins the pickup");
        foreach (var inbox in inboxes.Values)
        {
            check(inbox[0].ProtocolId == 39 && inbox[0].Payload.SequenceEqual(
                    new byte[] { 0x9A, 1, 7, 0, 13, 81, 42, 0, 9, 0, 12, 0 }),
                "the dice result preserves the actual picker and distinct winner uid without changing the inventory slot");
        }
        await service.BroadcastPickupRemovalAsync(instance, 410, 7, 3171, SendToSolo);
        check(inboxes.Values.All(inbox => inbox.Select(packet => packet.ProtocolId).SequenceEqual(new byte[] { 39, 86 })),
            "every client receives its dice result while the ground object still exists, before the removal");
        foreach (var inbox in inboxes.Values) inbox.Clear();
        await service.BroadcastInventoryItemPickupAsync(instance, 411, 7, 7, 12,
            new byte[] { 0, 0, 0, 0 }, SendToSolo);
        check(soloCalls == 1 && inboxes.Values.All(inbox => inbox.Count == 0),
            "a direct pickup keeps its existing session notification without showing peer dice results");
        soloInbox.Clear();
        await service.BroadcastInventoryItemPickupAsync(solo, 412, 19, 19, 13, null, packet =>
        {
            soloInbox.Add(packet);
            return ValueTask.CompletedTask;
        });
        check(soloInbox.Count == 1 && soloInbox[0].Payload.SequenceEqual(
                new byte[] { 0x9C, 1, 19, 0, 0, 0, 0, 0, 19, 0, 13, 0 }),
            "a solo item pickup remains byte compatible");
    }
}
