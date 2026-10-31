using System.Net;
using DFLegacy.Channel;
using DFLegacy.InterProcess;
using DFLegacy.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// DFLegacy.Channel —— 入口握手族（TCP 2311/8080 一类）的独立进程（S2 I2）。
//
// 只做本波的事：读同源配置 → 组内容 → 起握手宿主。链路客户端/服务端（Task 6/7）、admin 合并
// （Task 8）、protocol 表与验收平台（Task 9/10）都不在这里，别提前接。
//
// 进程内不得出现 JsonGameStore、不得写 state.json、不得引用 DFLegacy.Server（Global Constraints）。

// 与 Server 同源、同顺序：先 server.json（exe 同目录），再环境变量覆盖
// （Server 侧见 src/DFLegacy.Server/Program.cs:17-25）。
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("server.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

using var loggerFactory = LoggerFactory.Create(logging =>
{
    logging.ClearProviders();
    logging.AddSimpleConsole(console =>
    {
        console.SingleLine = true;
        console.TimestampFormat = "HH:mm:ss ";
    });
    logging.SetMinimumLevel(LogLevel.Information);
});
var logger = loggerFactory.CreateLogger("DFLegacy.Channel");

var options = ChannelOptions.Read(configuration.GetSection("DFLegacy"));
try
{
    // 先校验拓扑再碰端口（C5-CONFIG/2）：两个进程同时认领入口端口必须在 bind 之前就失败。
    options.Validate();
}
catch (InvalidOperationException exception)
{
    logger.LogError("Invalid DFLegacy.Channel configuration: {Message}", exception.Message);
    return 2;
}

// 拓扑守卫：这三种配置下本进程什么都不绑，但必须说清原因再退出，不能静默空转。
if (!options.Processes.Channel.Enabled)
{
    logger.LogWarning(
        "Processes.Channel.Enabled=false: the entrance handshake ports stay with the DFLegacy.Server process "
        + "(single-process mode), so this process binds nothing and exits.");
    return 0;
}

if (!options.Entrance.Enabled)
{
    logger.LogWarning(
        "Entrance.Enabled=false: the entrance handshake listener is disabled, so this process binds nothing and exits.");
    return 0;
}

// 端口归属：入口握手族 = {Entrance.Port} ∪ AdditionalPorts \ {Channel.Port}（EntrancePortFamilies.cs:11-26）。
var (entrancePorts, channelPorts) = EntrancePortFamilies.Split(
    options.Entrance.Port,
    options.Entrance.AdditionalPorts,
    options.Channel.Port);
if (entrancePorts.Length == 0)
{
    logger.LogWarning(
        "No entrance handshake ports to bind: Entrance.Port/AdditionalPorts only contained Channel.Port {ChannelPort}.",
        options.Channel.Port);
    return 0;
}

IPAddress address;
try
{
    address = IPAddress.Parse(options.Entrance.Host);
}
catch (FormatException)
{
    logger.LogError("Entrance.Host '{Host}' is not a valid IP address.", options.Entrance.Host);
    return 2;
}

// 内容：脚本来源镜像 Server（PVF 优先、缺 PVF 用解包目录、缺文件只 warn）。
var scriptSource = ChannelScriptSource.Read(options.ScriptPvfPath, options.SkillScriptPath);
logger.LogInformation(
    "Channel script source: {Source} ({Kind}).",
    scriptSource.SourceDescription,
    scriptSource.IsPvf ? "Script.pvf" : "unpacked script directory");

byte[]? ReadChannelScript()
{
    var script = scriptSource.ReadChannelScript();
    if (script is null)
    {
        // 与 EntranceService.LoadContent / EntranceHandshakeService.ReadChannelScript 同一条 warn：
        // 缺 etc/channel_info.etc 时协议 9 下载关闭，但不阻止启动（EntranceHandshakeService.cs:85-91）。
        logger.LogWarning(
            "{VirtualPath} is missing from {Source}; protocol 9 download is disabled.",
            ChannelScriptSource.ChannelScriptVirtualPath,
            scriptSource.SourceDescription);
    }

    return script;
}

// 广告地址（C5-HOST/2）：先按 GameplayDatagram 的 Host 回退，再走已提交的 EntranceDialHost.Resolve。
// 顺序不能反 —— 缺省 AdvertisedHost 是空串，直接丢给 Resolve 会跳过 Host 回退、退到 Entrance.Host，
// 拆分前后客户端会被指到不同地址。
var dialHost = EntranceDialHost.Resolve(
    options.GameplayDatagram.ResolveAdvertisedHost(),
    options.Entrance.Host,
    options.Channel.Host);
var channelInfo = new EntranceChannel(
    options.Channel.Name,
    dialHost,
    options.Channel.Port,
    options.Channel.ChannelNumber,
    options.Channel.MaximumUsers,
    options.Channel.CurrentUsers);
var content = new EntranceContentFactory(ReadChannelScript, channelInfo);
logger.LogInformation(
    "Clients will be told to dial {Host}:{Port} for channel {Name} {Number}.",
    dialHost,
    options.Channel.Port,
    options.Channel.Name,
    options.Channel.ChannelNumber);

using var lifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // Ctrl+C 走取消而不是直接杀进程：让 RunAsync 的 finally 有机会 Stop 掉监听。
    eventArgs.Cancel = true;
    lifetime.Cancel();
};

var sink = new ChannelSessionSink(message => logger.LogInformation("{Message}", message));
var server = new EntranceHandshakeServer(
    address,
    entrancePorts,
    content.Get,
    sink,
    message => logger.LogInformation("{Message}", message),
    (message, error) => logger.LogWarning(error, "{Message}", message),
    // 三个读参数显式传入（C5-READ/2）：宿主缺省值恰好也是这三个，但显式写出来才能让
    // 「Channel 用的是配置里的值」这件事在代码里可审。NoDelay 是旧行为常量
    // （EntranceService.cs 的共用读循环一处），不新增配置键。
    maximumPacketLength: options.MaximumPacketLength,
    rejectBadCrc32: options.RejectBadCrc32,
    noDelay: true);

logger.LogInformation(
    "Channel process owns entrance ports [{EntrancePorts}] and ignores the game-owned channel port(s) [{ChannelPorts}].",
    string.Join(", ", entrancePorts),
    string.Join(", ", channelPorts));

// 链路服务端（Task 7 / I3 后半）：让 game 进程把「我在这儿、我多大」注册上来，双方按同一间隔互发 STATUS。
// 只做校验与记账，不碰存档（spec §5.4）。
var startedAt = DateTimeOffset.UtcNow;
var link = new InterProcessLinkServer(
    address,
    options.Processes.Channel.LinkPort,
    new RegisterAckPayload(
        InterProcessMessageIds.LinkProtocolVersion,
        Accepted: 1,
        options.Channel.Name,
        (uint)options.Channel.Port),
    () => new StatusPayload(
        UptimeMs: (ulong)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
        ActiveSessions: (uint)sink.ActiveSessions,
        LoggedInUsers: 0,                      // channel 进程不做账号登录，这个字段恒 0
        TotalConnections: (ulong)sink.TotalSessions,
        // 收包口径：**不要**写成 Received+Sent —— server 的 TotalPackets 就是收包数
        // （RuntimeEntranceSink.cs:23-33），合并口径留给 Task 8 复核。
        TotalPackets: (ulong)sink.ReceivedPackets),
    TimeSpan.FromSeconds(1),
    message => logger.LogInformation("{Message}", message));

try
{
    // 串行管理生命周期（C5-LIFE/2）：握手宿主与链路服务端的 Start/Run 全在一个 try 里，清理只在 finally 一处。
    // 这样任一方端口绑定失败时，先前已经建好的监听也会被 Stop 释放（D5-START 的调用方一半；
    // 共享宿主忽略 Start token、Start/Stop 窗口仍是 open，不能据此声称完整并发安全）。
    await link.StartAsync(lifetime.Token);
    await server.StartAsync(lifetime.Token);
    var runs = new[] { server.RunAsync(lifetime.Token), link.RunAsync(lifetime.Token) };
    await Task.WhenAll(runs);
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception exception)
{
    logger.LogError(exception, "DFLegacy.Channel stopped after an error.");
    return 1;
}
finally
{
    // 先取消再 Stop：否则任一方故障退出后，另一方的循环还会继续跑（Task 7 刷新裁定第 2 条）。
    lifetime.Cancel();
    server.Stop();
    link.Stop();
    logger.LogInformation(
        "Channel process stopped: received={Received} sent={Sent} sessions={Sessions} active={Active} peerUp={PeerUp}.",
        sink.ReceivedPackets,
        sink.SentPackets,
        sink.TotalSessions,
        sink.ActiveSessions,
        link.PeerUp);
}
