using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class QuestItemGrantSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var root = Path.Combine(Path.GetTempPath(), $"dflegacy-quest-grant-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "stackable"));
            File.WriteAllText(Path.Combine(root, "stackable/stackable.lst"), "4000 `quest_4000.stk`\n");
            File.WriteAllText(Path.Combine(root, "stackable/quest_4000.stk"),
                "[name] `Quest task item`\n[stackable type] `[material]`\n[attach type] `[free]`\n"
                + "[rarity] 1\n[stack limit] 100\n[weight] 1\n[price] 1\n");
            var scripts = new ScriptFileSystem(
                new ServerOptions { ScriptPvfPath = string.Empty, SkillScriptPath = root },
                NullLogger<ScriptFileSystem>.Instance);
            var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
            items.Initialize();

            // 发放前（旧 mainInventory）与新道具落进空槽是现实形态：任务道具一次 1 个，
            // 首次发放必然占一个空槽 ⇒ 用旧背包投影更新包抛 KeyNotFoundException。
            var beforeGrant = new Dictionary<ushort, CharacterItemRecord>();
            var planned = new Dictionary<ushort, CharacterItemRecord>
            {
                [10] = new CharacterItemRecord(10, 4000, 1, Durability: 0)
            };
            var updates = EntranceService.BuildQuestItemUpdates(planned, [10], items);
            check(updates.Length == 1
                    && updates[0].Slot == 10
                    && updates[0].ItemId == 4000
                    && updates[0].CountOrValue == 1,
                "quest item UPDATE_ITEM_LIST entries project the granted item from the planned inventory");
            var missingThrew = false;
            try
            {
                _ = EntranceService.BuildQuestItemUpdates(beforeGrant, [10], items);
            }
            catch (KeyNotFoundException)
            {
                missingThrew = true;
            }

            check(missingThrew,
                "the pre-grant inventory cannot project a new quest item slot; the grant path must use the planned inventory");

            check(EntranceService.ShouldGrantPartyQuestItems(false, 14, false)
                    && !EntranceService.ShouldGrantPartyQuestItems(true, 14, false)
                    && !EntranceService.ShouldGrantPartyQuestItems(false, 0, false)
                    && !EntranceService.ShouldGrantPartyQuestItems(false, 14, true),
                "party kill credits replay quest drops only for ordinary deaths while still in a non-tutorial dungeon");

            check(QuestItemDropPlanner.PlanMonster(
                        PvfQuestFixture.Quest,
                        14,
                        0,
                        1,
                        planned.Values,
                        _ => true).SequenceEqual([new QuestItemDrop(4000, 1)]),
                "a second kill still rolls for the same party member from their own updated inventory");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static class PvfQuestFixture
    {
        public static readonly QuestDefinition Quest = new(
            1,
            0,
            1,
            99,
            "`[all]`",
            -1,
            1,
            -1,
            "seeking",
            [],
            [],
            true,
            false,
            "`[item]`",
            [],
            [],
            0)
        {
            ConditionRows = [[4000, 3]],
            MonsterRewardItems =
            [
                new QuestMonsterRewardItemDefinition(1, -1, -1, 4000, 1, 100, 3)
            ]
        };
    }
}
