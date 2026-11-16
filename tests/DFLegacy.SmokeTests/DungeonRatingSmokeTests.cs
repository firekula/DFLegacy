using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class DungeonRatingSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = new DungeonFixture();
        var requester = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var packets = new List<GameServerPacket>();
        ValueTask SendSelf(GameServerPacket packet)
        {
            packets.Add(packet);
            return ValueTask.CompletedTask;
        }
        ValueTask SendPeer(GameServerPacket packet) => throw new InvalidOperationException("Unexpected peer packet.");

        var run = fixture.CreatePartyInstance(2003);
        run.AddMember(new(requester, 0x1234, 0, "requester", SendSelf));
        run.AddMember(new(peer, 0x2345, 1, "peer", SendPeer));

        ValueTask<bool> TryCommand(string text = "赐我评分", byte type = 2,
            bool alive = true, bool pvp = false, bool chapterTwo = true) =>
            DungeonRatingCommand.TrySendAsync(type, text, run, requester, 0x1234, alive, pvp, SendSelf, chapterTwo);

        check(!await TryCommand() && packets.Count == 0, "rating command requires an active dungeon");
        check(run.TryStartRun(fixture.Layout, 0, 0, out _), "rating fixture starts a regular party run");
        check(!await TryCommand(" 赐我评分") && !await TryCommand(type: 16)
            && !await TryCommand(alive: false) && !await TryCommand(pvp: true)
            && !await TryCommand(chapterTwo: false) && packets.Count == 0,
            "rating requires exact supported in-dungeon chat and a living Chapter Two PvE player");

        var request = Convert.FromHexString("34000201000000000008000000B4CDCED2C6C0B7D6");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        check(EntranceService.TryReadSendMessage(request, out var parsed), "native rating chat request parses");
        var text = Encoding.GetEncoding(936).GetString(parsed.MessageBytes);
        check(await TryCommand(text, parsed.MessageType) && run.HasLockedSssRating(requester)
            && !run.HasLockedSssRating(peer), "the original chat request forces only the command issuer's rank");
        var expected = GameProtocolEngine.CreateMessageNotification(
            GameMessageType.Type16Brown, 0x1234, "@DFLegacy:dungeon-rating:sss:v1@"u8);
        check(packets is [var marker] && marker.Type == 0 && marker.ProtocolId == 12
            && ChapterTwoPacketProjection.Apply(marker).Payload.SequenceEqual(expected.Payload),
            "the Chapter Two client receives the exact private dungeon rank marker");
        check(await TryCommand(type: 3) && packets.Count == 2,
            "nearby chat and repeated commands preserve the same override");

        check(run.TryMoveRoom(1, 0, out _) && run.HasLockedSssRating(requester),
            "the rank override persists across rooms in the same run");
        var scoreBody = new byte[24];
        scoreBody[22] = DungeonRatingCommand.SssResultCode;
        check(DungeonClearScore.TryParse(scoreBody, out var score) && score.CalculatedResultCode == 0,
            "a client-supplied SSS grade does not change server-calculated rank on its own");
        check(DungeonRatingCommand.ResolveResultCode(run, requester, score, true, out var locked) == 105 && locked
            && DungeonRatingCommand.ResolveResultCode(run, peer, score, true, out _) == 0
            && DungeonRatingCommand.ResolveResultCode(run, requester, score, false, out _) == 0
            && DungeonRatingCommand.ResolveResultCode(run, requester, null, true, out _) == 0,
            "only an authorized member's valid Chapter Two settlement receives SSS");
        var result = GameProtocolEngine.CreateChapterTwoPlayResult(DungeonRatingCommand.SssResultCode);
        check(result.Type == 0 && result.ProtocolId == 34 && result.Payload is [105, 0, 0, 0, 0],
            "Chapter Two forced result carries native rank as the first byte");

        run.RemoveMember(requester);
        run.AddMember(new(requester, 0x1234, 0, "requester", SendSelf));
        check(!run.HasLockedSssRating(requester), "leaving discards the member's override");
        run.StopRun();
        check(!run.HasLockedSssRating(requester), "stopped runs have no rating override");
        check(run.TryStartRun(fixture.Layout, 0, 0, out _) && await TryCommand(),
            "a later run can set a new override");
        run.StopRun();
        check(run.TryStartRun(fixture.Layout, 0, 0, out _) && !run.HasLockedSssRating(requester),
            "a new run starts at its ordinary rating");
        run.EnterDetachedRoom(DungeonFixture.Room([]));
        check(!await TryCommand() && !run.HasLockedSssRating(requester),
            "detached tower and tutorial rooms decline regular dungeon rank commands");
    }
}
