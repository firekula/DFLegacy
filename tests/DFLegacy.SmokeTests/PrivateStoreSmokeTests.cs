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
        await CheckIdentitylessMaterialAsync(check);
        await CheckRecoveryAsync(check);
        await CheckRacesAndCapacityAsync(check);
        await CheckRejectionsAsync(check);
        await CheckServiceAsync(check);
        await CheckLifecycleAsync(check);
        await CheckReviewRegressionsAsync(check);
        var licensedAppearance = GameProtocolEngine.CreateCurrentCharacterInfo(new(0, [65], 3, 2, 45), 1,
            hasBlackDiamond: true, hasPrivateStorePermission: true);
        var ordinaryAppearance = GameProtocolEngine.CreateCurrentCharacterInfo(new(0, [65], 3, 2, 45), 1);
        check(licensedAppearance.Payload[22] == 1 && licensedAppearance.Payload[31] == 1 && ordinaryAppearance.Payload[22] == 0,
            "a licensed ACT1 town appearance enables the actor store component after guildId");
        await using (var badCatalog = await StoreFixture.CreateAsync())
        {
            File.WriteAllText(Path.Combine(badCatalog.Root, "etc/goldlimitbylevel.etc"), "[gold limit from level]\n60 9999999999999999999999\n");
            var rejected = false;
            try { rejected = !new PrivateStoreCatalog(new ScriptFileSystem(badCatalog.Options, NullLogger<ScriptFileSystem>.Instance)).TryGetGoldLimit(60, out _); }
            catch (OverflowException) { }
            check(rejected, "invalid oversized gold data disables stores without crashing the requesting session");
        }
    }

    private static async Task CheckDomainAsync(Action<bool, string> check)
    {
        await using var f = await StoreFixture.CreateAsync();
        check(f.Catalog.TryGetGoldLimit(60, out var cap) && cap == 300_000_000
            && f.Catalog.TryGetGoldLimit(1, out cap) && cap == 100_000, "store gold caps come from explicit level rows");
        check(f.Catalog.CheckPosition(new(1, 0, 1000, 250)) == 0
            && f.Catalog.CheckPosition(new(1, 1, 1000, 250)) == 62
            && f.Catalog.CheckPosition(new(1, 2, 1000, 250)) == 0
            && f.Catalog.CheckPosition(new(1, 3, 1000, 250)) == 62
            && f.Catalog.CheckPosition(new(1, 0, 10, 10)) == 62, "store positions require enabled non-gate area and walkable coordinates");
        check(f.Catalog.CheckPosition(new(1, 0, 445, 250)) == 82
            && f.Catalog.CheckPosition(new(1, 0, 525, 250)) == 0, "NPC exclusion uses strict80 horizontal boundary");
        check(PrivateStoreCatalog.Collides(new(1, 0, 1000, 250), new(1, 0, 1160, 350))
            && !PrivateStoreCatalog.Collides(new(1, 0, 1000, 250), new(1, 0, 1161, 350)), "store rectangles reject touching inclusive boundaries");
        await f.Store.SetCharacterWeaknessRecoveryAsync(f.A.Id, 70);
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
        check(!(await f.Store.RecoverCharacterWeaknessAsync(f.A.Id, 10)).Success && (await f.ReadA()).Gold == 1000,
            "out-of-actor stamina spending is refused while a store owns the economic state");
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
    private static async Task CheckIdentitylessMaterialAsync(Action<bool, string> check)
    {
        await using var f = await StoreFixture.CreateAsync(identitylessMaterial: true);
        var material = f.A.Inventory!.Single(i => i.Slot == 73);
        check(material.InstanceId == Guid.Empty, "ordinary material stacks have no per-instance identifier");
        var token = Guid.NewGuid();
        check(f.Store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id), "store acquires canonical material source");

        var display = await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 5)]);
        check(display.Status == DungeonRewardLedgerStatus.Committed,
            $"canonical material can be listed for sale (status {display.Status}, error {display.Error})");
        if (display.Status != DungeonRewardLedgerStatus.Committed) return;
        check(display.Character?.PrivateStoreStock?.Listings.Single() is
                { Quantity: 5, SourceInstanceId: var sourceId, Item.InstanceId: var escrowId }
            && sourceId == Guid.Empty && escrowId == Guid.Empty, "splitting a material stack creates no equipment identity");

        var purchase = await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 2);
        check(purchase.Status == DungeonRewardLedgerStatus.Committed
            && purchase.Item is { CountOrValue: 2, InstanceId: var buyerId } && buyerId == Guid.Empty,
            "partial material sale transfers the quantity without an equipment identity");
        var close = await f.Store.ClosePrivateStoreAsync(f.A.Id, token, false);
        check(close.Status == DungeonRewardLedgerStatus.Committed && close.Character?.PrivateStoreStock is null
            && close.Character?.Inventory?.Single(i => i.Slot == 73) == material with { CountOrValue = 8 },
            "closing returns only unsold material to its original identityless stack");
    }

    private static async Task CheckLifecycleAsync(Action<bool, string> check)
    {
        await using var f = await StoreFixture.CreateAsync();
        var town = new TownSessionDirectory(f.Options, TimeProvider.System, NullLogger<TownSessionDirectory>.Instance);
        var party = new PartyService(f.Store, town, TimeProvider.System, NullLogger<PartyService>.Instance);
        using var service = new PrivateStoreService(f.Store, f.Items, f.Catalog, town, party, NullLogger<PrivateStoreService>.Instance);
        await f.Store.GrantPremiumServiceToAccountAsync("alice", 8, 1);
        var session = Guid.NewGuid(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await town.EnterAsync(new(1, 0, 1, f.A.Id, f.A.CharacterNo, [65], "Seller", null, 1000, 250, 0, (_, _) => ValueTask.CompletedTask), default);
        async ValueTask Blocked(GameServerPacket p) { if (p.ProtocolId == 90) { entered.TrySetResult(); await release.Task; } }
        await service.AttachChannelAsync("alice", f.A.Id, 1, session, Blocked);
        await service.CreateAsync(f.A.Id, session, new(1, 0, 1000, 250));
        var display = service.DisplayAsync(f.A.Id, session, [65], [new(0, 101, 0, 73, 1)]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var detach = service.DetachChannelAsync(f.A.Id, session);
        var fresh = new List<GameServerPacket>();
        var attach = service.AttachChannelAsync("alice", f.A.Id, 1, Guid.NewGuid(), p => { fresh.Add(p); return ValueTask.CompletedTask; });
        check(!detach.IsCompleted && !attach.IsCompleted, "retiring a blocked store delivery prevents replacement attachment until it drains");
        release.TrySetResult(); await Task.WhenAll(display, detach, attach);
        check(fresh.Count == 0 && (await f.ReadA()).PrivateStoreStock is null
            && (await f.ReadA()).Inventory!.Single(i => i.Slot == 73).CountOrValue == 10,
            "old queued store packets cannot reach a new character attachment and escrow returns once");
        // Batch replacement may keep the exact same stock token; runtime leases still must be revoked.
        var token = Guid.NewGuid(); f.Store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id);
        await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 1)]);
        await f.Store.ReplaceAllCharactersAsync("alice", [await f.ReadA()]);
        check(f.Store.Commerce.Is(f.A.Id, CommerceActivity.StoreRevoked), "global roster replacement revokes an owner even when its saved stock token survives");
    }
    private static async Task CheckReviewRegressionsAsync(Action<bool, string> check)
    {
        check(!EntranceService.PrivateStoreOwnerCommandAllowed(GameProtocolEngine.DeleteItemCommand), "preparing/open store dispatch blocks CMD18 before any actor inventory mutation");
        foreach (var social in new byte[] { 63, 64, 128, 129, 130 })
            check(EntranceService.PrivateStoreOwnerCommandAllowed(social), $"preparing/open store dispatch preserves social command{social}");
        foreach (var secondStack in new[] { false, true })
        {
            await using var f = await StoreFixture.CreateAsync();
            var buyerItems = new List<CharacterItemRecord> { new(3, TradeFixtures.PotionItemId, 100, InstanceId: Guid.NewGuid()) };
            if (secondStack) buyerItems.Add(new(73, TradeFixtures.PotionItemId, 90, InstanceId: Guid.NewGuid()));
            await f.Store.SaveCharacterInventoryAsync("bobby", f.B.Id, 1000, buyerItems, []);
            var token = Guid.NewGuid(); f.Store.Commerce.TryAcquire(CommerceActivity.Store, token, f.A.Id);
            await f.Store.DisplayPrivateStoreAsync(f.A.Id, token, [new(0, 101, 0, 73, 5)]);
            var bought = await f.Store.BuyPrivateStoreAsync(f.B.Id, f.A.Id, token, 0, 4);
            check(bought.Status == DungeonRewardLedgerStatus.Committed && bought.DestinationSlot == 73
                && bought.Buyer!.Inventory!.Single(i => i.Slot == 73).CountOrValue == (secondStack ? 94u : 4u),
                $"a full first stack does not hide a valid {(secondStack ? "later stack" : "empty category slot")}");
        }
        await using (var f = await StoreFixture.CreateAsync())
        {
            Directory.CreateDirectory(Path.Combine(f.Root, "stackable/cash"));
            File.AppendAllText(Path.Combine(f.Root, "stackable/stackable.lst"), "16 `cash/shop_private.stk`\n");
            File.WriteAllText(Path.Combine(f.Root, "stackable/cash/shop_private.stk"), "[name] `Permit`\n[stackable type] `[private store]`\n[cash] 380\n");
            File.WriteAllText(Path.Combine(f.Root, "etc/newcashshop.etc"), "[item]\n100022 16 1 0 0 380 `Permit` 0 50002\n[/item]\n");
            var script = new ScriptFileSystem(f.Options, NullLogger<ScriptFileSystem>.Instance);
            var products = new CeraShopCatalog(script, TradeFixtures.CreateItems(f.Root), NullLogger<CeraShopCatalog>.Instance);
            var permission = products.TryGetProduct(100022, out var product) ? product.PremiumContract : null;
            check(permission is { ServiceType: 8, Days: 30 } && product!.CashPrice == 380, "original commodity100022 activates the30day private-store permit");
            if (permission is not null)
            {
                var purchased = await f.Store.SaveCharacterCeraShopPurchaseAsync("bobby", f.B.Id, 49620, 1000, [], [],
                    premiumServiceGrants: [new(permission.ServiceType, permission.Days)], expectedCash: 50000);
                check(purchased is not null && (await f.Store.GetPremiumServicesAsync("bobby")).Any(s => s.ServiceType == 8 && s.Active && s.RemainingSeconds > 29 * 86400),
                    "the standard cash purchase persists the permit without a dead inventory item");
            }
        }
        await using (var f = await StoreFixture.CreateAsync())
        {
            var town = new TownSessionDirectory(f.Options, TimeProvider.System, NullLogger<TownSessionDirectory>.Instance);
            var party = new PartyService(f.Store, town, TimeProvider.System, NullLogger<PartyService>.Instance);
            using var service = new PrivateStoreService(f.Store, f.Items, f.Catalog, town, party, NullLogger<PrivateStoreService>.Instance, TimeSpan.FromSeconds(2));
            await f.Store.GrantPremiumServiceToAccountAsync("alice", 8, 1);
            await f.Store.CreateAccountAsync(new("carol", "password", OicqCode: "123456"));
            var observer = (await f.Store.CreateCharacterAsync("carol", new("Observer"))).Character!;
            var sa = Guid.NewGuid(); var sb = Guid.NewGuid(); var sc = Guid.NewGuid();
            var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var kicked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancel = new CancellationTokenSource();
            async ValueTask SendObserver(GameServerPacket p) { if (p.ProtocolId == 83) { blocked.TrySetResult(); await Task.Delay(Timeout.Infinite, cancel.Token); } }
            foreach (var c in new[] { (f.A, (ushort)1), (f.B, (ushort)2), (observer, (ushort)3) })
                await town.EnterAsync(new(1, 0, c.Item2, c.Item1.Id, c.Item1.CharacterNo, [65], c.Item1.Name, null, 1000, 250, 0, (_, _) => ValueTask.CompletedTask), default);
            await service.AttachChannelAsync("alice", f.A.Id, 1, sa, _ => ValueTask.CompletedTask);
            await service.AttachChannelAsync("bobby", f.B.Id, 2, sb, _ => ValueTask.CompletedTask);
            await service.AttachChannelAsync("carol", observer.Id, 3, sc, SendObserver, kick: () => { kicked.TrySetResult(); cancel.Cancel(); });
            await service.CreateAsync(f.A.Id, sa, new(1, 0, 1000, 250));
            await service.DisplayAsync(f.A.Id, sa, [65], [new(0, 101, 0, 73, 5)]);
            await service.EnterAsync(observer.Id, sc, 1); await service.EnterAsync(f.B.Id, sb, 1);
            var buy = service.BuyAsync(f.B.Id, sb, 0, 1); await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAny(buy, Task.Delay(500));
            check(buy.IsCompletedSuccessfully, "a stalled third observer cannot hold the buyer's authoritative installation");
            var close = service.CloseAsync(f.A.Id, sa); await Task.WhenAny(close, Task.Delay(500));
            check(close.IsCompletedSuccessfully, "a stalled third observer cannot hold seller close or area cleanup");
            var detach = service.DetachChannelAsync(f.B.Id, sb); await Task.WhenAny(detach, Task.Delay(500));
            check(detach.IsCompletedSuccessfully, "buyer disconnect does not wait for unrelated observer transport");
            var bounded = false;
            if (buy.IsCompletedSuccessfully && close.IsCompletedSuccessfully)
            { try { await kicked.Task.WaitAsync(TimeSpan.FromSeconds(3)); bounded = true; } catch (TimeoutException) { } }
            check(bounded, "a stalled transport is retired and kicked by its delivery deadline");
            cancel.Cancel(); await Task.WhenAll(buy, close, detach);
            await service.DetachChannelAsync(f.A.Id, sa); await service.DetachChannelAsync(observer.Id, sc);
            using var ownerCancel = new CancellationTokenSource();
            var newSession = Guid.NewGuid();
            async ValueTask SlowOwner(GameServerPacket p) { if (p.ProtocolId == 88) await Task.Delay(Timeout.Infinite, ownerCancel.Token); }
            await service.AttachChannelAsync("alice", f.A.Id, 1, newSession, SlowOwner, kick: ownerCancel.Cancel);
            await service.CreateAsync(f.A.Id, newSession, new(1, 0, 1000, 250));
            await service.DetachChannelAsync(f.A.Id, newSession);
            check(!service.IsOwner(f.A.Id), "a transport-retired owner still releases its store on actual disconnect");
        }
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
    private static async Task CheckServiceAsync(Action<bool, string> check)
    {
        await using var f = await StoreFixture.CreateAsync();
        var directory = new TownSessionDirectory(f.Options, TimeProvider.System, NullLogger<TownSessionDirectory>.Instance);
        var party = new PartyService(f.Store, directory, TimeProvider.System, NullLogger<PartyService>.Instance);
        var service = new PrivateStoreService(f.Store, f.Items, f.Catalog, directory, party, NullLogger<PrivateStoreService>.Instance);
        using (service)
        {
            var pa = new List<GameServerPacket>(); var pb = new List<GameServerPacket>(); var sessionA = Guid.NewGuid(); var sessionB = Guid.NewGuid(); var kicked = false;
            ValueTask SendA(GameServerPacket p) { lock (pa) pa.Add(p); return ValueTask.CompletedTask; }
            ValueTask SendB(GameServerPacket p) { lock (pb) pb.Add(p); return ValueTask.CompletedTask; }
            await directory.EnterAsync(new(1, 0, 1, f.A.Id, f.A.CharacterNo, [65], "Seller", null, 1000, 250, 0, (p, _) => SendA(p)), default);
            await directory.EnterAsync(new(1, 0, 2, f.B.Id, f.B.CharacterNo, [66], "Buyer", null, 1200, 250, 0, (p, _) => SendB(p)), default);
            await service.AttachChannelAsync("alice", f.A.Id, 1, sessionA, SendA, kick: () => kicked = true);
            await service.AttachChannelAsync("bobby", f.B.Id, 2, sessionB, SendB);
            check(!await service.CreateAsync(f.A.Id, sessionA, new(1, 0, 1000, 250))
                && pa.Last().Payload.SequenceEqual(new byte[] { 0, 63 }), "unlicensed native create receives error63");
            var grant = await f.Store.GrantPremiumServiceToAccountAsync("alice", 8, 1);
            check(grant.Success && await service.CreateAsync(f.A.Id, sessionA, new(1, 0, 1000, 250))
                && pb.Any(p => p.ProtocolId == 78 && p.Type == 0), "licensed create broadcasts a preparing store to same public area");
            await service.EnterAsync(f.B.Id, sessionB, 1);
            check(pb.Last().ProtocolId == 86 && pb.Last().Payload.SequenceEqual(new byte[] { 0 }), "a preparing store cannot be entered");
            await service.DisplayAsync(f.A.Id, sessionA, [83, 72, 79, 80], [new(0, 101, 0, 73, 5)]);
            var trade = new ItemTradeService(f.Store, f.Items, f.Stats, directory, TimeProvider.System, NullLogger<ItemTradeService>.Instance);
            using (trade)
            {
                await trade.AttachChannelAsync("alice", f.A.Name, f.A.Id, 1, sessionA, SendA);
                await trade.AttachChannelAsync("bobby", f.B.Name, f.B.Id, 2, sessionB, SendB);
                await trade.HandleRequestAsync(f.B.Id, 1, 0);
                check(!pa.Any(p => p.Type == 0 && p.ProtocolId == 7), "direct trade admission cannot overlap an open store owner");
                await trade.CancelForAreaChangeAsync(f.B.Id);
            }
            await service.SendAreaStoresAsync(f.B.Id, sessionB);
            check(pb.Any(p => p.ProtocolId == 84 && p.Payload[0] == 1), "a later area snapshot contains title, position and completed state");
            await service.EnterAsync(f.B.Id, sessionB, 1);
            check(pb.Last().ProtocolId == 86 && pb.Last().Payload[0] == 1, "a visitor receives complete current stock");
            var bought = await service.BuyAsync(f.B.Id, sessionB, 0, 1);
            var boughtAgain = await service.BuyAsync(f.B.Id, sessionB, 0, 1);
            check(bought?.Gold == 899 && boughtAgain?.Gold == 798 && (await f.ReadA()).Gold == 1000
                && service.TryTakeGoldUpdate(f.A.Id), "two idle-seller purchases append credits without whole-record actor overwrites");
            var packets = pb.Where(p => p.ProtocolId is 83 or 85).ToArray();
            check(packets.Select(p => p.ProtocolId).SequenceEqual(new byte[] { 83, 85, 83, 85 }), "each visitor depletion precedes its own purchase ACK");
            check((await f.Store.ConsumePrivateStoreGoldCreditsAsync(f.A.Id))?.Gold == 1194
                && pa.Count(p => p.Type == 0 && p.ProtocolId == 82) == 2, "seller installs both sales once and receives both native sale notices");
            await service.DetachChannelAsync(f.A.Id, Guid.NewGuid());
            check(service.IsOwner(f.A.Id), "old-session detach cannot close a current store");
            var closed = await service.CloseAsync(f.A.Id, sessionA);
            check(closed?.Inventory!.Single(i => i.Slot == 73).CountOrValue == 8 && !service.IsOwner(f.A.Id)
                && pb.Any(p => p.Type == 0 && p.ProtocolId == 80) && pb.Any(p => p.Type == 0 && p.ProtocolId == 79),
                "close returns remaining stock and explicitly closes/removes visitor store state");
            check((await service.BuyAsync(f.B.Id, sessionB, 0, 1)) is null && pb.Last().Payload.SequenceEqual(new byte[] { 0, 21 }),
                "a visitor cannot keep buying after close");
            await service.CreateAsync(f.A.Id, sessionA, new(1, 0, 1000, 250));
            await service.DisplayAsync(f.A.Id, sessionA, [65], [new(0, 101, 0, 73, 1)]);
            await service.EnterAsync(f.B.Id, sessionB, 1);
            await f.Store.ForceClearRewardQuarantineAsync("private-store live-root replacement test");
            check(await service.BuyAsync(f.B.Id, sessionB, 0, 1) is null,
                "forced root recovery invalidates even a matching persisted store token immediately");
            await service.SweepAsync();
            check(kicked && service.IsOwner(f.A.Id), "revoked owner is disconnected while its stale economic writes remain frozen");
            await service.DetachChannelAsync(f.A.Id, sessionA); await service.DetachChannelAsync(f.B.Id, sessionB);
            check((await f.ReadA()).PrivateStoreStock is null && (await f.ReadA()).Inventory!.Single(i => i.Slot == 73).CountOrValue == 8,
                "recovery invalidation returns the actual restored escrow before releasing the old actor");
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
        public static async Task<StoreFixture> CreateAsync(bool identitylessMaterial = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "dflegacy-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); TradeFixtures.Create(root);
            Directory.CreateDirectory(Path.Combine(root, "etc")); Directory.CreateDirectory(Path.Combine(root, "town")); Directory.CreateDirectory(Path.Combine(root, "map"));
            File.WriteAllText(Path.Combine(root, "etc/goldlimitbylevel.etc"), "[gold limit from level]\n1 100000\n60 300000000\n");
            File.WriteAllText(Path.Combine(root, "town/town.lst"), "1 `test.twn`\n");
            File.WriteAllText(Path.Combine(root, "town/test.twn"), "[area]\n0\n`test.map`\n`[normal]`\n[/area]\n[area]\n1\n`test.map`\n`[gate]`\n[/area]\n[area]\n2\n`test.map`\n`[pub]`\n[/area]\n[area]\n3\n`test.map`\n`[normal]`\n`[use private store]` 0\n[/area]\n");
            File.WriteAllText(Path.Combine(root, "map/test.map"), "[virtual movable area]\n0 190 1600 200\n[/virtual movable area]\n[NPC]\n1 `[left]` 445 186 0\n[/NPC]\n");
            var options = new ServerOptions { DataPath = Path.Combine(root, "state.json"), ScriptPvfPath = "", SkillScriptPath = root };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            var catalog = new PrivateStoreCatalog(scripts); var items = TradeFixtures.CreateItems(root); var stats = TradeFixtures.CreateStats(root);
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items, statCatalog: stats, privateStoreCatalog: catalog);
            await store.CreateAccountAsync(new("alice", "password", OicqCode: "123456"));
            await store.CreateAccountAsync(new("bobby", "password", OicqCode: "123456"));
            var a = (await store.CreateCharacterAsync("alice", new("Seller"))).Character!;
            var b = (await store.CreateCharacterAsync("bobby", new("Buyer"))).Character!;
            // Permit-purchase assertions start from a fixture-owned Cash wallet.
            await store.GrantCeraToAccountAsync("alice", 50_000);
            await store.GrantCeraToAccountAsync("bobby", 50_000);
            a = (await store.SaveCharacterInventoryAsync("alice", a.Id, 1000,
                [new(73, TradeFixtures.PotionItemId, 10, InstanceId: identitylessMaterial ? Guid.Empty : Guid.NewGuid()), new(9, TradeFixtures.SwordItemId, 1, 3, 19, EquipmentQualitySeed: 37, InstanceId: Guid.NewGuid())], []))!;
            b = (await store.SaveCharacterInventoryAsync("bobby", b.Id, 1000, [], []))!;
            return new() { Root = root, Options = options, Store = store, Items = items, Stats = stats, Catalog = catalog, A = a, B = b };
        }
        public ValueTask DisposeAsync() { Directory.Delete(Root, true); return ValueTask.CompletedTask; }
    }
}
