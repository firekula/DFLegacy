using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    internal async Task<bool> CanClaimFreeRevivalCoinsAsync(string accountName, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return FindAccountLocked(accountName) is { } account
                && account.FreeRevivalCoinClaimDay < GetFatigueDayKey(now);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<CharacterRecord?> ClaimFreeRevivalCoinsAsync(string accountName, Guid characterId,
        byte amount, DateTimeOffset now, ItemCatalog catalog, CancellationToken cancellationToken)
    {
        if (amount is 0 or > GameProtocolEngine.MaximumFreeRevivalCoinGrant) return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            var day = GetFatigueDayKey(now);
            if (!TryFindCharacterLocked(accountName, characterId, out var account, out var index, out var character)
                || account.FreeRevivalCoinClaimDay >= day
                || IsEconomicWriteBlocked(characterId))
            {
                return null;
            }

            var inventory = (character.Inventory ?? []).ToDictionary(item => item.Slot);
            var coins = new CharacterMailAttachmentRecord(CharacterInventoryLayout.RevivalCoinItemId, amount);
            if (!CharacterInventoryPlanner.TryPlace(inventory, coins, catalog, uint.MaxValue, out var plan, out _))
            {
                return null;
            }

            var updated = character with { Inventory = plan.Inventory.Values.OrderBy(item => item.Slot).ToList() };
            var characters = account.Characters.ToList();
            characters[index] = updated;
            var accountIndex = _accounts.IndexOf(account);
            _accounts[accountIndex] = account with { Characters = characters, FreeRevivalCoinClaimDay = day };
            var snapshotCommitted = false;
            try
            {
                await SaveLockedAsync(cancellationToken, () => snapshotCommitted = true, isolateCommittedDiagnostics: true);
            }
            catch
            {
                if (!snapshotCommitted) _accounts[accountIndex] = account;
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
