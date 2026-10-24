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
            [new(5, 1, 3, 2, 0, 353, 264), new(6, 2, 4, 3, 3, 400, 200)]);
        var wire = DungeonDebugOverlayWire.Encode(snapshot, 77, DateTime.UtcNow.ToFileTimeUtc());
        check(wire.Length == 64 && BinaryPrimitives.ReadUInt32LittleEndian(wire) == 0x31474244
            && BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(24)) == 77
            && BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(38)) == 2
            && BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(48)) == 353,
            "local diagnostic wire matches the standalone DLL ABI");
        var fixturePath = Environment.GetEnvironmentVariable("DFLEGACY_DEBUG_TEST_FIXTURE");
        if (!string.IsNullOrEmpty(fixturePath)) File.WriteAllBytes(fixturePath, wire);
        TestPublisherAsync(fixture, check).GetAwaiter().GetResult();
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
        instance.AddMember(fixture.Member(character, 0, "debug"));
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
