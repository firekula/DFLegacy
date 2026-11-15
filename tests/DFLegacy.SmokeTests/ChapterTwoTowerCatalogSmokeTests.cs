using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoTowerCatalogSmokeTests
{
    private const ushort TowerId = 11001;

    internal static void Run(string pvfPath, Action<bool, string> check)
    {
        var options = new ServerOptions { ScriptPvfPath = Path.GetFullPath(pvfPath), ChapterTwoRemote = true };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var catalog = CreateCatalog(options, scripts);
        check(scripts.IsPvf && scripts.FileExists("map/illusiontower/025f.map"),
            "catalog fixtures use the actual Chapter Two PVF");
        var hasTower = catalog.TryGetTower(TowerId, out var tower);
        check(hasTower && tower.Floors.Count == 25,
            "the catalog exposes the actual 25-floor Tower of Illusion");
        if (!hasTower)
        {
            return;
        }

        check(tower.MinimumLevel == 1 && tower.BasisLevel == 50
            && tower.Ticket == new DungeonTowerTicket(4183, 1, true),
            "tower entry uses the DGN levels and one consumable 4183 invitation");
        check(tower.ApcAppearRate == 100 && !tower.ItemDropEnabled,
            "tower exposes its APC appearance rate and disabled normal item drop");
        check(tower.ApcCandidates.Count == 24 && tower.ApcCandidates.Any(apc =>
            apc is { ApcId: 5301, AppearancePoint: 50, Level: 50 }
            && apc.ScriptPath.EndsWith("/jisun.aic", StringComparison.OrdinalIgnoreCase)),
            "the actual AI script list provides the positive point pool and candidate levels");

        var monsterCounts = new[] { 3, 7, 0, 4, 0, 6, 6, 7, 5, 4, 0, 2, 7, 8, 1, 3, 0, 4, 8, 2, 7, 9, 0, 8, 9 };
        var floors = new Dictionary<int, DungeonTowerFloor>();
        for (var number = 1; number <= monsterCounts.Length; number++)
        {
            var found = catalog.TryCreateTowerFloor(TowerId, number, out var floor, new FixedRandom(high: false));
            check(found && floor.Definition.FloorNumber == number && floor.Definition.MapId == 31000 + number
                && floor.Room.MapId == 31000 + number && floor.Room.MapType == DungeonMapType.Normal
                && floor.Room.Monsters.Length == monsterCounts[number - 1],
                $"real floor {number} resolves its normal map and {monsterCounts[number - 1]} fixed monster rows");
            if (found)
            {
                floors.Add(number, floor);
            }
        }

        if (floors.Count != 25)
        {
            return;
        }

        check(floors[1].Room.Monsters.Length == 3
            && floors[1].Room.Monsters.All(monster => monster is { MonsterIndex: 4, Level: 50, Team: 100 })
            && floors[1].Room.Monsters.Select(monster => (monster.X, monster.Y))
                .SequenceEqual(new (ushort, ushort)[] { (458, 184), (360, 236), (473, 283) })
            && floors[1].Room.PassiveObjects.Select(obj => obj.PassiveObjectId).Order()
                .SequenceEqual(new ushort[] { 5104, 5105 }),
            "the first tower room preserves hill goblin positions and the two in-bounds passive objects");

        foreach (var (number, id, level, x, y) in new[]
        {
            (5, 15402, 25, 480, 250), (10, 20604, 15, 450, 250), (15, 5602, 40, 480, 250),
            (20, 10600, 40, 480, 250), (24, 5601, 40, 631, 250), (25, 608, 40, 560, 225)
        })
        {
            var floor = floors[number];
            check(floor.Definition.FixedApcs is [{ ActorType: "monster", Grade: "normal", Option1: 0, Option2: 0 }]
                && floor.Apcs is [{ MapListIndex: 0, Team: 100 } actor]
                && actor.ApcId == id && actor.Level == level && actor.Position == new DungeonTowerSpawnPosition(x, y, 0),
                $"real floor {number} uses fixed APC {id} at its script position and level");
        }

        var positions = new[]
        {
            new DungeonTowerSpawnPosition(350, 280, 25), new DungeonTowerSpawnPosition(450, 240, 25),
            new DungeonTowerSpawnPosition(350, 200, 25)
        };
        var candidates = tower.ApcCandidates.ToDictionary(apc => apc.ApcId);
        foreach (var (number, points, positionsCount) in new[] { (3, 50, 2), (11, 70, 3), (17, 90, 3), (23, 100, 3) })
        {
            var floor = floors[number];
            check(floor.Definition.RandomApcPoint == points
                && floor.Definition.MonsterSpawnPositions.SequenceEqual(positions.Take(positionsCount))
                && floor.Apcs.Count > 0
                && floor.Apcs.Select(apc => apc.ApcId).Distinct().Count() == floor.Apcs.Count
                && floor.Apcs.All(apc => candidates.TryGetValue(apc.ApcId, out var candidate) && candidate.Level == apc.Level)
                && floor.Apcs.Sum(apc => candidates[apc.ApcId].AppearancePoint) <= points
                && floor.Apcs.Select((apc, index) =>
                    apc.MapListIndex == 64 + index && apc.Team == 100 && apc.Position == positions[index % positionsCount]).All(ok => ok),
                $"real floor {number} draws distinct APCs within {points} points and uses native random spawn slots");
        }

        check(floors[23].Apcs.Count == 2
            && floors[23].Apcs.Sum(apc => candidates[apc.ApcId].AppearancePoint) == 70,
            "when the cheapest APC cannot repeat, the picker uses the next available point group");
        check(catalog.TryCreateTowerFloor(TowerId, 23, out var expensive, new FixedRandom(high: true))
            && expensive.Apcs.Count == 1
            && expensive.Apcs[0].ApcId == 15207,
            "a maximum roll on floor 23 can spend all 100 points on the actual fire APC");
        check(floors[25].Room.Monsters.All(monster => monster.MonsterIndex == 60030),
            "the final floor preserves its nine actual monsters alongside APC 608");
        check(!catalog.TryCreateTowerFloor(TowerId, 0, out _)
            && !catalog.TryCreateTowerFloor(TowerId, 26, out _)
            && !catalog.TryGetTower(11000, out _)
            && !catalog.TryCreateLayout(TowerId, out _)
            && catalog.TryCreateLayout(27, out _),
            "tower requests are bounded and retain ordinary Chapter Two dungeon layout");

        VerifyBrokenTowerDoesNotDiscardOrdinaryCatalog(check);
    }

    private static void VerifyBrokenTowerDoesNotDiscardOrdinaryCatalog(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-tower-catalog-");
        try
        {
            var dungeon = root.CreateSubdirectory("dungeon");
            var map = root.CreateSubdirectory("map");
            File.WriteAllText(Path.Combine(dungeon.FullName, "dungeon.lst"), "27 `LoCAl.dgn`\n11001 `ToWEr.dgn`\n");
            File.WriteAllText(Path.Combine(dungeon.FullName, "local.dgn"), """
                [maze info]
                [size] 1 1
                [greed] `P`
                [start map] 0 0 [/start map]
                [map specification] 0 0 7 [/map specification]
                """);
            File.WriteAllText(Path.Combine(map.FullName, "map.lst"), "7 `local.map`\n31001 `FtEsT.map`\n");
            File.WriteAllText(Path.Combine(map.FullName, "local.map"), "[type] `[normal]`");
            File.WriteAllText(Path.Combine(map.FullName, "ftest.map"), "[tyPE] `[norMAL]`");
            var towerPath = Path.Combine(dungeon.FullName, "tower.dgn");
            const string tower = """
                [DUnGeON TYPe] `ToWeR Of DeAtH`
                [ReQuiRed ItEm] 4183 1 1
                [DEATH TOWER MAP INDEXES]
                1
                1 31001
                [/death tower map indexes]
                """;
            File.WriteAllText(towerPath, tower);
            var options = new ServerOptions { ScriptPvfPath = "", SkillScriptPath = root.FullName };
            var catalog = CreateCatalog(options, new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance));
            check(catalog.TryCreateTowerFloor(TowerId, 1, out var floor) && floor.Room.MapId == 31001,
                "mixed-case tags and paths still resolve the configured tower floor");

            File.WriteAllText(towerPath, tower.Replace("1 31001", "1 31999", StringComparison.Ordinal));
            catalog = CreateCatalog(options, new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance));
            check(!catalog.TryGetTower(TowerId, out _) && catalog.TryCreateLayout(27, out _),
                "a missing tower map leaves the successfully parsed normal catalog in use");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static DungeonCatalog CreateCatalog(ServerOptions options, ScriptFileSystem scripts) =>
        new(scripts, new ObjectDropGenerator(options,
                new ObjectDropCatalog(scripts, NullLogger<ObjectDropCatalog>.Instance),
                new GoldDropCatalog(scripts, NullLogger<GoldDropCatalog>.Instance)),
            NullLogger<DungeonCatalog>.Instance);

    private sealed class FixedRandom(bool high) : IDropRandomSource
    {
        public int Next(int exclusiveMaximum) => high ? exclusiveMaximum - 1 : 0;
    }
}
