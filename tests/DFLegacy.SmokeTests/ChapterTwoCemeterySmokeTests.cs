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

        VerifyRun(partyId: 0, coLocateGuardians: true);
        VerifyRun(partyId: 42, coLocateGuardians: false);
        VerifyShadowMazePlacements(catalog, check);

        void VerifyRun(ushort partyId, bool coLocateGuardians)
        {
            var runName = coLocateGuardians ? "solo" : "party";
            var instance = new DungeonInstance(partyId, Guid.NewGuid(), catalog, items);
            var random = new EncounterRandom();
            var hasLayout = catalog.TryCreateLayout(35, out var layout, random: new RoomRandom(coLocateGuardians));
            check(hasLayout && (layout.StartX, layout.StartY, layout.BossX, layout.BossY) == (2, 2, 2, 1),
                $"{runName}: the real Cemetery layout resolves its start and boss coordinates");
            if (!hasLayout) return;

            var started = instance.TryStartRun(layout, 0, 0, out var start, random);
            check(started && start.Room.MapId == 5401 && !start.Room.Monsters.Any(IsGuardian),
                $"{runName}: the start room keeps its scripted actors");
            if (!started) return;

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
                    check(instance.TryTakeMonsterDeath(guardian.UniqueId, out _)
                        && !instance.TryTakeMonsterDeath(guardian.UniqueId, out _),
                        $"{runName}: guardian {guardian.MonsterIndex} settles once");
                }

                foreach (var monster in room.Monsters.Where(monster => !IsGuardian(monster)))
                    instance.TryTakeMonsterDeath(monster.UniqueId, out _);
            }

            check(seen.Order().SequenceEqual(Guardians) && guardianRooms.Count == (coLocateGuardians ? 1 : 4),
                $"{runName}: the run assigns every guardian exactly once and preserves independent room draws");
            check(instance.TryAdvanceRoom(2, 1, out var boss, out error) && error == 0
                && boss!.Room.MapId == 5424 && boss.Room.Monsters.Any(monster => monster.MonsterIndex == 1010),
                $"{runName}: defeating the four guardians admits the existing boss room");
            check(instance.TryStartRun(layout, 0, 0, out _, random)
                && !instance.TryAdvanceRoom(2, 1, out _, out error) && error == 7,
                $"{runName}: a new run clears guardian kill progress");
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
}
