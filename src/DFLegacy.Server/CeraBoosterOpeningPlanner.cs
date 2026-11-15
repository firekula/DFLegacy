namespace DFLegacy.Server;

public sealed record CeraBoosterOpeningPlan(
    ushort SourceSlot,
    ushort BoosterItemId,
    Dictionary<ushort, CharacterItemRecord> MainInventory,
    Dictionary<ushort, CharacterItemRecord> AvatarInventory,
    Dictionary<ushort, CharacterItemRecord> CreatureInventory,
    IReadOnlyList<CharacterMailAttachmentRecord> OverflowItems,
    IReadOnlyList<CeraShopContentReward> Rewards,
    CeraContainerImmediateEffects? ImmediateEffects = null);

public static class CeraBoosterOpeningPlanner
{
    public static bool TryCreate(
        IReadOnlyDictionary<ushort, CharacterItemRecord> mainInventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> avatarInventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> creatureInventory,
        ushort sourceSlot,
        ItemCatalog catalog,
        uint weightLimit,
        IDropRandomSource random,
        out CeraBoosterOpeningPlan plan,
        out string failure,
        int gold = 0,
        uint victoryPoints = 0,
        ushort warehouseCapacity = CharacterWarehouseProgression.InitialCapacity)
    {
        plan = null!;
        failure = string.Empty;
        if (!CharacterInventoryLayout.IsMainItemSlot(sourceSlot)
            || !mainInventory.TryGetValue(sourceSlot, out var sourceItem)
            || sourceItem.Slot != sourceSlot
            || sourceItem.CountOrValue == 0
            || !catalog.TryGetDefinition(sourceItem.ItemId, out var sourceDefinition))
        {
            failure = "the selected item is not a usable Cera container";
            return false;
        }

        if (catalog.UsesChapterTwoCashContainerPolicy)
        {
            return TryCreateChapterTwo(mainInventory, avatarInventory, creatureInventory, sourceItem,
                sourceDefinition, catalog, weightLimit, random, gold, victoryPoints, warehouseCapacity,
                out plan, out failure);
        }

        if (sourceDefinition.CeraBooster is null)
        {
            failure = "the selected item is not a usable Cera booster";
            return false;
        }

        var rewards = sourceDefinition.CeraBooster.SelectAll(random);
        if (rewards.Count == 0)
        {
            failure = "the Cera booster produced no rewards";
            return false;
        }

        var plannedMain = mainInventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        var plannedAvatar = avatarInventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        var plannedCreature = creatureInventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        var overflow = new List<CharacterMailAttachmentRecord>();
        var grantedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (sourceItem.CountOrValue == 1)
        {
            plannedMain.Remove(sourceSlot);
        }
        else
        {
            plannedMain[sourceSlot] = sourceItem with
            {
                CountOrValue = sourceItem.CountOrValue - 1
            };
        }

        foreach (var reward in rewards)
        {
            if (!catalog.TryGetDefinition(reward.ItemId, out var rewardDefinition))
            {
                failure = $"reward item {reward.ItemId} is not present in the item catalog";
                return false;
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

            if (isAvatar)
            {
                if (CharacterAvatarInventoryPlanner.TryPlace(
                        plannedAvatar,
                        attachment,
                        catalog,
                        out var avatarPlacement,
                        out _))
                {
                    plannedAvatar = avatarPlacement.Inventory;
                }
                else
                {
                    overflow.AddRange(
                        CharacterInventoryPlanner.CreateMailAttachments(
                            attachment,
                            catalog));
                }

                continue;
            }

            if (rewardDefinition.InventoryCategory == ItemInventoryCategory.Creature)
            {
                var creatureItem = new CharacterItemRecord(
                    ushort.MaxValue,
                    attachment.ItemId,
                    attachment.CountOrValue,
                    attachment.State,
                    attachment.Durability,
                    attachment.SealState,
                    InstanceId: attachment.InstanceId);
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
                    overflow.AddRange(
                        CharacterInventoryPlanner.CreateMailAttachments(
                            attachment,
                            catalog));
                }

                continue;
            }

            if (CharacterInventoryPlanner.TryPlace(
                    plannedMain,
                    attachment,
                    catalog,
                    weightLimit,
                    out var inventoryPlacement,
                    out _))
            {
                plannedMain = inventoryPlacement.Inventory;
            }
            else
            {
                overflow.AddRange(CharacterInventoryPlanner.CreateMailAttachments(
                    attachment,
                    catalog));
            }
        }

        plan = new CeraBoosterOpeningPlan(
            sourceSlot,
            sourceItem.ItemId,
            plannedMain,
            plannedAvatar,
            plannedCreature,
            overflow.AsReadOnly(),
            rewards.Select(reward => new CeraShopContentReward(reward.ItemId, reward.Count,
                reward.AvatarAbilityIndex, reward.AvatarPeriodDays)).ToArray());
        return true;
    }

    private static bool TryCreateChapterTwo(
        IReadOnlyDictionary<ushort, CharacterItemRecord> mainInventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> avatarInventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> creatureInventory,
        CharacterItemRecord sourceItem, ItemDefinition sourceDefinition, ItemCatalog catalog,
        uint weightLimit, IDropRandomSource random, int gold, uint victoryPoints, ushort warehouseCapacity,
        out CeraBoosterOpeningPlan plan, out string failure)
    {
        plan = null!;
        var plannedMain = mainInventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (sourceItem.CountOrValue == 1)
        {
            plannedMain.Remove(sourceItem.Slot);
        }
        else
        {
            plannedMain[sourceItem.Slot] = sourceItem with { CountOrValue = sourceItem.CountOrValue - 1 };
        }

        if (!CeraShopContentPlanner.TryCreatePurchase(plannedMain, avatarInventory, creatureInventory,
            sourceDefinition, 1, catalog, weightLimit, random,
            itemId => CeraShopContentPlanner.ShouldSettleAsEffect(catalog, itemId), out var placement, out failure))
        {
            return false;
        }

        if (!CeraShopContentPlanner.TrySettleImmediateEffects(placement.Rewards, catalog, gold, victoryPoints,
            warehouseCapacity, out var effects))
        {
            failure = "the Cera container effects could not be settled";
            return false;
        }

        plan = new(sourceItem.Slot, sourceItem.ItemId, placement.MainInventory, placement.AvatarInventory,
            placement.CreatureInventory, placement.OverflowItems, placement.Rewards, effects);
        return true;
    }
}
