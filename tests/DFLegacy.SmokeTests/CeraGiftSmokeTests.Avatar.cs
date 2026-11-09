using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static partial class CeraGiftSmokeTests
{
    private sealed class AvatarFixture(int? max = null) : IAsyncDisposable
    {
        internal readonly AvatarGiftFixture Catalog = new(max);
        internal readonly Clock Clock = new();
        internal ServerOptions Options => Catalog.Options;
        internal ItemCatalog Items => Catalog.Items;
        internal CeraShopCatalog Shop => Catalog.Shop;
        internal JsonGameStore Open(IDungeonRewardSnapshotWriter? writer = null) => writer is null
            ? new(Options, NullLogger<JsonGameStore>.Instance, itemCatalog: Items, avatarTimeProvider: Clock)
            : new(Options, NullLogger<JsonGameStore>.Instance, writer, itemCatalog: Items, avatarTimeProvider: Clock);
        internal async Task<(CharacterRecord A, CharacterRecord B)> Seed(JsonGameStore store)
        {
            await store.CreateAccountAsync(new("sender", "password", OicqCode: "123456"));
            await store.CreateAccountAsync(new("target", "password", OicqCode: "123456"));
            var roster = ((await store.CreateCharacterAsync("sender", new("Buyer", Level: 1))).Character!,
                (await store.CreateCharacterAsync("target", new("Receiver", Job: 1, Level: 1))).Character!);
            // Gift transactions need their own funded test wallets.
            await store.GrantCeraToAccountAsync("sender", 50_000);
            await store.GrantCeraToAccountAsync("target", 50_000);
            return ((await store.GetCharacterAsync("sender", roster.Item1.Id))!,
                (await store.GetCharacterAsync("target", roster.Item2.Id))!);
        }
        internal Task<CeraGiftResult> Gift(JsonGameStore store, CharacterRecord sender, string recipient,
            uint commodity = 270000, ushort term = 1, byte ability = 4) =>
            store.GiftCeraShopAsync("sender", sender.Id, recipient, [new(ability, term, commodity)], Shop);
        public ValueTask DisposeAsync() { Catalog.Dispose(); return ValueTask.CompletedTask; }
    }

    private static async Task AvatarTransactions(Action<bool, string> check)
    {
        foreach (var (commodity, term, price, seconds) in new[] {
            (270000u, (ushort)1, 300, 604800u), (270001u, (ushort)2, 660, 2592000u),
            (270002u, (ushort)3, 1980, 0u), (290000u, (ushort)1, 450, 604800u),
            (160000u, (ushort)1, 300, 604800u) })
        {
            await using var f = new AvatarFixture(); var store = f.Open(); var (a, b) = await f.Seed(store);
            var ability = (byte)(commodity == 290000 ? 43 : commodity == 160000 ? 1 : 4);
            var start = f.Clock.Utc.ToUnixTimeSeconds();
            var result = await f.Gift(store, a, b.Name, commodity, term, ability);
            var target = await Character(store, "target", b.Id);
            var sender = await Character(store, "sender", a.Id);
            var attachment = target.Mailbox?.SingleOrDefault()?.Attachment;
            check(result.Status == DungeonRewardLedgerStatus.Committed && sender.Cash == 50000 - price
                && (sender.AvatarInventory?.Count ?? 0) == 0 && attachment?.AvatarAbilityIndex == ability
                && attachment.AvatarRemainingSeconds == seconds && attachment.InstanceId != Guid.Empty
                && attachment.AvatarExpiresAtUnixSeconds == (seconds == 0 ? (long?)null : start + seconds)
                && (await store.QueryCeraGiftRecipientAsync(a.Id, b.Name)).ReceivedCount == 1,
                $"avatar gift {commodity} atomically debits{price}, preserves ability{ability}/term/UUID and grants no buyer item");
            if (attachment is null) continue;
            var mail = target.Mailbox!.Single();
            f.Clock.Utc = f.Clock.Utc.AddSeconds(60);
            var restarted = f.Open(); await restarted.InitializeAsync();
            var pending = (await Character(restarted, "target", b.Id)).Mailbox!.Single().Attachment!;
            check(pending.InstanceId == attachment.InstanceId && pending.AvatarAbilityIndex == ability
                && pending.AvatarExpiresAtUnixSeconds == attachment.AvatarExpiresAtUnixSeconds,
                $"avatar gift {commodity} unclaimed restart preserves grant identity, ability and absolute deadline");
            check(CharacterAvatarInventoryPlanner.TryPlace(new Dictionary<ushort, CharacterItemRecord>(),
                pending, f.Items, out var plan, out _), $"avatar gift {commodity} uses ordinary avatar mail placement");
            var claim = await restarted.ClaimCharacterMailAsync("target", b.Id, mail.Id, b.Gold,
                b.Inventory ?? [], b.Equipment ?? [], avatarInventory: plan.Inventory.Values);
            var reloaded = f.Open(); await reloaded.InitializeAsync();
            var item = (await Character(reloaded, "target", b.Id)).AvatarInventory?.SingleOrDefault();
            check(claim is not null && item?.AvatarAbilityIndex == ability && item.InstanceId == attachment.InstanceId
                && item.AvatarExpiresAtUnixSeconds == attachment.AvatarExpiresAtUnixSeconds
                && CharacterItemWireProjection.Project(item, f.Items).Durability == ability,
                $"avatar gift {commodity} real claim and restart retain original deadline/UUID and wire durability{ability}");
            if (item is not null)
            {
                var wire = CharacterItemWireProjection.Project(item, f.Items);
                var packet = GameProtocolEngine.CreateItemList(1, [new(item.Slot, item.ItemId,
                    wire.AddInfo, wire.ItemAttr, wire.Durability, wire.SealState)]);
                check(packet.Payload.Length == 15 && packet.Payload[0] == 1 && packet.Payload[1] == 1
                    && packet.Payload[12] == ability && packet.Payload[13] == 0,
                    $"claimed avatar {commodity} real ITEM_LIST serializer emits selected ability{ability} in native durability bytes");
            }
        }
    }

    private static async Task AvatarRejections(Action<bool, string> check)
    {
        foreach (var (commodity, term, ability, error) in new[] {
            (270000u, 1, 255, 8), (270000u, 1, 5, 8), (290000u, 1, 44, 8), (160000u, 1, 2, 8),
            (270000u, 0, 4, 8), (270000u, 4, 4, 8), (270001u, 1, 4, 8), (999999u, 1, 4, 21),
            (270010u, 1, 4, 8), (270011u, 1, 4, 8), (270012u, 1, 4, 8) })
        {
            await using var f = new AvatarFixture(); var store = f.Open(); var (a, b) = await f.Seed(store);
            var before = await File.ReadAllBytesAsync(f.Options.DataPath);
            var result = await f.Gift(store, a, b.Name, commodity, (ushort)term, (byte)ability);
            var after = await File.ReadAllBytesAsync(f.Options.DataPath);
            check(result.Status == DungeonRewardLedgerStatus.Rejected && result.Error == error
                && before.SequenceEqual(after),
                $"avatar invalid {commodity}/{term}/{ability} rejects{error} without changing cash/mail/quota/disk");
        }
    }

    private static async Task AvatarSelfPurchase(Action<bool, string> check)
    {
        foreach (var finite in new[] { false, true })
        foreach (var overflow in new[] { false, true })
        {
            await using var f = new AvatarFixture(); var store = f.Open(); var (a, _) = await f.Seed(store);
            // Literal gift0 packet: ability43, permanent commodity290002. No serializer builds the expected row.
            byte[] payload = finite ? [0, 0, 0, 1, 4, 1, 0, 0xB0, 0x1E, 4, 0]
                : [0, 0, 0, 1, 43, 3, 0, 0xD2, 0x6C, 4, 0];
            check(GameProtocolEngine.TryReadCeraShopPurchase(payload, out var request), "self avatar literal CMD67 decodes");
            var row = request.Products.Single();
            check(f.Shop.TryResolveAvatarPurchase(row, out var product, out var definition),
                "self avatar selection uses the shared ordinary-avatar resolver");
            var attachment = AvatarLifetimePolicy.CreatePurchaseAttachment(product, definition,
                f.Clock.Utc.ToUnixTimeSeconds(), row.AvatarAbilityIndex);
            var before = await File.ReadAllBytesAsync(f.Options.DataPath);
            check(!f.Shop.TryResolveAvatarPurchase(row with { ProductType = 255 }, out _, out _)
                && !f.Shop.TryResolveAvatarPurchase(row with { ProductType = (byte)(finite ? 5 : 44) }, out _, out _),
                "self avatar rejects FF and upper-bound selection before planning or saving");
            var after = await File.ReadAllBytesAsync(f.Options.DataPath);
            check(before.SequenceEqual(after), "invalid self avatar ability does not mutate cash, inventory or mail");
            var bag = overflow ? Enumerable.Range(0, 105).ToDictionary(i => (ushort)i,
                i => new CharacterItemRecord((ushort)i, 51020, 1, AvatarRemainingSeconds: 0, InstanceId: Guid.NewGuid()))
                : new Dictionary<ushort, CharacterItemRecord>();
            var placed = CharacterAvatarInventoryPlanner.TryPlace(bag, attachment, f.Items, out var plan, out _);
            var saved = await store.SaveCharacterCeraShopPurchaseAsync("sender", a.Id, finite ? 49700 : 47030, a.Gold,
                a.Inventory ?? [], a.Equipment ?? [], expectedCash: 50000,
                avatarInventory: placed ? plan.Inventory.Values : bag.Values,
                generatedMails: placed ? [] : [new("System", "Purchase overflow", Attachment: attachment)]);
            var actual = placed ? saved?.AvatarInventory?.Single(i => i.InstanceId == attachment.InstanceId).AvatarAbilityIndex
                : saved?.Mailbox?.Single().Attachment?.AvatarAbilityIndex;
            var savedDeadline = placed ? saved?.AvatarInventory?.Single(i => i.InstanceId == attachment.InstanceId).AvatarExpiresAtUnixSeconds
                : saved?.Mailbox?.Single().Attachment?.AvatarExpiresAtUnixSeconds;
            check(saved?.Cash == (finite ? 49700 : 47030) && actual == (finite ? 4 : 43) && placed != overflow
                && savedDeadline == (finite ? f.Clock.Utc.ToUnixTimeSeconds() + 604800 : (long?)null),
                $"self avatar purchase finite={finite} overflow={overflow} persists selected ability/expiry and exact debit through real save");
            before = await File.ReadAllBytesAsync(f.Options.DataPath);
            var stale = await store.SaveCharacterCeraShopPurchaseAsync("sender", a.Id, 47030, a.Gold, [], [],
                expectedCash: 50000, avatarInventory: plan?.Inventory.Values ?? bag.Values);
            after = await File.ReadAllBytesAsync(f.Options.DataPath);
            check(stale is null && before.SequenceEqual(after), "self avatar save retains expectedCash protection against a stale concurrent buyer");
        }
    }
}
