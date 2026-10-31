using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed record AvatarLifetimeSettlementResult(
    DungeonRewardLedgerStatus Status, CharacterRecord? Character = null, bool Changed = false);

public sealed partial class JsonGameStore
{
    private long AvatarNow => (avatarTimeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds();

    private CharacterRecord ReconcileAvatarLifetimes(CharacterRecord candidate, CharacterRecord? previous = null) =>
        itemCatalog is null ? candidate : AvatarLifetimePolicy.NormalizeCharacter(candidate, previous, itemCatalog, AvatarNow);

    public async Task<AvatarLifetimeSettlementResult> SettleAvatarExpirationsAsync(
        string userName, Guid characterId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown);
            var owner = _accounts.FirstOrDefault(a => string.Equals(a.UserName, userName, StringComparison.OrdinalIgnoreCase));
            var current = owner?.Characters.FirstOrDefault(c => c.Id == characterId);
            if (current is null || itemCatalog is null) return new(DungeonRewardLedgerStatus.Rejected);
            var updated = ReconcileAvatarLifetimes(current, current);
            if (updated == current) return new(DungeonRewardLedgerStatus.Committed, current);
            var accounts = CopyStoreAccountsLocked();
            SetStoreCharacter(accounts, updated);
            if (!await SaveAvatarAccountsLockedAsync(accounts, cancellationToken)) return new(DungeonRewardLedgerStatus.Unknown);
            return new(DungeonRewardLedgerStatus.Committed, updated, true);
        }
        finally { _gate.Release(); }
    }

    private async Task SettleAvatarLifetimesAtStartupLockedAsync(CancellationToken cancellationToken)
    {
        if (_rewardQuarantined || itemCatalog is null) return;
        var accounts = CopyStoreAccountsLocked();
        var changed = false;
        foreach (var account in accounts)
        for (var i = 0; i < account.Characters.Count; i++)
        {
            var old = account.Characters[i];
            var updated = ReconcileAvatarLifetimes(old, old);
            if (updated == old) continue;
            account.Characters[i] = updated;
            changed = true;
        }
        if (changed) await SaveAvatarAccountsLockedAsync(accounts, cancellationToken);
    }

    private async Task<bool> SaveAvatarAccountsLockedAsync(List<AccountRecord> accounts, CancellationToken cancellationToken)
    {
        try { await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, cancellationToken); }
        catch (Exception exception)
        {
            _rewardQuarantined = true;
            logger.LogError(exception, "Avatar-lifetime snapshot outcome uncertain; store quarantined.");
            return false;
        }
        _accounts = accounts;
        return true;
    }
}
