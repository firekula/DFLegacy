using System.Security.Cryptography;
using System.Text.Json;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

// Uses the production catalog and placement planner; never opens a live save.
internal static class MigratedJarAudit
{
    private static readonly ushort[] Ids = [7125, 7130, 7138, 7148, 7189];

    internal static int Run(string source, string target, string output, bool allJars = false)
    {
        if (Directory.Exists(output)) throw new IOException("Use a new evidence directory.");
        Directory.CreateDirectory(output);
        ItemCatalog Catalog(string path, bool c2) => new(
            new ScriptFileSystem(new ServerOptions { ScriptPvfPath = Path.GetFullPath(path) }, NullLogger<ScriptFileSystem>.Instance),
            NullLogger<ItemCatalog>.Instance, new ServerOptions { SkipUnsupportedChapterTwoCashContainers = c2 });
        var c1 = Catalog(source, false);
        var c2 = Catalog(target, true);
        var failures = new List<string>();
        var checks = 0;
        var plans = 0;
        var rows = new List<object>();
        void Check(bool ok, string label) { checks++; if (!ok) failures.Add(label); }
        var testedIds = allJars
            ? c1.Definitions.Values.Where(d => d.TypeTag == "upgradable legacy").Select(d => d.Id).Order().ToArray()
            : Ids;
        foreach (var id in testedIds)
        {
            var before = c1.Definitions[id];
            var after = c2.Definitions[id];
            Check(after.Lottery is not null, $"{id}: usable lottery definition");
            if (after.Lottery is not { } lottery) continue;
            Check(before.TypeTag == after.TypeTag && before.ScriptPath == after.ScriptPath, $"{id}: same identity");
            Check(before.Lottery!.FallbackReward == lottery.FallbackReward
                && before.Lottery.WeightedRewards.SequenceEqual(lottery.WeightedRewards), $"{id}: original rewards and odds preserved");
            Check(before.MinimumLevel == after.MinimumLevel && before.LotteryUseCost == after.LotteryUseCost, $"{id}: same use requirements");
            Check(before.Price == after.Price, $"{id}: original purchase price preserved");
            var choices = new List<(int Roll, LotteryRewardDefinition Reward)>();
            var cumulative = 0;
            foreach (var reward in lottery.WeightedRewards)
            {
                if (reward.Weight > 0 && cumulative < LotteryItemDefinition.RollUpperBound)
                    choices.Add((cumulative, reward));
                cumulative += reward.Weight;
            }
            if (cumulative < LotteryItemDefinition.RollUpperBound)
                choices.Add((cumulative, lottery.FallbackReward));
            foreach (var slot in new ushort[] { 5, 66 })
            foreach (var count in new uint[] { 1, 2 })
            foreach (var choice in choices)
            {
                var item = new CharacterItemRecord(slot, id, count, InstanceId: Guid.NewGuid());
                var inv = new Dictionary<ushort, CharacterItemRecord> { [slot] = item };
                var ok = LotteryItemPlanner.TryCreate(inv, new Dictionary<ushort, CharacterItemRecord>(),
                    new Dictionary<ushort, CharacterItemRecord>(), slot, 60, 1_000_000, uint.MaxValue,
                    choice.Roll, c2, out var plan, out var failure);
                plans++;
                var label = $"{id}/{slot}/{count}/{choice.Roll}";
                Check(ok, $"{label}: placement ({failure})");
                Check(inv.Count == 1 && inv[slot] == item, $"{label}: input not mutated");
                if (!ok) continue;
                Check(plan.Reward == choice.Reward, $"{label}: selected expected reward row");
                Check(plan.MainInventory.Values.Where(x => x.ItemId == id).Sum(x => x.CountOrValue) == count - 1, $"{label}: consumed one jar");
                var all = plan.MainInventory.Values.Concat(plan.AvatarInventory.Values).Concat(plan.CreatureInventory.Values).ToArray();
                Check(all.Count(x => x.ItemId == choice.Reward.ItemId) == 1, $"{label}: single reward instance");
                var received = all.First(x => x.ItemId == choice.Reward.ItemId);
                Check(all.Where(x => x.ItemId == choice.Reward.ItemId)
                    .Aggregate(0UL, (total, entry) => total + entry.CountOrValue) == choice.Reward.CountOrValue,
                    $"{label}: actual received reward quantity");
                Check((!CharacterItemIdentity.RequiresInstanceId(c2.Definitions[received.ItemId])
                        || received.InstanceId != Guid.Empty)
                    && received.Slot == plan.DestinationSlot, $"{label}: reward identity policy and slot");
                Check(plan.Gold == 1_000_000 - (after.LotteryUseCost ?? 0), $"{label}: expected gold deduction");
            }
            // A failed equipment draw must not consume the source stack.
            var equipmentChoice = choices.First(x => c2.Definitions[x.Reward.ItemId].InventoryCategory == ItemInventoryCategory.Equipment);
            // Main placement can use empty quick slots as a fallback as well.
            var blocked = Enumerable.Range(CharacterInventoryLayout.QuickSlotStart,
                    CharacterInventoryLayout.EquipmentSlotEnd - CharacterInventoryLayout.QuickSlotStart)
                .ToDictionary(x => (ushort)x, x => new CharacterItemRecord((ushort)x, equipmentChoice.Reward.ItemId, 1, InstanceId: Guid.NewGuid()));
            blocked[66] = new CharacterItemRecord(66, id, 2, InstanceId: Guid.NewGuid());
            var snapshot = blocked.ToArray();
            var blockedOk = LotteryItemPlanner.TryCreate(blocked, new Dictionary<ushort, CharacterItemRecord>(),
                new Dictionary<ushort, CharacterItemRecord>(), 66, 60, 1_000_000, uint.MaxValue,
                equipmentChoice.Roll, c2, out _, out var blockedFailure);
            Check(!blockedOk && blockedFailure == LotteryUseFailure.InventoryFull, $"{id}: full bag rejects draw");
            Check(snapshot.SequenceEqual(blocked), $"{id}: full bag preserves all input items");
            // The migrated inventory has full material and quick tabs; neither
            // may block an equipment reward when its own tab has room.
            var material = c2.Definitions.First(x => x.Value.InventoryCategory == ItemInventoryCategory.Material).Key;
            var unrelated = Enumerable.Range(73, 32).ToDictionary(x => (ushort)x,
                x => new CharacterItemRecord((ushort)x, material, 1, InstanceId: Guid.NewGuid()));
            foreach (var slot in Enumerable.Range(3, 6))
                unrelated[(ushort)slot] = new CharacterItemRecord((ushort)slot, equipmentChoice.Reward.ItemId, 1, InstanceId: Guid.NewGuid());
            unrelated[66] = new CharacterItemRecord(66, id, 2, InstanceId: Guid.NewGuid());
            var unrelatedSnapshot = unrelated.ToArray();
            var unrelatedOk = LotteryItemPlanner.TryCreate(unrelated, new Dictionary<ushort, CharacterItemRecord>(),
                new Dictionary<ushort, CharacterItemRecord>(), 66, 60, 1_000_000, uint.MaxValue,
                equipmentChoice.Roll, c2, out var unrelatedPlan, out _);
            Check(unrelatedOk && unrelatedPlan.DestinationSlot is >= 9 and < 41, $"{id}: full unrelated tabs permit equipment reward");
            Check(unrelatedSnapshot.SequenceEqual(unrelated), $"{id}: successful planning preserves source input");
            var weightOk = LotteryItemPlanner.TryCreate(unrelated, new Dictionary<ushort, CharacterItemRecord>(),
                new Dictionary<ushort, CharacterItemRecord>(), 66, 60, 1_000_000, 0,
                equipmentChoice.Roll, c2, out _, out var weightFailure);
            Check(!weightOk && weightFailure == LotteryUseFailure.Overweight, $"{id}: genuine overweight has its own failure despite empty equipment slots");
            Check(LotteryItemPlanner.GetClientErrorCode(weightFailure, true) == 21
                && LotteryItemPlanner.GetClientErrorCode(blockedFailure, true) == 4,
                $"{id}: Chapter Two distinguishes weight from full bag");
            Check(LotteryItemPlanner.GetClientErrorCode(weightFailure, false) == 4,
                $"{id}: Chapter One keeps existing rejection behavior");
            Check(unrelatedSnapshot.SequenceEqual(unrelated), $"{id}: overweight preserves jar and all source items");
            rows.Add(new { Id = id, Choices = choices.Count, lottery.FallbackReward, lottery.WeightedRewards });
        }
        // Keep the earlier five-jar acceptance profile reproducible. The expanded
        // audit is independent of the separately reviewed package restoration.
        if (!allJars)
            foreach (var id in new ushort[] { 27, 38, 39, 40, 41, 49, 52, 63, 69 })
                Check(!c2.Contains(id), $"cash package {id}: remains disabled in original five-jar profile");
        var result = new { Checks = checks, Plans = plans, Failures = failures,
            SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),
            TargetSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))), Items = rows };
        File.WriteAllText(Path.Combine(output, "jars.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Jar audit: {checks} checks, {plans} placement plans, {failures.Count} failures.");
        foreach (var failure in failures.Take(12)) Console.WriteLine(failure);
        return failures.Count == 0 ? 0 : 1;
    }
}
