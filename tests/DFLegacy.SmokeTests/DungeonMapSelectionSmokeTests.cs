using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class DungeonMapSelectionSmokeTests
{
    public static void RunActualLorienConfiguration(Action<bool, string> check, string pvfPath)
    {
        var options = new ServerOptions { ScriptPvfPath = pvfPath, SkillScriptPath = "" };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var catalog = CreateCatalog(options, scripts);
        check(catalog.TryCreateLayout(1, out var layout, random: new SequenceDropRandomSource(2, 0, 0))
                && layout.MazeIndex == 2 && layout.Width == 3 && layout.Height == 3
                && layout.BossX == 2 && layout.BossY == 2,
            "actual PVF retains Lorien maze2, 3x3 and boss coordinate (2,2)");
        if (layout is null) return;
        var found = catalog.TryCreateRoom(layout, 2, 2, out var room, random: new ZeroDropRandomSource());
        check(found && room.MapId == 6416 && room.MapType == DungeonMapType.Boss
                && room.Monsters.Any(m => m.MonsterIndex == 1077 && m.Type == GameDungeonMonsterTypes.Boss),
            "actual PVF boss-specific candidate resolves MAP6416 with its script-defined Piper boss1077");
        if (found)
            Console.WriteLine($"Actual Lorien: maze={layout.MazeIndex}, size={layout.Width}x{layout.Height}, boss=({layout.BossX},{layout.BossY}), map={room.MapId}, bossMonsters={string.Join(',', room.Monsters.Where(m => m.Type == GameDungeonMonsterTypes.Boss).Select(m => m.MonsterIndex))}");
    }

    private static DungeonCatalog CreateCatalog(ServerOptions options, ScriptFileSystem scripts) =>
        new(scripts,
            new ObjectDropGenerator(options,
                new ObjectDropCatalog(scripts, NullLogger<ObjectDropCatalog>.Instance),
                new GoldDropCatalog(scripts, NullLogger<GoldDropCatalog>.Instance)),
            NullLogger<DungeonCatalog>.Instance);

    public static void Run(Action<bool, string> check)
    {
        using var fixture = new Fixture();
        var catalog = fixture.Catalog;
        var lorien = fixture.Layout(1, 2);
        check(lorien.Width == 3 && lorien.Height == 3
                && lorien.BossX == 2 && lorien.BossY == 2,
            "Lorien configuration selects its third 3x3 maze and boss coordinate (2,2)");
        check(catalog.TryCreateRoom(lorien, 2, 2, out var boss, random: new ZeroDropRandomSource())
                && boss.MapId == 6416 && boss.MapType == DungeonMapType.Boss
                && boss.Monsters.Any(m => m.MonsterIndex == 1077 && m.Type == GameDungeonMonsterTypes.Boss),
            "Lorien dedicated boss map specification selects MAP6416 and its Piper boss, not fallback MAP13");
        check(catalog.TryCreateRoom(lorien, 0, 0, out var start, random: new ZeroDropRandomSource())
                && start.MapId == 1 && start.MapType == DungeonMapType.Normal
                && catalog.TryCreateRoom(lorien, 1, 0, out var normal, random: new ZeroDropRandomSource())
                && normal.MapId == 2 && normal.MapType == DungeonMapType.Normal,
            "boss specifications at ordinary/start coordinates cannot replace their normal specifications");

        for (var maze = 0; maze < 2; maze++)
        {
            var legacy = fixture.Layout(1, maze);
            check(catalog.TryCreateRoom(legacy, legacy.BossX, legacy.BossY, out var legacyBoss,
                       random: new ZeroDropRandomSource()) && legacyBoss.MapId == 13,
                $"Lorien legacy maze {maze} without a dedicated list retains its previous boss pool");
        }

        for (var maze = 0; maze < 3; maze++)
        {
            var isolated = fixture.Layout(2, maze);
            var expected = maze == 0 ? 6417 : maze == 1 ? 6418 : 6416;
            check(catalog.TryCreateRoom(isolated, isolated.BossX, isolated.BossY, out var selected,
                       random: new ZeroDropRandomSource()) && selected.MapId == expected,
                $"dedicated boss specifications are parsed separately for maze {maze}");
        }

        var invalid = fixture.Layout(3, 2);
        check(catalog.TryCreateRoom(invalid, 2, 2, out var fallback, random: new ZeroDropRandomSource())
                && fallback.MapId == 13 && fallback.MapType == DungeonMapType.Boss,
            "invalid coordinates/IDs and missing/normal/dummy candidates preserve the valid boss fallback");
        var first = fixture.Layout(4, 2);
        var second = fixture.Layout(4, 2);
        check(catalog.TryCreateRoom(first, 2, 2, out var firstBoss, random: new ZeroDropRandomSource())
                && firstBoss.MapId == 6417
                && catalog.TryCreateRoom(second, 2, 2, out var secondBoss,
                    random: new SequenceDropRandomSource(1)) && secondBoss.MapId == 6418,
            "dedicated boss candidate selection filters wrong/missing maps and permits both valid candidates");
        check(catalog.TryCreateRoom(second, 2, 2, out var repeated,
                    random: new ZeroDropRandomSource()) && repeated.MapId == 6418,
            "a selected dedicated boss map remains stable when the room is resolved again");
        var overlapping = fixture.Layout(5, 0);
        check(overlapping.StartX == overlapping.BossX && overlapping.StartY == overlapping.BossY
                && catalog.TryCreateRoom(overlapping, 0, 0, out var startingBoss,
                    random: new ZeroDropRandomSource())
                && startingBoss.MapId == 1 && startingBoss.MapType == DungeonMapType.Normal,
            "a single-room layout retains Start priority when its boss coordinate overlaps the start");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"dflegacy-boss-spec-{Guid.NewGuid():N}");
        public DungeonCatalog Catalog { get; }

        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(_root, "dungeon"));
            Directory.CreateDirectory(Path.Combine(_root, "map"));
            File.WriteAllText(Path.Combine(_root, "dungeon/dungeon.lst"),
                "1 `Lorien.dgn`\n2 `isolated.dgn`\n3 `invalid.dgn`\n4 `candidates.dgn`\n5 `overlapping.dgn`\n");
            const string firstMaze = """
                [maze info]
                [size] 3 2
                [greed] `PPPPPP`
                [start map] 0 0 [/start map]
                [boss map] 2 1 [/boss map]
                """;
            const string secondMaze = """
                [maze info]
                [size] 2 3
                [greed] `PPPPPP`
                [start map] 0 0 [/start map]
                [boss map] 1 2 [/boss map]
                """;
            const string thirdMaze = """
                [maze info]
                [size] 3 3
                [greed] `BBMMAA
                AAKKAA
                AADDEE`
                [start map] 0 0 [/start map]
                [boss map] 2 2 [/boss map]
                [map specification]
                0 0 1
                [/map specification]
                [map specification]
                1 0 2
                [/map specification]
                [map specification]
                2 2 5
                [/map specification]
                """;
            static string BossList(string entries) => $"\n[boss map specification]\n{entries}\n[/boss map specification]\n";
            const string metadata = "[minimum level] 1\n[basis level] 1\n";
            var legacy = firstMaze + "\n" + secondMaze + "\n";
            File.WriteAllText(Path.Combine(_root, "dungeon/Lorien.dgn"), metadata + legacy + thirdMaze
                + BossList("2 2 6416") + BossList("0 0 6416") + BossList("1 0 6416"));
            File.WriteAllText(Path.Combine(_root, "dungeon/isolated.dgn"), metadata
                + firstMaze + BossList("2 1 6417") + secondMaze + BossList("1 2 6418")
                + thirdMaze + BossList("2 2 6416"));
            File.WriteAllText(Path.Combine(_root, "dungeon/invalid.dgn"), metadata + legacy + thirdMaze
                + BossList("-1 2 6416") + BossList("3 2 6416") + BossList("2 2 0 -1 2 14 65000 65536"));
            File.WriteAllText(Path.Combine(_root, "dungeon/candidates.dgn"), metadata + legacy + thirdMaze
                + BossList("2 2 2 6417 6418 65000"));
            File.WriteAllText(Path.Combine(_root, "dungeon/overlapping.dgn"), metadata + """
                [maze info]
                [size] 1 1
                [greed] `P`
                [start map] 0 0 [/start map]
                [boss map] 0 0 [/boss map]
                [map specification] 0 0 1 [/map specification]
                """ + BossList("0 0 6416"));
            File.WriteAllText(Path.Combine(_root, "map/map.lst"),
                "1 `start.map`\n2 `normal.map`\n5 `normal5.map`\n13 `boss13.map`\n14 `dummy.map`\n"
                + "6416 `b6416.map`\n6417 `b6417.map`\n6418 `b6418.map`\n");
            WriteMap("start", "normal", "B", "1\n2\n3\n4\n5", 1);
            WriteMap("normal", "normal", "M", "1\n2\n3\n4", 1);
            WriteMap("normal5", "normal", "E", "1\n2\n3\n4", 1);
            WriteMap("boss13", "boss", "P", "1\n2\n3\n4", 2);
            WriteMap("dummy", "dummy", "E", "1\n2\n3\n4", 1);
            // The actual configured MAP6416 is owned by dungeon51 and accepts
            // topologyC; its explicit override must survive both differences.
            WriteMap("b6416", "boss", "C", "51", 1077);
            WriteMap("b6417", "boss", "E", "51", 1078);
            WriteMap("b6418", "boss", "E", "51", 1079);
            var options = new ServerOptions { ScriptPvfPath = "", SkillScriptPath = _root };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            Catalog = CreateCatalog(options, scripts);
        }

        public DungeonLayout Layout(ushort dungeon, int maze)
        {
            if (!Catalog.TryCreateLayout(dungeon, out var layout, random: new SequenceDropRandomSource(maze, 0, 0)))
                throw new InvalidOperationException("Boss specification fixture layout missing");
            return layout;
        }

        private void WriteMap(string name, string type, string topology, string dungeons, int monster)
        {
            var monsterType = type == "boss" ? "boss" : "normal";
            File.WriteAllText(Path.Combine(_root, $"map/{name}.map"),
                $"[dungeon]\n{dungeons}\n[/dungeon]\n[type] `[{type}]`\n[greed] `{topology}`\n"
                + $"[monster]\n{monster} 1 0 100 100 0 1 1 `[fixed]` `[{monsterType}]`\n[/monster]\n");
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
