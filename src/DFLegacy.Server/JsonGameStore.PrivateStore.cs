using System.Text.Json;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    internal CommerceActivityRegistry Commerce { get; } = new();
    private PrivateStoreCatalog? _storeCatalog;
    private PrivateStoreCatalog StoreCatalog => privateStoreCatalog ?? (_storeCatalog ??= new(new ScriptFileSystem(options,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ScriptFileSystem>.Instance)));
    private bool IsEconomicWriteBlocked(Guid id) => Commerce.Is(id, CommerceActivity.Pvp) || _pendingTradeDeliveries.ContainsKey(id) || Commerce.Is(id, CommerceActivity.Store) || Commerce.Is(id, CommerceActivity.StoreRevoked)
        || StoreCharacter(_accounts, id)?.PrivateStoreStock is not null;
    private static long PendingStoreGold(CharacterRecord c) => c.PendingPrivateStoreGoldCredits?.Sum(x => (long)x.Amount) ?? 0;
    private List<AccountRecord> CopyStoreAccountsLocked() => JsonSerializer.Deserialize<List<AccountRecord>>(JsonSerializer.Serialize(_accounts))!;
    private static CharacterRecord? StoreCharacter(IEnumerable<AccountRecord> accounts, Guid id) => accounts.SelectMany(a => a.Characters).FirstOrDefault(c => c.Id == id);
    private static void SetStoreCharacter(List<AccountRecord> accounts, CharacterRecord c)
    {
        var a = accounts.First(a => a.Characters.Any(old => old.Id == c.Id));
        a.Characters[a.Characters.FindIndex(old => old.Id == c.Id)] = c;
    }
    private async Task<bool> SaveStoreAccountsLockedAsync(List<AccountRecord> accounts, CancellationToken ct)
    {
        try { await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, ct); }
        catch (Exception e)
        { _rewardQuarantined = true; logger.LogError(e, "Private-store snapshot outcome uncertain; store quarantined."); return false; }
        _accounts = accounts; return true;
    }

    internal async Task<PrivateStoreMutationResult> DisplayPrivateStoreAsync(Guid owner, Guid token,
        IReadOnlyList<PrivateStoreDisplayRequest> requests, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown);
            var c = StoreCharacter(_accounts, owner);
            if (c is null) return new(DungeonRewardLedgerStatus.Rejected, 58);
            if (!Commerce.Owns(owner, CommerceActivity.Store, token) || c.PrivateStoreStock is not null)
                return new(DungeonRewardLedgerStatus.Rejected, 57);
            if (itemCatalog is null || requests.Count is < 1 or > 7 || requests.Select(r => r.Slot).Distinct().Count() != requests.Count
                || requests.Select(r => r.SourceSlot).Distinct().Count() != requests.Count || _pendingTradeDeliveries.ContainsKey(owner))
                return new(DungeonRewardLedgerStatus.Rejected, 17);
            var inventory = (c.Inventory ?? []).ToDictionary(i => i.Slot);
            var listings = new List<PrivateStoreListing>(); long total = 0;
            foreach (var request in requests)
            {
                if (request.Slot > 6 || request.SourceKind != 0 || request.UnitPrice is 0 or > int.MaxValue
                    || !CharacterInventoryLayout.IsMainItemSlot(request.SourceSlot)
                    || !inventory.TryGetValue(request.SourceSlot, out var item) || item.InstanceId == Guid.Empty
                    || !itemCatalog.TryGetDefinition(item.ItemId, out var def)
                    || def.InventoryCategory is not (ItemInventoryCategory.Equipment or ItemInventoryCategory.Material or ItemInventoryCategory.Consumable)
                    || def.Cash is > 0 || !CharacterItemSealing.CanTrade(item, def))
                    return new(DungeonRewardLedgerStatus.Rejected, 17);
                var wire = CharacterItemWireProjection.Project(item, itemCatalog);
                var count = def.IsStackable ? request.AddInfo : 1;
                if (count == 0 || def.IsStackable && count > item.CountOrValue || !def.IsStackable && request.AddInfo != wire.AddInfo)
                    return new(DungeonRewardLedgerStatus.Rejected, 17);
                total += (long)request.UnitPrice * count;
                if (total > int.MaxValue) return new(DungeonRewardLedgerStatus.Rejected, 17);
                var rest = def.IsStackable ? item.CountOrValue - count : 0;
                var stock = def.IsStackable ? item with { CountOrValue = count, InstanceId = rest > 0 ? Guid.NewGuid() : item.InstanceId } : item;
                if (rest == 0) inventory.Remove(request.SourceSlot);
                else inventory[request.SourceSlot] = item with { CountOrValue = rest };
                listings.Add(new(request.Slot, request.UnitPrice, request.SourceSlot, item.InstanceId, stock, count, request.AddInfo));
            }
            var updated = c with { Inventory = inventory.Values.OrderBy(i => i.Slot).ToList(), PrivateStoreStock = new(token, listings) };
            var accounts = CopyStoreAccountsLocked(); SetStoreCharacter(accounts, updated);
            return await SaveStoreAccountsLockedAsync(accounts, ct) ? new(DungeonRewardLedgerStatus.Committed, Character: updated) : new(DungeonRewardLedgerStatus.Unknown);
        }
        finally { _gate.Release(); }
    }

    internal async Task<PrivateStorePurchaseResult> BuyPrivateStoreAsync(Guid buyerId, Guid sellerId, Guid token,
        ushort slot, uint addInfo, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown);
            PrivateStorePurchaseResult Reject(byte e) => new(DungeonRewardLedgerStatus.Rejected, e);
            var buyer = StoreCharacter(_accounts, buyerId); var seller = StoreCharacter(_accounts, sellerId);
            if (seller is null || buyer is null || itemCatalog is null || statCatalog is null) return Reject(58);
            if (buyerId == sellerId || Commerce.Is(buyerId, CommerceActivity.Store)) return Reject(60);
            if (Commerce.Is(buyerId, CommerceActivity.Pvp) || Commerce.Is(buyerId, CommerceActivity.Trade) || _pendingTradeDeliveries.ContainsKey(buyerId)) return Reject(1);
            if (seller.PrivateStoreStock?.StoreId != token || !Commerce.Owns(sellerId, CommerceActivity.Store, token)) return Reject(21);
            var listing = seller.PrivateStoreStock.Listings.FirstOrDefault(l => l.Slot == slot);
            if (listing is null || !itemCatalog.TryGetDefinition(listing.Item.ItemId, out var def)) return Reject(17);
            var quantity = def.IsStackable ? addInfo : 1;
            if (def.IsStackable ? addInfo == 0 : addInfo != listing.AddInfo) return Reject(1);
            if (quantity > listing.Quantity) return Reject(17);
            var gross64 = (long)listing.UnitPrice * quantity;
            if (gross64 is <= 0 or > int.MaxValue) return Reject(1);
            var gross = (uint)gross64; var fee = (uint)(gross64 * 4 / 100);
            var pending = PendingDungeonGold(seller) + PendingStoreGold(seller) + PendingMentorGold(seller);
            if (!StoreCatalog.TryGetGoldLimit(seller.Level, out var sellerLimit) || seller.Gold + pending + gross64 > sellerLimit) return Reject(22);
            if (buyer.Gold < gross64) return Reject(10);
            var moved = def.IsStackable ? listing.Item with { CountOrValue = quantity, InstanceId = quantity < listing.Quantity ? Guid.NewGuid() : listing.Item.InstanceId } : listing.Item;
            if (!TryPlaceStorePurchase(buyer, moved, def, out var inventory, out var placed)) return Reject(4);
            var updatedBuyer = buyer with { Gold = checked(buyer.Gold - (int)gross), Inventory = inventory };
            var stock = seller.PrivateStoreStock.Listings.Where(l => l.Slot != slot).ToList();
            if (quantity < listing.Quantity) stock.Add(listing with { Quantity = listing.Quantity - quantity, AddInfo = listing.AddInfo - quantity,
                Item = listing.Item with { CountOrValue = listing.Item.CountOrValue - quantity } });
            var updatedSeller = seller with { PrivateStoreStock = seller.PrivateStoreStock with { Listings = stock.OrderBy(l => l.Slot).ToList() },
                PendingPrivateStoreGoldCredits = [.. seller.PendingPrivateStoreGoldCredits ?? [], new(Guid.NewGuid(), gross - fee)] };
            var accounts = CopyStoreAccountsLocked(); SetStoreCharacter(accounts, updatedBuyer); SetStoreCharacter(accounts, updatedSeller);
            if (!await SaveStoreAccountsLockedAsync(accounts, ct)) return new(DungeonRewardLedgerStatus.Unknown);
            var cash = accounts.First(a => a.Characters.Any(c => c.Id == buyerId)).Cash;
            return new(DungeonRewardLedgerStatus.Committed, Buyer: updatedBuyer, Seller: updatedSeller, Item: placed,
                DestinationSlot: placed.Slot, Gross: gross, Fee: fee, SoldQuantity: quantity,
                ProjectedSellerGold: checked((uint)(seller.Gold + PendingStoreGold(updatedSeller))), Cash: (uint)Math.Max(0, cash ?? 0));
        }
        finally { _gate.Release(); }
    }

    private bool TryPlaceStorePurchase(CharacterRecord buyer, CharacterItemRecord moved, ItemDefinition def,
        out List<CharacterItemRecord> items, out CharacterItemRecord placed)
    {
        items = []; placed = null!;
        if (itemCatalog is null || statCatalog is null || !CharacterInventoryLayout.TryGetCategoryRange(def.InventoryCategory, out var start, out var end)) return false;
        var inventory = (buyer.Inventory ?? []).ToDictionary(i => i.Slot);
        if (def.MaximumHavingCount is > 0 && inventory.Values.Where(i => i.ItemId == def.Id).Sum(i => (long)i.CountOrValue) + (def.IsStackable ? moved.CountOrValue : 1) > def.MaximumHavingCount) return false;
        var category = Enumerable.Range(start, end - start).Select(i => (ushort)i).ToArray();
        var quick = Enumerable.Range(CharacterInventoryLayout.QuickSlotStart, CharacterInventoryLayout.QuickSlotEnd - CharacterInventoryLayout.QuickSlotStart).Select(i => (ushort)i).ToArray();
        ushort? destination = null;
        if (def.IsStackable)
        {
            foreach (var s in quick.Concat(category))
            {
                if (!inventory.TryGetValue(s, out var old) || !CanStackStoreItems(old, moved)) continue;
                if ((long)old.CountOrValue + moved.CountOrValue > CharacterInventoryPlanner.GetStackLimit(def)) continue;
                destination = s; break;
            }
        }
        if (destination is null)
        {
            var empty = def.IsStackable && def.InventoryCategory == ItemInventoryCategory.Consumable ? quick.Concat(category) : category.Concat(quick);
            foreach (var s in empty) if (!inventory.ContainsKey(s)) { destination = s; break; }
            if (def.IsStackable && moved.CountOrValue > CharacterInventoryPlanner.GetStackLimit(def)) return false;
        }
        if (destination is null) return false;
        placed = moved with { Slot = destination.Value };
        inventory[destination.Value] = inventory.TryGetValue(destination.Value, out var existing)
            ? existing with { CountOrValue = existing.CountOrValue + moved.CountOrValue } : placed;
        var weightLimit = CharacterInventoryPlanner.CalculateCarriedWeightLimit(statCatalog.Get(buyer).InventoryLimit, buyer.Equipment ?? [], itemCatalog);
        if (CharacterInventoryPlanner.CalculateCarriedWeight(inventory.Values, buyer.Equipment ?? [], buyer.CreatureInventory ?? [], itemCatalog) > weightLimit) return false;
        items = inventory.Values.OrderBy(i => i.Slot).ToList(); return true;
    }
    private static bool CanStackStoreItems(CharacterItemRecord a, CharacterItemRecord b) => a.ItemId == b.ItemId
        && a.State == b.State && a.Durability == b.Durability && a.SealState == b.SealState;

    private bool TryRestoreStoreStockLocked(CharacterRecord c, bool force, out CharacterRecord updated)
    {
        updated = c;
        var inventory = (c.Inventory ?? []).ToDictionary(i => i.Slot);
        var mails = new List<CreateCharacterMailRequest>();
        foreach (var listing in c.PrivateStoreStock?.Listings ?? [])
        {
            if (!inventory.TryGetValue(listing.SourceSlot, out var existing))
                inventory[listing.SourceSlot] = listing.Item with { Slot = listing.SourceSlot };
            else if (existing.InstanceId == listing.SourceInstanceId && CanStackStoreItems(existing, listing.Item)
                && itemCatalog?.TryGetDefinition(existing.ItemId, out var def) == true && def.IsStackable
                && (long)existing.CountOrValue + listing.Quantity <= CharacterInventoryPlanner.GetStackLimit(def))
                inventory[listing.SourceSlot] = existing with { CountOrValue = existing.CountOrValue + listing.Quantity };
            else
            {
                if (!force) return false;
                var i = listing.Item;
                mails.Add(new("个人商店", "离场收摊时返还的物品。", Attachment: new(i.ItemId, i.CountOrValue, i.State, i.Durability, i.SealState,
                    i.AvatarRemainingSeconds, i.AvatarAbilityIndex, i.EquipmentQualitySeed, i.InstanceId, i.AvatarExpiresAtUnixSeconds)));
            }
        }
        if (!TryAppendGeneratedMailsLocked(c, mails, out var mailbox, out _, out _)) return false;
        updated = c with { Inventory = inventory.Values.OrderBy(i => i.Slot).ToList(), PrivateStoreStock = null,
            Gold = checked((int)(c.Gold + PendingStoreGold(c))), PendingPrivateStoreGoldCredits = null, Mailbox = mailbox };
        return true;
    }
    internal async Task<PrivateStoreMutationResult> ClosePrivateStoreAsync(Guid owner, Guid token, bool force, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown);
            var c = StoreCharacter(_accounts, owner);
            if (c is null) return new(DungeonRewardLedgerStatus.Rejected, 58);
            var revoked = force && Commerce.Owns(owner, CommerceActivity.StoreRevoked, token);
            if (!revoked && (!Commerce.Owns(owner, CommerceActivity.Store, token) || c.PrivateStoreStock is { } s && s.StoreId != token))
                return new(DungeonRewardLedgerStatus.Rejected, 59);
            if (!TryRestoreStoreStockLocked(c, force, out var updated)) return new(DungeonRewardLedgerStatus.Rejected, 22);
            var accounts = CopyStoreAccountsLocked(); SetStoreCharacter(accounts, updated);
            return await SaveStoreAccountsLockedAsync(accounts, ct) ? new(DungeonRewardLedgerStatus.Committed, Character: updated) : new(DungeonRewardLedgerStatus.Unknown);
        }
        finally { _gate.Release(); }
    }
    internal async Task<CharacterRecord?> ConsumePrivateStoreGoldCreditsAsync(Guid owner, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            if (_pendingTradeDeliveries.ContainsKey(owner)) return null;
            var c = StoreCharacter(_accounts, owner);
            if (c?.PendingPrivateStoreGoldCredits is not { Count: > 0 }) return null;
            var updated = c with { Gold = checked((int)(c.Gold + PendingStoreGold(c))), PendingPrivateStoreGoldCredits = null };
            var accounts = CopyStoreAccountsLocked(); SetStoreCharacter(accounts, updated);
            if (!await SaveStoreAccountsLockedAsync(accounts, ct)) throw new IOException("Private-store credit outcome uncertain.");
            return updated;
        }
        finally { _gate.Release(); }
    }
    private async Task RecoverPrivateStoresAtStartupLockedAsync(CancellationToken ct)
    {
        if (_rewardQuarantined || !_accounts.SelectMany(a => a.Characters).Any(c => c.PrivateStoreStock is not null || c.PendingPrivateStoreGoldCredits is { Count: > 0 })) return;
        var accounts = CopyStoreAccountsLocked();
        foreach (var c in accounts.SelectMany(a => a.Characters).ToArray())
            if (c.PrivateStoreStock is not null || c.PendingPrivateStoreGoldCredits is { Count: > 0 })
            {
                if (!TryRestoreStoreStockLocked(c, true, out var restored)) throw new IOException("Private-store stock could not be safely returned at startup.");
                SetStoreCharacter(accounts, restored);
            }
        if (!await SaveStoreAccountsLockedAsync(accounts, ct)) throw new IOException("Private-store startup recovery outcome uncertain.");
    }
}
