using DFLegacy.Protocol;
using DFLegacy.Server;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;

internal static partial class CeraGiftSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        // ACT1 0x417 dispatcher consumes an unused byte before its byte error.
        byte[] expected = [0, 0, 77, 2, 0, 0, 0, 0xB2, 0x86, 1, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        check(GameProtocolEngine.CreateCeraShopBuyError(77, 2, 100018).Payload.SequenceEqual(expected),
            "ACT1 CMD67 failure has unused-byte/error-byte prefix and four 32-bit fields");
        Wire(check);
        await Transactions(check);
        await Rejections(check);
        await Concurrency(check);
        await Faults(check);
        await Sessions(check);
        await PreserveEconomicState(check);
        await CreatureCatalogAndWire(check);
        await CreatureGiftTransactions(check);
        await CreatureGiftLifecycle(check);
        await CreatureGiftAtomicRegression(check);
        await AvatarTransactions(check);
        await AvatarRejections(check);
        await AvatarSelfPurchase(check);
        await AvatarExpiryRaces(check);
        await AvatarAtomicLimits(check);
        await AvatarConcurrentAndFaults(check);
        await AvatarSessionsAndEconomicState(check);
    }

    private static void Wire(Action<bool, string> check)
    {
        // Literal CP936 bytes for 收礼; no production serializer constructs expectations.
        byte[] name = [0xCA, 0xD5, 0xC0, 0xF1];
        byte[] query = [0x11, 0, 4, 0, 0, 0, .. name];
        check(GameProtocolEngine.TryReadCeraGiftRecipient(query, out var recipient) && recipient == "收礼",
            "CMD146 reads CP936 byte length after sequence");
        check(!GameProtocolEngine.TryReadCeraGiftRecipient([0, 0, 2, 0, 0, 0, .. name], out _)
            && !GameProtocolEngine.TryReadCeraGiftRecipient([0, 0, 1, 0, 0, 0, 0xCA], out _)
            && !GameProtocolEngine.TryReadCeraGiftRecipient([0, 0, 1, 0, 0, 0, 0], out _),
            "query rejects character-count length, incomplete multibyte and embedded NUL");
        byte[] request = [0x12, 0, 1, 4, 0, 0, 0, .. name, 1, 0xFF, 0, 0, 0xB2, 0x86, 1, 0];
        check(GameProtocolEngine.TryReadCeraShopPurchase(request, out var purchase)
            && purchase.IsGift && purchase.RecipientName == "收礼"
            && purchase.Products.SequenceEqual(new[] { new GameCeraShopPurchaseItem(0xFF, 0, 100018) }),
            "CMD67 gift1 retains canonical candidate name and exact seven-byte product row");
        check(!GameProtocolEngine.TryReadCeraShopPurchase([.. request, 0], out _)
            && !GameProtocolEngine.TryReadCeraShopPurchase(request[..^1], out _),
            "gift checkout rejects trailing and truncated native product rows");
        byte[] reply = [1, 4, 0, 0, 0, .. name, 60, 0, 1, 0x12, 0, 1];
        var packet = GameProtocolEngine.CreateCeraGiftRecipientReply("收礼", 60, 1, 0x12, 0, 1);
        check(packet.ProtocolId == 146 && packet.Payload.SequenceEqual(reply),
            "CMD146 success emits name/string32, level/u16, job/growth/sex/count bytes");
        check(GameProtocolEngine.CreateCeraGiftRecipientError(21).Payload.SequenceEqual(new byte[] { 0, 21 }),
            "CMD146 failure is the native two-byte payload");
        byte[] giftReply = [1, 1, 2, 0, 0, 0, 0xB2, 0x86, 1, 0,
            0xB2, 0x86, 1, 0, 0xB2, 0x86, 1, 0, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0];
        check(GameProtocolEngine.CreateCeraShopBuyReply(2, 100018, isGift: true).Payload.SequenceEqual(giftReply),
            "ACT1 gift1 success has native progress keys and empty buyer reward vector");
    }

    private static async Task Transactions(Action<bool, string> check)
    {
        await using var f = new Fixture();
        var store = f.Open();
        var (a, b, sibling) = await f.Seed(store);
        check(f.Shop.MaxGiftCount == 1 && f.Shop.TryGetProduct(100021, out var forbidden) && forbidden.GiftDisallowed
            && f.Shop.TryGetProduct(100022, out var sticker) && sticker.Sticker == 99,
            "catalog retains ACT1 default max1, gift disallowance and post-description sticker99");
        check((await store.QueryCeraGiftRecipientAsync(a.Id, a.Name)).Error == 7
            && (await store.QueryCeraGiftRecipientAsync(a.Id, "Absent")).Error == 21,
            "query rejects active self7 and absent21 without a pending gift credential");
        var query = await store.QueryCeraGiftRecipientAsync(a.Id, b.Name);
        check(query.Error == 0 && query.Character?.Id == b.Id && query.ReceivedCount == 0,
            "offline low-level target can be queried without online/level/admin restrictions");
        await store.SendCharacterMailAsync(b.Id, new("Old", "Preserved", 23));
        var gift = await f.Gift(store, a, b.Name);
        var sender = await Character(store, "sender", a.Id);
        var target = await Character(store, "target", b.Id);
        check(gift.Status == DungeonRewardLedgerStatus.Committed && gift.SenderCash == 49850
            && sender.Cash == 49850 && !(sender.Inventory ?? []).Any(i => i.ItemId == 15)
            && target.Mailbox?.Count == 2 && target.Mailbox.Single(m => m.Sender == a.Name).Attachment is { ItemId: 15, CountOrValue: 1 },
            "one atomic offline gift debits150 once, mails named target and gives buyer no item");
        if (gift.Status != DungeonRewardLedgerStatus.Committed) return;
        check((await store.QueryCeraGiftRecipientAsync(a.Id, sibling.Name)).ReceivedCount == 1,
            "receiver account shares its monthly count with another character");
        var before = await File.ReadAllBytesAsync(f.Options.DataPath);
        var over = await f.Gift(store, a, sibling.Name);
        var after = await File.ReadAllBytesAsync(f.Options.DataPath);
        check(over.Status == DungeonRewardLedgerStatus.Rejected && over.Error == 4
            && before.SequenceEqual(after),
            "transaction rechecks shared ACT1 monthly cap and preserves exact snapshot on rejection");
        var stale = await store.SaveCharacterItemSpacesAsync("target", b.Id, b.Gold, b.Inventory ?? [], b.Equipment ?? [], b.Warehouse ?? []);
        check(stale?.Mailbox?.Count == 2 && stale.Mailbox.Single(m => m.Sender == "Old").Gold == 23,
            "stale inventory actor save cannot erase canonical gift or preexisting mail");
        f.Clock.Utc = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(8));
        check((await store.QueryCeraGiftRecipientAsync(a.Id, b.Name)).ReceivedCount == 0,
            "Shanghai month boundary resets visible receiver account count");
        check((await f.Gift(store, a, sibling.Name)).Status == DungeonRewardLedgerStatus.Committed,
            "next month another target-account character receives its own mail");
        f.Clock.Utc = new DateTimeOffset(2027, 11, 1, 0, 0, 0, TimeSpan.FromHours(8));
        check((await store.QueryCeraGiftRecipientAsync(a.Id, b.Name)).ReceivedCount == 0,
            "complete YearMonth resets same-month cross-year counter (documented adaptation)");
        var restarted = f.Open();
        await restarted.InitializeAsync();
        target = await Character(restarted, "target", b.Id);
        var mail = target.Mailbox!.Single(m => m.Sender == a.Name);
        check(CharacterInventoryPlanner.TryPlace((target.Inventory ?? []).ToDictionary(i => i.Slot), mail.Attachment!,
            f.Items, uint.MaxValue, out var placement, out _), "gift physical attachment uses existing native mail placement");
        var claim = await restarted.ClaimCharacterMailAsync("target", b.Id, mail.Id, target.Gold,
            placement.Inventory.Values, target.Equipment ?? []);
        var second = await restarted.ClaimCharacterMailAsync("target", b.Id, mail.Id, target.Gold,
            placement.Inventory.Values, target.Equipment ?? []);
        var restartedAgain = f.Open(); await restartedAgain.InitializeAsync();
        var claimed = await Character(restartedAgain, "target", b.Id);
        check(claim is not null && second is null && claimed.Inventory?.Where(i => i.ItemId == 15).Sum(i => (long)i.CountOrValue) == 1
            && claimed.Mailbox!.Single(m => m.Id == mail.Id).Attachment is null,
            "restart preserves gift debit/mail and existing native claim yields exactly one item across restarts");

        await using var same = new Fixture();
        var sameStore = same.Open(); var (sa, _, _) = await same.Seed(sameStore);
        var alt = (await sameStore.CreateCharacterAsync("sender", new("SameAlt", Level: 1))).Character!;
        check((await same.Gift(sameStore, sa, alt.Name)).Status == DungeonRewardLedgerStatus.Committed
            && (await Character(sameStore, "sender", alt.Id)).Cash == 49850
            && (await sameStore.GetCharacterMailsAsync(alt.Id)).Single().Attachment?.ItemId == 15,
            "same-account different-character gift deducts shared wallet once and mails only chosen alt");
    }

    private static async Task Rejections(Action<bool, string> check)
    {
        foreach (var scenario in new[] { "self", "missing", "deleted", "blacklist", "disallowed", "sticker", "sticker-bare", "missing-product", "effect", "cash", "mailbox", "multi", "multi-invalid" })
        {
            await using var f = new Fixture(); var store = f.Open();
            var (a, b, _) = await f.Seed(store);
            var recipient = b.Name; uint commodity = 100018; byte error = 4;
            switch (scenario)
            {
                case "self": recipient = a.Name; error = 7; break;
                case "missing": recipient = "Unknown"; error = 3; break;
                case "deleted": await store.DeleteCharacterAsync("target", (int)b.Slot); error = 3; break;
                case "blacklist": await store.AddBlacklistAsync("target", a.Name); error = 77; break;
                case "disallowed": commodity = 100021; error = 8; break;
                case "sticker": commodity = 100022; error = 8; break;
                case "sticker-bare": commodity = 100024; error = 8; break;
                case "missing-product": commodity = 999999; error = 21; break;
                case "effect": commodity = 100023; error = 8; break;
                case "cash": await store.SaveCharacterCeraShopPurchaseAsync("sender", a.Id, 149, a.Gold, a.Inventory ?? [], a.Equipment ?? []); error = 11; break;
                case "mailbox":
                    for (var i = 0; i < 255; i++) await store.SendCharacterMailAsync(b.Id, new("Old", "Full"));
                    break;
                case "multi": break;
                case "multi-invalid": error = 21; break;
            }
            var before = await File.ReadAllBytesAsync(f.Options.DataPath);
            var result = await store.GiftCeraShopAsync("sender", a.Id, recipient,
                scenario == "multi" ? [new(0xFF, 0, commodity), new(0xFF, 0, commodity)]
                : scenario == "multi-invalid" ? [new(0xFF, 0, commodity), new(0xFF, 0, 999999)] : [new(0xFF, 0, commodity)], f.Shop);
            var after = await File.ReadAllBytesAsync(f.Options.DataPath);
            check(result.Status == DungeonRewardLedgerStatus.Rejected && result.Error == error
                && before.SequenceEqual(after),
                $"gift {scenario} rejects{error} with cash/mail/count/ledger conservation");
        }
        await using var quantity = new Fixture(max: 2); var qstore = quantity.Open();
        var (qa, qb, _) = await quantity.Seed(qstore);
        check((await quantity.Gift(qstore, qa, qb.Name, 100019)).Status == DungeonRewardLedgerStatus.Committed
            && (await qstore.QueryCeraGiftRecipientAsync(qa.Id, qb.Name)).ReceivedCount == 1
            && (await qstore.GetCharacterMailsAsync(qb.Id)).Single().Attachment?.CountOrValue == 10,
            "one commodity with quantity10 counts as one received gift rather than ten attachments");
        check((await quantity.Gift(qstore, qa, qb.Name, 100019)).Status == DungeonRewardLedgerStatus.Committed
            && (await qstore.GetCharacterMailsAsync(qb.Id)).Length == 2,
            "PVF max gift count override permits a legitimate repeated order without fake deduplication");
    }

    private static async Task Concurrency(Action<bool, string> check)
    {
        await using var f = new Fixture(); var store = f.Open(); var (a, b, other) = await f.Seed(store);
        var results = await Task.WhenAll(f.Gift(store, a, b.Name), f.Gift(store, a, other.Name));
        check(results.Count(r => r.Status == DungeonRewardLedgerStatus.Committed) == 1
            && (await Character(store, "sender", a.Id)).Cash == 49850
            && (await store.GetCharacterMailsAsync(b.Id)).Length + (await store.GetCharacterMailsAsync(other.Id)).Length == 1,
            "concurrent stale recipient queries cannot bypass account cap or double debit");
        await using var cash = new Fixture(max: 2); var cashStore = cash.Open(); var (ca, cb, _) = await cash.Seed(cashStore);
        await cashStore.SaveCharacterCeraShopPurchaseAsync("sender", ca.Id, 150, ca.Gold, ca.Inventory ?? [], ca.Equipment ?? []);
        results = await Task.WhenAll(cash.Gift(cashStore, ca, cb.Name), cash.Gift(cashStore, ca, cb.Name));
        check(results.Count(r => r.Status == DungeonRewardLedgerStatus.Committed) == 1
            && results.Any(r => r.Error == 11) && (await Character(cashStore, "sender", ca.Id)).Cash == 0,
            "two concurrent requests use canonical wallet and cannot spend the same150 twice");
    }

    private static async Task Faults(Action<bool, string> check)
    {
        foreach (var afterReplace in new[] { false, true })
        {
            await using var f = new Fixture(); var writer = new FaultWriter(afterReplace);
            var store = f.Open(writer); var (a, b, _) = await f.Seed(store);
            writer.Armed = true;
            var result = await f.Gift(store, a, b.Name); var calls = writer.Calls;
            var retry = await f.Gift(store, a, b.Name);
            check(result.Status == DungeonRewardLedgerStatus.Unknown && retry.Status == DungeonRewardLedgerStatus.Unknown
                && writer.Calls == calls && (await Character(store, "sender", a.Id)).Cash == 50000,
                $"gift snapshot fault afterReplace={afterReplace} quarantines outcome without speculative publication/retry");
            var restarted = f.Open(); await restarted.InitializeAsync();
            check((await Character(restarted, "sender", a.Id)).Cash == (afterReplace ? 49850 : 50000)
                && (await restarted.GetCharacterMailsAsync(b.Id)).Length == (afterReplace ? 1 : 0)
                && (await restarted.QueryCeraGiftRecipientAsync(a.Id, b.Name)).ReceivedCount == (afterReplace ? 1 : 0),
                $"fault restart afterReplace={afterReplace} observes all debit/mail/count or none");
        }
    }

    private static async Task Sessions(Action<bool, string> check)
    {
        await using var f = new Fixture(); var store = f.Open(); var (a, b, _) = await f.Seed(store);
        var alt = (await store.CreateCharacterAsync("sender", new("OnlineAlt"))).Character!;
        var sessions = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
        var cashA = new CeraCashInvalidation(); var cashAlt = new CeraCashInvalidation(); var alarms = 0;
        var leaseA = await sessions.ClaimAsync(a.Id, Guid.NewGuid(), () => { }, default, ceraInfo: cashA.Invalidate);
        var leaseAlt = await sessions.ClaimAsync(alt.Id, Guid.NewGuid(), () => { }, default, ceraInfo: cashAlt.Invalidate);
        var leaseB = await sessions.ClaimAsync(b.Id, Guid.NewGuid(), () => { }, default, mailReceived: () => alarms++);
        try
        {
            await store.GrantCeraToAccountAsync("sender", 1000);
            const uint delayedTopUpValue = 51000;
            var gift = await f.Gift(store, a, b.Name);
            await CeraGiftSessionEffects.NotifyCommittedAsync(gift, "sender", store, sessions);
            // Simulate the older top-up notification arriving after the debit notification.
            sessions.NotifyCera(a.Id, delayedTopUpValue); sessions.NotifyCera(alt.Id, delayedTopUpValue);
            var actualA = await cashA.ConsumeAsync(store, "sender");
            var actualAlt = await cashAlt.ConsumeAsync(store, "sender");
            check(actualA == 50850 && actualAlt == 50850 && alarms == 1,
                "gift invalidates every sender-account online actor and late stale top-up notification reloads canonical50850");
            check(await cashA.ConsumeAsync(store, "sender") is null,
                "cash invalidation is consumed once without publishing an unsolicited stale balance");
            await CeraGiftSessionEffects.NotifyCommittedAsync(new(DungeonRewardLedgerStatus.Unknown), "sender", store, sessions);
            check(alarms == 1 && await cashAlt.ConsumeAsync(store, "sender") is null,
                "unknown/rejected gift cannot publish cash or mail success effects");
        }
        finally { sessions.Release(leaseA); sessions.Release(leaseAlt); sessions.Release(leaseB); }
    }

    private static async Task PreserveEconomicState(Action<bool, string> check)
    {
        await using var f = new Fixture(max: 2); var store = f.Open(); var (a, b, _) = await f.Seed(store);
        // Compatibility state includes both existing economic ledgers and an uninstalled gold credit.
        var state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        var pickup = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        state["DungeonRewardLedger"] = JsonSerializer.SerializeToNode(new[] { new DungeonRewardLedgerEntry(
            "prior-card", "prior-fingerprint", a.Id, 12, 0, 1, 1, DungeonRewardLedgerStatus.Committed,
            now, now, null, 17, null, null) });
        state["DungeonGoldLedger"] = JsonSerializer.SerializeToNode(new[] { new DungeonGoldLedgerEntry(
            pickup, [a.Id], [0], [19], [19]) });
        var senderNode = state["Accounts"]![0]!["Characters"]![0]!;
        senderNode["PendingDungeonGoldCredits"] = JsonSerializer.SerializeToNode(new[] { new DungeonGoldCredit(pickup, 19) });
        var rewardBefore = state["DungeonRewardLedger"]!.ToJsonString();
        var goldBefore = state["DungeonGoldLedger"]!.ToJsonString();
        await File.WriteAllTextAsync(f.Options.DataPath, state.ToJsonString());
        store = f.Open(); await store.InitializeAsync();
        // Both bags are full; mailbox delivery does not depend on buyer or recipient bag capacity.
        var full = Enumerable.Range(3, 134).Select(s => new CharacterItemRecord((ushort)s, 15, 1000)).ToArray();
        await store.SaveCharacterItemSpacesAsync("sender", a.Id, a.Gold, full, [], []);
        await store.SaveCharacterItemSpacesAsync("target", b.Id, b.Gold, full, [], []);
        var senderBefore = await Character(store, "sender", a.Id);
        var result = await store.GiftCeraShopAsync("sender", a.Id, b.Name,
            [new(0xFF, 0, 100018), new(0xFF, 0, 100019)], f.Shop);
        state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        var senderAfter = await Character(store, "sender", a.Id);
        check(result.Status == DungeonRewardLedgerStatus.Committed && senderAfter.Cash == 48450
            && senderAfter.Inventory!.SequenceEqual(senderBefore.Inventory!) && senderAfter.Gold == senderBefore.Gold
            && senderAfter.PendingDungeonGoldCredits!.Single().PickupId == pickup
            && (await store.GetCharacterMailsAsync(b.Id)).Select(m => m.Attachment!.CountOrValue).Order().SequenceEqual(new uint[] { 1, 10 })
            && (await store.QueryCeraGiftRecipientAsync(a.Id, b.Name)).ReceivedCount == 2,
            "two-product atomic gift succeeds with full bags and preserves sender inventory/gold/pending credit");
        check(state["DungeonRewardLedger"]!.ToJsonString() == rewardBefore && state["DungeonGoldLedger"]!.ToJsonString() == goldBefore,
            "gift atomic snapshot preserves prior card and shared-gold ledgers byte-for-byte in JSON values");
    }

    private static async Task<CharacterRecord> Character(JsonGameStore store, string account, Guid id) =>
        (await store.GetCharactersAsync(account)).Single(c => c.Id == id);

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Utc = new(2026, 10, 31, 23, 59, 59, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Utc;
    }
    private sealed class FaultWriter(bool afterReplace) : IDungeonRewardSnapshotWriter
    {
        internal bool Armed; internal int Calls;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken ct)
        {
            Calls++;
            if (Armed && !afterReplace) throw new IOException("before gift replacement");
            await new DungeonRewardSnapshotWriter().WriteAsync(path, snapshot, ct);
            if (Armed) throw new IOException("after gift replacement");
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dflegacy-gift-" + Guid.NewGuid().ToString("N"));
        internal readonly ServerOptions Options;
        internal readonly ItemCatalog Items;
        internal readonly CeraShopCatalog Shop;
        internal readonly Clock Clock = new();
        internal Fixture(int? max = null)
        {
            Directory.CreateDirectory(Path.Combine(root, "stackable")); Directory.CreateDirectory(Path.Combine(root, "etc"));
            File.WriteAllText(Path.Combine(root, "stackable/stackable.lst"), "15 `physical.stk`\n3 `reset.stk`\n");
            File.WriteAllText(Path.Combine(root, "stackable/physical.stk"), "[name] `Physical`\n[stackable type] `[equipment variation]`\n[attach type] `[free]`\n[stack limit] 1000\n[weight] 0\n");
            File.WriteAllText(Path.Combine(root, "stackable/reset.stk"), "[name] `Reset`\n[stackable type] `[skill]`\n[attach type] `[free]`\n[stack limit] 1000\n");
            File.WriteAllText(Path.Combine(root, "etc/newcashshop.etc"), "[item]\n100018 15 1 0 0 150 `Physical` 0 0\n100019 15 10 0 0 1400 `Ten` 0 0\n100021 15 50 0 0 6000 `Forbidden` 0 0\n100022 15 1 0 0 150 `Sticker` 99 0\n100023 3 1 0 0 150 `Effect` 0 0\n100024 15 1 0 0 150 99 0\n[/item]\n[gift disallowance]\n100021\n[/gift disallowance]\n" + (max is null ? "" : $"[max gift count]\n{max}\n[/max gift count]\n"));
            Options = new() { DataPath = Path.Combine(root, "state.json"), SkillScriptPath = root, ScriptPvfPath = "" };
            var scripts = new ScriptFileSystem(Options, NullLogger<ScriptFileSystem>.Instance);
            Items = new(scripts, NullLogger<ItemCatalog>.Instance); Shop = new(scripts, Items, NullLogger<CeraShopCatalog>.Instance);
        }
        internal JsonGameStore Open(FaultWriter? writer = null) => writer is null
            ? new(Options, NullLogger<JsonGameStore>.Instance, itemCatalog: Items, avatarTimeProvider: Clock)
            : new(Options, NullLogger<JsonGameStore>.Instance, writer, itemCatalog: Items, avatarTimeProvider: Clock);
        internal async Task<(CharacterRecord A, CharacterRecord B, CharacterRecord Other)> Seed(JsonGameStore store)
        {
            await store.CreateAccountAsync(new("sender", "password", OicqCode: "123456"));
            await store.CreateAccountAsync(new("target", "password", OicqCode: "123456"));
            var roster = ((await store.CreateCharacterAsync("sender", new("Sender", Level: 1))).Character!,
                (await store.CreateCharacterAsync("target", new("收礼", Job: 1, GrowType: 2, Level: 1))).Character!,
                (await store.CreateCharacterAsync("target", new("Sibling", Level: 1))).Character!);
            // Gift transactions need their own funded test wallets.
            await store.GrantCeraToAccountAsync("sender", 50_000);
            await store.GrantCeraToAccountAsync("target", 50_000);
            return ((await store.GetCharacterAsync("sender", roster.Item1.Id))!,
                (await store.GetCharacterAsync("target", roster.Item2.Id))!,
                (await store.GetCharacterAsync("target", roster.Item3.Id))!);
        }
        internal Task<CeraGiftResult> Gift(JsonGameStore store, CharacterRecord a, string name, uint commodity = 100018) =>
            store.GiftCeraShopAsync("sender", a.Id, name, [new(0xFF, 0, commodity)], Shop);
        public ValueTask DisposeAsync() { Directory.Delete(root, true); return ValueTask.CompletedTask; }
    }
}
