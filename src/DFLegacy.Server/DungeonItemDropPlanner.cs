namespace DFLegacy.Server;

public enum DungeonItemDropFailure
{
    None,
    UnsupportedItemSpace,
    InvalidSlot,
    ItemNotFound,
    InvalidCount,
    InvalidItem,
    NotTradeable,
    RarityRestricted,
    GoldLimit
}

public sealed record DungeonItemDropPlan(
    byte ItemSpace,
    ushort SourceSlot,
    uint Count,
    CharacterItemRecord DroppedItem,
    Dictionary<ushort, CharacterItemRecord> Inventory,
    uint Gold = 0,
    bool IsGold = false);

public static class DungeonItemDropPlanner
{
    public static byte GetErrorCode(DungeonItemDropFailure failure) => failure switch
    {
        DungeonItemDropFailure.RarityRestricted => 13,
        DungeonItemDropFailure.GoldLimit => 70,
        DungeonItemDropFailure.NotTradeable or DungeonItemDropFailure.InvalidItem
            or DungeonItemDropFailure.UnsupportedItemSpace => 23,
        _ => 17
    };

    public static bool TryPlan(
        IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        byte itemSpace,
        ushort sourceSlot,
        uint count,
        ItemCatalog catalog,
        out DungeonItemDropPlan plan,
        out DungeonItemDropFailure failure,
        uint currentGold = 0)
    {
        plan = null!;
        failure = DungeonItemDropFailure.None;

        if (itemSpace is not (0 or 7))
        {
            failure = DungeonItemDropFailure.UnsupportedItemSpace;
            return false;
        }

        if (itemSpace == 0 && sourceSlot == CharacterInventoryLayout.GoldSlot)
        {
            if (count > 1000)
            {
                failure = DungeonItemDropFailure.GoldLimit;
                return false;
            }
            if (count == 0 || count > currentGold)
            {
                failure = DungeonItemDropFailure.InvalidCount;
                return false;
            }
            plan = new DungeonItemDropPlan(itemSpace, sourceSlot, count,
                new CharacterItemRecord(ushort.MaxValue, 0, count),
                inventory.ToDictionary(pair => pair.Key, pair => pair.Value),
                currentGold - count, IsGold: true);
            return true;
        }

        var validSlot = itemSpace switch
        {
            0 => CharacterInventoryLayout.IsMainItemSlot(sourceSlot),
            // Space 7 is the Creature bag. Equipped Creature/Artifact slots
            // are exposed through space 3 and must not enter CMD 50.
            7 => sourceSlot < CharacterCreatureInventoryLayout.EquippedCreatureSlot,
            _ => false
        };
        if (!validSlot)
        {
            failure = DungeonItemDropFailure.InvalidSlot;
            return false;
        }

        if (!inventory.TryGetValue(sourceSlot, out var sourceItem))
        {
            failure = DungeonItemDropFailure.ItemNotFound;
            return false;
        }

        if (!catalog.TryGetDefinition(sourceItem.ItemId, out var definition))
        {
            failure = DungeonItemDropFailure.InvalidItem;
            return false;
        }

        // CParty::drop_item@085A6986: this is a discard policy, distinct
        // from trade/mail. [sealing trade] passes, [sealing] requires byte1.
        if (!(definition.AttachType is ItemAttachType.Free or ItemAttachType.SealingTrade
            || definition.AttachType == ItemAttachType.Sealing && sourceItem.SealState == 1))
        {
            failure = DungeonItemDropFailure.NotTradeable;
            return false;
        }

        if (definition.Rarity > 2)
        {
            failure = DungeonItemDropFailure.RarityRestricted;
            return false;
        }

        if (definition.IsTitle || definition.InventoryCategory == ItemInventoryCategory.Avatar)
        {
            failure = DungeonItemDropFailure.InvalidItem;
            return false;
        }

        if (itemSpace == 0
                && (!CharacterInventoryLayout.IsCompatibleSlot(definition, sourceSlot)
                    || definition.InventoryCategory is
                        ItemInventoryCategory.Avatar or ItemInventoryCategory.Creature)
            || itemSpace == 7
                && (definition.InventoryCategory != ItemInventoryCategory.Creature
                    || !CharacterCreatureInventoryLayout.IsCompatibleSlot(
                        definition,
                        sourceSlot)))
        {
            failure = DungeonItemDropFailure.InvalidSlot;
            return false;
        }

        if (count == 0
            || count > sourceItem.CountOrValue
            || !definition.IsStackable && count != 1)
        {
            failure = DungeonItemDropFailure.InvalidCount;
            return false;
        }

        var plannedInventory = inventory.ToDictionary(
            pair => pair.Key,
            pair => pair.Value);
        if (definition.IsStackable && count < sourceItem.CountOrValue)
        {
            plannedInventory[sourceSlot] = sourceItem with
            {
                CountOrValue = sourceItem.CountOrValue - count
            };
        }
        else
        {
            plannedInventory.Remove(sourceSlot);
        }

        plan = new DungeonItemDropPlan(
            itemSpace,
            sourceSlot,
            count,
            sourceItem with
            {
                Slot = ushort.MaxValue,
                CountOrValue = count
            },
            plannedInventory,
            currentGold);
        return true;
    }
}
