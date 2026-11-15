namespace DFLegacy.Server;

/// <summary>官服只读骰子容量预筛，使用 ACT1 栏位；不做负重/等级/max-having/拆堆。</summary>
public static class NativeLootInventory
{
    public static bool CanInsert(IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        DungeonGroundItem ground, ItemDefinition definition)
    {
        if (!CharacterInventoryLayout.TryGetCategoryRange(definition.InventoryCategory, out var start, out var end))
            return false;
        return CanInsertRanges(inventory, ground, definition,
            CharacterInventoryLayout.QuickSlotStart, CharacterInventoryLayout.QuickSlotEnd, start, end);
    }

    internal static bool CanReceive(CharacterRecord character, DungeonGroundItem ground, ItemDefinition definition)
    {
        if (definition.InventoryCategory == ItemInventoryCategory.Avatar)
            return Enumerable.Range(0, CharacterAvatarInventoryLayout.Capacity)
                .Any(slot => !(character.AvatarInventory ?? []).Any(item => item.Slot == slot && item.ItemId != 0));
        if (definition.InventoryCategory == ItemInventoryCategory.Creature)
            return CharacterCreatureInventoryLayout.TryGetBagRange(definition, out var start, out var end)
                && CanInsertRanges((character.CreatureInventory ?? []).ToDictionary(item => item.Slot),
                    ground, definition, start, end, start, end);
        return CanInsert((character.Inventory ?? []).ToDictionary(item => item.Slot), ground, definition);
    }

    private static bool CanInsertRanges(IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        DungeonGroundItem ground, ItemDefinition definition, int quickStart, int quickEnd, int mainStart, int mainEnd)
    {
        var count = (long)ground.CountOrValue;
        var limit = definition.StackLimit ?? 9_999_999;
        bool Fits(long value) => !definition.IsStackable || value is >= 0 and <= int.MaxValue && value <= limit;
        int TryStack(int start, int end)
        {
            if (definition.IsEquipment) return -1;
            for (var slot = start; slot < end; slot++)
            {
                if (!inventory.TryGetValue((ushort)slot, out var existing) || existing.ItemId != ground.ItemId) continue;
                if (existing.CountOrValue >= int.MaxValue - count) return -2;
                return Fits(existing.CountOrValue + count) ? slot : -10;
            }
            return -4;
        }
        int TryEmpty(int start, int end)
        {
            if (!Fits(count)) return -10;
            for (var slot = start; slot < end; slot++)
                if (!inventory.TryGetValue((ushort)slot, out var item) || item.ItemId == 0) return slot;
            return -2;
        }
        foreach (var (start, end) in new[] { (quickStart, quickEnd), (mainStart, mainEnd) })
        {
            var stacked = TryStack(start, end);
            if (stacked >= 0) return true;
            if (stacked == -10) return false;
        }
        var ranges = definition.IsStackable && definition.InventoryCategory == ItemInventoryCategory.Consumable
            ? new[] { (quickStart, quickEnd), (mainStart, mainEnd) }
            : new[] { (mainStart, mainEnd), (quickStart, quickEnd) };
        foreach (var (start, end) in ranges)
            if (TryEmpty(start, end) >= 0) return true;
        return false;
    }
}
