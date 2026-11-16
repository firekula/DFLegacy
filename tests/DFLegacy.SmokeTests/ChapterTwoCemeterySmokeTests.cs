using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoCemeterySmokeTests
{
    private static readonly ushort[] Guardians = [50073, 50074, 50075, 50076];
    private static readonly (byte X, byte Y)[] Ring =
        [(1, 2), (0, 2), (0, 1), (0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (4, 1), (4, 2), (3, 2), (2, 2)];

    internal static void Run(string pvfPath, Action<bool, string> check)
    {
        var options = new ServerOptions { ScriptPvfPath = Path.GetFullPath(pvfPath), ChapterTwoRemote = true };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var catalog = new DungeonCatalog(scripts,
            new ObjectDropGenerator(options,
                new ObjectDropCatalog(scripts, NullLogger<ObjectDropCatalog>.Instance),
                new GoldDropCatalog(scripts, NullLogger<GoldDropCatalog>.Instance)),
            NullLogger<DungeonCatalog>.Instance);
        var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
        var directory = new TownSessionDirectory(options, TimeProvider.System, NullLogger<TownSessionDirectory>.Instance);
        using var parties = new PartyService(new JsonGameStore(options, NullLogger<JsonGameStore>.Instance),
            directory, TimeProvider.System, NullLogger<PartyService>.Instance);
        using var dungeonParties = new DungeonPartyService(parties, directory, new DungeonInstanceRegistry(),
            TimeProvider.System, NullLogger<DungeonPartyService>.Instance);

        VerifyRun(partyId: 0, coLocateGuardians: true);
        VerifyRun(partyId: 42, coLocateGuardians: false);
        for (byte difficulty = 0; difficulty <= DungeonDifficultyProgression.HighestDifficulty; difficulty++)
        {
            VerifyGuardiansAfterLeavingHell(difficulty, 1);
            VerifyGuardiansAfterLeavingHell(difficulty, 2);
        }
        VerifyShadowMazePlacements(catalog, check);

        void VerifyRun(ushort partyId, bool coLocateGuardians)
        {
            var runName = coLocateGuardians ? "solo" : "party";
            var instance = new DungeonInstance(partyId, Guid.NewGuid(), catalog, items);
            var received = new List<GameServerPacket>[partyId == 0 ? 1 : 3];
            for (var slot = 0; slot < received.Length; slot++)
            {
                var inbox = received[slot] = [];
                check(instance.TryRegisterMemberForEntry(new DungeonInstanceMember(Guid.NewGuid(),
                    (ushort)(slot + 1), (byte)slot, $"member-{slot}", _ => ValueTask.CompletedTask,
                    SendConditional: (packet, canWrite) =>
                    {
                        if (canWrite()) inbox.Add(packet);
                        return ValueTask.CompletedTask;
                    }, IsChapterTwo: true)), $"{runName}: member {slot} is registered");
            }
            var random = new EncounterRandom();
            var hasLayout = catalog.TryCreateLayout(35, out var layout, random: new RoomRandom(coLocateGuardians));
            check(hasLayout && (layout.StartX, layout.StartY, layout.BossX, layout.BossY) == (2, 2, 2, 1),
                $"{runName}: the real Cemetery layout resolves its start and boss coordinates");
            if (!hasLayout) return;

            var started = instance.TryStartRun(layout, 0, 0, out var start, random);
            check(started && start.Room.MapId == 5401 && !start.Room.Monsters.Any(IsGuardian),
                $"{runName}: the start room keeps its scripted actors");
            if (!started) return;
            var initialRoom = instance.CaptureCurrentRoomSnapshot(1);
            var entry = DungeonEntryPackets.PlanMemberEntry(instance, initialRoom, null, null, 0, chapterTwo: true);
            check(entry.TakeLast(3).Select(packet => packet.ProtocolId)
                .SequenceEqual(new byte[] { GameProtocolEngine.DungeonInfoNotification, 186, GameProtocolEngine.StartMapNotification }),
                $"{runName}: the server projects its authoritative gate state before the first map");
            check(DungeonEntryPackets.PlanMemberEntry(instance, initialRoom, null, null, 0)
                    .All(packet => packet.ProtocolId != 186),
                $"{runName}: the new gate packet is scoped to Chapter Two");
            var locked = entry.SingleOrDefault(packet => packet.ProtocolId == 186);
            var token = locked?.Payload.Length == 9
                ? BinaryPrimitives.ReadUInt32LittleEndian(locked.Payload.AsSpan(4)) : 0;
            check(token != 0 && locked!.Payload.AsSpan(0, 4).SequenceEqual("CEM1"u8) && locked.Payload[8] == 0,
                $"{runName}: first entry binds a locked state to the new run token");

            foreach (var monster in start.Room.Monsters)
                instance.TryTakeMonsterDeath(monster.UniqueId, out _);
            var stillLocked = !instance.TryAdvanceRoom(2, 1, out _, out var error) && error == 7;
            check(stillLocked, $"{runName}: killing the ordinary start-room monsters leaves the boss entry locked with error 7");
            if (!stillLocked) return;

            var seen = new List<ushort>();
            var guardianRooms = new HashSet<(byte, byte)>();
            foreach (var (x, y) in Ring)
            {
                if (!instance.TryMoveRoom(x, y, out var entered, random))
                {
                    check(false, $"{runName}: the real route advances to ({x}, {y})");
                    return;
                }

                var room = entered.Room;
                var eligible = room.Monsters.Where(IsGuardian).ToArray();
                var packet = GameProtocolEngine.CreateStartMap(x, y, room.MapId, room.Monsters);
                foreach (var guardian in eligible)
                {
                    seen.Add(guardian.MonsterIndex);
                    guardianRooms.Add((x, y));
                    var slot = 10 + 9 * Array.IndexOf(room.Monsters, guardian);
                    var expectedIndex = room.MapId switch { 5407 => 7, 5408 => 8, 5412 => 12, 5411 => 8, _ => -10 };
                    check(guardian is { Level: 46, IsHostile: true, IsBoxMonster: 0 }
                        && packet.Payload[slot] == expectedIndex + guardian.MonsterIndex - Guardians[0]
                        && BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(slot + 3)) == guardian.MonsterIndex,
                        $"{runName}: guardian {guardian.MonsterIndex} preserves the client MAP row in START_MAP for map {room.MapId}");
                    check(guardian.Type == GameDungeonMonsterTypes.Normal && packet.Payload[slot + 6] == 0,
                        $"{runName}: guardian {guardian.MonsterIndex} keeps its original grade during champion selection");
                    var generation = instance.RoomGeneration;
                    check(instance.TryBeginMonsterDeath(guardian.UniqueId, generation, out _, out _, out var unlocked)
                        && instance.CompleteMonsterDeath(generation)
                        && !instance.TryBeginMonsterDeath(guardian.UniqueId, generation, out _, out _, out var duplicate)
                        && duplicate is null,
                        $"{runName}: guardian {guardian.MonsterIndex} settles once");
                    check((unlocked is { IsOpen: true } opened && opened.RunToken == token) == (seen.Count == 4),
                        $"{runName}: the fourth accepted kill alone produces an unlock for the current run");
                    if (unlocked is { } gate)
                        dungeonParties.BroadcastCemeteryGateOpenAsync(instance, gate).GetAwaiter().GetResult();
                }

                foreach (var monster in room.Monsters.Where(monster => !IsGuardian(monster)))
                    instance.TryTakeMonsterDeath(monster.UniqueId, out _);
            }

            check(seen.Order().SequenceEqual(Guardians) && guardianRooms.Count == (coLocateGuardians ? 1 : 4),
                $"{runName}: the run assigns every guardian exactly once and preserves independent room draws");
            check(received.All(inbox => inbox is [{ ProtocolId: 186, Payload: { Length: 9 } body }]
                && BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)) == token && body[8] == 2),
                $"{runName}: all Chapter Two members receive the same single authoritative unlock");
            var reopenedEntry = DungeonEntryPackets.PlanMemberEntry(instance,
                instance.CaptureCurrentRoomSnapshot(1), null, null, 0, chapterTwo: true);
            check(reopenedEntry.SingleOrDefault(packet => packet.ProtocolId == 186)?.Payload is { Length: 9 } replay
                && BinaryPrimitives.ReadUInt32LittleEndian(replay.AsSpan(4)) == token && replay[8] == 1,
                $"{runName}: a live entrant receives the already-open state");
            check(instance.TryAdvanceRoom(2, 1, out var boss, out error) && error == 0
                && boss!.Room.MapId == 5424 && boss.Room.Monsters.Any(monster => monster.MonsterIndex == 1010),
                $"{runName}: defeating the four guardians admits the existing boss room");
            check(instance.TryStartRun(layout, 0, 0, out _, random)
                && !instance.TryAdvanceRoom(2, 1, out _, out error) && error == 7,
                $"{runName}: a new run clears guardian kill progress");
            dungeonParties.BroadcastCemeteryGateOpenAsync(instance, new(token, IsOpen: true)).GetAwaiter().GetResult();
            check(instance.GetCemeteryGate() is { IsOpen: false } nextGate && nextGate.RunToken != token
                && received.All(inbox => inbox.Count == 1),
                $"{runName}: the next run rejects a delayed unlock from the previous instance");
            var noHunts = new DungeonLayout(layout.DungeonId, layout.Maze,
                new(layout.StartX, layout.StartY), new(layout.BossX, layout.BossY));
            var startedWithoutHunts = instance.TryStartRun(noHunts, 0, 0, out _, random);
            var gateWithoutHunts = instance.GetCemeteryGate();
            check(startedWithoutHunts && gateWithoutHunts is { IsOpen: true },
                $"{runName}: the server can project a Cemetery layout without any required hunts (started={startedWithoutHunts}, gate={gateWithoutHunts})");
        }

        void VerifyGuardiansAfterLeavingHell(byte difficulty, byte option)
        {
            var runName = $"Cemetery hell exit (difficulty {difficulty}, option {option})";
            var instance = new DungeonInstance(42, Guid.NewGuid(), catalog, items);
            // The third eligible hunt cell is the seal-door coordinate (2,0).
            var hasLayout = catalog.TryCreateLayout(35, out var layout, 4, new HellGuardianLayoutRandom());
            var random = new EncounterRandom();
            var started = hasLayout && instance.TryStartRun(layout, difficulty, option, out _, random);
            check(started, $"{runName}: starts a real Cemetery layout");
            if (!started) return;

            var current = instance.CaptureCurrentRoomSnapshot(1).Room;
            foreach (var (x, y) in Ring)
            {
                foreach (var monster in current.Monsters)
                    instance.TryTakeMonsterDeath(monster.UniqueId, out _);

                if (!instance.TryMoveRoom(x, y, out var entered, random))
                {
                    check(false, $"{runName}: visits ({x},{y})");
                    return;
                }

                current = entered.Room;
                if (current is not { IsHellRoom: true }) continue;

                var guardians = current.Monsters.Where(IsGuardian).ToArray();
                var prepared = guardians.Select(monster => monster.MonsterIndex).SequenceEqual(Guardians[..2])
                    && current.Monsters.Any(monster => monster.IsHostile && !IsGuardian(monster))
                    && instance.TryTakeMonsterDeath(guardians[0].UniqueId, out _);
                check(prepared, $"{runName}: leaves one guardian and the other hell enemies alive");
                if (!prepared) return;

                for (var visit = 0; visit < 2; visit++)
                {
                    var returned = instance.TryMoveRoom(3, 0, out var departed, random)
                        && departed.CompletedHellRoomOnExit == (visit == 0)
                        && instance.TryMoveRoom(x, y, out entered, random);
                    if (!returned)
                    {
                        check(false, $"{runName}: leaving and returning to the hell room succeeds");
                        return;
                    }

                    var snapshot = instance.CaptureRoomSnapshot(x, y, entered, 1);
                    var wire = DungeonEntryPackets.PlanStartMap(snapshot).Payload;
                    var keptGuardian = entered.Room.MapId == 60016
                        && entered.Room.Monsters.SequenceEqual(guardians[1..])
                        && !entered.ReuseClientRoomState
                        && snapshot.HellPartyMode == 0
                        && snapshot.PassiveObjectDrops.Length == 0
                        && wire.Length >= 20 && wire[6] == 2 && wire[9] == 1 && wire[^1] == 0
                        && wire[10] == guardians[1].MapListIndex
                        && BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(11)) == guardians[1].UniqueId
                        && BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(13)) == guardians[1].MonsterIndex;
                    check(keptGuardian, $"{runName}: revisit {visit + 1} restores only the surviving guardian with its original START_MAP row");
                    if (!keptGuardian) return;
                }

                check(instance.TryTakeMonsterDeath(guardians[1].UniqueId, out _)
                    && !instance.TryTakeMonsterDeath(guardians[1].UniqueId, out _),
                    $"{runName}: the restored guardian is counted exactly once");
                current = instance.CaptureCurrentRoomSnapshot(1).Room;
                check(!current.IsHellRoom && current.Monsters.Length == 0,
                    $"{runName}: the room is empty after its last guardian dies");
            }

            check(instance.TryAdvanceRoom(2, 1, out var boss, out var code) && code == 0 && boss?.Room.MapId == 5424,
                $"{runName}: all four real guardian kills open the boss door with the hell enemies skipped");
        }
    }

    private static void VerifyShadowMazePlacements(DungeonCatalog catalog, Action<bool, string> check)
    {
        var candidates = new (byte X, byte Y, ushort MapId, byte Slot, ushort SpawnX, ushort SpawnY)[][]
        {
            [(0, 0, 7015, 12, 505, 275), (1, 1, 7008, 8, 451, 308)],
            [(1, 1, 7010, 8, 354, 340), (0, 2, 7017, 12, 424, 307)],
            [(1, 1, 7007, 8, 352, 310), (2, 2, 7020, 12, 523, 296)],
            [(2, 0, 7022, 12, 564, 344), (1, 1, 7009, 8, 506, 312)]
        };
        for (var maze = 0; maze < candidates.Length; maze++)
        {
            for (var draw = 0; draw < candidates[maze].Length; draw++)
            {
                var (x, y, mapId, slot, spawnX, spawnY) = candidates[maze][draw];
                var alternative = candidates[maze][1 - draw];
                var random = new LayoutRandom(maze, draw);
                check(catalog.TryCreateLayout(37, out var layout, random: random)
                    && catalog.TryCreateRoom(layout, x, y, out var selected, random: random)
                    && selected.MapId == mapId
                    && selected.Monsters.Where(monster => monster.MonsterIndex == 50097).ToArray()
                        is [{ MapListIndex: var actualSlot, Level: 48, X: var actualX, Y: var actualY }]
                    && (actualSlot, actualX, actualY) == (slot, spawnX, spawnY)
                    && catalog.TryCreateRoom(layout, alternative.X, alternative.Y, out var other, random: random)
                    && other.Monsters.All(monster => monster.MonsterIndex != 50097),
                    $"Shadow Maze layout {maze}: drawn map {mapId} hosts the sole original 50097 guard at its MAP position");
            }
        }
    }

    private static bool IsGuardian(GameDungeonMonster monster) => Guardians.Contains(monster.MonsterIndex);

    private sealed class RoomRandom(bool coLocateGuardians) : IDropRandomSource
    {
        private int _nextRoom;

        public int Next(int exclusiveMaximum) =>
            exclusiveMaximum == 11 && !coLocateGuardians ? _nextRoom++ % exclusiveMaximum : 0;
    }

    private sealed class EncounterRandom : IDropRandomSource
    {
        public int Next(int exclusiveMaximum) => exclusiveMaximum is > 2 and < 100 ? exclusiveMaximum - 1 : 0;
    }

    private sealed class LayoutRandom(int maze, int draw) : IDropRandomSource
    {
        private readonly Queue<int> _rolls = new([maze, 0, 0, draw]);

        public int Next(int exclusiveMaximum) => _rolls.TryDequeue(out var roll) ? roll : 0;
    }

    private sealed class HellGuardianLayoutRandom : IDropRandomSource
    {
        private readonly Queue<int> _rolls = new([0, 0, 0, 2, 2, 0, 1]);

        public int Next(int exclusiveMaximum) => _rolls.TryDequeue(out var roll) ? roll : 0;
    }
}
