using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed record RandomBoxOpeningPlan(Dictionary<ushort, CharacterItemRecord> Inventory,
    IReadOnlyList<GameRandomBoxReward> Rewards);

public static class RandomBoxOpeningPlanner
{
    public static bool TryCreate(IReadOnlyDictionary<ushort, CharacterItemRecord> inventory,
        ushort boxSlot, ushort hammerSlot, int characterLevel, ItemCatalog catalog, uint weightLimit,
        IDropRandomSource random, out RandomBoxOpeningPlan plan, out byte error)
    {
        plan = null!;
        error = 17;
        if (boxSlot == hammerSlot || !CharacterInventoryLayout.IsMainItemSlot(boxSlot)
            || !CharacterInventoryLayout.IsMainItemSlot(hammerSlot)
            || !inventory.TryGetValue(boxSlot, out var box) || box.Slot != boxSlot || box.ItemId != 200
            || !inventory.TryGetValue(hammerSlot, out var hammer) || hammer.Slot != hammerSlot || hammer.ItemId != 201
            || !catalog.TryGetDefinition(box.ItemId, out var definition) || definition.RandomBox is not { } randomBox
            || randomBox.RequiredItems.Count != 2 || !randomBox.RequiredItems.TryGetValue(200, out var boxes)
            || !randomBox.RequiredItems.TryGetValue(201, out var hammers)
            || box.CountOrValue < boxes || hammer.CountOrValue < hammers)
            return false;
        if (characterLevel < definition.MinimumLevel.GetValueOrDefault(1))
        {
            error = 14;
            return false;
        }

        var planned = inventory.ToDictionary(pair => pair.Key, pair => pair.Value);
        Consume(box, boxes);
        Consume(hammer, hammers);
        var results = new List<GameRandomBoxReward>();
        foreach (var group in randomBox.Groups)
        {
            var reward = group.Select(random);
            if (!catalog.TryGetDefinition(reward.ItemId, out var rewardDefinition)) return false;
            var attachment = CharacterItemIdentity.Ensure(new CharacterMailAttachmentRecord(reward.ItemId, reward.Count,
                Durability: rewardDefinition.IsEquipment ? catalog.GetInitialDurability(reward.ItemId) : (ushort)0,
                SealState: rewardDefinition.AttachType == ItemAttachType.Sealing ? (byte)1 : (byte)0), rewardDefinition);
            if (!CharacterInventoryPlanner.TryPlace(planned, attachment, catalog, weightLimit,
                    out var placement, out var failure))
            {
                error = failure == InventoryPlacementFailure.Overweight ? (byte)21 : (byte)4;
                return false;
            }
            foreach (var item in placement.Inventory.Values.Where(item => item.ItemId == reward.ItemId))
            {
                var previous = planned.GetValueOrDefault(item.Slot);
                var previousCount = previous?.ItemId == item.ItemId ? previous.CountOrValue : 0;
                if (item.CountOrValue > previousCount)
                    results.Add(new(item.Slot, item.ItemId, item.CountOrValue - previousCount,
                        item.Durability, reward.Attribute));
            }
            planned = placement.Inventory;
        }
        plan = new(planned, results.AsReadOnly());
        error = 0;
        return true;

        void Consume(CharacterItemRecord item, uint count)
        {
            if (item.CountOrValue == count) planned.Remove(item.Slot);
            else planned[item.Slot] = item with { CountOrValue = item.CountOrValue - count };
        }
    }
}
