using DFLegacy.InterProcess;
using DFLegacy.Server;

/// <summary>
/// Task 8（I4）的纯投影断言：链路断开时不发明数字、链路连通时合并双侧计数（收包口径）。
/// </summary>
internal static class StatusProjectionSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        // 链路断开：投影必须等于本地值，不能有凭空编出来的 channel 侧数字。
        var down = StatusProjection.ChannelLink(
            connected: false,
            lastStatusAt: null,
            status: null,
            localConnections: 3,
            localPackets: 7,
            localSessions: 1);
        check(down is { Connected: false, RemoteSessions: 0, LastStatusAt: null }
                && down.TotalConnections == 3
                && down.TotalPackets == 7
                && down.ActiveSessions == 1,
            "with the link down the projection equals the local counters (no invented channel-side values)");

        // 链路连通：channel 侧上报的计数加回 admin 总计；LastStatusAt 透传。
        var status = new StatusPayload(UptimeMs: 10, ActiveSessions: 4, LoggedInUsers: 2, TotalConnections: 100, TotalPackets: 200);
        var up = StatusProjection.ChannelLink(
            connected: true,
            lastStatusAt: DateTimeOffset.UnixEpoch,
            status: status,
            localConnections: 3,
            localPackets: 7,
            localSessions: 1);
        check(up is { Connected: true, RemoteSessions: 4, LastStatusAt: not null }
                && up.TotalConnections == 103
                && up.TotalPackets == 207
                && up.ActiveSessions == 5,
            "with the link up the channel-side counters are added back into the admin totals");

        // connected=true 但对端还没发过 STATUS：不能把 null 里的数字当 0 之外的东西。
        var pending = StatusProjection.ChannelLink(
            connected: true,
            lastStatusAt: null,
            status: null,
            localConnections: 3,
            localPackets: 7,
            localSessions: 1);
        check(pending is { Connected: true, RemoteSessions: 0 }
                && pending.TotalConnections == 3
                && pending.TotalPackets == 7,
            "connected but no STATUS yet: projection stays at local values until the first remote report arrives");
    }
}
