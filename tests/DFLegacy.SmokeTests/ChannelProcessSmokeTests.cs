using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DFLegacy.Channel;
using DFLegacy.InterProcess;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Configuration;
using ChannelRoleOptions = DFLegacy.Channel.ChannelOptions;

/// <summary>
/// Task 5（S2 / I2）的门槛：<c>DFLegacy.Channel</c> 新 exe。
/// </summary>
/// <remarks>
/// 覆盖三块，按「先纯函数、再真进程」排：
/// <list type="number">
/// <item><b>配置/内容</b>（表驱动，无进程）：窄模型逐键映射、环境覆盖、缺省值逐项对齐
/// <c>ServerOptions</c>/<c>ProcessOptions</c>、拓扑校验镜像、广告地址回退、脚本来源回退。</item>
/// <item><b>接线/生命周期</b>（真进程）：单体一个 Server 进程绑三个口；拆分时入口口归 Channel 进程、
/// 频道口归 Server 进程；每个入口口实际发 11 收 12；非默认读参数在真实装配路径生效；启动失败与
/// 拓扑守卫的退出码和端口释放。</item>
/// <item><b>I2 内容对比</b>：同一个测试客户端在两档下用同一份真 PVF 资产，按各会话密钥解码后
/// 逐字节比较协议 10 脚本、协议 6 缓存决定与协议 3 频道元数据。</item>
/// </list>
/// 明确不声称的：最终 Tier 2 的原始帧全同（随机会话密钥 ⇒ 原始密文必然不同，留给
/// D5-RAW-COMPARE / Task 11）；真机（D5-REAL）；完整并发安全（D5-START / D5-CANCEL 仍 open）。
/// </remarks>
internal static class ChannelProcessSmokeTests
{
    private const string ChannelScriptVirtualPath = ChannelScriptSource.ChannelScriptVirtualPath;

    /// <summary>广告地址探针：GameplayDatagram.Host 设成它、AdvertisedHost 留空。</summary>
    private const string GameplayProbeHost = "127.0.0.9";

    private const string ChannelName = "Local Channel";
    private const int ChannelNumber = 1;
    private const int ChannelMaximumUsers = 100;
    private const int ChannelCurrentUsers = 1;

    /// <summary>
    /// 协议 6 明文（32 字节）：int32(0) + 16 字节会话 token + 12 字节零。
    /// Task 4 从真客户端抓包里解出过同一串（EntranceSplitSmokeTests.RunHandshakeReplayAsync 的
    /// recordedCachePlaintext），token 前缀 "dfls-local" 是 EntranceProtocolEngine.cs:8-21 的既有常量。
    /// </summary>
    private const string RecordedCachePlaintextHex =
        "0000000064666c732d6c6f63616c000000000000000000000000000000000000";

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(20);

    /// <summary>「等到某个可观察状态出现」的上限（链路注册、日志行等）。</summary>
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    public static async Task RunAsync(Action<bool, string> check)
    {
        RunConfigurationMapping(check);
        RunConfigurationValidation(check);
        await RunScriptSourceResolutionAsync(check);
        await RunTopologiesAsync(check);
        await RunLinkEndToEndAsync(check);
        await RunLinkAdminEndToEndAsync(check);
        await RunReadParametersAsync(check);
        await RunTopologyGuardsAsync(check);
    }

    // ------------------------------------------------------------------ 配置

    /// <summary>
    /// C5-CONFIG/2：窄模型只读它消费的键，环境（同源提供器）在 JSON 之后覆盖。
    /// 这里用 AddJsonStream + AddInMemoryCollection 复现「JSON 提供器在前、覆盖提供器在后」的
    /// 同一套 Configuration 语义；真环境变量的映射由下面的真进程夹具覆盖。
    /// </summary>
    private static void RunConfigurationMapping(Action<bool, string> check)
    {
        const string json = """
            {
              "DFLegacy": {
                "Entrance": { "Enabled": false, "Host": "10.1.2.3", "Port": 26111, "AdditionalPorts": [ 27001, 26808 ] },
                "Channel": {
                  "Name": "Configured Channel", "Host": "10.1.2.4", "Port": 27001,
                  "ChannelNumber": 3, "MaximumUsers": 250, "CurrentUsers": 7
                },
                "GameplayDatagram": { "Host": "10.1.2.5", "AdvertisedHost": "10.1.2.6" },
                "Processes": { "Channel": { "Enabled": true, "LinkPort": 27004 }, "Game": { "EntranceBinding": false } },
                "ScriptPvfPath": "pvf/Configured.pvf",
                "SkillScriptPath": "unpacked",
                "MaximumPacketLength": 65536,
                "RejectBadCrc32": true
              }
            }
            """;

        var mapped = ReadChannelOptionsFrom(json);
        var rows = new (string Key, object Expected, object Actual)[]
        {
            ("Entrance.Enabled", false, mapped.Entrance.Enabled),
            ("Entrance.Host", "10.1.2.3", mapped.Entrance.Host),
            ("Entrance.Port", 26111, mapped.Entrance.Port),
            ("Channel.Name", "Configured Channel", mapped.Channel.Name),
            ("Channel.Host", "10.1.2.4", mapped.Channel.Host),
            ("Channel.Port", 27001, mapped.Channel.Port),
            ("Channel.ChannelNumber", 3, mapped.Channel.ChannelNumber),
            ("Channel.MaximumUsers", 250, mapped.Channel.MaximumUsers),
            ("Channel.CurrentUsers", 7, mapped.Channel.CurrentUsers),
            ("GameplayDatagram.Host", "10.1.2.5", mapped.GameplayDatagram.Host),
            ("GameplayDatagram.AdvertisedHost", "10.1.2.6", mapped.GameplayDatagram.AdvertisedHost),
            ("Processes.Channel.Enabled", true, mapped.Processes.Channel.Enabled),
            ("Processes.Channel.LinkPort", 27004, mapped.Processes.Channel.LinkPort),
            ("Processes.Game.EntranceBinding", false, mapped.Processes.Game.EntranceBinding),
            ("ScriptPvfPath", "pvf/Configured.pvf", mapped.ScriptPvfPath),
            ("SkillScriptPath", "unpacked", mapped.SkillScriptPath),
            ("MaximumPacketLength", 65536, mapped.MaximumPacketLength),
            ("RejectBadCrc32", true, mapped.RejectBadCrc32),
        };
        foreach (var row in rows)
        {
            check(Equals(row.Expected, row.Actual), $"channel config maps {row.Key} from the shared server.json section");
        }

        check(mapped.Entrance.AdditionalPorts.SequenceEqual([27001, 26808]),
            "channel config maps Entrance.AdditionalPorts as an ordered array (same provider merging semantics as the server)");
        check(EntrancePortFamilies.Split(mapped.Entrance.Port, mapped.Entrance.AdditionalPorts, mapped.Channel.Port)
                .EntrancePorts.SequenceEqual([26111, 26808]),
            "the configured entrance family is Entrance.Port plus AdditionalPorts minus Channel.Port");

        // 覆盖提供器排在 JSON 之后 ⇒ 必须赢（环境变量在 Server 里也是这个位置）。
        var overridden = ReadChannelOptionsFrom(
            json,
            ("DFLegacy:Entrance:Port", "36011"),
            ("DFLegacy:Entrance:AdditionalPorts:1", "36999"),
            ("DFLegacy:MaximumPacketLength", "2048"),
            ("DFLegacy:RejectBadCrc32", "false"),
            ("DFLegacy:GameplayDatagram:AdvertisedHost", "10.9.9.9"),
            ("DFLegacy:Processes:Channel:LinkPort", "37004"),
            ("DFLegacy:ScriptPvfPath", "pvf/Overridden.pvf"));
        var overrideRows = new (string Key, object Expected, object Actual)[]
        {
            ("Entrance.Port", 36011, overridden.Entrance.Port),
            ("Entrance.AdditionalPorts[1]", 36999, overridden.Entrance.AdditionalPorts[1]),
            ("MaximumPacketLength", 2048, overridden.MaximumPacketLength),
            ("RejectBadCrc32", false, overridden.RejectBadCrc32),
            ("GameplayDatagram.AdvertisedHost", "10.9.9.9", overridden.GameplayDatagram.AdvertisedHost),
            ("Processes.Channel.LinkPort", 37004, overridden.Processes.Channel.LinkPort),
            ("ScriptPvfPath", "pvf/Overridden.pvf", overridden.ScriptPvfPath),
        };
        foreach (var row in overrideRows)
        {
            check(Equals(row.Expected, row.Actual),
                $"a provider registered after JSON overrides {row.Key}, exactly like DFLegacy__... does for DFLegacy.Server");
        }

        // 广告地址：缺省/空 AdvertisedHost 必须回退到 GameplayDatagram.Host（C5-HOST 的第一步）。
        var noAdvertised = ReadChannelOptionsFrom("""{ "DFLegacy": { "GameplayDatagram": { "Host": "10.2.3.4" } } }""");
        check(noAdvertised.GameplayDatagram.ResolveAdvertisedHost() == "10.2.3.4",
            "an unset AdvertisedHost resolves to GameplayDatagram.Host, mirroring ServerOptions.GameplayDatagram.ResolveAdvertisedHost");
        var emptyAdvertised = ReadChannelOptionsFrom(
            """{ "DFLegacy": { "GameplayDatagram": { "Host": "10.2.3.4", "AdvertisedHost": "" } } }""");
        check(emptyAdvertised.GameplayDatagram.ResolveAdvertisedHost() == "10.2.3.4",
            "an empty AdvertisedHost resolves to GameplayDatagram.Host as well (IsNullOrWhiteSpace, not just null)");
        check(mapped.GameplayDatagram.ResolveAdvertisedHost() == "10.1.2.6",
            "a concrete AdvertisedHost wins over GameplayDatagram.Host");

        // 缺省值逐项对齐 ServerOptions / ProcessOptions：两边读同一份 server.json，缺省值漂移
        // 就会让单体/拆分两档的客户端可见字节不同。
        var channelDefaults = new ChannelRoleOptions();
        var serverDefaults = new ServerOptions();
        var processDefaults = new ProcessOptions();
        var defaultRows = new (string Key, object Expected, object Actual)[]
        {
            ("Entrance.Enabled", serverDefaults.Entrance.Enabled, channelDefaults.Entrance.Enabled),
            ("Entrance.Host", serverDefaults.Entrance.Host, channelDefaults.Entrance.Host),
            ("Entrance.Port", serverDefaults.Entrance.Port, channelDefaults.Entrance.Port),
            ("Channel.Name", serverDefaults.Channel.Name, channelDefaults.Channel.Name),
            ("Channel.Host", serverDefaults.Channel.Host, channelDefaults.Channel.Host),
            ("Channel.Port", serverDefaults.Channel.Port, channelDefaults.Channel.Port),
            ("Channel.ChannelNumber", serverDefaults.Channel.ChannelNumber, channelDefaults.Channel.ChannelNumber),
            ("Channel.MaximumUsers", serverDefaults.Channel.MaximumUsers, channelDefaults.Channel.MaximumUsers),
            ("Channel.CurrentUsers", serverDefaults.Channel.CurrentUsers, channelDefaults.Channel.CurrentUsers),
            ("GameplayDatagram.Host", serverDefaults.GameplayDatagram.Host, channelDefaults.GameplayDatagram.Host),
            ("GameplayDatagram.AdvertisedHost", serverDefaults.GameplayDatagram.AdvertisedHost, channelDefaults.GameplayDatagram.AdvertisedHost),
            ("ScriptPvfPath", serverDefaults.ScriptPvfPath, channelDefaults.ScriptPvfPath),
            ("SkillScriptPath", serverDefaults.SkillScriptPath, channelDefaults.SkillScriptPath),
            ("MaximumPacketLength", serverDefaults.MaximumPacketLength, channelDefaults.MaximumPacketLength),
            ("RejectBadCrc32", serverDefaults.RejectBadCrc32, channelDefaults.RejectBadCrc32),
            ("Processes.Channel.Enabled", processDefaults.Channel.Enabled, channelDefaults.Processes.Channel.Enabled),
            ("Processes.Channel.LinkPort", processDefaults.Channel.LinkPort, channelDefaults.Processes.Channel.LinkPort),
            ("Processes.Game.EntranceBinding", processDefaults.Game.EntranceBinding, channelDefaults.Processes.Game.EntranceBinding),
        };
        foreach (var row in defaultRows)
        {
            check(Equals(row.Expected, row.Actual),
                $"channel option default {row.Key} equals the server-side default (a drift here changes split-mode bytes)");
        }

        check(channelDefaults.Entrance.AdditionalPorts.Length == 0
                && serverDefaults.Entrance.AdditionalPorts.Length == 0,
            "both sides default Entrance.AdditionalPorts to an empty array");
        check(channelDefaults.Processes.Channel.Enabled == false
                && channelDefaults.Processes.Game.EntranceBinding == true,
            "the default topology is today's single-process mode (Channel disabled, game binds the entrance ports)");
    }

    /// <summary>
    /// C5-CONFIG/2：拓扑校验必须镜像 ProcessOptions.Validate()，同样的拒绝条件 + 同样的消息，
    /// 并且在绑定端口之前失败。
    /// </summary>
    private static void RunConfigurationValidation(Action<bool, string> check)
    {
        static string? FailureMessage(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }
        }

        var channelBoth = new ChannelRoleOptions();
        channelBoth.Processes.Channel.Enabled = true;
        channelBoth.Processes.Game.EntranceBinding = true;
        var serverBoth = new ProcessOptions();
        serverBoth.Channel.Enabled = true;
        serverBoth.Game.EntranceBinding = true;
        var channelBothMessage = FailureMessage(channelBoth.Validate);
        var serverBothMessage = FailureMessage(serverBoth.Validate);
        check(channelBothMessage is not null && channelBothMessage == serverBothMessage,
            "a split layout that still has the game binding the entrance ports is rejected with the same message as ProcessOptions.Validate()");

        var channelLowPort = new ChannelRoleOptions();
        channelLowPort.Processes.Channel.LinkPort = 0;
        var serverLowPort = new ProcessOptions();
        serverLowPort.Channel.LinkPort = 0;
        check(FailureMessage(channelLowPort.Validate) is { } low
                && low == FailureMessage(serverLowPort.Validate),
            "LinkPort=0 is rejected with the same message as ProcessOptions.Validate()");

        var channelHighPort = new ChannelRoleOptions();
        channelHighPort.Processes.Channel.LinkPort = 70000;
        var serverHighPort = new ProcessOptions();
        serverHighPort.Channel.LinkPort = 70000;
        check(FailureMessage(channelHighPort.Validate) is { } high
                && high == FailureMessage(serverHighPort.Validate),
            "LinkPort=70000 is rejected with the same message as ProcessOptions.Validate()");

        var channelSplit = new ChannelRoleOptions();
        channelSplit.Processes.Channel.Enabled = true;
        channelSplit.Processes.Game.EntranceBinding = false;
        check(FailureMessage(channelSplit.Validate) is null,
            "the valid split layout (Channel.Enabled=true, Game.EntranceBinding=false) passes ChannelOptions.Validate()");
    }

    // -------------------------------------------------------------- 脚本来源

    /// <summary>
    /// C5-SCRIPT/2：PVF 优先、缺 PVF 回退解包目录、相对路径按 AppContext.BaseDirectory、
    /// 缺 etc/channel_info.etc 返回 null、已存在但损坏的 PVF 照既有异常语义抛。
    /// </summary>
    private static async Task RunScriptSourceResolutionAsync(Action<bool, string> check)
    {
        var repositoryRoot = FindRepositoryRoot();
        var pvfPath = repositoryRoot is null
            ? null
            : Path.Combine(repositoryRoot, "DNF2008", "Script.pvf");
        check(pvfPath is not null && File.Exists(pvfPath),
            $"the real script asset used by both topologies exists at <repo>/DNF2008/Script.pvf (looked in {pvfPath ?? "<no repo root>"})");
        if (pvfPath is null || !File.Exists(pvfPath))
        {
            return;
        }

        // 1) 有效 PVF 优先。
        var fromPvf = ChannelScriptSource.Read(pvfPath, "");
        check(fromPvf.IsPvf && fromPvf.SourceDescription == Path.GetFullPath(pvfPath),
            "a readable Script.pvf wins and its absolute path is reported as the source");
        var expectedScript = new PvfArchiveReader(pvfPath).ReadAllBytes(ChannelScriptVirtualPath);
        var pvfScript = fromPvf.ReadChannelScript();
        check(pvfScript is not null && pvfScript.SequenceEqual(expectedScript),
            $"the PVF branch returns etc/channel_info.etc unchanged ({expectedScript.Length} bytes)");
        check(expectedScript.Length > 0,
            "the shipped Script.pvf really contains etc/channel_info.etc (otherwise protocol 9 comparison would be vacuous)");

        var scratch = Path.Combine(Path.GetTempPath(), "dflegacy-channel-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            // 2) PVF 有效但缺条目 → null（调用方 warn），不是异常。
            var emptyPvf = Path.Combine(scratch, "empty.pvf");
            WritePvfWithoutEntries(emptyPvf);
            var emptySource = ChannelScriptSource.Read(emptyPvf, "");
            check(emptySource.IsPvf && emptySource.ReadChannelScript() is null,
                "a valid PVF without etc/channel_info.etc yields null (the caller warns and protocol 9 stays disabled)");

            // 3) 缺 PVF 时回退到解包目录。
            var unpacked = Path.Combine(scratch, "unpacked");
            Directory.CreateDirectory(Path.Combine(unpacked, "etc"));
            var directoryScript = Encoding.ASCII.GetBytes("directory-channel-script-" + Guid.NewGuid().ToString("N"));
            await File.WriteAllBytesAsync(Path.Combine(unpacked, "etc", "channel_info.etc"), directoryScript);
            var fromDirectory = ChannelScriptSource.Read(Path.Combine(scratch, "absent.pvf"), unpacked);
            check(!fromDirectory.IsPvf && fromDirectory.SourceDescription == Path.GetFullPath(unpacked),
                "a missing Script.pvf falls back to the unpacked SkillScriptPath directory");
            var directoryBytes = fromDirectory.ReadChannelScript();
            check(directoryBytes is not null && directoryBytes.SequenceEqual(directoryScript),
                "the directory branch returns <SkillScriptPath>/etc/channel_info.etc unchanged");

            // 4) 目录里也没有这个虚拟文件 → null。
            var emptyDirectory = Path.Combine(scratch, "empty-directory");
            Directory.CreateDirectory(emptyDirectory);
            check(ChannelScriptSource.Read(Path.Combine(scratch, "absent.pvf"), emptyDirectory)
                    .ReadChannelScript() is null,
                "a directory without etc/channel_info.etc yields null instead of throwing");

            // 5) 两边都不可用：来源说明仍是配置过的 PVF 路径，不编造。
            var nothing = ChannelScriptSource.Read(Path.Combine(scratch, "absent.pvf"), Path.Combine(scratch, "absent-dir"));
            check(nothing.ReadChannelScript() is null
                    && nothing.SourceDescription == Path.GetFullPath(Path.Combine(scratch, "absent.pvf")),
                "with no usable source the description still names the configured PVF path (no invented fallback)");

            // 6) 完全没配置 → "(not configured)"，与 ScriptFileSystem 同口径。
            check(ChannelScriptSource.Read("", " ").SourceDescription == "(not configured)",
                "an unconfigured PVF source reports '(not configured)', like ScriptFileSystem.SourceDescription");

            // 7) 相对路径按 AppContext.BaseDirectory 解析（ScriptFileSystem.ResolveConfiguredPath 的口径）。
            var relative = ChannelScriptSource.Read("relative/Script.pvf", "");
            check(relative.SourceDescription == Path.GetFullPath("relative/Script.pvf", AppContext.BaseDirectory),
                "relative configured paths resolve against AppContext.BaseDirectory, not the process working directory");

            // 8) 已存在但损坏的 PVF 照既有异常语义抛，不静默回退到目录。
            var corrupt = Path.Combine(scratch, "corrupt.pvf");
            await File.WriteAllBytesAsync(corrupt, new byte[64]);
            var threw = false;
            try
            {
                ChannelScriptSource.Read(corrupt, unpacked);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }

            check(threw,
                "an existing but corrupt Script.pvf throws InvalidDataException instead of silently falling back to the directory");
        }
        finally
        {
            TryDeleteDirectory(scratch);
        }
    }

    // ------------------------------------------------------ 接线 / I2 内容对比

    private static async Task RunTopologiesAsync(Action<bool, string> check)
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            check(false, "the repository root (DFLegacy.Emulator.slnx) is reachable from the smoke test output directory");
            return;
        }

        var configuration = DetectBuildConfiguration();
        var serverExe = Path.Combine(repositoryRoot, "src", "DFLegacy.Server", "bin", configuration, "net10.0", "DFLegacy.Server.exe");
        var channelExe = Path.Combine(repositoryRoot, "src", "DFLegacy.Channel", "bin", configuration, "net10.0", "DFLegacy.Channel.exe");
        var pvfPath = Path.Combine(repositoryRoot, "DNF2008", "Script.pvf");
        check(File.Exists(serverExe), $"the game-role executable is built at {serverExe}");
        check(File.Exists(channelExe), $"the channel-role executable is built at {channelExe}");
        check(File.Exists(pvfPath), $"the shared script asset exists at {pvfPath}");
        if (!File.Exists(serverExe) || !File.Exists(channelExe) || !File.Exists(pvfPath))
        {
            return;
        }

        var asset = new PvfArchiveReader(pvfPath).ReadAllBytes(ChannelScriptVirtualPath);
        var ports = FixturePorts.Allocate();
        var scratch = Path.Combine(Path.GetTempPath(), "dflegacy-channel-topology-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var environment = BuildEnvironment(pvfPath, scratch, ports);
        var expectedMetadata = EntranceMetadata.BuildSingleLocalChannel(
            new EntranceChannel(
                ChannelName,
                EntranceDialHost.Resolve(GameplayProbeHost, "127.0.0.1", "127.0.0.1"),
                ports.ChannelPort,
                ChannelNumber,
                ChannelMaximumUsers,
                ChannelCurrentUsers));
        check(EntranceDialHost.Resolve(GameplayProbeHost, "127.0.0.1", "127.0.0.1") == GameplayProbeHost,
            "the fixture's advertised host really exercises the Host fallback (a dialable GameplayDatagram.Host beats Entrance.Host)");
        var recordedCachePlaintext = Convert.FromHexString(RecordedCachePlaintextHex);

        HandshakeExchange monoOnEntrance = null!;
        HandshakeExchange monoOnAdditional = null!;
        try
        {
            // ---------- 单体：一个 Server 进程绑 2311/8080/7001 三个口 ----------
            var monoEnvironment = WithTopology(environment, channelEnabled: false, gameBindsEntrance: true);
            using (var game = ChildProcess.Start(serverExe, Path.GetDirectoryName(serverExe)!, monoEnvironment))
            {
                var ready = await WaitForAllAsync(game, ReadinessTimeout,
                    $"Entrance listener ready at 127.0.0.1:{ports.EntrancePort}",
                    $"Entrance listener ready at 127.0.0.1:{ports.AdditionalPort}",
                    $"Entrance listener ready at 127.0.0.1:{ports.ChannelPort}");
                ReportUnready(check, "single-process Server", ready, game);
                if (!ready)
                {
                    return;
                }

                check(ready
                        && game.Output.Contains($"Entrance listener ready at 127.0.0.1:{ports.EntrancePort}", StringComparison.Ordinal)
                        && game.Output.Contains($"Entrance listener ready at 127.0.0.1:{ports.AdditionalPort}", StringComparison.Ordinal)
                        && game.Output.Contains($"Entrance listener ready at 127.0.0.1:{ports.ChannelPort}", StringComparison.Ordinal),
                    "single-process mode: one DFLegacy.Server process reports listeners on Entrance.Port, AdditionalPorts and Channel.Port (all three ports in one process)");

                monoOnEntrance = await TryRunHandshakeAsync(ports.EntrancePort, check, "single-process Entrance.Port");
                monoOnAdditional = await TryRunHandshakeAsync(ports.AdditionalPort, check, "single-process AdditionalPorts[0]");
                var monoChannelProbe = await ProbeProtocolAsync(ports.ChannelPort, protocolId: 11, replyProtocolId: 12);
                check(monoChannelProbe.Connected && monoChannelProbe.Reply is null,
                    "single-process mode: the channel port does accept a TCP connection, but the handshake engine does not serve it "
                    + "(protocol 11 gets no protocol 12 there, so a refused/dead port cannot make this pass)");

                var stopped = await game.StopAndWaitAsync(ExitTimeout);
                check(stopped,
                    "single-process mode: the Server process has exited within the timeout after a forced termination "
                    + "(a graceful, signal-driven stop is NOT covered by this wave and stays recorded as open)");
            }

            check(CanBind(ports.EntrancePort) && CanBind(ports.AdditionalPort) && CanBind(ports.ChannelPort),
                "single-process mode: the entrance, additional and channel ports are all free again after the process exits");

            // ---------- 拆分：Channel 进程拿入口族，Server 进程只拿频道口 ----------
            var splitEnvironment = WithTopology(environment, channelEnabled: true, gameBindsEntrance: false);
            using var splitGame = ChildProcess.Start(serverExe, Path.GetDirectoryName(serverExe)!, splitEnvironment);
            using var splitChannel = ChildProcess.Start(channelExe, Path.GetDirectoryName(channelExe)!, splitEnvironment);

            var gameReady = await splitGame.WaitForOutputAsync(
                $"Entrance listener ready at 127.0.0.1:{ports.ChannelPort}", ReadinessTimeout);
            ReportUnready(check, "split game role", gameReady, splitGame);
            if (!gameReady)
            {
                return;
            }

            check(!splitGame.Output.Contains($"Entrance listener ready at 127.0.0.1:{ports.EntrancePort}", StringComparison.Ordinal)
                    && !splitGame.Output.Contains($"Entrance listener ready at 127.0.0.1:{ports.AdditionalPort}", StringComparison.Ordinal),
                "split mode: the game-role process does not bind the entrance ports");
            check(splitGame.Output.Contains("are owned by the DFLegacy.Channel process", StringComparison.Ordinal),
                "split mode: the game-role process says out loud why it is not binding the entrance ports");

            var channelReady = await WaitForAllAsync(splitChannel, ReadinessTimeout,
                $"Entrance listener ready at 127.0.0.1:{ports.EntrancePort}",
                $"Entrance listener ready at 127.0.0.1:{ports.AdditionalPort}");
            ReportUnready(check, "split channel role", channelReady, splitChannel);
            if (!channelReady)
            {
                return;
            }

            check(!splitChannel.Output.Contains($"Entrance listener ready at 127.0.0.1:{ports.ChannelPort}", StringComparison.Ordinal),
                "split mode: the channel-role process never binds the game-owned channel port");

            var splitOnEntrance = await TryRunHandshakeAsync(ports.EntrancePort, check, "split Entrance.Port");
            var splitOnAdditional = await TryRunHandshakeAsync(ports.AdditionalPort, check, "split AdditionalPorts[0]");
            var splitChannelProbe = await ProbeProtocolAsync(ports.ChannelPort, protocolId: 11, replyProtocolId: 12);
            check(splitChannelProbe.Connected && splitChannelProbe.Reply is null,
                "split mode: the channel port does accept a TCP connection, but the handshake engine does not serve it "
                + "(protocol 11 gets no protocol 12 there, so a refused/dead port cannot make this pass)");

            // ---------- I2：同一客户端、同一资产，两档解码后逐字节一致 ----------
            var exchanges = new (string Label, HandshakeExchange Exchange)[]
            {
                ("single-process Entrance.Port", monoOnEntrance),
                ("single-process AdditionalPorts[0]", monoOnAdditional),
                ("split Entrance.Port", splitOnEntrance),
                ("split AdditionalPorts[0]", splitOnAdditional),
            };
            foreach (var (label, exchange) in exchanges)
            {
                var scriptMismatch = DescribePadding(exchange.Script, asset);
                check(scriptMismatch is null,
                    $"I2: {label} downloads etc/channel_info.etc byte-for-byte from the shared Script.pvf ({asset.Length} bytes){scriptMismatch}");
                var metadataMismatch = DescribePadding(exchange.Metadata, expectedMetadata);
                check(metadataMismatch is null,
                    $"I2: {label} sends channel metadata byte-identical to the configured channel record{metadataMismatch}");
                check(exchange.CachePlaintext.SequenceEqual(recordedCachePlaintext),
                    $"I2: {label} answers protocol 5 with the same cache decision and session token a real client already received");
            }

            // 非空前置：握手失败时 EmptyExchange() 给的是空数组，光比 SequenceEqual 会恒真。
            check(monoOnEntrance.Script.Length > 0
                    && monoOnAdditional.Script.Length > 0
                    && splitOnEntrance.Script.Length > 0
                    && splitOnAdditional.Script.Length > 0
                    && monoOnEntrance.Script.SequenceEqual(splitOnEntrance.Script)
                    && monoOnAdditional.Script.SequenceEqual(splitOnAdditional.Script)
                    && monoOnEntrance.Script.SequenceEqual(monoOnAdditional.Script),
                "I2 gate: the handshake script bytes are identical across single-process and split mode, and across both entrance ports");
            check(monoOnEntrance.Metadata.Length > 0
                    && splitOnEntrance.Metadata.Length > 0
                    && monoOnEntrance.Metadata.SequenceEqual(splitOnEntrance.Metadata)
                    && monoOnEntrance.Metadata.SequenceEqual(monoOnAdditional.Metadata)
                    && splitOnAdditional.Metadata.SequenceEqual(monoOnEntrance.Metadata),
                "I2 gate: the channel metadata is identical across both topologies and both entrance ports");

            // 每会话随机密钥（EntranceProtocolEngine.cs:10）：原始字节必然不同，解码后必须相同。
            var keys = exchanges.Select(pair => Convert.ToHexString(pair.Exchange.KeyMaterial)).ToArray();
            check(keys.Distinct().Count() == keys.Length,
                "each entrance session gets its own random key material, so a raw-ciphertext comparison cannot be the gate");
            check(exchanges.Select(pair => Convert.ToHexString(pair.Exchange.KeyReplyBody)).Distinct().Count() == keys.Length,
                "protocol 12 therefore differs on the wire between sessions while the decoded content stays identical");

            var stoppedGame = await splitGame.StopAndWaitAsync(ExitTimeout);
            var stoppedChannel = await splitChannel.StopAndWaitAsync(ExitTimeout);
            check(stoppedGame && stoppedChannel,
                "split mode: both role processes have exited within the timeout after a forced termination "
                + "(a graceful, signal-driven stop is NOT covered by this wave and stays recorded as open)");
            check(CanBind(ports.EntrancePort) && CanBind(ports.AdditionalPort) && CanBind(ports.ChannelPort),
                "split mode: every fixture port is free again after both role processes exit");

            // Channel 进程自己的标签/计数日志（C5-SINK/2）：两个入口端口必须可区分。
            check(splitChannel.Output.Contains($"Entrance session entrance:{ports.EntrancePort} opened", StringComparison.Ordinal)
                    && splitChannel.Output.Contains($"Entrance session entrance:{ports.AdditionalPort} opened", StringComparison.Ordinal),
                "the channel process keeps the host's entrance:<port> label, so its two entrance ports stay distinguishable");
            check(splitChannel.Output.Contains("no active entrance sessions remain", StringComparison.Ordinal),
                "the channel process logs that its entrance sessions were closed again (no leaked active session)");
        }
        finally
        {
            TryDeleteDirectory(scratch);
        }
    }

    /// <summary>
    /// Task 7 / I3 后半：**真实两进程**的链路连通 —— 真正的 <c>DFLegacy.Channel</c> exe 起链路服务端，
    /// 测试进程里的真 <see cref="InterProcessLinkClient"/> 注册上去，两个方向都验。
    /// </summary>
    /// <remarks>
    /// 与 <c>InterProcessLinkSmokeTests</c> 的分工：那边在同一个进程里验服务端自己的行为（含拒绝路径），
    /// 这边验「装配起来的 channel 进程真的能接链路」，配置走的是同一份 server.json + 环境覆盖。
    /// </remarks>
    private static async Task RunLinkEndToEndAsync(Action<bool, string> check)
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            check(false, "the repository root is reachable for the link end-to-end fixture");
            return;
        }

        var channelExe = Path.Combine(repositoryRoot, "src", "DFLegacy.Channel", "bin",
            DetectBuildConfiguration(), "net10.0", "DFLegacy.Channel.exe");
        var pvfPath = Path.Combine(repositoryRoot, "DNF2008", "Script.pvf");
        if (!File.Exists(channelExe) || !File.Exists(pvfPath))
        {
            check(false, $"the link end-to-end fixture needs {channelExe} and {pvfPath}");
            return;
        }

        var ports = FixturePorts.Allocate();
        var scratch = Path.Combine(Path.GetTempPath(), "dflegacy-channel-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var environment = WithTopology(
                BuildEnvironment(pvfPath, scratch, ports),
                channelEnabled: true,
                gameBindsEntrance: false);
            using var channel = ChildProcess.Start(channelExe, Path.GetDirectoryName(channelExe)!, environment);
            var linkReady = await channel.WaitForOutputAsync(
                $"Inter-process link listening at 127.0.0.1:{ports.LinkPort}", ReadinessTimeout);
            ReportUnready(check, "channel link role", linkReady, channel);
            if (!linkReady)
            {
                return;
            }

            // 就像 game 进程会做的那样：把自己的身份注册上去，然后按间隔报 STATUS。
            var register = new RegisterPayload(
                ProtocolVersion: InterProcessMessageIds.LinkProtocolVersion,
                GcNo: 3011,
                ChannelNo: (uint)ChannelNumber,
                MaximumUsers: (uint)ChannelMaximumUsers,
                CurrentUsers: (uint)ChannelCurrentUsers,
                AdvertisedHost: "127.0.0.1",
                GamePort: (uint)ports.ChannelPort);
            var reportedStatus = new StatusPayload(
                UptimeMs: 5, ActiveSessions: 1, LoggedInUsers: 0, TotalConnections: 2, TotalPackets: 4);
            await using var client = new InterProcessLinkClient(new InterProcessLinkClientOptions(
                "127.0.0.1", ports.LinkPort, TimeSpan.FromMilliseconds(200), 3, register, () => reportedStatus));
            using var clientStop = new CancellationTokenSource();
            var clientRun = client.RunAsync(clientStop.Token);
            try
            {
                check(await PollAsync(() => client.Connected, WaitTimeout),
                    "a real DFLegacy.Channel process accepts a real link client's REGISTER (registration survives the process boundary)");
                check(client.LastAck is { Accepted: 1 } ack
                        && ack.ChannelPort == (uint)ports.ChannelPort
                        && ack.ChannelName == ChannelName,
                    "the ACK carries the channel identity straight out of the channel process's own config (name and port match the fixture)");
                check(await PollAsync(() => client.LastRemoteStatus is not null, WaitTimeout),
                    "the channel process sends its own STATUS back, so channel→game works across the process boundary too");
                check(await PollAsync(
                        () => channel.Output.Contains("Game process registered from", StringComparison.Ordinal),
                        WaitTimeout),
                    "the channel process logs the registration with the decoded ids");
                check(await PollAsync(
                        () => channel.Output.Contains("game_port=", StringComparison.Ordinal),
                        WaitTimeout),
                    "the logged registration includes the game port the client asked to advertise");
            }
            finally
            {
                clientStop.Cancel();
                check((await AwaitQuietlyAsync(clientRun, WaitTimeout)).Fault is null,
                    "the link client against the real channel process stops cleanly");
                await channel.StopAndWaitAsync(ExitTimeout);
            }

            check(CanBind(ports.LinkPort),
                "the channel process releases the link port when it stops (no listener left behind)");
        }
        finally
        {
            TryDeleteDirectory(scratch);
        }
    }

    /// <summary>
    /// Task 8 / I4：**真实两进程** —— Server(split) 的 ChannelLinkService 拨号 Channel 的链路服务端，
    /// 然后 admin 登录后 GET /api/status，既有字段不回归 + 新增 channelLink.connected=true。
    /// </summary>
    private static async Task RunLinkAdminEndToEndAsync(Action<bool, string> check)
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            check(false, "the repository root is reachable for the link-admin e2e fixture");
            return;
        }

        var serverExe = Path.Combine(repositoryRoot, "src", "DFLegacy.Server", "bin",
            DetectBuildConfiguration(), "net10.0", "DFLegacy.Server.exe");
        var channelExe = Path.Combine(repositoryRoot, "src", "DFLegacy.Channel", "bin",
            DetectBuildConfiguration(), "net10.0", "DFLegacy.Channel.exe");
        var pvfPath = Path.Combine(repositoryRoot, "DNF2008", "Script.pvf");
        if (!File.Exists(serverExe) || !File.Exists(channelExe) || !File.Exists(pvfPath))
        {
            check(false, $"the link-admin e2e fixture needs {serverExe}, {channelExe} and {pvfPath}");
            return;
        }

        var ports = FixturePorts.Allocate();
        var seedDir = Path.Combine(Path.GetTempPath(), "dflegacy-channel-admin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(seedDir);
        var statePath = Path.Combine(seedDir, "state.json");

        // 用同进程 JsonGameStore 预种一个账号：真实 Server 启动时 ApplyBootstrapAdminsAsync（Program.cs:182）
        // 会把它提权为管理员。这样 admin 登录才不 403。
        var seedStore = new JsonGameStore(
            new ServerOptions { DataPath = statePath },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonGameStore>.Instance);
        await seedStore.CreateAccountAsync(new CreateAccountRequest("admin", "bosspass", OicqCode: "10001"));

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var environment = WithTopology(
            BuildEnvironment(pvfPath, seedDir, ports),
            channelEnabled: true,
            gameBindsEntrance: false);
        environment["DFLegacy__Admin__Users__0"] = "admin";

        using var channel = ChildProcess.Start(channelExe, Path.GetDirectoryName(channelExe)!, environment);
        using var server = ChildProcess.Start(serverExe, Path.GetDirectoryName(serverExe)!, environment);
        try
        {
            var channelLinkReady = await channel.WaitForOutputAsync(
                $"Inter-process link listening at 127.0.0.1:{ports.LinkPort}", ReadinessTimeout);
            ReportUnready(check, "channel link role", channelLinkReady, channel);
            if (!channelLinkReady)
            {
                return;
            }

            var serverUp = await server.WaitForOutputAsync("Application started", ReadinessTimeout);
            ReportUnready(check, "server admin role", serverUp, server);
            if (!serverUp)
            {
                return;
            }

            // 双侧存活可见：Server 拨 Channel → 两边日志各证一侧（I4 的核心）。
            var linkUp = await server.WaitForOutputAsync(
                "Inter-process link to the channel process is up", WaitTimeout);
            check(linkUp,
                "split mode: the game process's ChannelLinkService dials the channel process's link server and the link comes up");
            check(await channel.WaitForOutputAsync("Game process registered from", WaitTimeout),
                "split mode: the channel process logs the game's registration with the decoded ids");

            // /api/status 既有字段不回归 + 新增 channelLink（I4 门槛）。
            var loginResponse = await http.PostAsync(
                $"http://127.0.0.1:{ports.AdminPort}/api/auth/login",
                new StringContent(
                    """{"userName":"admin","password":"bosspass"}""",
                    Encoding.UTF8,
                    "application/json"));
            check(loginResponse.IsSuccessStatusCode,
                "admin login succeeds against the real server (bootstrap promotion at startup worked)");
            if (!loginResponse.IsSuccessStatusCode)
            {
                return;
            }

            using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
            var token = loginDoc.RootElement.GetProperty("token").GetString();
            check(!string.IsNullOrEmpty(token), "the login response carries a non-empty bearer token");

            // 用一次真实的入口连接，把 channel 侧的会话计数推到 >= 1：这样 channelLink.totalConnections
            // 的合并才不是「两边都 0 的巧合」，而是一条真的跨进程观测（客户端连 Channel 入口 → channel 记
            // TotalSessions → STATUS 报给 game → 投影合进 /api/status）。
            await TouchTcpAsync(ports.EntrancePort);

            var merged = await PollAsync(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{ports.AdminPort}/api/status");
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                var response = await http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                return root.TryGetProperty("channelLink", out var link)
                    && link.GetProperty("connected").GetBoolean()
                    && link.GetProperty("totalConnections").GetInt64() >= 1;
            }, WaitTimeout);
            check(merged,
                "/api/status: after a real entrance connection, channelLink.connected is true and "
                + "channelLink.totalConnections >= 1 (the channel-side counter crossed the process boundary into admin)");

            var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{ports.AdminPort}/api/status");
            statusRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var statusResponse = await http.SendAsync(statusRequest);
            check(statusResponse.IsSuccessStatusCode,
                "/api/status accepts the bearer token (no 401/403)");
            if (!statusResponse.IsSuccessStatusCode)
            {
                return;
            }

            using var statusDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
            var statusRoot = statusDoc.RootElement;
            check(statusRoot.TryGetProperty("totalConnections", out _) && statusRoot.TryGetProperty("totalPackets", out _),
                "/api/status: existing numeric fields (totalConnections, totalPackets) are still present — no regression");
            check(statusRoot.TryGetProperty("activeSessions", out _) && statusRoot.TryGetProperty("entrance", out _),
                "/api/status: existing 'activeSessions' and 'entrance' fields are still present — no regression");
        }
        finally
        {
            await server.StopAndWaitAsync(ExitTimeout);
            await channel.StopAndWaitAsync(ExitTimeout);
            TryDeleteDirectory(seedDir);
        }

        check(CanBind(ports.AdminPort) && CanBind(ports.LinkPort),
            "the link-admin e2e leaves no listeners behind on the admin or link port");
    }

    /// <summary>对 loopback 端口做一次「连上就关」，让对端的会话计数 +1（不读不写任何帧）。</summary>
    private static async Task TouchTcpAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
        {
            // 连不上就静默：断言不靠这一下成功，靠后面的轮询。
        }
    }

    private static async Task<bool> PollAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return predicate();
    }

    private static async Task<bool> PollAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return await predicate();
    }

    private static async Task<(bool Completed, Exception? Fault)> AwaitQuietlyAsync(Task task, TimeSpan timeout)
    {
        var finished = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(finished, task))
        {
            return (false, null);
        }

        try
        {
            await task;
            return (true, null);
        }
        catch (Exception exception)
        {
            return (true, exception);
        }
    }

    /// <summary>
    /// C5-READ/2：宿主必须拿到配置里的 MaximumPacketLength / RejectBadCrc32（noDelay 是常量）。
    /// 用一个非默认值的 channel 进程验证它们在真实装配路径上生效 —— 只直接 new 宿主证明不了这一层。
    /// </summary>
    private static async Task RunReadParametersAsync(Action<bool, string> check)
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            check(false, "the repository root is reachable for the read-parameter fixture");
            return;
        }

        var channelExe = Path.Combine(repositoryRoot, "src", "DFLegacy.Channel", "bin",
            DetectBuildConfiguration(), "net10.0", "DFLegacy.Channel.exe");
        var pvfPath = Path.Combine(repositoryRoot, "DNF2008", "Script.pvf");
        if (!File.Exists(channelExe) || !File.Exists(pvfPath))
        {
            check(false, $"the read-parameter fixture needs {channelExe} and {pvfPath}");
            return;
        }

        var ports = FixturePorts.Allocate();
        var scratch = Path.Combine(Path.GetTempPath(), "dflegacy-channel-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var environment = WithTopology(BuildEnvironment(pvfPath, scratch, ports), channelEnabled: true, gameBindsEntrance: false);
            environment["DFLegacy__MaximumPacketLength"] = "64";
            environment["DFLegacy__RejectBadCrc32"] = "true";

            using var channel = ChildProcess.Start(channelExe, Path.GetDirectoryName(channelExe)!, environment);
            var ready = await channel.WaitForOutputAsync(
                $"Entrance listener ready at 127.0.0.1:{ports.EntrancePort}", ReadinessTimeout);
            ReportUnready(check, "read-parameter channel", ready, channel);
            if (!ready)
            {
                return;
            }

            // 正向对照：19 字节载荷的 11（43 字节帧）在 64 字节上限内且 CRC 正确 ⇒ 必须被回 12。
            var accepted = await TryExchangeAsync(ports.EntrancePort, BuildFrame(11, new byte[32], badCrc: false), ReplyTimeout);
            check(accepted is { ProtocolId: 12 },
                "with MaximumPacketLength=64 a normal 43-byte protocol 11 frame is still answered (the two negatives below are not 'server is dead')");

            // 坏 CRC：RejectBadCrc32=true 时必须无回包并断会话。
            var rejected = await TryExchangeAsync(ports.EntrancePort, BuildFrame(11, new byte[32], badCrc: true), ReplyTimeout);
            check(rejected is null,
                "with RejectBadCrc32=true a wrong-CRC32 frame is not answered (the configured flag reached the host, not just the model)");

            // 超上限：111 字节帧 > 64 ⇒ 读循环抛错、会话结束、无回包。
            var oversized = await TryExchangeAsync(ports.EntrancePort, BuildFrame(11, new byte[100], badCrc: false), ReplyTimeout);
            check(oversized is null,
                "with MaximumPacketLength=64 a 111-byte frame is dropped without a reply (the configured cap reached the host's read loop)");

            var stopped = await channel.StopAndWaitAsync(ExitTimeout);
            check(stopped,
                "the read-parameter channel process has exited within the timeout after a forced termination "
                + "(a graceful, signal-driven stop is NOT covered by this wave and stays recorded as open)");
        }
        finally
        {
            TryDeleteDirectory(scratch);
        }
    }

    /// <summary>
    /// C5-CONFIG / C5-LIFE 的守卫与失败路径：不绑端口时必须说明原因；拓扑非法时在 bind 之前失败；
    /// 第二个端口绑定失败时进程以非零码退出且不留下监听。
    /// </summary>
    /// <remarks>
    /// 「已经绑上的第一个端口在失败路径上被 Stop 释放」这一条<b>只能靠 code review 兜</b>：进程一退出，
    /// 操作系统就会释放它的监听，因此从进程外面无法区分「finally 里 Stop 过」和「进程死了」。
    /// 这里只断言可观察的部分（非零退出、第一口确实曾经绑上、退出后端口可用）。
    /// </remarks>
    private static async Task RunTopologyGuardsAsync(Action<bool, string> check)
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            check(false, "the repository root is reachable for the topology-guard fixtures");
            return;
        }

        var channelExe = Path.Combine(repositoryRoot, "src", "DFLegacy.Channel", "bin",
            DetectBuildConfiguration(), "net10.0", "DFLegacy.Channel.exe");
        if (!File.Exists(channelExe))
        {
            check(false, $"the topology-guard fixtures need {channelExe}");
            return;
        }

        var ports = FixturePorts.Allocate();
        var scratch = Path.Combine(Path.GetTempPath(), "dflegacy-channel-guards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var environment = WithTopology(
                BuildEnvironment(Path.Combine(repositoryRoot, "DNF2008", "Script.pvf"), scratch, ports),
                channelEnabled: true,
                gameBindsEntrance: false);

            // 1) Processes.Channel.Enabled=false：入口口归 game 进程，本进程不绑并说清原因。
            var disabledChannel = new Dictionary<string, string>(environment)
            {
                ["DFLegacy__Processes__Channel__Enabled"] = "false",
                ["DFLegacy__Processes__Game__EntranceBinding"] = "true",
            };
            var (exitCode, output) = await RunToCompletionAsync(
                channelExe, Path.GetDirectoryName(channelExe)!, disabledChannel);
            check(exitCode == 0
                    && output.Contains("Processes.Channel.Enabled=false", StringComparison.Ordinal)
                    && !output.Contains("Entrance listener ready", StringComparison.Ordinal),
                "Channel.Enabled=false: the channel process binds nothing, explains why, and exits 0");

            // 2) Entrance.Enabled=false：同样不绑并说明原因。
            var disabledEntrance = new Dictionary<string, string>(environment)
            {
                ["DFLegacy__Entrance__Enabled"] = "false",
            };
            (exitCode, output) = await RunToCompletionAsync(
                channelExe, Path.GetDirectoryName(channelExe)!, disabledEntrance);
            check(exitCode == 0
                    && output.Contains("Entrance.Enabled=false", StringComparison.Ordinal)
                    && !output.Contains("Entrance listener ready", StringComparison.Ordinal),
                "Entrance.Enabled=false: the channel process binds nothing, explains why, and exits 0");

            // 3) 拓扑非法：必须在 bind 之前失败。
            var invalidTopology = new Dictionary<string, string>(environment)
            {
                ["DFLegacy__Processes__Channel__Enabled"] = "true",
                ["DFLegacy__Processes__Game__EntranceBinding"] = "true",
            };
            (exitCode, output) = await RunToCompletionAsync(
                channelExe, Path.GetDirectoryName(channelExe)!, invalidTopology);
            check(exitCode == 2
                    && output.Contains("Invalid DFLegacy.Channel configuration", StringComparison.Ordinal)
                    && output.Contains("Processes.Game.EntranceBinding=false", StringComparison.Ordinal)
                    && !output.Contains("Entrance listener ready", StringComparison.Ordinal),
                "a split layout that leaves the game binding the entrance ports is rejected before any bind (exit code 2, reason logged, no listener started)");

            // 4) 第二个端口绑定失败：第一口确实已经绑上，进程以非零码结束且不留下监听。
            using (var occupied = new TcpListener(IPAddress.Loopback, ports.AdditionalPort))
            {
                occupied.Start();
                (exitCode, output) = await RunToCompletionAsync(
                    channelExe, Path.GetDirectoryName(channelExe)!, environment);
                check(exitCode == 1 && output.Contains("stopped after an error", StringComparison.Ordinal),
                    "a second-port bind failure ends the channel process with a non-zero exit code instead of hanging");
                check(output.Contains($"Entrance listener ready at 127.0.0.1:{ports.EntrancePort}", StringComparison.Ordinal),
                    "the first entrance port really had been bound before the second one failed");
                occupied.Stop();
            }

            check(CanBind(ports.EntrancePort) && CanBind(ports.AdditionalPort),
                "no listener is left behind after the failed start: both entrance ports are bindable again");
        }
        finally
        {
            TryDeleteDirectory(scratch);
        }
    }

    // ------------------------------------------------------------- 握手客户端

    private sealed record HandshakeExchange(
        byte[] KeyMaterial,
        byte[] KeyReplyBody,
        byte[] CachePlaintext,
        byte[] Script,
        byte[] Metadata);

    /// <summary>
    /// 一个测试客户端：11 → 5 → 9 → 1，并用本会话的密钥解码 6/10/3。
    /// 每个回包都校验线路结构（type、协议号、声明长度、记录数、CRC 字段），
    /// 结构不对时按名字失败而不是抛。
    /// </summary>
    private static async Task<HandshakeExchange> TryRunHandshakeAsync(
        int port,
        Action<bool, string> check,
        string label)
    {
        using var client = new TcpClient();
        try
        {
            using var connectTimeout = new CancellationTokenSource(ReplyTimeout);
            await client.ConnectAsync(IPAddress.Loopback, port, connectTimeout.Token);
            var stream = client.GetStream();

            var keyReply = await ExchangeAsync(stream, 11, new byte[32], 12);
            if (keyReply is null)
            {
                check(false, $"{label}: protocol 11 is answered by protocol 12");
                return EmptyExchange();
            }

            check(keyReply.DeclaredLength == PacketFrame.HeaderLength + keyReply.Body.Length
                    && keyReply.RecordCount == 1
                    && keyReply.DeclaredCrc32 == 0,
                $"{label}: protocol 12 is a well-formed frame (declared length matches the body, recordCount=1, no CRC32)");
            check(keyReply.Body.Length == 37,
                $"{label}: protocol 12 carries the 36-byte key payload plus the record count byte");
            if (keyReply.Body.Length != 37)
            {
                return EmptyExchange();
            }

            // Body[0] 是记录数，后面是 int32(0) + 32 字节密钥材料。
            var keyMaterial = keyReply.Body[5..37];
            var cacheReply = await ExchangeAsync(stream, 5, new byte[16], 6);
            check(cacheReply is { ProtocolId: 6 },
                $"{label}: protocol 5 is answered by protocol 6");
            var scriptReply = await ExchangeAsync(stream, 9, [], 10);
            check(scriptReply is { ProtocolId: 10 },
                $"{label}: protocol 9 is answered by protocol 10");
            var metadataReply = await ExchangeAsync(stream, 1, [], 3);
            check(metadataReply is { ProtocolId: 3 },
                $"{label}: protocol 1 is answered by protocol 3");

            foreach (var (name, reply) in new[]
                     {
                         ("6", cacheReply), ("10", scriptReply), ("3", metadataReply),
                     })
            {
                check(reply is not null
                        && reply.DeclaredLength == PacketFrame.HeaderLength + reply.Body.Length
                        && reply.RecordCount == 1
                        && reply.DeclaredCrc32 == 0,
                    $"{label}: protocol {name} is a well-formed frame (declared length matches the body, recordCount=1, no CRC32)");
            }

            if (cacheReply is null || scriptReply is null || metadataReply is null)
            {
                return EmptyExchange();
            }

            return new HandshakeExchange(
                keyMaterial,
                keyReply.Body,
                EntranceCrypto.DecryptBlocks(cacheReply.Body[1..], keyMaterial),
                EntranceCrypto.DecryptBlocks(EntranceCrypto.Decompress(scriptReply.Body[1..]), keyMaterial),
                EntranceCrypto.DecryptBlocks(EntranceCrypto.Decompress(metadataReply.Body[1..]), keyMaterial));
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
        {
            check(false, $"{label}: the handshake did not complete ({exception.GetType().Name}: {exception.Message})");
            return EmptyExchange();
        }
    }

    /// <summary>
    /// 探一个端口：先确认它**接得上**（能区分「有监听但不是握手引擎」和「根本没监听」），
    /// 再发一个协议帧等指定协议号的回包。
    /// </summary>
    /// <remarks>
    /// 只有 <see cref="ProbeResult.Connected"/> 为真时「没有协议 12 回包」才有意义；否则连接被拒
    /// 也会得到 null，那样一条「握手引擎不服务该端口」的断言就变成了无牙断言。
    /// </remarks>
    private static async Task<ProbeResult> ProbeProtocolAsync(int port, byte protocolId, byte replyProtocolId)
    {
        using var client = new TcpClient();
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, connectTimeout.Token);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
        {
            return new ProbeResult(false, null);
        }

        var stream = client.GetStream();
        try
        {
            await stream.WriteAsync(BuildFrame(protocolId, new byte[32], badCrc: false));
            await stream.FlushAsync();
            var reply = await TryReadFrameAsync(stream, TimeSpan.FromSeconds(3));
            return new ProbeResult(true, reply is not null && reply.ProtocolId == replyProtocolId ? reply : null);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
        {
            // 连上了但对端立刻关掉/重置：仍然算「接了连接、没给这个协议的回包」。
            return new ProbeResult(true, null);
        }
    }

    private sealed record ProbeResult(bool Connected, WireFrame? Reply);

    private static async Task<WireFrame?> ExchangeAsync(
        NetworkStream stream,
        byte protocolId,
        byte[] payload,
        byte expectedReplyProtocolId)
    {
        await stream.WriteAsync(BuildFrame(protocolId, payload, badCrc: false));
        await stream.FlushAsync();
        var reply = await TryReadFrameAsync(stream, ReplyTimeout);
        return reply is not null && reply.ProtocolId == expectedReplyProtocolId ? reply : null;
    }

    /// <summary>
    /// 连上去发一帧再等回包。连接/写入失败也吞成 null：调用点会把它变成一条具名 FAIL，
    /// 而不是让未处理异常把整套 smoke 打断。
    /// </summary>
    private static async Task<WireFrame?> TryExchangeAsync(int port, byte[] frame, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            using var connectTimeout = new CancellationTokenSource(timeout);
            await client.ConnectAsync(IPAddress.Loopback, port, connectTimeout.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(frame);
            await stream.FlushAsync();
            return await TryReadFrameAsync(stream, timeout);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
        {
            return null;
        }
    }

    private sealed record WireFrame(byte Type, byte ProtocolId, uint DeclaredLength, uint DeclaredCrc32, byte[] Body)
    {
        public byte RecordCount => Body.Length == 0 ? (byte)0 : Body[0];
    }

    /// <summary>
    /// 读一个 10 字节头 + 声明长度的载荷；超时返回 null，对端关闭/重置也返回 null。
    /// 不直接用 PacketFrame.ReadAsync 是为了拿到「线路声明的长度」本身做结构断言。
    /// </summary>
    private static async Task<WireFrame?> TryReadFrameAsync(NetworkStream stream, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            var header = new byte[PacketFrame.HeaderLength];
            if (!await TryReadExactAsync(stream, header, timeoutSource.Token))
            {
                return null;
            }

            var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));
            if (declaredLength < PacketFrame.HeaderLength || declaredLength > PacketFrame.DefaultMaximumLength)
            {
                return new WireFrame(header[0], header[1], declaredLength,
                    BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(6, 4)), []);
            }

            var body = new byte[declaredLength - PacketFrame.HeaderLength];
            if (body.Length > 0 && !await TryReadExactAsync(stream, body, timeoutSource.Token))
            {
                return null;
            }

            return new WireFrame(header[0], header[1], declaredLength,
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(6, 4)), body);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static async Task<bool> TryReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    /// <summary>构一个 10 字节头 + 记录数 + 载荷的帧；badCrc 时把声明的 CRC32 打反。</summary>
    private static byte[] BuildFrame(byte protocolId, byte[] payload, bool badCrc)
    {
        var body = new byte[payload.Length + 1];
        body[0] = 1;
        payload.CopyTo(body, 1);
        var crc = badCrc ? Crc32.Compute(body) ^ 0xFFFFFFFFu : 0u;
        var frame = new byte[PacketFrame.HeaderLength + body.Length];
        frame[0] = 0;
        frame[1] = protocolId;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2, 4), (uint)frame.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(6, 4), crc);
        body.CopyTo(frame, PacketFrame.HeaderLength);
        return frame;
    }

    private static HandshakeExchange EmptyExchange() => new([], [], [], [], []);

    /// <summary>
    /// 解密后的明文是 AES 分组的整倍数（EntranceCrypto.EncryptBlocks 的既有行为），所以期待值之后
    /// 只能有零填充。返回 null 表示匹配，否则返回一句可以直接贴进检查名的差异说明。
    /// </summary>
    private static string? DescribePadding(byte[] actual, byte[] expected)
    {
        var paddedLength = ((expected.Length + EntranceCrypto.BlockSize - 1) / EntranceCrypto.BlockSize)
            * EntranceCrypto.BlockSize;
        if (actual.Length != paddedLength)
        {
            return $"; decoded {actual.Length} bytes, expected {expected.Length} plus AES padding = {paddedLength}";
        }

        if (!actual.AsSpan(0, expected.Length).SequenceEqual(expected))
        {
            return "; the decoded payload differs from the expected bytes";
        }

        return actual.AsSpan(expected.Length).IndexOfAnyExcept((byte)0) >= 0
            ? "; the AES padding after the payload is not all zero"
            : null;
    }

    // ------------------------------------------------------------------ 夹具

    private sealed record FixturePorts(
        int EntrancePort,
        int AdditionalPort,
        int ChannelPort,
        int LinkPort,
        int AdminPort,
        int CharacterDatagramPort,
        int UdpPrimaryPort,
        int UdpSecondaryPort,
        int ProbePort)
    {
        public static FixturePorts Allocate()
        {
            // bind(0) 只保证「那一刻空闲」；端口之间也可能撞车（撞车会以难读的绑定/路由错误变红）。
            // 所以分配后去重，最多重试若干轮，并在耗尽时抛清楚的话。
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var candidate = new FixturePorts(
                    GetFreePort(), GetFreePort(), GetFreePort(), GetFreePort(), GetFreePort(),
                    GetFreePort(), GetFreePort(), GetFreePort(), GetFreePort());
                var values = new[]
                {
                    candidate.EntrancePort, candidate.AdditionalPort, candidate.ChannelPort, candidate.LinkPort,
                    candidate.AdminPort, candidate.CharacterDatagramPort,
                    candidate.UdpPrimaryPort, candidate.UdpSecondaryPort, candidate.ProbePort,
                };
                if (values.Distinct().Count() == values.Length)
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Could not allocate nine distinct fixture ports.");
        }
    }

    /// <summary>
    /// 与 server.json 同源的覆盖：全部走 DFLegacy__... 环境变量，先于 JSON 之后的提供器生效，
    /// 因此这一份夹具同时验证了「环境覆盖在 JSON 之后」这条真实装配路径。
    /// </summary>
    private static Dictionary<string, string> BuildEnvironment(string pvfPath, string scratchDirectory, FixturePorts ports) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["DFLegacy__Entrance__Enabled"] = "true",
            ["DFLegacy__Entrance__Host"] = "127.0.0.1",
            ["DFLegacy__Entrance__Port"] = ports.EntrancePort.ToString(),
            ["DFLegacy__Entrance__AdditionalPorts__0"] = ports.ChannelPort.ToString(),
            ["DFLegacy__Entrance__AdditionalPorts__1"] = ports.AdditionalPort.ToString(),
            ["DFLegacy__Channel__Name"] = ChannelName,
            ["DFLegacy__Channel__Host"] = "127.0.0.1",
            ["DFLegacy__Channel__Port"] = ports.ChannelPort.ToString(),
            ["DFLegacy__Channel__ChannelNumber"] = ChannelNumber.ToString(),
            ["DFLegacy__Channel__MaximumUsers"] = ChannelMaximumUsers.ToString(),
            ["DFLegacy__Channel__CurrentUsers"] = ChannelCurrentUsers.ToString(),
            ["DFLegacy__Processes__Channel__LinkPort"] = ports.LinkPort.ToString(),
            // AdvertisedHost 故意不设：缺省空串 ⇒ 必须回退到 Host（C5-HOST/2 的第一步）。
            ["DFLegacy__GameplayDatagram__Host"] = GameplayProbeHost,
            ["DFLegacy__GameplayDatagram__PrimaryPort"] = ports.UdpPrimaryPort.ToString(),
            ["DFLegacy__GameplayDatagram__SecondaryPort"] = ports.UdpSecondaryPort.ToString(),
            ["DFLegacy__CharacterDatagram__Enabled"] = "false",
            ["DFLegacy__CharacterDatagram__Host"] = "127.0.0.1",
            ["DFLegacy__CharacterDatagram__Port"] = ports.CharacterDatagramPort.ToString(),
            ["DFLegacy__GameProbe__Enabled"] = "false",
            ["DFLegacy__GameProbe__Port"] = ports.ProbePort.ToString(),
            ["DFLegacy__Admin__Host"] = "127.0.0.1",
            ["DFLegacy__Admin__Port"] = ports.AdminPort.ToString(),
            ["DFLegacy__DataPath"] = Path.Combine(scratchDirectory, "state.json"),
            ["DFLegacy__ScriptPvfPath"] = pvfPath,
            ["DFLegacy__SkillScriptPath"] = "",
            ["DFLegacy__MaximumPacketLength"] = PacketFrame.DefaultMaximumLength.ToString(),
            ["DFLegacy__RejectBadCrc32"] = "false",
            ["DFLegacy__EnablePacketTracing"] = "false",
        };

    private static Dictionary<string, string> WithTopology(
        IReadOnlyDictionary<string, string> environment,
        bool channelEnabled,
        bool gameBindsEntrance)
    {
        var copy = new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase)
        {
            ["DFLegacy__Processes__Channel__Enabled"] = channelEnabled ? "true" : "false",
            ["DFLegacy__Processes__Game__EntranceBinding"] = gameBindsEntrance ? "true" : "false",
        };
        return copy;
    }

    private static ChannelRoleOptions ReadChannelOptionsFrom(
        string json,
        params (string Key, string Value)[] overrides)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        if (overrides.Length > 0)
        {
            builder.AddInMemoryCollection(
                overrides.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));
        }

        return ChannelRoleOptions.Read(builder.Build().GetSection("DFLegacy"));
    }

    private static async Task<bool> WaitForAllAsync(ChildProcess process, TimeSpan timeout, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (!await process.WaitForOutputAsync(needle, timeout))
            {
                return false;
            }
        }

        return true;
    }

    private static void ReportUnready(
        Action<bool, string> check,
        string role,
        bool ready,
        ChildProcess process)
    {
        check(ready, $"{role} becomes ready within {ReadinessTimeout.TotalSeconds:0}s");
        if (ready)
        {
            return;
        }

        Console.Error.WriteLine($"--- {role} output (tail) ---");
        foreach (var line in process.Output.Split('\n').TakeLast(30))
        {
            Console.Error.WriteLine(line.TrimEnd());
        }

        if (process.HasExited)
        {
            Console.Error.WriteLine($"--- {role} exited with code {process.ExitCode} ---");
        }
    }

    private static async Task<(int ExitCode, string Output)> RunToCompletionAsync(
        string exePath,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        using var process = ChildProcess.Start(exePath, workingDirectory, environment);
        var exited = await process.WaitForExitAsync(ExitTimeout);
        return (exited ? process.ExitCode : -1, process.Output);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static bool CanBind(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DFLegacy.Emulator.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>从 <c>...\bin\Release\net10.0\</c> 反推构建配置，夹具才能找到同配置的两个 exe。</summary>
    private static string DetectBuildConfiguration()
    {
        var segments = AppContext.BaseDirectory
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindLastIndex(segments, segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < segments.Length ? segments[index + 1] : "Release";
    }

    private static void WritePvfWithoutEntries(string path)
    {
        using var stream = File.Create(path);
        stream.Write("DNF_SCRIPT_PACK"u8);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(header[..4], 4);   // treeLength：一个空表也要 4 字节
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), 0);  // fileCount = 0
        stream.Write(header);
        stream.Write(new byte[4]);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>子进程 + 输出聚合。只用于本波自己拉起来的进程，Dispose 一定回收。</summary>
    private sealed class ChildProcess : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new();
        private readonly object _gate = new();

        private ChildProcess(Process process) => _process = process;

        public string Output
        {
            get
            {
                lock (_gate)
                {
                    return _output.ToString();
                }
            }
        }

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public static ChildProcess Start(
            string exePath,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment)
        {
            var startInfo = new ProcessStartInfo(exePath)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // 开发机上可能已经有 DFLegacy__... 覆盖；先全删掉，避免混进夹具（只影响我们拉起的子进程）。
            var inherited = startInfo.Environment.Keys
                .Where(key => key.StartsWith("DFLegacy__", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var key in inherited)
            {
                startInfo.Environment.Remove(key);
            }

            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }

            var process = new Process { StartInfo = startInfo };
            var child = new ChildProcess(process);
            process.OutputDataReceived += (_, args) => child.Append(args.Data);
            process.ErrorDataReceived += (_, args) => child.Append(args.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return child;
        }

        public async Task<bool> WaitForOutputAsync(string needle, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Output.Contains(needle, StringComparison.Ordinal))
                {
                    return true;
                }

                if (_process.HasExited)
                {
                    // 进程提前退出：等输出泵把尾巴刷完再判定，否则会漏掉最后几行。
                    await Task.Delay(200);
                    return Output.Contains(needle, StringComparison.Ordinal);
                }

                await Task.Delay(50);
            }

            return Output.Contains(needle, StringComparison.Ordinal);
        }

        public async Task<bool> WaitForExitAsync(TimeSpan timeout)
        {
            if (_process.HasExited)
            {
                _process.WaitForExit();
                return true;
            }

            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                await _process.WaitForExitAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            _process.WaitForExit();
            return true;
        }

        public async Task<bool> StopAndWaitAsync(TimeSpan timeout)
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // 已经退出了。
                }
            }

            return await WaitForExitAsync(timeout);
        }

        private void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                _output.AppendLine(line);
            }
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
                catch (InvalidOperationException)
                {
                }
            }

            _process.Dispose();
        }
    }
}
