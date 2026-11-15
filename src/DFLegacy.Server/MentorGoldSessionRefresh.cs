using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal static class MentorGoldSessionRefresh
{
    internal static Func<MentorGoldRetirementReason> CaptureRetirement(CharacterSessionLease? lease) =>
        () => lease?.WasReplaced == true ? MentorGoldRetirementReason.ForcedReplacement : MentorGoldRetirementReason.NormalLeave;
    // The owner actor calls this at its safe point. Competing currency callbacks use the same gate.
    internal static async Task ApplyNoticeAsync(MentorService service, JsonGameStore store, Guid master, Guid incarnation,
        SemaphoreSlim currencyGate, Func<Task> installPendingTrade, Action<CharacterRecord> installGold,
        Func<GameServerPacket, ValueTask> send, Func<GameServerPacket> absolute, CancellationToken ct)
    {
        if (!service.TryTakeGoldNotice(master, incarnation)) return;
        if (!await ApplyAsync(store, master, incarnation, currencyGate, installPendingTrade, installGold, send, absolute, ct))
            service.DeferGoldNotice(master, incarnation);
    }

    internal static async Task<bool> ApplyAsync(JsonGameStore store, Guid master, Guid incarnation,
        SemaphoreSlim currencyGate, Func<Task> installPendingTrade, Action<CharacterRecord> installGold,
        Func<GameServerPacket, ValueTask> send, Func<GameServerPacket> absolute, CancellationToken ct)
    {
        await currencyGate.WaitAsync(ct);
        try
        {
            await installPendingTrade();
            var result = await store.ConsumeMentorGoldCreditsAsync(master, incarnation, ct);
            if (result.Status == DungeonRewardLedgerStatus.Unknown) throw new IOException("Mentor gold consumption outcome uncertain.");
            if (result.Status == DungeonRewardLedgerStatus.Rejected) return false;
            if (result.Character is null) return true;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding(936);
            // NOTI76 increments native client Money. Its following absolute refresh must include the award.
            foreach (var award in result.Awards ?? [])
                if (award.CreditedGold > 0)
                    await send(GameProtocolEngine.CreateMentorGoldAward(award.CreditedGold, encoding.GetBytes(award.SourceName)));
            installGold(result.Character);
            await send(absolute());
            return true;
        }
        finally { currencyGate.Release(); }
    }
}
