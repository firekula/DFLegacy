using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoCeraContainerSmokeTests
{
    private static readonly ushort[] Restored = [38, 39, 40, 41, 49, 52, 53, 54, 55, 56];
    private static readonly ushort[] Unresolved = [27, 63, 64, 65, 66, 67, 69, 70, 71, 72, 73, 89, 92, 95, 98];
    private static readonly IReadOnlyDictionary<ushort, CharacterItemRecord> EmptyItems = new Dictionary<ushort, CharacterItemRecord>();

    internal static async Task RunAsync(string chapterOnePath, string chapterTwoPath, bool restoredShop,
        Action<bool, string> check)
    {
        var (c1, c1Shop) = Load(chapterOnePath, false);
        var (c2, c2Shop) = Load(chapterTwoPath, true);
        Console.WriteLine($"Cera definitions C1={c1.CeraBoosterCount}/{c1.CeraPackageCount}, C2={c2.CeraBoosterCount}/{c2.CeraPackageCount}; "
            + $"restored={string.Join(',', Restored.Where(c2.Contains))}; unresolved={string.Join(',', Unresolved.Where(c2.Contains))}");
        check(c2.CeraBoosterCount == 9 && c2.CeraPackageCount == 1
            && Restored.All(c2.Contains) && !Unresolved.Any(c2.Contains),
            "Chapter Two loads exactly the supported original package set without re-enabling unsupported definitions");
        check(c1.CeraBoosterCount == 19 && c1.CeraPackageCount == 2 && c1.Contains(27),
            "Chapter One keeps its original package definitions");
        check(!c1Shop.Products.Any(product => Restored.Contains(product.ItemId)),
            "Chapter One storefront still reads its original offers");
        check(!c2Shop.Products.Any(product => product.ItemId is 9 or 11 or 29 || Unresolved.Contains(product.ItemId)),
            "unsupported Chapter Two entitlements and containers cannot be purchased");
        if (restoredShop)
        {
            foreach (var id in Restored)
            {
                var price = id <= 41 ? 29800 : id == 49 ? 13550 : 22200;
                check(c2Shop.TryGetProduct((uint)(109000 + id), out var offer) && offer.ItemId == id
                    && offer.Quantity == 1 && offer.CashPrice == price && offer.ClientCategory == 2,
                    $"restored shop commodity {109000 + id} contains item {id} and charges historical price {price}");
                check(offer.GetPurchaseResultCategory(true) == (id == 49 ? 4 : 2)
                    && offer.GetPurchaseResultCategory(false) == 2,
                    $"actual restored commodity {109000 + id} selects its native result catalog without changing C1");
            }
            check(c2Shop.TryGetProduct(100053, out var original) && original.ItemId == 37
                && c2Shop.TryGetProduct(100054, out var hammer) && hammer.ItemId == 201,
                "restoring legacy offers leaves both conflicting existing commodities intact");
        }
        var bag = new Dictionary<ushort, CharacterItemRecord> { [41] = new(41, 49, 2) };
        var opened = CeraBoosterOpeningPlanner.TryCreate(bag, new Dictionary<ushort, CharacterItemRecord>(),
                new Dictionary<ushort, CharacterItemRecord>(), 41, c2, uint.MaxValue, GameRandomSource.Shared,
                out var plan, out var failure);
        check(opened && plan.MainInventory[41].CountOrValue == 1
                && plan.Rewards.Any(r => r.ItemId == 3 && r.Count == 1) && bag[41].CountOrValue == 2,
            $"fixed package 49 plans one backpack use without mutating its input stack: {failure}");
        check(!CeraBoosterOpeningPlanner.TryCreate(bag, EmptyItems, EmptyItems, 41, c1, uint.MaxValue, new ChoiceRandom(),
                out _, out _), "Chapter One fixed packages retain their prior backpack policy");
        await CheckFailedSaveAsync(check);
        if (!opened) return;
        CheckRewardRows(c1, c2, check);
        CheckCapacityAndInvalidRewards(c2, check);
        await CheckOpeningAndPurchaseAsync(c2, c2Shop, restoredShop, check);
    }

    private static (ItemCatalog Items, CeraShopCatalog Shop) Load(string path, bool chapterTwo)
    {
        var options = new ServerOptions { ScriptPvfPath = Path.GetFullPath(path), SkipUnsupportedChapterTwoCashContainers = chapterTwo };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        if (!scripts.IsPvf) throw new FileNotFoundException("A real script PVF is required", options.ScriptPvfPath);
        var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance, options);
        return (items, new CeraShopCatalog(scripts, items, NullLogger<CeraShopCatalog>.Instance));
    }

    private static void CheckRewardRows(ItemCatalog c1, ItemCatalog c2, Action<bool, string> check)
    {
        var totalRows = 2; // package 49 is fixed and covered by the transaction test
        var runs = 0;
        foreach (var id in Restored)
        {
            if (!c1.TryGetDefinition(id, out var before) || !c2.TryGetDefinition(id, out var after)) continue;
            check(before.MinimumLevel == 1 && after.MinimumLevel == 1 && before.UsableJobMask == after.UsableJobMask
                && Enumerable.Range(0, 5).All(after.IsUsableByJob), $"item {id} preserves the source job and level policy");
            if (id == 49)
            {
                check(before.CeraPackage!.Rewards.SequenceEqual(after.CeraPackage!.Rewards), "package 49 keeps both exact source rewards");
                continue;
            }

            var sourceGroups = before.CeraBooster!.Groups;
            var targetGroups = after.CeraBooster!.Groups;
            check(sourceGroups.Count == targetGroups.Count && sourceGroups.Zip(targetGroups).All(pair =>
                    pair.First.Kind == pair.Second.Kind && pair.First.DrawCount == pair.Second.DrawCount
                    && pair.First.TotalWeight == pair.Second.TotalWeight && pair.First.Rewards.SequenceEqual(pair.Second.Rewards)),
                $"booster {id} preserves the C1 reward groups, probabilities, quantities and avatar options");
            var input = new Dictionary<ushort, CharacterItemRecord> { [41] = new(41, id, 2) };
            var call = 0;
            foreach (var group in targetGroups)
            {
                var roll = 0;
                foreach (var reward in group.Rewards)
                {
                    totalRows++;
                    if (reward.Weight <= 0) continue;
                    runs++;
                    var bought = CeraShopContentPlanner.TryCreatePurchase(EmptyItems, EmptyItems, EmptyItems, after, 1, c2, uint.MaxValue,
                        new ChoiceRandom(call, roll), itemId => CeraShopContentPlanner.ShouldSettleAsEffect(c2, itemId),
                        out var purchase, out var purchaseFailure);
                    var opened = CeraBoosterOpeningPlanner.TryCreate(input, EmptyItems, EmptyItems, 41, c2, uint.MaxValue,
                        new ChoiceRandom(call, roll), out var open, out var openFailure);
                    var expected = new CeraShopContentReward(reward.ItemId, reward.Count, reward.AvatarAbilityIndex, reward.AvatarPeriodDays);
                    check(bought && opened && purchase.Rewards.Contains(expected) && purchase.Rewards.SequenceEqual(open.Rewards)
                        && open.MainInventory[41].CountOrValue == 1 && input[41].CountOrValue == 2
                        && RewardsPlacedExactly(purchase.MainInventory, purchase.AvatarInventory, purchase.CreatureInventory,
                            purchase.OverflowItems, purchase.Rewards, c2)
                        && RewardsPlacedExactly(open.MainInventory, open.AvatarInventory, open.CreatureInventory,
                            open.OverflowItems, open.Rewards, c2),
                        $"booster {id} reward row {reward.ItemId}/{roll} can be bought and opened: {purchaseFailure} {openFailure}");
                    roll += reward.Weight;
                }
                call += group.DrawCount;
            }
        }
        check(totalRows == 910, $"all 910 supported reward rows were traversed (actual {totalRows})");
        Console.WriteLine($"Reward traversal: {totalRows} rows; {runs} positive-weight forced choices on both buy and open paths.");
    }

    private static bool RewardsPlacedExactly(Dictionary<ushort, CharacterItemRecord> main,
        Dictionary<ushort, CharacterItemRecord> avatar, Dictionary<ushort, CharacterItemRecord> creature,
        IReadOnlyList<CharacterMailAttachmentRecord> overflow, IReadOnlyList<CeraShopContentReward> rewards, ItemCatalog catalog)
    {
        foreach (var group in rewards.GroupBy(reward => reward.ItemId))
        {
            if (!catalog.TryGetDefinition(group.Key, out var definition)) return false;
            var space = definition.InventoryCategory switch
            {
                ItemInventoryCategory.Avatar => avatar,
                ItemInventoryCategory.Creature => creature,
                _ => main
            };
            var actual = space.Values.Where(item => item.ItemId == group.Key).Sum(item =>
                             CharacterCreatureInventoryLayout.IsCreature(definition) ? 1L : item.CountOrValue)
                         + overflow.Where(item => item.ItemId == group.Key).Sum(item => (long)item.CountOrValue);
            if (actual != group.Sum(reward => (long)reward.Count)) return false;
        }
        return true;
    }

    private static void CheckCapacityAndInvalidRewards(ItemCatalog catalog, Action<bool, string> check)
    {
        var stack = new Dictionary<ushort, CharacterItemRecord> { [41] = new(41, 49, 1), [42] = new(42, 3, 1) };
        check(CeraBoosterOpeningPlanner.TryCreate(stack, EmptyItems, EmptyItems, 41, catalog, uint.MaxValue, new ChoiceRandom(), out var stacked, out _)
            && !stacked.MainInventory.ContainsKey(41) && stacked.MainInventory[42].CountOrValue == 2,
            "the final package copy disappears and a compatible reward stacks with an existing item");

        var weighted = catalog.Definitions.Values.First(d => d.InventoryCategory == ItemInventoryCategory.Consumable && d.Weight > 0);
        var booster = new Dictionary<ushort, CharacterItemRecord> { [41] = new(41, 38, 2), [42] = new(42, weighted.Id, 1) };
        var avatarFull = Enumerable.Range(0, 105).ToDictionary(i => (ushort)i, i => new CharacterItemRecord((ushort)i, 39009, 1));
        var creatureFull = Enumerable.Range(0, 140).ToDictionary(i => (ushort)i, i => new CharacterItemRecord((ushort)i, 63007, 1));
        check(CeraBoosterOpeningPlanner.TryCreate(booster, avatarFull, creatureFull, 41, catalog, 0, new ChoiceRandom(),
                out var overflow, out _) && overflow.MainInventory[41].CountOrValue == 1
                && overflow.OverflowItems.Any(item => catalog.TryGetDefinition(item.ItemId, out var def)
                    && def.InventoryCategory == ItemInventoryCategory.Avatar)
                && overflow.OverflowItems.Any(item => catalog.TryGetDefinition(item.ItemId, out var def)
                    && def.InventoryCategory == ItemInventoryCategory.Creature)
                && overflow.OverflowItems.Any(item => catalog.TryGetDefinition(item.ItemId, out var def)
                    && def.InventoryCategory is ItemInventoryCategory.Equipment or ItemInventoryCategory.Consumable),
            "a full avatar/creature space and a zero weight allowance send the genuine rewards to overflow mail");

        foreach (var reward in new ushort[] { 29, 65534 })
        {
            using var temporary = new TemporaryDirectory();
            var options = new ServerOptions { SkillScriptPath = temporary.Path, ScriptPvfPath = "", SkipUnsupportedChapterTwoCashContainers = true };
            var folder = Directory.CreateDirectory(Path.Combine(temporary.Path, "stackable"));
            File.WriteAllText(Path.Combine(folder.FullName, "stackable.lst"), "3 `water.stk`\n29 `unsupported.stk`\n49 `package.stk`\n");
            File.WriteAllText(Path.Combine(folder.FullName, "water.stk"), "[stackable type] `[etc]` 0\n");
            File.WriteAllText(Path.Combine(folder.FullName, "unsupported.stk"), "[stackable type] `[etc]` 0\n");
            File.WriteAllText(Path.Combine(folder.FullName, "package.stk"),
                $"[stackable type] `[cera package]` 0\n[package data]\n3 1\n{reward} 1\n[/package data]\n");
            var altered = new ItemCatalog(new(options, NullLogger<ScriptFileSystem>.Instance), NullLogger<ItemCatalog>.Instance, options);
            var source = new Dictionary<ushort, CharacterItemRecord> { [41] = new(41, 49, 2) };
            check(!CeraBoosterOpeningPlanner.TryCreate(source, EmptyItems, EmptyItems, 41, altered, uint.MaxValue, new ChoiceRandom(), out _, out _)
                && source.Count == 1 && source[41].CountOrValue == 2,
                $"package with missing or unsupported reward {reward} fails as a whole without consuming its source");
        }
    }

    private static async Task CheckOpeningAndPurchaseAsync(ItemCatalog items, CeraShopCatalog shop, bool restoredShop,
        Action<bool, string> check)
    {
        const string account = "cera-atomic";
        const uint contractSeconds = 15 * 24 * 60 * 60;
        using var temporary = new TemporaryDirectory();
        var options = new ServerOptions { DataPath = Path.Combine(temporary.Path, "state.json"), SeedTestAccount = false };
        var fail = false;
        var replacement = new AtomicSnapshotReplacement((source, target) =>
        {
            if (fail) throw new IOException("test blocked package snapshot replacement");
            File.Move(source, target, overwrite: true);
        }, (_, _) => Task.CompletedTask);
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, replacement, items);
        await store.InitializeAsync();
        await store.CreateAccountAsync(new(account, "fixture-only", OicqCode: "123456"));
        var created = (await store.CreateCharacterAsync(account, new("PackOpen"))).Character!;
        var fullBag = Enumerable.Range(3, 6).Concat(Enumerable.Range(41, 32))
            .Select(slot => new CharacterItemRecord((ushort)slot, slot == 41 ? (ushort)49 : (ushort)8,
                slot == 41 ? 2u : 1u)).ToArray();
        var original = (await store.SaveCharacterCeraShopPurchaseAsync(account, created.Id, 20000, 777, fullBag, []))!;
        var beforeDisk = await File.ReadAllBytesAsync(options.DataPath);
        var openingSucceeded = CeraBoosterOpeningPlanner.TryCreate(original.Inventory!.ToDictionary(item => item.Slot),
            EmptyItems, EmptyItems, 41, items, uint.MaxValue, new ChoiceRandom(), out var plan, out var failure,
            original.Gold, original.VictoryPoints, original.WarehouseCapacity);
        check(openingSucceeded && plan.OverflowItems is [{ ItemId: 3, CountOrValue: 1 }]
            && plan.ImmediateEffects?.PremiumServices is [{ ServiceType: GameProtocolEngine.MasterContractServiceType, Days: 15 }]
            && !plan.MainInventory.Values.Any(item => item.ItemId is 3 or 46),
            $"full backpack routes Lethe's Water to mail and plans the actual 15-day account contract: {failure}");
        if (!openingSucceeded) return;

        var mails = EntranceService.CreateOverflowMailRequests(plan.OverflowItems, "Cera container reward");
        fail = true;
        try
        {
            await store.SaveCharacterCeraContainerOpeningAsync(account, original, plan, mails);
            check(false, "a blocked package opening snapshot is reported to its caller");
        }
        catch (IOException) { }
        var blocked = (await store.GetCharactersAsync(account)).Single();
        var afterDisk = await File.ReadAllBytesAsync(options.DataPath);
        check(blocked.Cash == 20000 && blocked.Gold == 777 && blocked.Inventory!.SequenceEqual(original.Inventory!)
            && !(await store.GetPremiumServicesAsync(account)).Any(service => service.Active)
            && (await store.GetCharacterMailsAsync(created.Id)).Length == 0 && beforeDisk.SequenceEqual(afterDisk),
            "a failed backpack save preserves currency, source stack, mail and premium both live and on disk");

        fail = false;
        var opened = await store.SaveCharacterCeraContainerOpeningAsync(account, original, plan, mails);
        check(opened?.Inventory?.Single(item => item.Slot == 41).CountOrValue == 1 && opened.Cash == 20000 && opened.Gold == 777,
            "a successful opening consumes one of two packages and leaves Cera and gold unchanged");
        if (opened is null) return;
        var firstContract = (await store.GetPremiumServicesAsync(account)).SingleOrDefault(service =>
            service.ServiceType == GameProtocolEngine.MasterContractServiceType);
        var firstMails = await store.GetCharacterMailsAsync(created.Id);
        check(firstContract is { Active: true, RemainingSeconds: >= contractSeconds - 10 and <= contractSeconds }
            && firstMails is [{ Attachment: { ItemId: 3, CountOrValue: 1 } }],
            "one successful opening creates exactly one mail and grants the original contract duration");
        check(await store.SaveCharacterCeraContainerOpeningAsync(account, original, plan, mails) is null
            && (await store.GetCharacterMailsAsync(created.Id)).Length == firstMails.Length
            && (await store.GetPremiumServicesAsync(account)).SingleOrDefault(service =>
                service.ServiceType == GameProtocolEngine.MasterContractServiceType)?.ExpiresAt == firstContract?.ExpiresAt,
            "replaying a stale opening cannot duplicate a contract or an overflow mail");

        var reloadedStore = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
        await reloadedStore.InitializeAsync();
        var reloaded = (await reloadedStore.GetCharactersAsync(account)).Single();
        var mail = (await reloadedStore.GetCharacterMailsAsync(created.Id)).SingleOrDefault();
        check(reloaded.Inventory!.Single(item => item.Slot == 41).CountOrValue == 1 && mail?.Id == firstMails.SingleOrDefault()?.Id
            && (await reloadedStore.GetPremiumServicesAsync(account)).SingleOrDefault(service =>
                service.ServiceType == GameProtocolEngine.MasterContractServiceType)?.ExpiresAt == firstContract?.ExpiresAt,
            "opening source, overflow mail and account contract survive a fresh store load");
        if (mail is null) return;

        var full = reloaded.Inventory!.ToDictionary(item => item.Slot);
        check(!EntranceService.TryPlanMailAttachment(full, EmptyItems, EmptyItems, mail.Attachment, items, uint.MaxValue,
                out _, out _, out _, out _, out _, out _), "full backpack leaves the overflow reward waiting to be claimed");
        var cleared = (await reloadedStore.SaveCharacterItemSpacesAsync(account, created.Id, reloaded.Gold,
            full.Values.Where(item => item.Slot != 42), reloaded.Equipment ?? [], reloaded.Warehouse ?? []))!;
        var canClaim = EntranceService.TryPlanMailAttachment(cleared.Inventory!.ToDictionary(item => item.Slot), EmptyItems, EmptyItems,
            mail.Attachment, items, uint.MaxValue, out var claimedItems, out var claimedAvatars, out var claimedCreatures,
            out var listType, out var destination, out var unclaimed);
        check(canClaim && listType == 0 && destination == 42 && unclaimed == 0, "freeing a slot makes the actual overflow reward claimable");
        if (!canClaim) return;
        var claimed = await reloadedStore.ClaimCharacterMailAsync(account, created.Id, mail.Id, cleared.Gold,
            claimedItems.Values, cleared.Equipment ?? [], avatarInventory: claimedAvatars.Values, creatureInventory: claimedCreatures.Values,
            expectedMail: mail, unclaimedCount: unclaimed);
        check(claimed?.Character.Inventory?.Single(item => item.Slot == 42) is { ItemId: 3, CountOrValue: 1 }
            && await reloadedStore.ClaimCharacterMailAsync(account, created.Id, mail.Id, cleared.Gold,
                claimedItems.Values, cleared.Equipment ?? [], expectedMail: mail) is null,
            "the normal mailbox claim delivers the item once and rejects a second claim");
        if (!restoredShop || claimed is null || !shop.TryGetProduct(109049, out var offer) || !items.TryGetDefinition(49, out var package)) return;

        var buyer = claimed.Character;
        var purchaseOk = CeraShopContentPlanner.TryCreatePurchase(buyer.Inventory!.ToDictionary(item => item.Slot),
            EmptyItems, EmptyItems, package, offer.Quantity, items, uint.MaxValue, new ChoiceRandom(),
            itemId => CeraShopContentPlanner.ShouldSettleAsEffect(items, itemId), out var purchase, out _);
        check(purchaseOk, "the restored live shop offer uses the fixed content purchase planner");
        if (!purchaseOk) return;
        var settled = CeraShopContentPlanner.TrySettleImmediateEffects(purchase.Rewards, items, buyer.Gold,
            buyer.VictoryPoints, buyer.WarehouseCapacity, out var effects);
        check(settled, "the restored shop offer settles its actual account contract before saving");
        if (!settled) return;
        var bought = await reloadedStore.SaveCharacterCeraShopPurchaseAsync(account, created.Id, buyer.Cash - offer.CashPrice,
            effects.Gold, purchase.MainInventory.Values, buyer.Equipment ?? [], avatarInventory: purchase.AvatarInventory.Values,
            creatureInventory: purchase.CreatureInventory.Values, premiumServiceGrants: effects.PremiumServices,
            expectedCash: buyer.Cash, victoryPoints: effects.VictoryPoints, expectedVictoryPoints: buyer.VictoryPoints,
            warehouseCapacity: effects.WarehouseCapacity, expectedWarehouseCapacity: buyer.WarehouseCapacity, expectedCharacter: buyer);
        var secondContract = (await reloadedStore.GetPremiumServicesAsync(account)).SingleOrDefault(service =>
            service.ServiceType == GameProtocolEngine.MasterContractServiceType);
        check(bought is { Cash: 6450, Gold: 777 } && bought.Inventory!.Single(item => item.ItemId == 3).CountOrValue == 2
            && bought.Inventory!.Single(item => item.ItemId == 49).CountOrValue == 1
            && secondContract?.ExpiresAt == firstContract?.ExpiresAt + contractSeconds,
            "buying 49 charges 13,550 Cera, gives one Lethe's Water, and extends the account contract by 15 days");

        var companion = (await reloadedStore.CreateCharacterAsync(account, new("PackFull"))).Character!;
        var inboxFull = (await reloadedStore.SaveCharacterCeraShopPurchaseAsync(account, companion.Id, 6450, 444, fullBag, [],
            generatedMails: Enumerable.Repeat(new CreateCharacterMailRequest("Fixture", "Mailbox occupied"), 255)))!;
        var canPlanFullMailbox = CeraBoosterOpeningPlanner.TryCreate(inboxFull.Inventory!.ToDictionary(item => item.Slot),
            EmptyItems, EmptyItems, 41, items, uint.MaxValue, new ChoiceRandom(), out var fullMailboxPlan, out _, inboxFull.Gold);
        check(canPlanFullMailbox, "the same full bag plans an overflow reward for a character with a full inbox");
        if (!canPlanFullMailbox) return;
        var rejected = await reloadedStore.SaveCharacterCeraContainerOpeningAsync(account, inboxFull, fullMailboxPlan,
            EntranceService.CreateOverflowMailRequests(fullMailboxPlan.OverflowItems, "Cera container reward"));
        var afterRejection = (await reloadedStore.GetCharactersAsync(account)).Single(character => character.Id == companion.Id);
        check(rejected is null && afterRejection.Inventory!.Single(item => item.Slot == 41).CountOrValue == 2
            && afterRejection.Cash == 6450 && afterRejection.Gold == 444
            && (await reloadedStore.GetCharacterMailsAsync(companion.Id)).Length == 255
            && (await reloadedStore.GetPremiumServicesAsync(account)).SingleOrDefault(service =>
                service.ServiceType == GameProtocolEngine.MasterContractServiceType)?.ExpiresAt == secondContract?.ExpiresAt,
            "a full mailbox rejects the package use without taking currency, consuming the source or extending the contract");
    }

    private static async Task CheckFailedSaveAsync(Action<bool, string> check)
    {
        using var temporary = new TemporaryDirectory();
        var options = new ServerOptions { DataPath = Path.Combine(temporary.Path, "state.json"), SeedTestAccount = false };
        var fail = false;
        var replacer = new AtomicSnapshotReplacement((source, target) =>
        {
            if (fail) throw new IOException("test blocked replacement before writing the snapshot");
            File.Move(source, target, overwrite: true);
        }, (_, _) => Task.CompletedTask);
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, replacer);
        await store.CreateAccountAsync(new("cera-pack", "fixture-only", OicqCode: "123456"));
        var character = (await store.CreateCharacterAsync("cera-pack", new("PackTest"))).Character!;
        var seeded = (await store.SaveCharacterCeraShopPurchaseAsync("cera-pack", character.Id, 20000, 1234,
            [new(41, 49, 2)], []))!;
        var before = await File.ReadAllBytesAsync(options.DataPath);
        fail = true;
        try
        {
            await store.SaveCharacterCeraShopPurchaseAsync("cera-pack", character.Id, 6450, 4321,
                [new(41, 49, 1), new(42, 3, 1)], [],
                generatedMails: [new("System", "Overflow", Attachment: new(3, 1))],
                premiumServiceGrants: [new(GameProtocolEngine.MasterContractServiceType, 15)],
                expectedCash: seeded.Cash);
            check(false, "a blocked purchase snapshot is reported to its caller");
        }
        catch (IOException) { }
        var current = (await store.GetCharactersAsync("cera-pack")).Single();
        check(current.Cash == 20000 && current.Gold == 1234 && current.Inventory!.Single().CountOrValue == 2
            && (await store.GetCharacterMailsAsync(current.Id)).Length == 0
            && !(await store.GetPremiumServicesAsync("cera-pack")).Any(service => service.Active),
            "failed purchase preserves the source stack, account wallet, gold, mailbox and contract in memory");
        var after = await File.ReadAllBytesAsync(options.DataPath);
        check(before.SequenceEqual(after),
            "failed purchase does not change the durable snapshot");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("dflegacy-cera-packages-");
        internal string Path => _directory.FullName;
        public void Dispose() => _directory.Delete(recursive: true);
    }

    private sealed class ChoiceRandom(int targetCall = -1, int targetRoll = 0) : IDropRandomSource
    {
        private int _call;
        public int Next(int exclusiveMaximum)
        {
            var value = _call++ == targetCall ? targetRoll : 0;
            if (value < 0 || value >= exclusiveMaximum) throw new InvalidOperationException("Fixture roll is out of range.");
            return value;
        }
    }
}
