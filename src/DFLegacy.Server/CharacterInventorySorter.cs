namespace DFLegacy.Server;

public static class CharacterInventorySorter
{
    private static readonly (ushort Start, ushort End, bool SortEquipment)[] MainSections =
    [
        (CharacterInventoryLayout.EquipmentSlotStart,
            CharacterInventoryLayout.EquipmentSlotEnd,
            true),
        (CharacterInventoryLayout.ConsumableSlotStart,
            CharacterInventoryLayout.ConsumableSlotEnd,
            false),
        (CharacterInventoryLayout.MaterialSlotStart,
            CharacterInventoryLayout.MaterialSlotEnd,
            false),
        (CharacterInventoryLayout.QuestSlotStart,
            CharacterInventoryLayout.QuestSlotEnd,
            false)
    ];

    public static Dictionary<ushort, CharacterItemRecord> SortMain(
        IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        ItemCatalog catalog)
    {
        var arranged = inventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var section in MainSections)
        {
            var items = inventory
                .Where(pair => pair.Key >= section.Start && pair.Key < section.End)
                .Select(pair => (OldSlot: pair.Key, Item: pair.Value));
            var sorted = section.SortEquipment
                ? items.OrderBy(entry => GetRarity(entry.Item.ItemId, catalog))
                    .ThenBy(entry => entry.Item.ItemId)
                    .ThenBy(entry => entry.OldSlot)
                    .ToArray()
                : items.OrderBy(entry => entry.Item.ItemId)
                    .ThenBy(entry => entry.OldSlot)
                    .ToArray();
            for (var slot = (int)section.Start; slot < section.End; slot++)
            {
                arranged.Remove((ushort)slot);
            }

            for (var index = 0; index < sorted.Length; index++)
            {
                var targetSlot = checked((ushort)(section.Start + index));
                arranged[targetSlot] = sorted[index].Item with { Slot = targetSlot };
            }
        }

        return arranged;
    }

    public static Dictionary<ushort, CharacterItemRecord> SortWarehouse(
        IReadOnlyDictionary<ushort, CharacterItemRecord> warehouse,
        ItemCatalog catalog,
        ushort capacity,
        bool mergeStacks = false)
    {
        var sorted = warehouse
            .Where(pair => pair.Key < capacity)
            .Select(pair => (OldSlot: pair.Key, Item: pair.Value))
            .OrderBy(entry => GetCategoryRank(entry.Item.ItemId, catalog))
            .ThenBy(entry => entry.Item.ItemId)
            .ThenBy(entry => entry.OldSlot)
            .ToArray();
        var arranged = new Dictionary<ushort, CharacterItemRecord>(sorted.Length);
        var openStacks = new Dictionary<(ushort ItemId, byte State, ushort Durability, byte Seal), ushort>();
        foreach (var (_, sortedItem) in sorted)
        {
            var item = sortedItem;
            var slot = checked((ushort)arranged.Count);
            if (mergeStacks && catalog.TryGetDefinition(item.ItemId, out var definition) && definition.IsStackable)
            {
                var limit = CharacterInventoryPlanner.GetStackLimit(definition);
                var key = (item.ItemId, item.State, item.Durability, item.SealState);
                if (item.CountOrValue > 0 && item.CountOrValue <= limit && openStacks.TryGetValue(key, out var previousSlot))
                {
                    var previous = arranged[previousSlot];
                    var added = Math.Min(item.CountOrValue, limit - previous.CountOrValue);
                    arranged[previousSlot] = previous with { CountOrValue = previous.CountOrValue + added };
                    if (arranged[previousSlot].CountOrValue == limit) openStacks.Remove(key);
                    item = item with { CountOrValue = item.CountOrValue - added };
                    if (item.CountOrValue == 0) continue;
                }

                if (item.CountOrValue > 0 && item.CountOrValue < limit) openStacks[key] = slot;
            }

            arranged[slot] = item with { Slot = slot };
        }

        return arranged;
    }

    private static int GetRarity(ushort itemId, ItemCatalog catalog) =>
        catalog.TryGetDefinition(itemId, out var definition)
            ? definition.Rarity ?? 9_999
            : 9_999;

    private static int GetCategoryRank(ushort itemId, ItemCatalog catalog) =>
        catalog.TryGetDefinition(itemId, out var definition)
            ? (int)definition.InventoryCategory
            : int.MaxValue;
}
