namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    internal async Task<CharacterRecord?> UseChapterTwoGrowthPotionAsync(
        string userName,
        Guid characterId,
        CharacterItemRecord expected,
        ChapterTwoGrowthPotionDefinition definition,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined || IsEconomicWriteBlocked(characterId)
                || expected.ItemId != ChapterTwoGrowthPotionDefinition.ItemId
                || !CharacterInventoryLayout.IsMainItemSlot(expected.Slot)
                || expected.CountOrValue == 0)
                return null;

            var account = _accounts.FirstOrDefault(account =>
                string.Equals(account.UserName, userName, StringComparison.OrdinalIgnoreCase));
            var characterIndex = account?.Characters.FindIndex(character => character.Id == characterId) ?? -1;
            if (characterIndex < 0) return null;

            var character = account!.Characters[characterIndex];
            if (character.ChapterTwoGrowthPotion is { } previous
                && (previous.CooldownUntilUtc > now || previous.EffectUntilUtc > now))
                return null;

            var inventory = character.Inventory?.ToList() ?? [];
            var slotIndex = inventory.FindIndex(item => item.Slot == expected.Slot);
            if (slotIndex < 0 || inventory[slotIndex] != expected) return null;

            if (expected.CountOrValue == 1)
                inventory.RemoveAt(slotIndex);
            else
                inventory[slotIndex] = expected with { CountOrValue = expected.CountOrValue - 1 };

            var updated = character with
            {
                Inventory = inventory,
                ChapterTwoGrowthPotion = new ChapterTwoGrowthPotionState(
                    now.AddMilliseconds(definition.CooldownMilliseconds),
                    now.AddMilliseconds(definition.EffectMilliseconds))
            };
            account.Characters[characterIndex] = updated;
            var committed = false;
            try
            {
                await SaveLockedAsync(cancellationToken, () => committed = true, isolateCommittedDiagnostics: true);
            }
            catch
            {
                if (!committed) account.Characters[characterIndex] = character;
                throw;
            }

            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }
}
