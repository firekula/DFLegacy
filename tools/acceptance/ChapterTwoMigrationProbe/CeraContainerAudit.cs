using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

// Read-only catalog and real-planner audit. No running service, save, or resource is changed.
internal static class CeraContainerAudit
{
    internal static int Run(string sourcePvf, string targetPvf, string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(output);
        var c1Scripts = Scripts(sourcePvf);
        var c2Scripts = Scripts(targetPvf);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(936);
        var c1 = new ItemCatalog(c1Scripts, NullLogger<ItemCatalog>.Instance);
        var c2 = new ItemCatalog(c2Scripts, NullLogger<ItemCatalog>.Instance,
            new ServerOptions { SkipUnsupportedChapterTwoCashContainers = true });
        var shop = new CeraShopCatalog(c1Scripts, c1, NullLogger<CeraShopCatalog>.Instance);
        var candidates = c1.Definitions.Values.Where(d => d.CeraBooster is not null || d.CeraPackage is not null
            || d.Name.Contains("礼包") || d.Name.Contains("福包") || d.Name.Contains("礼盒") || d.Name.Contains("包裹"))
            .OrderBy(d => d.Id).ToArray();
        var results = new List<object>();
        foreach (var item in candidates)
        {
            File.WriteAllText(Path.Combine(output, $"c1-{item.Id}.stk"), c1Scripts.ReadAllText(item.ScriptPath, encoding));
            string? c2ParseError = null;
            object? c2Parsed = null;
            if (c2Scripts.FileExists(item.ScriptPath))
            {
                var raw = c2Scripts.ReadAllText(item.ScriptPath, encoding);
                File.WriteAllText(Path.Combine(output, $"c2-{item.Id}.stk"), raw);
                if (item.CeraBooster is not null || item.CeraPackage is not null)
                {
                    // Exercise exactly the current parser per raw item, even though the C2
                    // catalog policy excludes it. Reflection is confined to this evidence tool.
                    var method = typeof(ItemCatalog).GetMethod(item.CeraBooster is null ? "ReadCeraPackage" : "ReadCeraBooster",
                        BindingFlags.Static | BindingFlags.NonPublic)!;
                    try { c2Parsed = method.Invoke(null, [item.TypeTag, raw, item.ScriptPath]); }
                    catch (TargetInvocationException ex) { c2ParseError = ex.InnerException?.Message ?? ex.Message; }
                }
            }

            var groups = item.CeraBooster?.Groups;
            var rewardRows = groups is not null
                ? groups.SelectMany(g => g.Rewards.Select(r => new CeraShopContentReward(r.ItemId, r.Count, r.AvatarAbilityIndex, r.AvatarPeriodDays))).ToArray()
                : item.CeraPackage?.Rewards.Select(r => new CeraShopContentReward(r.ItemId, r.Count)).ToArray() ?? [];
            var missing = rewardRows.Where(r => !c1.Contains(r.ItemId)).Select(r => r.ItemId).Distinct().ToArray();
            var scenarios = new List<(int Call, int Roll)> { (-1, 0) };
            var offset = 0;
            if (groups is not null)
            {
                foreach (var group in groups)
                {
                    var cumulative = 0;
                    foreach (var reward in group.Rewards)
                    {
                        scenarios.Add((offset, cumulative));
                        cumulative += reward.Weight;
                    }
                    offset += group.DrawCount;
                }
            }
            var purchaseFailures = new List<string>();
            var openFailures = new List<string>();
            var purchaseRuns = 0;
            var openRuns = 0;
            var unchangedOnFailure = true;
            foreach (var scenario in scenarios)
            {
                if (item.CeraPackage is not null || groups is not null)
                {
                    purchaseRuns++;
                    var ok = CeraShopContentPlanner.TryCreatePurchase(new Dictionary<ushort, CharacterItemRecord>(),
                        new Dictionary<ushort, CharacterItemRecord>(), new Dictionary<ushort, CharacterItemRecord>(),
                        item, 1, c1, uint.MaxValue, new ChoiceRandom(scenario.Call, scenario.Roll),
                        id => CharacterWarehouseProgression.TryGetItemTargetCapacity(id, out _)
                            || shop.TryGetPremiumContract(id, out _) || shop.TryGetDirectCurrencyGrant(id, out _),
                        out _, out var reason);
                    if (!ok) purchaseFailures.Add(reason);
                }
                if (groups is not null)
                {
                    openRuns++;
                    var source = new CharacterItemRecord(41, item.Id, 2, InstanceId: Guid.NewGuid());
                    var inventory = new Dictionary<ushort, CharacterItemRecord> { [41] = source };
                    var ok = CeraBoosterOpeningPlanner.TryCreate(inventory, new Dictionary<ushort, CharacterItemRecord>(),
                        new Dictionary<ushort, CharacterItemRecord>(), 41, c1, uint.MaxValue,
                        new ChoiceRandom(scenario.Call, scenario.Roll), out var plan, out var reason);
                    if (!ok) openFailures.Add(reason);
                    else if (!plan.MainInventory.TryGetValue(41, out var remainder) || remainder.CountOrValue != 1)
                        openFailures.Add("Source container was not consumed exactly once.");
                    unchangedOnFailure &= inventory.Count == 1 && inventory[41] == source;
                }
            }
            results.Add(new
            {
                item.Id, item.Name, item.TypeTag, item.ScriptPath,
                C1Products = shop.Products.Where(p => p.ItemId == item.Id).ToArray(),
                C1Rewards = rewardRows.Select(r => new {
                    r.ItemId, Name = c1.TryGetDefinition(r.ItemId, out var d) ? d.Name : "MISSING", d?.TypeTag, d?.ScriptPath,
                    r.Count, r.AvatarPeriodDays, r.AvatarAbilityIndex,
                    WarehouseCapacity = CharacterWarehouseProgression.TryGetItemTargetCapacity(r.ItemId, out var cap) ? cap : (ushort?)null,
                    Contract = shop.TryGetPremiumContract(r.ItemId, out var contract) ? contract : null,
                    C2RewardDefined = c2.Contains(r.ItemId)
                }),
                MissingRewardIds = missing, PurchasePlannerRuns = purchaseRuns,
                PurchasePlannerFailures = purchaseFailures.Distinct().ToArray(),
                BackpackPlannerRuns = openRuns, BackpackPlannerFailures = openFailures.Distinct().ToArray(),
                InputInventoryUnchanged = unchangedOnFailure, C2Enabled = c2.Contains(item.Id),
                C2ParserError = c2ParseError, C2ParsedContents = c2Parsed
            });
        }
        var inputs = new[] { sourcePvf, targetPvf }.Select(p => new
        { Path = Path.GetFullPath(p), SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) });
        var json = new JsonSerializerOptions { WriteIndented = true };
        var targetContainers = new List<object>();
        foreach (Match entry in Regex.Matches(c2Scripts.ReadAllText("stackable/stackable.lst", encoding), @"(?m)^\s*(\d+)\s+`([^`]+)`"))
        {
            var path = "stackable/" + entry.Groups[2].Value.Replace('\\', '/');
            if (!c2Scripts.FileExists(path)) continue;
            var raw = c2Scripts.ReadAllTextUncached(path, encoding);
            var type = Regex.Match(raw, @"\[stackable type\]\s*`\[(cera booster|cera package)\]`");
            if (!type.Success) continue;
            string? error = null;
            var method = typeof(ItemCatalog).GetMethod(type.Groups[1].Value == "cera package" ? "ReadCeraPackage" : "ReadCeraBooster", BindingFlags.Static | BindingFlags.NonPublic)!;
            try { method.Invoke(null, [type.Groups[1].Value, raw, path]); }
            catch (TargetInvocationException ex) { error = ex.InnerException?.Message ?? ex.Message; }
            targetContainers.Add(new { Id = int.Parse(entry.Groups[1].Value), Path = path, Type = type.Groups[1].Value, Error = error });
        }
        foreach (var id in new ushort[] { 3, 7, 8, 29, 46 })
            if (c1.TryGetDefinition(id, out var definition))
                File.WriteAllText(Path.Combine(output, $"c1-reward-{id}.stk"), c1Scripts.ReadAllText(definition.ScriptPath, encoding));
        File.WriteAllText(Path.Combine(output, "c2-all-containers.json"), JsonSerializer.Serialize(targetContainers, json));
        File.WriteAllText(Path.Combine(output, "inputs.json"), JsonSerializer.Serialize(inputs, json));
        File.WriteAllText(Path.Combine(output, "containers.json"), JsonSerializer.Serialize(results, json));
        Console.WriteLine($"Audited {candidates.Length} named/container definitions, {c1.CeraBoosterCount} boosters, {c1.CeraPackageCount} packages. See containers.json for per-path failures.");
        return 0; // An audit finding is recorded, not hidden by failing to write the report.
    }

    private static ScriptFileSystem Scripts(string path) => new(new ServerOptions { ScriptPvfPath = Path.GetFullPath(path) }, NullLogger<ScriptFileSystem>.Instance);

    private sealed class ChoiceRandom(int targetCall, int targetRoll) : IDropRandomSource
    {
        private int _call;
        public int Next(int exclusiveMaximum)
        {
            var value = _call++ == targetCall ? targetRoll : 0;
            if (value < 0 || value >= exclusiveMaximum) throw new InvalidOperationException("Invalid deterministic audit choice.");
            return value;
        }
    }
}
