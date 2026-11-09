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

            AddSortedStacks(arranged, section.Start, sorted.Select(entry => entry.Item), catalog);
        }

        return arranged;
    }

    public static Dictionary<ushort, CharacterItemRecord> SortWarehouse(
        IReadOnlyDictionary<ushort, CharacterItemRecord> warehouse,
        ItemCatalog catalog,
        ushort capacity)
    {
        var sorted = warehouse
            .Where(pair => pair.Key < capacity)
            .Select(pair => (OldSlot: pair.Key, Item: pair.Value))
            .OrderBy(entry => GetCategoryRank(entry.Item.ItemId, catalog))
            .ThenBy(entry => entry.Item.ItemId)
            .ThenBy(entry => entry.OldSlot)
            .ToArray();
        var arranged = new Dictionary<ushort, CharacterItemRecord>(sorted.Length);
        AddSortedStacks(arranged, 0, sorted.Select(entry => entry.Item), catalog);
        return arranged;
    }

    private static void AddSortedStacks(
        Dictionary<ushort, CharacterItemRecord> arranged,
        ushort startSlot,
        IEnumerable<CharacterItemRecord> sorted,
        ItemCatalog catalog)
    {
        var partialStacks = new Dictionary<(ushort ItemId, byte State, ushort Durability, byte SealState), ushort>();
        var nextSlot = (int)startSlot;
        foreach (var item in sorted)
        {
            var stackLimit = catalog.TryGetDefinition(item.ItemId, out var definition)
                && definition.IsStackable
                ? CharacterInventoryPlanner.GetStackLimit(definition)
                : 0;
            var key = (item.ItemId, item.State, item.Durability, item.SealState);
            var remaining = item.CountOrValue;
            if (stackLimit != 0 && partialStacks.TryGetValue(key, out var partialSlot))
            {
                var partial = arranged[partialSlot];
                var added = Math.Min(remaining, stackLimit - partial.CountOrValue);
                arranged[partialSlot] = partial with { CountOrValue = partial.CountOrValue + added };
                remaining -= added;
                if (arranged[partialSlot].CountOrValue == stackLimit)
                {
                    partialStacks.Remove(key);
                }

                if (remaining == 0)
                {
                    continue;
                }
            }

            var targetSlot = checked((ushort)nextSlot++);
            arranged.Add(targetSlot, item with { Slot = targetSlot, CountOrValue = remaining });
            if (remaining < stackLimit)
            {
                partialStacks[key] = targetSlot;
            }
        }
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
