using System.Buffers.Binary;
using DFLegacy.Server;

internal static class DungeonDebugOverlaySmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        using var fixture = new DungeonFixture();
        var run = new DungeonRunState(fixture.Catalog);
        check(run.TryStart(fixture.Layout, 0, 0, out var start, fixture.Random), "debug fixture starts");
        var originalMap = start.Room.MapId;
        var preview = run.PrepareBossRoomForDebug(out var defeated, out var destroyed, fixture.Random);
        check(preview is not null && run.RoomX == fixture.Layout.StartX && run.RoomY == fixture.Layout.StartY
            && run.CurrentRoom?.MapId == originalMap && defeated.Length == 0 && destroyed.Length == 0,
            "preparing the boss encounter does not move, visit, defeat or clear it");
        check(ReferenceEquals(preview, run.PrepareBossRoomForDebug(out _, out _, new RejectRandom())),
            "repeated debug reads reuse the encounter without rerolling");
        DungeonRoomTransition? entered = null;
        while (run.RoomX < fixture.Layout.BossX)
        {
            var nextX = (byte)(run.RoomX + 1);
            check(run.TryMoveRoom(nextX, 0, out entered,
                nextX == fixture.Layout.BossX ? new RejectRandom() : fixture.Random), "move to prepared boss");
        }
        check(entered is { FirstVisit: true, Revisit: false, ReuseClientRoomState: false }
            && entered.Room.MapId == preview?.MapId
            && entered.Room.ClientRandomSeed == preview?.ClientRandomSeed
            && entered.Room.Monsters.SequenceEqual(preview!.Monsters),
            "first real entry uses the exact preview roster/seed while preserving first-visit semantics");
        run.Stop();
        check(run.PrepareBossRoomForDebug(out _, out _) is null, "stopped runs expose no old boss encounter");

        var snapshot = new DungeonDebugOverlaySnapshot(123, [77], 2, 101, 117, 0, 0, 3, 1, false, false,
            [new(5, 1, 3, 2, 0, 353, 264), new(6, 2, 4, 3, 3, 400, 200)],
            true, new() { [77] = new(true, DungeonDebugDropResult.Applied, 123) });
        var wire = DungeonDebugOverlayWire.Encode(snapshot, 77, DateTime.UtcNow.ToFileTimeUtc());
        check(wire.Length == 76 && BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(4)) == 2
            && BinaryPrimitives.ReadUInt32LittleEndian(wire) == 0x31474244
            && BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(24)) == 77
            && BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(38)) == 2
            && BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(48)) == 353,
            "local diagnostic v2 wire retains the roster and adds acknowledged drop-test state");
        var fixturePath = Environment.GetEnvironmentVariable("DFLEGACY_DEBUG_TEST_FIXTURE");
        if (!string.IsNullOrEmpty(fixturePath)) File.WriteAllBytes(fixturePath, wire);
        TestDropControl(fixture, check);
        TestPublisherAsync(fixture, check).GetAwaiter().GetResult();
    }

    private static void TestDropControl(DungeonFixture fixture, Action<bool, string> check)
    {
        var owner = Guid.NewGuid();
        var instance = fixture.CreateSoloInstance(owner);
        instance.AddMember(fixture.Member(owner, 0, "test") with { AccountName = "test" });
        var peer = fixture.Member(Guid.NewGuid(), 1, "ordinary") with { AreaUserId = 2, AccountName = "ordinary" };
        instance.AddMember(peer);
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _, fixture.Random), "drop control run starts");
        var before = instance.CaptureDebugOverlay()!;
        var now = DateTime.UtcNow.ToFileTimeUtc();
        var request = new DungeonDebugDropRequest(now, before.RunId, 1, true);
        var wire = request.Encode();
        check(wire.Length == 32 && DungeonDebugDropRequest.TryDecode(wire, out var decoded) && decoded == request,
            "drop control request has a fixed cross-language wire format");
        check(!DungeonDebugDropRequest.TryDecode(wire[..^1], out _)
            && !DungeonDebugDropRequest.TryDecode([.. wire, 0], out _), "truncated/oversized commands rejected");
        wire[30] = 2;
        check(!DungeonDebugDropRequest.TryDecode(wire, out _), "non-boolean drop request rejected");
        check(instance.ApplyDebugDropRequest(request, now) == DungeonDebugDropResult.Applied && instance.DropDebugAll,
            "test account explicitly enables the current instance's real drop-all state");
        check(instance.ApplyDebugDropRequest(request, now) == DungeonDebugDropResult.Applied && instance.DropDebugAll,
            "duplicate enable is idempotent");
        var off = request with { RequestId = now + 1, Enabled = false };
        instance.ApplyDebugDropRequest(off, now + 1);
        instance.ApplyDebugDropRequest(request, now + 1);
        check(!instance.DropDebugAll, "older command cannot overwrite newer requested state");
        var result = instance.ApplyDebugDropRequest(request with { RequestId = now + 2, UserId = peer.AreaUserId }, now + 2);
        check(result == DungeonDebugDropResult.Denied && !instance.DropDebugAll, "ordinary teammate cannot change drop-all");
        result = instance.ApplyDebugDropRequest(request with { RequestId = now + 3, RunId = before.RunId + 1 }, now + 3);
        check(result == DungeonDebugDropResult.Stale && !instance.DropDebugAll, "different run cannot change state");
        result = instance.ApplyDebugDropRequest(request with { RequestId = now - 60_000_000 }, now);
        check(result == DungeonDebugDropResult.Stale && !instance.DropDebugAll, "expired request cannot change state");
        var state = instance.CaptureDebugOverlay()!;
        var view = DungeonDebugOverlayWire.Encode(state, 1, now);
        var trailer = view.AsSpan(view.Length - 12);
        check(trailer[0] == 0 && trailer[1] == 1, "snapshot exposes actual shared state and test-user permission");
        view = DungeonDebugOverlayWire.Encode(state, peer.AreaUserId, now);
        check(view[^11] == 0, "ordinary peer sees disabled control permission");
        instance.ApplyDebugDropRequest(request with { RequestId = now + 1_000_000_000 }, now + 4);
        instance.ApplyDebugDropRequest(request with { RequestId = now + 5 }, now + 5);
        check(instance.DropDebugAll, "rejected future request cannot poison the ordering of valid controls");
        instance.ApplyDebugDropRequest(off, now + 5);
        check(instance.DropDebugAll, "reply to rejected requests cannot permit replay older than the last applied intent");
        instance.ApplyDebugDropRequest(request with { RequestId = long.MaxValue }, now + 6);
        instance.ApplyDebugDropRequest(request with { RequestId = now + 5 }, now + 6);
        var retryView = DungeonDebugOverlayWire.Encode(instance.CaptureDebugOverlay()!, 1, now + 6);
        check(BinaryPrimitives.ReadInt64LittleEndian(retryView.AsSpan(retryView.Length - 8)) == now + 5
            && retryView[^10] == 1, "retry of the last applied command restores its acknowledgment after an unrelated rejection");
        instance.StopRun();
        check(instance.ApplyDebugDropRequest(off, now) == DungeonDebugDropResult.Stale,
            "stopped run rejects controls");
    }

    private sealed class RejectRandom : IDropRandomSource
    {
        public int Next(int exclusiveMaximum) => throw new InvalidOperationException("prepared room rerolled");
    }

    private static async Task TestPublisherAsync(DungeonFixture fixture, Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfdebug-publisher-{Guid.NewGuid():N}");
        var registry = new DungeonInstanceRegistry();
        var character = Guid.NewGuid();
        var instance = fixture.CreateSoloInstance(character);
        instance.AddMember(fixture.Member(character, 0, "debug") with { AccountName = "test" });
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _, fixture.Random), "publisher fixture starts");
        registry.BindCharacter(character, instance);
        var options = new ServerOptions();
        options.Debug.ClientOverlay = new() { Enabled = true, SnapshotDirectory = directory };
        using var service = new DungeonDebugOverlayService(options, registry,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DungeonDebugOverlayService>.Instance);
        var path = Path.Combine(directory, "dfdebug-1.bin");
        try
        {
            await service.StartAsync(CancellationToken.None);
            for (var i = 0; i < 30 && !File.Exists(path); ++i) await Task.Delay(50);
            check(File.Exists(path), "publisher produces an entry-time boss diagnostic file");
            if (!File.Exists(path)) return;
            var original = File.GetLastWriteTimeUtc(path);
            using (var busyReader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                await Task.Delay(600);
            for (var i = 0; i < 30 && File.GetLastWriteTimeUtc(path) == original; ++i) await Task.Delay(50);
            check(File.GetLastWriteTimeUtc(path) > original,
                "publisher recovers from a reader temporarily preventing atomic replacement");
            var commandPath = Path.Combine(directory, "dfdebug-drop-1.bin");
            var control = new DungeonDebugDropRequest(DateTime.UtcNow.ToFileTimeUtc(), instance.CaptureDebugOverlay()!.RunId, 1, true);
            await File.WriteAllBytesAsync(commandPath + ".tmp", control.Encode());
            File.Move(commandPath + ".tmp", commandPath);
            for (var i = 0; i < 30 && !instance.DropDebugAll; ++i) await Task.Delay(50);
            check(instance.DropDebugAll && !File.Exists(commandPath), "publisher consumes an atomic control command and applies real drop state");
            for (var i = 0; i < 30; ++i)
            {
                var state = await File.ReadAllBytesAsync(path);
                if (BinaryPrimitives.ReadInt64LittleEndian(state.AsSpan(state.Length - 8)) == control.RequestId) break;
                await Task.Delay(50);
            }
            var published = await File.ReadAllBytesAsync(path);
            check(published[^12] == 1 && published[^10] == 1
                && BinaryPrimitives.ReadInt64LittleEndian(published.AsSpan(published.Length - 8)) == control.RequestId,
                "published state acknowledges the exact request only after application");
            await File.WriteAllBytesAsync(commandPath, new byte[33]);
            await Task.Delay(600);
            check(!File.Exists(commandPath) && instance.DropDebugAll, "malformed control is consumed without changing state or stopping publisher");
            instance.StopRun();
            for (var i = 0; i < 30 && File.Exists(path); ++i) await Task.Delay(50);
            check(!File.Exists(path), "leaving a dungeon removes the old published snapshot");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            File.Delete(path);
            File.Delete(path + ".tmp");
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}
