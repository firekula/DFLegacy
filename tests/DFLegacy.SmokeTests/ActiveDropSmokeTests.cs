using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ActiveDropSmokeTests
{
    public static Task RunAsync(Action<bool, string> check)
    {
        using var fixture = new ActiveDropItemFixture();
        foreach (var (id, seal, allowed) in new (ushort, byte, bool)[]
        {
            (50010, 0, true), (50011, 0, true), (50012, 0, true),
            (50013, 0, false), (50014, 0, false), (50014, 1, true),
            (50014, 3, false), (50015, 0, true), (50016, 0, false),
            (50017, 0, false), (50018, 0, false)
        })
        {
            var item = new CharacterItemRecord(9, id, 1, State: 7, Durability: 23,
                SealState: seal, EquipmentQualitySeed: 123456, InstanceId: Guid.NewGuid());
            var inventory = new Dictionary<ushort, CharacterItemRecord> { [9] = item };
            var actual = DungeonItemDropPlanner.TryPlan(inventory, 0, 9, 1,
                fixture.Catalog, out var plan, out _);
            check(actual == allowed, $"active discard template {id}, seal {seal}: allowed={allowed}");
            check(inventory[9] == item, "discard planning never mutates the caller's inventory");
            if (actual)
                check(plan.DroppedItem == item with { Slot = ushort.MaxValue }
                        && !plan.Inventory.ContainsKey(9),
                    "discard retains equipment quality, reinforcement, durability, seal and identity");
        }
        check(fixture.Catalog.TryGetDefinition(50015, out var sealingTrade)
                && !CharacterItemSealing.CanTrade(new CharacterItemRecord(9, 50015, 1), sealingTrade),
            "discard eligibility does not relax the existing trade/mail policy");
        check(DungeonItemDropPlanner.GetErrorCode(DungeonItemDropFailure.RarityRestricted) == 13
                && DungeonItemDropPlanner.GetErrorCode(DungeonItemDropFailure.NotTradeable) == 23,
            "discard rejects excessive rarity with native error13 and attachment with error23");
        foreach (var (balance, amount, allowed, error) in new (uint, uint, bool, byte)[]
        {
            (2000, 1, true, 0), (2000, 1000, true, 0), (2000, 1001, false, 70),
            (2000, 0, false, 17), (9, 10, false, 17), (10, 10, true, 0)
        })
        {
            var inventory = new Dictionary<ushort, CharacterItemRecord> { [9] = new(9, 50010, 1) };
            var allowedActual = DungeonItemDropPlanner.TryPlan(inventory, 0, 0, amount,
                fixture.Catalog, out var goldPlan, out var failure, currentGold: balance);
            check(allowedActual == allowed, $"active gold drop balance {balance}, amount {amount}: allowed={allowed}");
            if (allowedActual)
                check(goldPlan.IsGold && goldPlan.Gold == balance - amount
                        && goldPlan.DroppedItem.ItemId == 0 && goldPlan.DroppedItem.CountOrValue == amount
                        && goldPlan.Inventory.Count == 1 && goldPlan.Inventory[9] == inventory[9],
                    "gold planning debits only currency and retains every inventory item");
            else
                check(DungeonItemDropPlanner.GetErrorCode(failure) == error,
                    $"gold drop {amount} returns native error{error}");
        }
        return Task.CompletedTask;
    }
}

internal sealed class ActiveDropItemFixture : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dflegacy-active-drop-" + Guid.NewGuid().ToString("N"));
    public ItemCatalog Catalog { get; }

    public ActiveDropItemFixture()
    {
        Directory.CreateDirectory(Path.Combine(root, "equipment"));
        var rows = new List<string>();
        foreach (var (id, rarity, attach, type) in new[]
        {
            (50010, 0, "free", "weapon"), (50011, 1, "free", "coat"),
            (50012, 2, "free", "weapon"), (50013, 3, "free", "weapon"),
            (50014, 1, "sealing", "weapon"), (50015, 1, "sealing trade", "weapon"),
            (50016, 0, "free", "title name"), (50017, 0, "free", "hat avatar"),
            (50018, 0, "trade", "weapon")
        })
        {
            rows.Add($"{id} `{id}.equ`");
            File.WriteAllText(Path.Combine(root, $"equipment/{id}.equ"),
                $"[name] `Drop {id}`\n[equipment type] `[{type}]`\n[attach type] `[{attach}]`\n"
                + $"[rarity] {rarity}\n[weight] 1\n[minimum level] 1\n[durability] 30\n");
        }
        File.WriteAllLines(Path.Combine(root, "equipment/equipment.lst"), rows);
        var options = new ServerOptions { ScriptPvfPath = "", SkillScriptPath = root };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        Catalog = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
    }

    public void Dispose() => Directory.Delete(root, true);
}
