using DFLegacy.InterProcess;

namespace DFLegacy.Server;

/// <summary>
/// game 侧的链路客户端托管（I4）。仅在拆分模式（<c>Processes.Channel.Enabled=true</c>）下拨号 channel 进程；
/// 单体模式直接返回，<see cref="Client"/> 留 null，<c>/api/status.channelLink.connected</c> 为 false。
/// </summary>
/// <remarks>
/// 生命周期靠 <see cref="BackgroundService"/>'s <c>stoppingToken</c>：宿主停止时取消 →
/// <see cref="InterProcessLinkClient.RunAsync"/> 退出 → 本方法返回。客户端的 CTS 由 RunAsync 的
/// <c>using</c> 持有（Task 6 的设计），所以这里不需要额外 Dispose。
/// </remarks>
public sealed class ChannelLinkService(
    ServerOptions options,
    RuntimeState runtime,
    ILogger<ChannelLinkService> logger) : BackgroundService
{
    /// <summary>当前客户端实例（供 /api/status 读 <see cref="InterProcessLinkClient.Connected"/>/
    /// <see cref="InterProcessLinkClient.LastStatusAt"/>/<see cref="InterProcessLinkClient.LastRemoteStatus"/>）。
    /// 拆分模式拨号后非 null；单体模式 null。</summary>
    public InterProcessLinkClient? Client { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Processes.Channel.Enabled)
        {
            logger.LogInformation("Single-process mode: the channel link client is not started.");
            return;
        }

        var register = new RegisterPayload(
            ProtocolVersion: InterProcessMessageIds.LinkProtocolVersion,
            GcNo: (uint)(options.Channel.ServerNumber * 1000 + options.Channel.ChannelNumber),
            ChannelNo: (uint)options.Channel.ChannelNumber,
            MaximumUsers: (uint)options.Channel.MaximumUsers,
            CurrentUsers: (uint)options.Channel.CurrentUsers,
            AdvertisedHost: options.GameplayDatagram.ResolveAdvertisedHost(),
            GamePort: (uint)options.Channel.Port);

        var client = new InterProcessLinkClient(new InterProcessLinkClientOptions(
            options.Entrance.Host,
            options.Processes.Channel.LinkPort,
            TimeSpan.FromSeconds(1),
            MissedStatusLimit: 3,
            register,
            () => new StatusPayload(
                UptimeMs: (ulong)(DateTimeOffset.UtcNow - runtime.StartedAt).TotalMilliseconds,
                ActiveSessions: (uint)runtime.Sessions().Length,
                // game 进程不在 RuntimeState 里记「已登录角色数」；这条暂报 0，留给后续波。
                LoggedInUsers: 0,
                TotalConnections: (ulong)runtime.TotalConnections,
                // 收包口径：server 的 TotalPackets 就是收包数（RuntimeState.cs:71-75），不是收+发。
                TotalPackets: (ulong)runtime.TotalPackets)))
        {
            PeerUp = () => logger.LogInformation("Inter-process link to the channel process is up"),
            PeerDown = reason => logger.LogWarning("Inter-process link to the channel process is down: {Reason}", reason),
        };

        Client = client;
        await client.RunAsync(stoppingToken);
    }
}
