using System.Net;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 单体模式下由本进程托管入口握手族（TCP 2311 与 AdditionalPorts 去掉 Channel.Port 后的项，
/// 即装机配置里的 8080）；拆分模式（<c>Processes.Channel.Enabled=true</c>）不再绑这些端口，
/// 由 DFLegacy.Channel 进程接管。两种模式共用同一个 <see cref="EntranceHandshakeServer"/> 实现。
/// 频道玩法族（Channel.Port=7001）仍由 <see cref="EntranceService"/> 绑定。
/// </summary>
public sealed class EntranceHandshakeService(
    ServerOptions options,
    RuntimeState runtime,
    ScriptFileSystem scripts,
    ILogger<EntranceHandshakeService> logger,
    ListenerReadiness? readiness = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 拆分模式的判据跟着 ProcessOptions.Validate 走：Channel.Enabled=true ⇒ 入口端口归
        // channel 进程。用 Channel.Enabled 而不是 Game.EntranceBinding，是为了在「两个开关都为
        // false」这种既有配置下保持今天的行为（入口端口仍由本进程绑），不在 I1 里引入新语义。
        var (entrancePorts, _) = EntrancePortFamilies.Split(
            options.Entrance.Port,
            options.Entrance.AdditionalPorts,
            PvpChannelConfiguration.Ports(options));
        if (options.Processes.Channel.Enabled)
        {
            // 设计稿 §8 R2：启动日志必须说清「绑了什么、忽略了什么」，否则配置漂移只能靠猜。
            logger.LogWarning(
                "Entrance handshake ports {Ports} are owned by the DFLegacy.Channel process; this process will not bind them.",
                string.Join(", ", entrancePorts));
            return;
        }

        if (!options.Entrance.Enabled)
        {
            logger.LogWarning("Entrance handshake listener is disabled.");
            return;
        }

        if (entrancePorts.Length == 0)
        {
            logger.LogWarning(
                "No entrance handshake ports to bind: Entrance.Port/AdditionalPorts only contained Channel.Port {ChannelPort}.",
                options.Channel.Port);
            return;
        }

        var address = IPAddress.Parse(options.Entrance.Host);
        // 频道信息里「客户端该拨哪个地址」沿用既有口径（EntranceDialHost.Resolve，与
        // EntranceService.ResolveChannelDialHost 同优先级），否则拆分前后客户端会被指到不同地址。
        var content = new EntranceContentFactory(ReadChannelScript, PvpChannelConfiguration.Channels(options));
        var server = new EntranceHandshakeServer(
            address,
            entrancePorts,
            content.Get,
            new RuntimeEntranceSink(runtime, options.EnablePacketTracing, options.HexDumpLimit),
            message => logger.LogInformation("{Message}", message),
            (message, error) => logger.LogWarning(error, "{Message}", message),
            options.MaximumPacketLength,
            options.RejectBadCrc32);
        try
        {
            await server.StartAsync(stoppingToken);
            readiness?.MarkReady("entrance");
            await server.RunAsync(stoppingToken);
        }
        finally { readiness?.MarkStopped("entrance"); server.Stop(); }
    }

    private byte[]? ReadChannelScript()
    {
        const string virtualPath = "etc/channel_info.etc";
        if (scripts.FileExists(virtualPath))
        {
            return scripts.ReadAllBytes(virtualPath);
        }

        // 与 EntranceService.LoadContent 同一条 warn（HEAD :17631-17635；工作区受未提交改动影响在 :17634-17638）：
        // 缺文件时协议 9 下载关闭，只 warn 不阻止启动。
        logger.LogWarning(
            "{VirtualPath} is missing from {Source}; protocol 9 download is disabled.",
            virtualPath,
            scripts.SourceDescription);
        return null;
    }
}
