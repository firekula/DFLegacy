using DFLegacy.InterProcess;

namespace DFLegacy.Server;

public sealed record ChannelLinkStatus(
    bool Connected,
    DateTimeOffset? LastStatusAt,
    int RemoteSessions,
    long TotalConnections,
    long TotalPackets,
    int ActiveSessions);

/// <summary>
/// 把 game 侧的本地计数与 channel 侧上报的计数合并成 <c>/api/status</c> 的对外口径（I4）。
/// </summary>
/// <remarks>
/// 链路断开时<b>只报本地值</b>（不发明数字）—— 这是 R1 的处置：让「谁挂了」能被看见。
/// 合并口径以收包为准：<see cref="TotalPackets"/> = 本地收包 + 对端收包，不是本地收+发。
/// </remarks>
public static class StatusProjection
{
    public static ChannelLinkStatus ChannelLink(
        bool connected,
        DateTimeOffset? lastStatusAt,
        StatusPayload? status,
        long localConnections,
        long localPackets,
        int localSessions)
    {
        var remoteActive = connected && status is not null ? (int)status.ActiveSessions : 0;
        var remoteConnections = connected && status is not null ? (long)status.TotalConnections : 0;
        var remotePackets = connected && status is not null ? (long)status.TotalPackets : 0;
        return new ChannelLinkStatus(
            connected,
            lastStatusAt,
            remoteActive,
            localConnections + remoteConnections,
            localPackets + remotePackets,
            localSessions + remoteActive);
    }
}
