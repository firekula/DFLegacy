namespace DFLegacy.Server;

public sealed record ChapterTwoQuickStorePlan(
    Dictionary<ushort, CharacterItemRecord> Inventory,
    Dictionary<ushort, CharacterItemRecord> Warehouse,
    ushort DestinationSlot);

public static class ChapterTwoQuickStorePlanner
{
    // Reserved only for the Chapter Two shortcut. Native signed -2 is encoded
    // as FFFE; ordinary manual placement and signed -1 keep their old behavior.
    public const ushort DestinationMarker = 0xfffe;

    public static bool TryPlan(
        IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> warehouse,
        ushort sourceSlot, ushort itemId, uint count, ushort capacity,
        ItemCatalog catalog, Func<byte, CharacterItemRecord, bool> isReserved,
        out ChapterTwoQuickStorePlan plan, out string failure) =>
        TryPlanCore(inventory, warehouse, sourceSlot, itemId, count, capacity,
            catalog, isReserved, false, uint.MaxValue, out plan, out failure);

    public static bool TryPlanWithdrawal(
        IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> warehouse,
        ushort sourceSlot, ushort itemId, uint count, ushort capacity,
        ItemCatalog catalog, Func<byte, CharacterItemRecord, bool> isReserved,
        uint weightLimit, out ChapterTwoQuickStorePlan plan, out string failure) =>
        TryPlanCore(inventory, warehouse, sourceSlot, itemId, count, capacity,
            catalog, isReserved, true, weightLimit, out plan, out failure);

    private static bool TryPlanCore(
        IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        IReadOnlyDictionary<ushort, CharacterItemRecord> warehouse,
        ushort sourceSlot, ushort itemId, uint count, ushort capacity,
        ItemCatalog catalog, Func<byte, CharacterItemRecord, bool> isReserved,
        bool withdraw, uint weightLimit, out ChapterTwoQuickStorePlan plan, out string failure)
    {
        plan = null!;
        failure = "invalid, changed or unavailable source";
        capacity = Math.Min(capacity, CharacterWarehouseProgression.MaximumCapacity);
        var sources = withdraw ? warehouse : inventory;
        var targets = withdraw ? inventory : warehouse;
        if ((withdraw ? sourceSlot >= capacity : !CharacterInventoryLayout.IsMainItemSlot(sourceSlot))
            || !sources.TryGetValue(sourceSlot, out var source)
            || source.ItemId != itemId || count == 0 || count > source.CountOrValue
            || !catalog.TryGetDefinition(itemId, out var definition)
            || (!withdraw && !CharacterInventoryLayout.IsCompatibleSlot(definition, sourceSlot))
            || definition.InventoryCategory is not (ItemInventoryCategory.Equipment
                or ItemInventoryCategory.Consumable or ItemInventoryCategory.Material)
            || isReserved(withdraw ? (byte)2 : (byte)0, source)
            || (!definition.IsStackable && (count != 1 || source.CountOrValue != 1)))
            return false;

        ushort start = 0, end = capacity;
        if (withdraw && !CharacterInventoryLayout.TryGetCategoryRange(definition.InventoryCategory, out start, out end))
            return false;
        var limit = definition.IsStackable ? CharacterInventoryPlanner.GetStackLimit(definition) : 1;
        if (count > limit) { failure = "quantity exceeds one stack"; return false; }
        ushort destination = ushort.MaxValue;
        if (definition.IsStackable)
        {
            foreach (var existing in targets.Values.OrderBy(x => x.Slot))
            {
                if (existing.Slot < start || existing.Slot >= end || isReserved(withdraw ? (byte)0 : (byte)2, existing)
                    || existing.CountOrValue > limit - count
                    || existing with { Slot = source.Slot, CountOrValue = source.CountOrValue } != source)
                    continue;
                destination = existing.Slot;
                break;
            }
        }
        if (destination == ushort.MaxValue)
            for (ushort slot = start; slot < end; slot++)
                if (!targets.ContainsKey(slot)) { destination = slot; break; }
        if (destination == ushort.MaxValue) { failure = "no slot can hold the complete quantity"; return false; }

        var bag = inventory.ToDictionary(x => x.Key, x => x.Value);
        var cargo = warehouse.ToDictionary(x => x.Key, x => x.Value);
        var plannedSource = withdraw ? cargo : bag;
        var plannedTarget = withdraw ? bag : cargo;
        if (count == source.CountOrValue) plannedSource.Remove(sourceSlot);
        else plannedSource[sourceSlot] = source with { CountOrValue = source.CountOrValue - count };
        plannedTarget[destination] = plannedTarget.TryGetValue(destination, out var stack)
            ? stack with { CountOrValue = stack.CountOrValue + count }
            : CharacterItemIdentity.MoveToSlot(source with { CountOrValue = count }, destination, catalog);
        if (withdraw && CharacterInventoryPlanner.CalculateWeight(bag.Values, catalog) > weightLimit)
        {
            failure = "inventory weight limit exceeded";
            return false;
        }
        plan = new(bag, cargo, destination);
        failure = "";
        return true;
    }
}
