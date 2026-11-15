namespace DFLegacy.Server;

internal enum EntranceReadWakeReason
{
    Packet,
    Deadline,
    PartyMailTick
}

/// <summary>
/// 客户端读等待与跨会话邮件共用的等待器。邮件轮询不能被更长的场景/会员期限覆盖，
/// 也不能要求客户端先发包；tick 不取消仍在进行的那一次网络读取。
/// </summary>
internal static class EntranceSessionReadWait
{
    internal static readonly TimeSpan PartyMailCheckInterval = TimeSpan.FromMilliseconds(250);

    internal static bool ShouldPollPartyMailbox(
        bool isChannelPort,
        bool hasCharacter,
        bool isPartyDungeon,
        bool hasPendingMail,
        int partyMemberCount) =>
        isChannelPort && hasCharacter
        && (isPartyDungeon || hasPendingMail || partyMemberCount > 1);

    internal static async Task<EntranceReadWakeReason> WaitAsync(
        Task pendingPacketRead,
        TimeSpan deadlineWait,
        bool pollPartyMailbox,
        CancellationToken cancellationToken,
        Task? wakeTask = null,
        TimeSpan? additionalWakeDelay = null)
    {
        var additionalFirst = additionalWakeDelay is { } extra
            && (deadlineWait == Timeout.InfiniteTimeSpan || extra <= deadlineWait);
        if (additionalFirst) deadlineWait = additionalWakeDelay!.Value;
        var mailTickFirst = pollPartyMailbox
            && (deadlineWait == Timeout.InfiniteTimeSpan || deadlineWait > PartyMailCheckInterval);
        var delay = mailTickFirst ? PartyMailCheckInterval : deadlineWait;
        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = Task.Delay(delay, delayCancellation.Token);
        try
        {
            if (wakeTask is null)
            {
                await Task.WhenAny(pendingPacketRead, timeout);
            }
            else
            {
                await Task.WhenAny(pendingPacketRead, timeout, wakeTask);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // 同时到达时优先处理包；调用方仍负责唯一一次 await/消费该网络读取。
            return pendingPacketRead.IsCompleted
                ? EntranceReadWakeReason.Packet
                : wakeTask?.IsCompleted == true || mailTickFirst || additionalFirst
                    ? EntranceReadWakeReason.PartyMailTick
                    : EntranceReadWakeReason.Deadline;
        }
        finally
        {
            await delayCancellation.CancelAsync();
        }
    }
}
