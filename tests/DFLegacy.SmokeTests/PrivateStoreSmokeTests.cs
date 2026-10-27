using DFLegacy.Protocol;
using System.Text;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;

internal static class PrivateStoreSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        void Vector(GameServerPacket p, byte type, byte id, string hex) => check(
            p.Type == type && p.ProtocolId == id && Convert.ToHexString(p.Payload) == hex,
            $"ACT1 store packet {type}/{id} preserves its native byte contract");
        var pos = new PrivateStorePosition(1, 0, 1000, 250);
        var g = new PrivateStoreGoodsRow(2, 101, 3033, 3, 4, 5);
        const string row = "020065000000D90B03000000040500";
        Vector(GameProtocolEngine.CreatePrivateStoreReply(88), 1, 88, "01");
        Vector(GameProtocolEngine.CreatePrivateStoreReply(88, 63), 1, 88, "003F");
        Vector(GameProtocolEngine.CreatePrivateStoreCreated(7, pos), 0, 78, "07000100E803FA00");
        Vector(GameProtocolEngine.CreatePrivateStoreRemoved(7), 0, 79, "0700");
        Vector(GameProtocolEngine.CreatePrivateStoreForceClose(), 0, 80, "");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var title = Encoding.GetEncoding(936).GetBytes("店");
        Vector(GameProtocolEngine.CreatePrivateStoreDisplayed(7, title), 0, 81, "070002000000B5EA");
        Vector(GameProtocolEngine.CreatePrivateStoreSale(97, 3033, 2, 1, [65], 101, 4), 0, 82,
            "0161000000D90B02000100000001000000416500000004000000");
        Vector(GameProtocolEngine.CreatePrivateStoreSaleFailure(22), 0, 82, "0016");
        Vector(GameProtocolEngine.CreatePrivateStoreGoodsSold(2, 1), 0, 83, "020001000000");
        Vector(GameProtocolEngine.CreatePrivateStoreList([new(7, title, true, pos)]), 0, 84, "01070002000000B5EA010100E803FA00");
        Vector(GameProtocolEngine.CreatePrivateStoreInfo(0, [g]), 0, 85, "0001" + row);
        Vector(GameProtocolEngine.CreatePrivateStoreEnterReply(7, [g]), 1, 86, "01070001" + row);
        Vector(GameProtocolEngine.CreatePrivateStoreEnterFailure(), 1, 86, "00");
        Vector(GameProtocolEngine.CreatePrivateStoreBuyReply(5, 6, 7, 41, 3033, 3, 4, 5), 1, 85,
            "010500000006000000070000002900D90B03000000040500");
        check(GameProtocolEngine.TryReadPrivateStoreCreate(Convert.FromHexString("01000100E803FA00"), out var position) && position == pos
            && !GameProtocolEngine.TryReadPrivateStoreCreate(Convert.FromHexString("01000100E803FA000000"), out _), "create reads the ACT1 six-byte payload without a puppet slot");
        check(GameProtocolEngine.TryReadPrivateStoreEnter(Convert.FromHexString("010078563412"), out var uid) && uid == 0x12345678
            && !GameProtocolEngine.TryReadPrivateStoreEnter([1, 0, 7, 0], out _), "enter preserves the full u32 request UID");
        check(GameProtocolEngine.TryReadPrivateStoreBuy(Convert.FromHexString("0100020000000000"), out var slot, out var info) && slot == 2 && info == 0
            && !GameProtocolEngine.TryReadPrivateStoreBuy(Convert.FromHexString("01000200FFFFFFFF"), out _, out _), "buy permits zero equipment quality but rejects negative signed quantity");
        var display = Convert.FromHexString("010002000000B5EA0102006500000000490003000000");
        check(GameProtocolEngine.TryReadPrivateStoreDisplay(display, out var parsedTitle, out var rows)
            && parsedTitle.SequenceEqual(title) && rows.Count == 1 && rows[0] == new PrivateStoreDisplayRequest(2, 101, 0, 73, 3), "display reads exact ACT1 rows and CP936 title");
        check(!GameProtocolEngine.TryReadPrivateStoreDisplay([.. display, 0, 0], out _, out _)
            && !GameProtocolEngine.TryReadPrivateStoreDisplay(display[..^1], out _, out _)
            && !GameProtocolEngine.TryReadPrivateStoreDisplay(Convert.FromHexString("0100FFFFFFFF"), out _, out _)
            && !GameProtocolEngine.TryReadPrivateStoreDisplay(Convert.FromHexString("0100010000000000"), out _, out _), "display rejects extra puppet fields, truncation, oversized lengths and NUL titles");
        await CheckDomainAsync(check);
        await CheckRecoveryAsync(check);
        await CheckRacesAndCapacityAsync(check);
        await CheckRejectionsAsync(check);
    }

    private static async Task CheckDomainAsync(Action<bool, string> check)
    {
        await using var f = await StoreFixture.CreateAsync();
        check(f.Catalog.TryGetGoldLimit(60, out var cap) && cap == 300_000_000
            && f.Catalog.TryGetGoldLimit(1, out cap) && cap == 100_000, "store gold caps come from explicit level rows");
        check(f.Catalog.CheckPosition(new(1, 0, 1000, 250)) == 0
            && f.Catalog.CheckPosition(new(1, 1, 1000, 250)) == 62
            && f.Catalog.CheckPosition(new(1, 0, 10, 10)) == 62, "store positions require enabled non-gate area and walkable coordinates");
        check(f.Catalog.CheckPosition(new(1, 0, 445, 250)) == 82
            && f.Catalog.CheckPosition(new(1, 0, 525, 250)) == 0, "NPC exclusion uses strict80 horizontal boundary");
        check(PrivateStoreCatalog.Collides(new(1, 0, 1000, 250), new(1, 0, 1160, 350))
            && !PrivateStoreCatalog.Collides(new(1, 0, 1000, 250), new(1, 0, 1161, 350)), "store rectangles reject touching inclusive boundaries");
        var token = Guid.NewGuid();
        check(f.Store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id), "store owner acquires exclusive commerce activity");
        var bad = await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 3), new(1, 101, 0, 73, 3)]);
        check(bad.Status == DungeonRewardLedgerStatus.Rejected && (await f.ReadA()).Inventory!.Single(i => i.Slot == 73).CountOrValue == 10,
            "duplicate source rejection leaves all stock in inventory");
        var opened = await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 5), new(1, 24, 0, 9, 37)]);
        check(opened.Status == DungeonRewardLedgerStatus.Committed && opened.Character!.PrivateStoreStock!.Listings.Count == 2
            && opened.Character.Inventory!.Single(i => i.Slot == 73).CountOrValue == 5
            && opened.Character.Inventory!.All(i => i.Slot != 9), "display atomically withdraws partial stock and whole equipment");
        var stale = await f.Store.SaveCharacterInventoryAsync("alice", f.A.Id, f.A.Gold, f.A.Inventory!, []);
        check(stale is null, "an owner blind inventory save cannot resurrect escrowed items");
        var sale = await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 1);
        check(sale.Status == DungeonRewardLedgerStatus.Committed && sale.Gross == 101 && sale.Fee == 4
            && sale.Buyer!.Gold == 899 && sale.ProjectedSellerGold == 1097
            && (await f.ReadA()).Gold == 1000, "101 gold sale debits buyer once and holds97 net for the seller actor");
        var paid = await f.Store.ConsumePrivateStoreGoldCreditsAsync(f.A.Id);
        check(paid!.Gold == 1097 && await f.Store.ConsumePrivateStoreGoldCreditsAsync(f.A.Id) is null,
            "seller credit consumption persists exactly once");
        var gear = await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 1, 37);
        check(gear.Status == DungeonRewardLedgerStatus.Committed && gear.Fee == 0
            && gear.Item!.EquipmentQualitySeed == 37 && gear.Item.State == 3 && gear.Item.Durability == 19
            && gear.Item.InstanceId == f.GearId, "24gold equipment keeps quality, reinforcement, durability and identity with zero rounded fee");
        var result = await f.Store.ClosePrivateStoreAsync(f.A.Id, token, false);
        f.Store.Commerce.Release(token, f.A.Id);
        check(result.Status == DungeonRewardLedgerStatus.Committed && result.Character!.Gold == 1121
            && result.Character.PrivateStoreStock is null
            && result.Character.Inventory!.Single(i => i.Slot == 73).CountOrValue == 9,
            "closing consumes sale proceeds and returns only remaining stock");
        var loaded = f.Reload(); await loaded.InitializeAsync();
        check((await loaded.GetCharactersAsync("alice"))[0].Gold == 1121
            && (await loaded.GetCharactersAsync("bobby"))[0].Gold == 875, "completed store balances survive restart");
    }

    private sealed class StoreFailingWriter(bool afterReplace) : IDungeonRewardSnapshotWriter
    {
        internal bool Armed; internal int Calls;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken ct)
        {
            Calls++;
            if (Armed && !afterReplace) throw new IOException("before store snapshot replace");
            await new DungeonRewardSnapshotWriter().WriteAsync(path, snapshot, ct);
            if (Armed) throw new IOException("after store snapshot replace");
        }
    }
    private static async Task CheckRecoveryAsync(Action<bool, string> check)
    {
        foreach (var operation in new[] { "display", "buy", "close", "consume" })
        foreach (var after in new[] { false, true })
        {
            await using var f = await StoreFixture.CreateAsync();
            var writer = new StoreFailingWriter(after);
            var store = new JsonGameStore(f.Options, NullLogger<JsonGameStore>.Instance, writer, itemCatalog: f.Items, statCatalog: f.Stats);
            await store.InitializeAsync(); var token = Guid.NewGuid(); store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id);
            if (operation != "display") await store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 5)]);
            if (operation is "close" or "consume") await store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 1);
            writer.Armed = true;
            DungeonRewardLedgerStatus status;
            if (operation == "display") status = (await store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 5)])).Status;
            else if (operation == "buy") status = (await store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 1)).Status;
            else if (operation == "close") status = (await store.ClosePrivateStoreAsync(f.A.Id, token, false)).Status;
            else { try { await store.ConsumePrivateStoreGoldCreditsAsync(f.A.Id); status = DungeonRewardLedgerStatus.Committed; }
                catch (IOException) { status = DungeonRewardLedgerStatus.Unknown; } }
            var calls = writer.Calls;
            var retry = await store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 1);
            check(status == DungeonRewardLedgerStatus.Unknown && retry.Status == DungeonRewardLedgerStatus.Unknown && writer.Calls == calls,
                $"{operation} uncertain {(after ? "after" : "before")} snapshot quarantines without another write");
            var recovered = f.Reload(); await recovered.InitializeAsync();
            var a = (await recovered.GetCharactersAsync("alice"))[0]; var b = (await recovered.GetCharactersAsync("bobby"))[0];
            var sold = operation is "close" or "consume" || operation == "buy" && after;
            check(a.PrivateStoreStock is null && a.PendingPrivateStoreGoldCredits is null
                && a.Gold == (sold ? 1097 : 1000) && b.Gold == (sold ? 899 : 1000)
                && a.Inventory!.Single(i => i.Slot == 73).CountOrValue == (sold ? 9 : 10),
                $"{operation} {(after ? "after" : "before")} restart follows actual disk exactly once and returns unsold escrow");
        }
    }
    private static async Task CheckRacesAndCapacityAsync(Action<bool, string> check)
    {
        await using (var f = await StoreFixture.CreateAsync())
        {
            var token = Guid.NewGuid(); f.Store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id);
            await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 5)]);
            var attempts = await Task.WhenAll(f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 5), f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 5));
            check(attempts.Count(x => x.Status == DungeonRewardLedgerStatus.Committed) == 1
                && (await f.ReadA()).PrivateStoreStock!.Listings.Count == 0, "concurrent final-stock purchase commits once and leaves the sold-out store open");
            check(!f.Store.Commerce.TryAcquire(CommerceActivity.Trade, Guid.NewGuid(), f.A.Id, f.B.Id), "store activity atomically prevents a two-person trade admission");
        }
        await using (var f = await StoreFixture.CreateAsync())
        {
            var token = Guid.NewGuid(); f.Store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id);
            await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 5)]);
            var sale = await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 1);
            using var dungeon = new DungeonFixture();
            var award = await f.Store.CommitDungeonGoldDistributionAsync(Guid.NewGuid(), [dungeon.Member(f.A.Id, 0, f.A.Name)], 0, int.MaxValue);
            check(award.Amounts[0] == int.MaxValue - 1097u, "dungeon gold capacity reserves pending97gold private-sale proceeds");
            var withinCap = false;
            try
            {
                var consumeDungeon = await f.Store.ConsumeDungeonGoldCreditsAsync(f.A.Id);
                var consumeStore = await f.Store.ConsumePrivateStoreGoldCreditsAsync(f.A.Id);
                withinCap = consumeDungeon!.Gold == int.MaxValue - 97 && consumeStore!.Gold == int.MaxValue;
            }
            catch (OverflowException) { }
            check(withinCap, "independent credit actors can consume both types without exceeding the integer cap");
        }
        await using (var f = await StoreFixture.CreateAsync())
        {
            await f.Store.SaveCharacterInventoryAsync("alice", f.A.Id, 299_999_900, f.A.Inventory!, []);
            var token = Guid.NewGuid(); f.Store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id);
            await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 5)]);
            var denied = await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 1);
            check(denied.Error == 22 && (await f.Store.GetCharactersAsync("bobby"))[0].Gold == 1000,
                "seller level cap checks gross101 before a net97 credit would fit");
        }
    }
    private static async Task CheckRejectionsAsync(Action<bool, string> check)
    {
        await using (var f = await StoreFixture.CreateAsync())
        {
            var token = Guid.NewGuid(); f.Store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id);
            foreach (var bad in new PrivateStoreDisplayRequest[] { new(7, 1, 0, 73, 1), new(0, 0, 0, 73, 1), new(0, uint.MaxValue, 0, 73, 1),
                new(0, 1, 1, 73, 1), new(0, 1, 0, 73, 0), new(0, 1, 0, 73, 11), new(0, 1, 0, 9, 0), new(0, 1_000_000_000, 0, 73, 5) })
                check((await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [bad])).Error == 17
                    && (await f.ReadA()).PrivateStoreStock is null, "invalid listing quantity/price/source/gear quality has no partial withdrawal");
            await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 5)]);
            check((await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, Guid.NewGuid(), 0, 1)).Error == 21,
                "an old listing generation cannot address a current store");
            check((await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 6)).Error == 17
                && (await f.Store.BuyPrivateStoreAsync(f.A.Id, f.A.Id, token, 0, 1)).Error == 60, "overbuy and self-purchase never transfer stock");
            await f.Store.SaveCharacterInventoryAsync("bobby", f.B.Id, 0, [], []);
            check((await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 1)).Error == 10, "insufficient buyer gold rejects before delivery");
            var inv = Enumerable.Range(73, 32).Concat(Enumerable.Range(3, 6)).Select(s => new CharacterItemRecord((ushort)s,
                TradeFixtures.PotionItemId, 100, InstanceId: Guid.NewGuid())).ToArray();
            await f.Store.SaveCharacterInventoryAsync("bobby", f.B.Id, 1000, inv, []);
            check((await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 1)).Error == 4,
                "a full matching stack cannot overfill via a purchase delta");
        }
        await using (var f = await StoreFixture.CreateAsync())
        {
            await f.Store.SaveCharacterInventoryAsync("alice", f.A.Id, 1000,
                [new(9, TradeFixtures.HeavyItemId, 1, InstanceId: Guid.NewGuid()), new(73, TradeFixtures.BannedItemId, 1, InstanceId: Guid.NewGuid())], []);
            var token = Guid.NewGuid(); f.Store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id);
            check((await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 1, 0, 73, 1)])).Error == 17, "bound items cannot be offered for sale");
            await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 1, 0, 9, 1)]);
            check((await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 1)).Error == 4, "buyer weight limit is authoritative before any debit");
        }
        await using (var f = await StoreFixture.CreateAsync())
        {
            var token = Guid.NewGuid(); f.Store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id);
            await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 5)]);
            await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 1);
            var root = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
            var a = root["Accounts"]!.AsArray().Single(n => n!["UserName"]!.GetValue<string>() == "alice")!["Characters"]![0]!;
            var occupied = a["Inventory"]!.AsArray().Single(i => i!["Slot"]!.GetValue<int>() == 73)!;
            occupied["InstanceId"] = Guid.NewGuid().ToString();
            occupied["ItemId"] = TradeFixtures.SwordItemId;
            await File.WriteAllTextAsync(f.Options.DataPath, root.ToJsonString());
            var restored = f.Reload(); await restored.InitializeAsync();
            var c = (await restored.GetCharactersAsync("alice"))[0];
            check(c.Gold == 1097 && c.PrivateStoreStock is null
                && (c.Mailbox ?? []).Any(m => m.Attachment?.ItemId == TradeFixtures.PotionItemId && m.Attachment.CountOrValue == 4),
                "startup preserves a conflicting restored inventory and returns unsold stock in durable mail");
        }
    }

    private sealed class StoreFixture : IAsyncDisposable
    {
        public required string Root; public required ServerOptions Options;
        public required JsonGameStore Store; public required ItemCatalog Items; public required CharacterStatCatalog Stats;
        public required PrivateStoreCatalog Catalog; public required CharacterRecord A; public required CharacterRecord B;
        public Guid GearId => A.Inventory!.Single(i => i.Slot == 9).InstanceId;
        public Task<CharacterRecord> ReadA() => Read("alice");
        private async Task<CharacterRecord> Read(string account) => (await Store.GetCharactersAsync(account))[0];
        public JsonGameStore Reload() => new(Options, NullLogger<JsonGameStore>.Instance, itemCatalog: Items, statCatalog: Stats, privateStoreCatalog: Catalog);
        public static async Task<StoreFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "dflegacy-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); TradeFixtures.Create(root);
            Directory.CreateDirectory(Path.Combine(root, "etc")); Directory.CreateDirectory(Path.Combine(root, "town")); Directory.CreateDirectory(Path.Combine(root, "map"));
            File.WriteAllText(Path.Combine(root, "etc/goldlimitbylevel.etc"), "[gold limit from level]\n1 100000\n60 300000000\n");
            File.WriteAllText(Path.Combine(root, "town/town.lst"), "1 `test.twn`\n");
            File.WriteAllText(Path.Combine(root, "town/test.twn"), "[area]\n0\n`test.map`\n`[normal]`\n[/area]\n[area]\n1\n`test.map`\n`[gate]`\n[/area]\n");
            File.WriteAllText(Path.Combine(root, "map/test.map"), "[virtual movable area]\n0 190 1600 200\n[/virtual movable area]\n[NPC]\n1 `[left]` 445 186 0\n[/NPC]\n");
            var options = new ServerOptions { DataPath = Path.Combine(root, "state.json"), ScriptPvfPath = "", SkillScriptPath = root };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            var catalog = new PrivateStoreCatalog(scripts); var items = TradeFixtures.CreateItems(root); var stats = TradeFixtures.CreateStats(root);
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items, statCatalog: stats, privateStoreCatalog: catalog);
            await store.CreateAccountAsync(new("alice", "password", OicqCode: "123456"));
            await store.CreateAccountAsync(new("bobby", "password", OicqCode: "123456"));
            var a = (await store.CreateCharacterAsync("alice", new("Seller"))).Character!;
            var b = (await store.CreateCharacterAsync("bobby", new("Buyer"))).Character!;
            a = (await store.SaveCharacterInventoryAsync("alice", a.Id, 1000,
                [new(73, TradeFixtures.PotionItemId, 10, InstanceId: Guid.NewGuid()), new(9, TradeFixtures.SwordItemId, 1, 3, 19, EquipmentQualitySeed: 37, InstanceId: Guid.NewGuid())], []))!;
            b = (await store.SaveCharacterInventoryAsync("bobby", b.Id, 1000, [], []))!;
            return new() { Root = root, Options = options, Store = store, Items = items, Stats = stats, Catalog = catalog, A = a, B = b };
        }
        public ValueTask DisposeAsync() { Directory.Delete(Root, true); return ValueTask.CompletedTask; }
    }
}
