namespace DFLegacy.Server;

public sealed record CeraShopContentReward(
    ushort ItemId,
    uint Count,
    ushort AvatarAbilityIndex = 0,
    int AvatarPeriodDays = 0);

public sealed record CeraShopContentPlacementPlan(
    Dictionary<ushort, CharacterItemRecord> MainInventory,
    Dictionary<ushort, CharacterItemRecord> AvatarInventory,
    Dictionary<ushort, CharacterItemRecord> CreatureInventory,
    IReadOnlyList<CharacterMailAttachmentRecord> OverflowItems,
    IReadOnlyList<CeraShopContentReward> Rewards);

public sealed record CeraContainerImmediateEffects(
    int Gold,
    uint VictoryPoints,
    ushort WarehouseCapacity,
    IReadOnlyList<PremiumServicePurchaseGrant> PremiumServices);

public static class CeraShopContentPlanner
{
    internal static bool ShouldSettleAsEffect(ItemCatalog catalog, ushort itemId) =>
        CharacterWarehouseProgression.TryGetItemTargetCapacity(itemId, out _)
        || catalog.TryGetDefinition(itemId, out var definition)
            && (CeraShopCatalog.TryResolvePremiumContract(definition, out _)
                || CeraShopCatalog.TryResolveDirectCurrencyGrant(itemId, definition, out _));

    internal static bool TrySettleImmediateEffects(
        IReadOnlyList<CeraShopContentReward> rewards, ItemCatalog catalog,
        int gold, uint victoryPoints, ushort warehouseCapacity,
        out CeraContainerImmediateEffects effects)
    {
        effects = null!;
        var premiumServices = new List<PremiumServicePurchaseGrant>();
        foreach (var reward in rewards)
        {
            if (!catalog.TryGetDefinition(reward.ItemId, out var definition)) return false;
            if (CeraShopCatalog.TryResolvePremiumContract(definition, out var contract))
            {
                var days = (long)contract.Days * reward.Count;
                if (days is <= 0 or > int.MaxValue) return false;
                premiumServices.Add(new(contract.ServiceType, (int)days));
            }
            else if (CeraShopCatalog.TryResolveDirectCurrencyGrant(reward.ItemId, definition, out var grant)
                && !CeraShopDirectCurrencyPlanner.TryApply(gold, victoryPoints, grant, reward.Count,
                    out gold, out victoryPoints))
            {
                return false;
            }
            warehouseCapacity = CharacterWarehouseProgression.ApplyItemTarget(warehouseCapacity, reward.ItemId);
        }

        effects = new(gold, victoryPoints, warehouseCapacity, premiumServices.AsReadOnly());
        return true;
    }

    public static bool TryCreatePurchase(
        IReadOnlyDictionary<ushort, CharacterItemRecord> mainInventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> avatarInventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> creatureInventory,
        ItemDefinition containerDefinition,
        uint containerCount,
        ItemCatalog catalog,
        uint weightLimit,
        IDropRandomSource random,
        Func<ushort, bool>? shouldSettleAsEffect,
        out CeraShopContentPlacementPlan plan,
        out string failure)
    {
        plan = null!;
        failure = string.Empty;
        if (containerCount == 0 || catalog.IsUnavailableChapterTwoCashItem(containerDefinition.Id))
        {
            failure = "the Cera-shop container is unavailable or has zero quantity";
            return false;
        }

        var rewards = new List<CeraShopContentReward>();
        if (containerDefinition.CeraBooster is { } booster)
        {
            for (var opened = 0u; opened < containerCount; opened++)
            {
                rewards.AddRange(booster.SelectAll(random).Select(reward =>
                    new CeraShopContentReward(
                        reward.ItemId,
                        reward.Count,
                        reward.AvatarAbilityIndex,
                        reward.AvatarPeriodDays)));
            }
        }
        else if (containerDefinition.CeraPackage is { } package)
        {
            foreach (var reward in package.Rewards)
            {
                var totalCount = (ulong)reward.Count * containerCount;
                if (totalCount == 0 || totalCount > uint.MaxValue)
                {
                    failure = $"Cera package reward {reward.ItemId} count overflowed";
                    return false;
                }

                rewards.Add(new CeraShopContentReward(
                    reward.ItemId,
                    checked((uint)totalCount)));
            }
        }
        else
        {
            failure = "the purchased item is not a Cera booster or Cera package";
            return false;
        }

        return TryPlace(
            mainInventory,
            avatarInventory,
            creatureInventory,
            rewards,
            catalog,
            weightLimit,
            shouldSettleAsEffect,
            out plan,
            out failure);
    }

    public static bool TryPlace(
        IReadOnlyDictionary<ushort, CharacterItemRecord> mainInventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> avatarInventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> creatureInventory,
        IReadOnlyList<CeraShopContentReward> rewards,
        ItemCatalog catalog,
        uint weightLimit,
        Func<ushort, bool>? shouldSettleAsEffect,
        out CeraShopContentPlacementPlan plan,
        out string failure)
    {
        plan = null!;
        failure = string.Empty;
        if (rewards.Count == 0)
        {
            failure = "the Cera-shop container produced no rewards";
            return false;
        }

        var plannedMain = mainInventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        var plannedAvatar = avatarInventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        var plannedCreature = creatureInventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        var overflow = new List<CharacterMailAttachmentRecord>();
        var grantedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var reward in rewards)
        {
            if (reward.Count == 0
                || !catalog.TryGetDefinition(reward.ItemId, out var rewardDefinition)
                || catalog.IsUnavailableChapterTwoCashItem(reward.ItemId))
            {
                failure = $"reward item {reward.ItemId} is unavailable or has zero quantity";
                return false;
            }

            if (shouldSettleAsEffect?.Invoke(reward.ItemId) == true)
            {
                continue;
            }

            var isAvatar = rewardDefinition.InventoryCategory
                == ItemInventoryCategory.Avatar;
            if (isAvatar && reward.AvatarPeriodDays is < 0 or > 49_710)
            {
                failure = "the avatar reward period exceeds the ACT1 remaining-seconds field";
                return false;
            }
            var avatarSeconds = isAvatar ? checked((uint)reward.AvatarPeriodDays * 86_400u) : 0u;
            var attachment = CharacterItemIdentity.Ensure(
                new CharacterMailAttachmentRecord(
                reward.ItemId,
                reward.Count,
                Durability: rewardDefinition.InventoryCategory
                        == ItemInventoryCategory.Equipment
                    ? catalog.GetInitialDurability(reward.ItemId)
                    : (ushort)0,
                SealState: rewardDefinition.AttachType == ItemAttachType.Sealing
                    ? (byte)1
                    : (byte)0,
                AvatarRemainingSeconds: isAvatar ? avatarSeconds : null,
                AvatarAbilityIndex: isAvatar
                    ? reward.AvatarAbilityIndex
                    : null,
                AvatarExpiresAtUnixSeconds: avatarSeconds == 0 ? null : checked(grantedAt + avatarSeconds)),
                rewardDefinition);

            foreach (var chunk in CharacterInventoryPlanner.CreateMailAttachments(
                         attachment,
                         catalog))
            {
                if (isAvatar)
                {
                    if (CharacterAvatarInventoryPlanner.TryPlace(
                            plannedAvatar,
                            chunk,
                            catalog,
                            out var avatarPlacement,
                            out _))
                    {
                        plannedAvatar = avatarPlacement.Inventory;
                    }
                    else
                    {
                        overflow.Add(chunk);
                    }

                    continue;
                }

                if (rewardDefinition.InventoryCategory == ItemInventoryCategory.Creature)
                {
                    var creatureItem = new CharacterItemRecord(
                        ushort.MaxValue,
                        chunk.ItemId,
                        chunk.CountOrValue,
                        chunk.State,
                        chunk.Durability,
                        chunk.SealState,
                        InstanceId: chunk.InstanceId);
                    if (CharacterCreatureInventoryPlanner.TryPlace(
                            plannedCreature,
                            creatureItem,
                            catalog,
                            preferredEmptySlot: null,
                            out var creaturePlacement,
                            out _))
                    {
                        plannedCreature = creaturePlacement.Inventory;
                    }
                    else
                    {
                        overflow.Add(chunk);
                    }

                    continue;
                }

                if (CharacterInventoryPlanner.TryPlace(
                        plannedMain,
                        chunk,
                        catalog,
                        weightLimit,
                        out var inventoryPlacement,
                        out _))
                {
                    plannedMain = inventoryPlacement.Inventory;
                }
                else
                {
                    overflow.Add(chunk);
                }
            }
        }

        plan = new CeraShopContentPlacementPlan(
            plannedMain,
            plannedAvatar,
            plannedCreature,
            overflow.AsReadOnly(),
            rewards);
        return true;
    }
}
