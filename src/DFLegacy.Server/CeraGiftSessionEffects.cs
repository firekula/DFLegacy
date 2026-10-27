namespace DFLegacy.Server;

// Notifications are invalidations. Their legacy absolute value may arrive after
// another wallet transaction; consumers must reload the canonical account.
public sealed class CeraCashInvalidation
{
    private int _pending;
    public void Invalidate(uint legacyValue) => Interlocked.Exchange(ref _pending, 1);
    public async Task<int?> ConsumeAsync(JsonGameStore store, string accountName, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _pending, 0) == 0) return null;
        return await store.GetAccountCeraAsync(accountName, cancellationToken);
    }
}

public static class CeraGiftSessionEffects
{
    public static async Task NotifyCommittedAsync(CeraGiftResult result, string senderAccount,
        JsonGameStore store, CharacterSessionRegistry sessions, CancellationToken cancellationToken = default)
    {
        if (result.Status != DungeonRewardLedgerStatus.Committed) return;
        foreach (var character in await store.GetCharactersAsync(senderAccount, cancellationToken))
            sessions.NotifyCera(character.Id, (uint)result.SenderCash);
        sessions.NotifyMailReceived(result.RecipientId);
    }
}
