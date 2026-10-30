using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed record CeraGiftQueryResult(byte Error, CharacterRecord? Character = null, byte ReceivedCount = 0);
public sealed record CeraGiftResult(DungeonRewardLedgerStatus Status, byte Error = 4, int SenderCash = 0,
    Guid RecipientId = default, IReadOnlyList<CeraShopProduct>? Products = null,
    uint ErrorCommodity = uint.MaxValue, int ErrorCategory = 0);

public sealed partial class JsonGameStore
{
    public async Task<int?> GetAccountCeraAsync(string userName, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined) return null;
            var account = _accounts.FirstOrDefault(a => string.Equals(a.UserName, userName, StringComparison.OrdinalIgnoreCase));
            return account is null ? null : Math.Max(0, account.Cash ?? DefaultAccountCash);
        }
        finally { _gate.Release(); }
    }

    private int GiftYearMonth
    {
        get
        {
            var local = TimeZoneInfo.ConvertTime((avatarTimeProvider ?? TimeProvider.System).GetUtcNow(),
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
            return local.Year * 100 + local.Month;
        }
    }

    private static int ReceivedGifts(AccountRecord account, int yearMonth) => account.GiftReceiveYearMonth == yearMonth
        ? Math.Clamp(account.GiftReceiveCount, 0, byte.MaxValue) : 0;

    public async Task<CeraGiftQueryResult> QueryCeraGiftRecipientAsync(Guid senderId, string recipientName,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined) return new(2);
            var sender = StoreCharacter(_accounts, senderId);
            if (sender is null) return new(2);
            if (string.Equals(sender.Name, recipientName, StringComparison.Ordinal)) return new(7);
            var account = _accounts.FirstOrDefault(a => a.Characters.Any(c => c.Name == recipientName));
            var target = account?.Characters.FirstOrDefault(c => c.Name == recipientName);
            return target is null ? new(21) : new(0, target, (byte)ReceivedGifts(account!, GiftYearMonth));
        }
        finally { _gate.Release(); }
    }

    public async Task<CeraGiftResult> GiftCeraShopAsync(string userName, Guid senderId, string recipientName,
        IReadOnlyList<GameCeraShopPurchaseItem> requests, CeraShopCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown);
            uint errorCommodity = requests.Count > 0 ? requests[0].CommodityNo : uint.MaxValue;
            var errorCategory = 0;
            CeraGiftResult Reject(byte error) => new(DungeonRewardLedgerStatus.Rejected, error,
                ErrorCommodity: errorCommodity, ErrorCategory: errorCategory);
            var senderAccount = _accounts.FirstOrDefault(a => string.Equals(a.UserName, userName, StringComparison.OrdinalIgnoreCase));
            var sender = senderAccount?.Characters.FirstOrDefault(c => c.Id == senderId);
            if (sender is null || itemCatalog is null || requests.Count is < 1 or > 32) return Reject(4);
            var recipientAccount = _accounts.FirstOrDefault(a => a.Characters.Any(c => c.Name == recipientName));
            var recipient = recipientAccount?.Characters.FirstOrDefault(c => c.Name == recipientName);
            if (recipient is null) return Reject(3); // Physical deletion has no tombstone in this store.
            if (sender.Id == recipient.Id) return Reject(7); // Enforce the ACT1 self-query constraint at checkout too.
            if (recipientAccount!.Blacklist?.Any(b => b.Name == sender.Name) == true) return Reject(77);

            var products = new List<CeraShopProduct>();
            var mailRequests = new List<CreateCharacterMailRequest>();
            var transactionNow = AvatarNow;
            long total = 0;
            foreach (var request in requests)
            {
                errorCommodity = request.CommodityNo;
                if (!catalog.TryGetProduct(request.CommodityNo, out var product)) return Reject(21);
                errorCategory = product.ClientCategory;
                CharacterMailAttachmentRecord attachment;
                ItemDefinition definition;
                if (!product.UsesMainInventory)
                {
                    if (product.GiftDisallowed || product.Sticker == 99
                        || !catalog.TryResolveAvatarPurchase(request, out product, out definition)) return Reject(8);
                    attachment = AvatarLifetimePolicy.CreatePurchaseAttachment(
                        product, definition, transactionNow, request.AvatarAbilityIndex);
                }
                else if (string.Equals(product.Section, "creature", StringComparison.OrdinalIgnoreCase))
                {
                    if (!catalog.TryResolveCreatureEggGift(request, out product, out definition)) return Reject(8);
                    // Fresh permanent sealed eggs only. Mail count is quantity, not the
                    // creature wire UID, which the existing claim planner allocates.
                    attachment = CharacterItemIdentity.Ensure(new CharacterMailAttachmentRecord(
                        product.ItemId, 1, State: 0, Durability: 0, SealState: 1), definition);
                }
                else
                {
                    if (!CanGiftPhysicalProduct(product, request.AttributeValue, out definition)) return Reject(8);
                    attachment = CharacterItemIdentity.Ensure(new CharacterMailAttachmentRecord(product.ItemId, product.Quantity,
                        Durability: definition.IsEquipment ? itemCatalog.GetInitialDurability(product.ItemId) : (ushort)0,
                        SealState: CharacterItemSealing.GetInitialSealState(definition)), definition);
                }
                total += product.CashPrice;
                if (total > int.MaxValue) return Reject(11);
                // Bound splitting before allocating, even for a malformed local catalog.
                var stackLimit = definition.IsStackable ? Math.Max(1u, (uint)Math.Max(1, definition.StackLimit ?? 1)) : 1u;
                if (((ulong)product.Quantity + stackLimit - 1) / stackLimit > byte.MaxValue) return Reject(4);
                foreach (var chunk in CharacterInventoryPlanner.CreateMailAttachments(attachment, itemCatalog))
                    mailRequests.Add(new(sender.Name, $"{sender.Name}赠送的商城礼物", Attachment: chunk));
                products.Add(product);
            }
            var cash = Math.Max(0, senderAccount!.Cash ?? DefaultAccountCash);
            if (cash < total) return Reject(11);
            var yearMonth = GiftYearMonth;
            var received = ReceivedGifts(recipientAccount, yearMonth);
            // ACT1 exposes the cap in UI; recheck under the transaction gate to stop stale/concurrent bypasses.
            if ((long)received + requests.Count > catalog.MaxGiftCount) return Reject(4);
            if (!TryAppendGeneratedMailsLocked(recipient, mailRequests, out var mailbox, out _, out _)) return Reject(4);

            var accounts = CopyStoreAccountsLocked();
            var targetIndex = accounts.FindIndex(a => a.Id == recipientAccount.Id);
            var targetAccount = accounts[targetIndex];
            var targetCharacterIndex = targetAccount.Characters.FindIndex(c => c.Id == recipient.Id);
            targetAccount.Characters[targetCharacterIndex] = targetAccount.Characters[targetCharacterIndex] with { Mailbox = mailbox };
            accounts[targetIndex] = targetAccount with { GiftReceiveYearMonth = yearMonth, GiftReceiveCount = received + requests.Count };
            var senderIndex = accounts.FindIndex(a => a.Id == senderAccount.Id);
            var nextCash = cash - (int)total;
            accounts[senderIndex] = SetAccountCash(accounts[senderIndex], nextCash);
            try { await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, cancellationToken); }
            catch (Exception exception)
            {
                _rewardQuarantined = true;
                logger.LogError(exception, "Cera-gift snapshot outcome uncertain; store quarantined.");
                return new(DungeonRewardLedgerStatus.Unknown);
            }
            _accounts = accounts;
            return new(DungeonRewardLedgerStatus.Committed, 0, nextCash, recipient.Id, products.AsReadOnly());
        }
        finally { _gate.Release(); }
    }

    private bool CanGiftPhysicalProduct(CeraShopProduct product, ushort attribute, out ItemDefinition definition)
    {
        definition = null!;
        return attribute == product.AttributeValue && !product.GiftDisallowed && product.Sticker != 99
            && product.Section is "item" or "material" or "recoveryitem" or "visual"
            && product.PremiumContract is null && product.WarehouseUpgradeCapacity is null && product.DirectCurrencyGrant is null
            && product.ItemId is not (1 or 2 or 3 or 28)
            && !CharacterWarehouseProgression.TryGetItemTargetCapacity(product.ItemId, out _)
            && itemCatalog!.TryGetDefinition(product.ItemId, out definition)
            && definition.InventoryCategory is ItemInventoryCategory.Equipment or ItemInventoryCategory.Consumable or ItemInventoryCategory.Material
            && definition.CeraBooster is null && definition.CeraPackage is null && definition.Lottery is null
            && product.Quantity > 0 && (!definition.IsEquipment || product.Quantity == 1);
    }
}
