namespace DFLegacy.Server;

internal sealed record EnchantCommit(
    CharacterRecord Requester,
    CharacterRecord Enchanter,
    bool Succeeded,
    uint OldProficiency,
    IReadOnlyList<(byte ListType, ushort Slot, uint Count)> Materials);

public sealed partial class JsonGameStore
{
    internal async Task<EnchantCommit?> CommitCooperativeEnchantAsync(
        Guid transactionId, string requesterAccount, Guid requesterId, string enchanterAccount, Guid enchanterId,
        byte targetList, CharacterItemRecord target, CharacterItemRecord recipeItem, EnchantRecipe recipe,
        uint fee, ushort chance, uint expectedProficiency, ChapterTwoEnchantCatalog catalog, Func<bool> isCurrent,
        Func<Guid, byte, ushort, bool> isEscrowed, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined || requesterId == enchanterId || !isCurrent()
                || !TryFindCharacterLocked(requesterAccount, requesterId,
                    out var accountA, out var indexA, out var requester)
                || !TryFindCharacterLocked(enchanterAccount, enchanterId,
                    out var accountB, out var indexB, out var enchanter)
                || enchanter.ExpertJob != 1 || enchanter.EnchantingProficiency != expectedProficiency
                || catalog.Level(enchanter.EnchantingProficiency) < recipe.RequiredLevel
                || IsEconomicWriteBlocked(requesterId) || IsEconomicWriteBlocked(enchanterId)
                || fee > requester.Gold || fee > int.MaxValue - enchanter.Gold
                || !HasDungeonGoldCapacity(enchanter, enchanter.Gold + (int)fee))
                return null;

            var requesterInventory = (requester.Inventory ?? []).ToDictionary(item => item.Slot);
            var requesterEquipment = (requester.Equipment ?? []).ToDictionary(item => item.Slot);
            var enchanterInventory = (enchanter.Inventory ?? []).ToDictionary(item => item.Slot);
            var targetInventory = targetList == 0 ? requesterInventory : requesterEquipment;
            if (!targetInventory.TryGetValue(target.Slot, out var actualTarget) || actualTarget != target
                || isEscrowed(requesterId, targetList, target.Slot)
                || !enchanterInventory.TryGetValue(recipeItem.Slot, out var actualRecipe) || actualRecipe != recipeItem
                || isEscrowed(enchanterId, 0, recipeItem.Slot) || recipeItem.CountOrValue == 0
                || recipeItem.ItemId != recipe.RecipeId)
                return null;

            var consumed = new Dictionary<ushort, uint>();
            Remove(recipeItem, 1);
            foreach (var required in recipe.Ingredients)
            {
                var remaining = required.Count;
                foreach (var material in enchanterInventory.Values.Where(item => item.ItemId == required.ItemId
                    && item.Slot > 2 && !isEscrowed(enchanterId, 0, item.Slot)).OrderBy(item => item.Slot).ToArray())
                {
                    var quantity = Math.Min(remaining, material.CountOrValue);
                    Remove(material, quantity);
                    remaining -= quantity;
                    if (remaining == 0) break;
                }
                if (remaining != 0) return null;
            }

            // Native chance is expressed as a percentage. All resources on an accepted attempt
            // are accounted for in one store revision, including a failed roll.
            var succeeded = GameRandomSource.Shared.Next(100) < chance;
            if (succeeded)
                targetInventory[target.Slot] = target with { EnchantmentCardId = recipe.CardId };
            var increase = catalog.Increase(enchanter, chance, succeeded);
            var savedRequester = requester with
            {
                Inventory = requesterInventory.Values.OrderBy(item => item.Slot).ToList(),
                Equipment = requesterEquipment.Values.OrderBy(item => item.Slot).ToList(),
                Gold = requester.Gold - (int)fee,
            };
            var savedEnchanter = enchanter with
            {
                Inventory = enchanterInventory.Values.OrderBy(item => item.Slot).ToList(),
                Gold = enchanter.Gold + (int)fee,
                EnchantingProficiency = (uint)Math.Min(uint.MaxValue, (long)enchanter.EnchantingProficiency + increase),
            };

            if (!isCurrent() || consumed.Count > byte.MaxValue) return null;
            accountA.Characters[indexA] = savedRequester;
            accountB.Characters[indexB] = savedEnchanter;
            try
            {
                await SaveLockedAsync(cancellationToken, isolateCommittedDiagnostics: true);
            }
            catch
            {
                accountA.Characters[indexA] = requester;
                accountB.Characters[indexB] = enchanter;
                throw;
            }

            // The owning sockets must adopt the durable records before any later inventory write.
            _pendingTradeDeliveries[requesterId] = transactionId;
            _pendingTradeDeliveries[enchanterId] = transactionId;
            return new(savedRequester, savedEnchanter, succeeded, enchanter.EnchantingProficiency,
                consumed.Select(pair => ((byte)0, pair.Key, pair.Value)).ToArray());

            void Remove(CharacterItemRecord item, uint quantity)
            {
                var remaining = item.CountOrValue - quantity;
                if (remaining == 0) enchanterInventory.Remove(item.Slot);
                else enchanterInventory[item.Slot] = item with { CountOrValue = remaining };
                consumed[item.Slot] = remaining;
            }
        }
        finally { _gate.Release(); }
    }
}
