using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoTowerRewardPlannerSmokeTests
{
    internal static void Run(string pvfPath, Action<bool, string> check)
    {
        var options = new ServerOptions
        {
            ScriptPvfPath = Path.GetFullPath(pvfPath),
            ChapterTwoRemote = true,
            SkipUnsupportedChapterTwoCashContainers = true
        };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var experience = new DungeonExperienceCatalog(options, scripts, NullLogger<DungeonExperienceCatalog>.Instance);
        var gold = new GoldDropCatalog(scripts, NullLogger<GoldDropCatalog>.Instance);
        var equipment = new ClearRewardCatalog(scripts, NullLogger<ClearRewardCatalog>.Instance);
        var planner = new ChapterTwoTowerRewardPlanner(
            scripts, experience, gold, equipment, NullLogger<ChapterTwoTowerRewardPlanner>.Instance);

        check(scripts.IsPvf && scripts.FileExists("etc/deathtower.etc"),
            "tower reward reconstruction reads the Chapter Two server PVF");

        var defeat = planner.Plan(9, 5, 50, new FixedRolls(999));
        check(defeat.Experience == 13244 && defeat.Gold == 0
            && defeat.Cards.SequenceEqual(new GameChapterTwoTowerRewardItem[] { new(ushort.MaxValue, 0) }),
            "nine cleared floors use the original common EXP weight and an empty card on a failed roll");

        var goldCard = planner.Plan(9, 5, 50, new FixedRolls(359, 15));
        check(goldCard.Experience == 13244 && goldCard.Gold == 4965
            && goldCard.Cards.SequenceEqual(new GameChapterTwoTowerRewardItem[] { new(0, 4965) }),
            "the original level-50 gold table, spread, and tower weight produce a native gold card");
        var wire = GameProtocolEngine.CreateChapterTwoTowerReward(goldCard.Experience, goldCard.Cards);
        check(wire.Payload.SequenceEqual(Convert.FromHexString("BC33000001000065130000000000")),
            "the deterministic Chapter Two gold card has the original client reward payload");

        var itemCard = planner.Plan(9, 5, 50, new FixedRolls(360, 0, 0, 0, 0));
        var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance, options);
        check(itemCard.Cards is [{ ItemId: 10078, QuantityOrValue: 0 } item]
            && items.TryGetDefinition(item.ItemId, out var definition) && definition.IsEquipment
            && definition.ScriptPath.EndsWith("/n_tunic_oskyarn.equ", StringComparison.OrdinalIgnoreCase)
            && itemCard.Gold == 0,
            "the original clear reward catalog supplies equipment 10078 for the deterministic card");

        var fullClear = planner.Plan(25, characterLevel: 50, new FixedRolls(999, 999, 999, 999));
        check(fullClear.Experience == 72407 && fullClear.Cards.Count == 4,
            "twenty-five cleared floors use the common EXP weight and four original card slots");

        var capped = planner.Plan(25, 2, 50, new FixedRolls(999, 999));
        check(capped.Cards.Count == 2, "the dungeon item limit caps the original table's card count");

        var unavailable = planner.Plan(0, 5, 50, new FixedRolls());
        check(unavailable.Experience == 0 && unavailable.Gold == 0 && unavailable.Cards.Count == 0,
            "zero completed floors have no table row to award");

        var beyondTower = planner.Plan(26, 5, 50, new FixedRolls(999, 999, 999, 999, 999));
        check(beyondTower.Experience == 0 && beyondTower.Gold == 0 && beyondTower.Cards.Count == 0,
            "the Illusion Tower has no reward after its PVF final floor despite the longer common table");

        var temp = Directory.CreateTempSubdirectory("dflegacy-c2-tower-reward-");
        try
        {
            var tablePath = Path.Combine(temp.FullName, "etc", "deathtower.etc");
            var dungeonPath = Path.Combine(temp.FullName, "dungeon", "towers", "towerofillusion.dgn");
            Directory.CreateDirectory(Path.GetDirectoryName(tablePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(dungeonPath)!);
            File.WriteAllBytes(tablePath, scripts.ReadAllBytes("etc/deathtower.etc"));
            File.WriteAllText(dungeonPath, """
                [tower max clear item num] 2 // dungeon cap
                [death tower map indexes]
                25
                [/death tower map indexes]
                """);
            var alternateScripts = new ScriptFileSystem(new ServerOptions
            {
                ScriptPvfPath = Path.Combine(temp.FullName, "missing.pvf"),
                SkillScriptPath = temp.FullName
            }, NullLogger<ScriptFileSystem>.Instance);
            var alternatePlanner = new ChapterTwoTowerRewardPlanner(
                alternateScripts, experience, gold, equipment, NullLogger<ChapterTwoTowerRewardPlanner>.Instance);
            var cappedByDungeon = alternatePlanner.Plan(25, 5, 50, new FixedRolls(999, 999, 999, 999, 999));
            check(cappedByDungeon.Experience == fullClear.Experience && cappedByDungeon.Cards.Count == 2,
                "the dungeon script can cap the reward card count below the common table and caller limit");
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    private sealed class FixedRolls(params int[] rolls) : IDropRandomSource
    {
        private int _nextRoll;

        public int Next(int exclusiveMaximum)
        {
            if (_nextRoll == rolls.Length)
            {
                throw new InvalidOperationException("The planner asked for an unexpected random value.");
            }
            var value = rolls[_nextRoll++];
            return value >= 0 && value < exclusiveMaximum
                ? value
                : throw new InvalidOperationException($"The scripted roll {value} is outside [0, {exclusiveMaximum}).");
        }
    }
}
