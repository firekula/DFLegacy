using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed class GiftBoosterOpeningSession(ItemCatalog catalog)
{
    private readonly HashSet<(Guid Character, ushort Sequence)> committed = [];
    private readonly Queue<(Guid Character, ushort Sequence)> order = [];

    internal async Task<(CharacterRecord? Character, GameServerPacket Reply)> OpenAsync(
        JsonGameStore store, string account, CharacterRecord expected, GameGiftBoosterCommand command,
        uint weightLimit, IDropRandomSource random, CancellationToken cancellationToken)
    {
        var key = (expected.Id, command.Sequence);
        var slot = command.SourceSlot;
        var main = (expected.Inventory ?? []).ToDictionary(item => item.Slot);
        if (committed.Contains(key) || !CharacterInventoryLayout.IsMainItemSlot(slot)
            || !main.TryGetValue(slot, out var source) || source.CountOrValue == 0
            || !catalog.TryGetDefinition(source.ItemId, out var definition) || definition.GiftBooster is not { } booster
            || expected.Level < definition.MinimumLevel.GetValueOrDefault(1)
            || definition.MaximumLevel is > 0 && expected.Level > definition.MaximumLevel
            || definition.UsableJobMask != 0 && !definition.IsUsableByJob(expected.Job)
            || booster.Groups.Sum(group => (long)group.DrawCount) > ushort.MaxValue)
            return Failure(17);

        var remaining = source.CountOrValue - 1;
        if (remaining == 0) main.Remove(slot);
        else main[slot] = source with { CountOrValue = remaining };

        var rewards = booster.SelectAll(random)
            .Select(reward => new CeraShopContentReward(reward.ItemId, reward.Count,
                reward.AvatarAbilityIndex, reward.AvatarPeriodDays)).ToArray();
        if (!CeraShopContentPlanner.TryPlace(main,
            (expected.AvatarInventory ?? []).ToDictionary(item => item.Slot),
            (expected.CreatureInventory ?? []).ToDictionary(item => item.Slot),
            rewards, catalog, weightLimit, null, out var placement, out _))
            return Failure(17);
        if (placement.OverflowItems.Count != 0) return Failure(4);

        CharacterRecord? saved;
        try
        {
            // Reuse the item-space transaction with unchanged currencies and no premium grants.
            saved = await store.SaveCharacterCeraShopPurchaseAsync(account, expected.Id, null, expected.Gold,
                placement.MainInventory.Values, expected.Equipment ?? [], cancellationToken,
                avatarInventory: placement.AvatarInventory.Values, creatureInventory: placement.CreatureInventory.Values,
                expectedCharacter: expected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(17);
        }
        if (saved is null) return Failure(17);

        // The rolling command sequence is scoped to this connection.
        if (order.Count == 256) committed.Remove(order.Dequeue());
        committed.Add(key);
        order.Enqueue(key);
        return (saved, GameProtocolEngine.CreateGiftBoosterReply(slot, remaining,
            rewards.Select(reward => (reward.ItemId, reward.Count)).ToArray()));
    }

    private static (CharacterRecord?, GameServerPacket) Failure(byte error) =>
        (null, GameProtocolEngine.CreateGiftBoosterError(error));
}
