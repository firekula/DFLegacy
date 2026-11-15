using System.Buffers.Binary;
using DFLegacy.Protocol;

internal static class ChapterTwoPvpQuickJoinSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        await using var fixture = await PvpIndividualSmokeTests.Fixture.Create();
        await fixture.Attach(2, 55);
        await fixture.Command(0, GameProtocolEngine.MakePvpRoomCommand, [1, 3, 0, 0]);
        var room = fixture.RoomId(0);
        fixture.Clear();

        await Request(2, 0);
        await Request(1, 1);
        await Request(1, 2);
        await Request(1, 0, nonzeroPadding: true);
        check(fixture.Packets[2].Single(IsQuickJoinReply).Payload is [0, _]
            && fixture.Packets[1].Where(IsQuickJoinReply).Count(packet => packet.Payload is [0, _]) == 3
            && !fixture.Service.IsBusy(fixture.Characters[1].Id)
            && !fixture.Service.IsBusy(fixture.Characters[2].Id),
            "Chapter Two PvP quick join rejects another channel, other modes, and invalid padding");

        fixture.Clear();
        await Request(1, 0);
        var packets = fixture.Packets[1];
        var membership = packets.FindIndex(packet => packet is { Type: 0, ProtocolId: 43 }
            && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload) == room);
        var acknowledgement = packets.FindIndex(IsQuickJoinReply);
        check(membership > 0 && acknowledgement > membership && packets[acknowledgement].Payload is [1]
            && packets.Take(membership).Any(packet => packet is { Type: 0, ProtocolId: 11 })
            && packets.All(packet => packet is not { Type: 1, ProtocolId: GameProtocolEngine.EnterPvpRoomCommand })
            && fixture.Service.IsBusy(fixture.Characters[1].Id),
            "Chapter Two ordinary quick join publishes peers and seat membership before its native ACK");

        Task Request(int entrant, byte mode, bool nonzeroPadding = false)
        {
            var body = new byte[18];
            BinaryPrimitives.WriteUInt16LittleEndian(body, ++fixture.Sequences[entrant]);
            body[2] = mode;
            if (nonzeroPadding) body[^1] = 1;
            return fixture.Service.HandleAsync(fixture.Characters[entrant].Id, fixture.Attachments[entrant],
                GameProtocolEngine.PvpQuickJoinCommand, body, chapterTwo: true);
        }
    }

    private static bool IsQuickJoinReply(GameServerPacket packet) =>
        packet is { Type: GameProtocolEngine.CommandPacketType, ProtocolId: GameProtocolEngine.PvpQuickJoinCommand };
}
