namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    internal async Task<CharacterRecord?> MoveToEmptyInventorySlotAsync(string accountName, Guid characterId,
        InventoryAddress source, InventoryAddress destination, CharacterItemRecord expectedItem,
        CancellationToken cancellationToken = default)
    {
        if (!source.IsValid || !destination.IsValid || source.Section != destination.Section || source == destination)
            return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            if (IsEconomicWriteBlocked(characterId) || !TryFindCharacterLocked(accountName, characterId,
                    out var account, out var index, out var character))
                return null;

            var isAvatar = source.Section == InventorySection.Avatar;
            var legacy = isAvatar ? character.AvatarInventory : character.Inventory;
            var overflow = character.ExtendedInventory;
            var fromLegacy = source.TryGetLegacySlot(out var sourceSlot);
            var toLegacy = destination.TryGetLegacySlot(out var targetSlot);
            var item = fromLegacy
                ? legacy?.SingleOrDefault(entry => entry.Slot == sourceSlot)
                : overflow?.SingleOrDefault(entry => entry.Address == source)?.Item;
            if (item is null || item != expectedItem) return null;
            var occupied = toLegacy
                ? legacy?.Any(entry => entry.Slot == targetSlot)
                : overflow?.Any(entry => entry.Address == destination);
            if (occupied == true) return null;

            var updated = character;
            if (fromLegacy || toLegacy)
            {
                var entries = (legacy ?? []).Where(entry => !fromLegacy || entry.Slot != sourceSlot).ToList();
                if (toLegacy) entries.Add(item with { Slot = targetSlot });
                entries.Sort((left, right) => left.Slot.CompareTo(right.Slot));
                updated = isAvatar ? updated with { AvatarInventory = entries } : updated with { Inventory = entries };
            }
            if (!fromLegacy || !toLegacy)
            {
                var entries = (overflow ?? []).Where(entry => entry.Address != source).ToList();
                if (!toLegacy) entries.Add(new ExtendedInventoryItem(destination, item with { Slot = 0 }));
                updated = updated with { ExtendedInventory = entries.Count > 0 ? entries : null };
            }
            var committed = false;
            account.Characters[index] = updated;
            try
            {
                await SaveLockedAsync(cancellationToken, () => committed = true, isolateCommittedDiagnostics: true);
            }
            catch
            {
                if (!committed) account.Characters[index] = character;
                throw;
            }
            return account.Characters[index];
        }
        finally { _gate.Release(); }
    }
}
