using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed class GameplayDatagramService(
    ServerOptions options,
    RuntimeState runtime,
    ILogger<GameplayDatagramService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, GameplaySession> _sessions = new();
    private readonly ConcurrentDictionary<string, IPEndPoint> _observedByAddress = new();
    private readonly ConcurrentDictionary<string, Guid> _sessionsByEndpoint = new();

    // 临时审计（DFLEGACY_UDP_AUDIT=1）：只加日志、不发包、不改协议。
    // 目的：判定客户端把游戏 UDP 发到哪里去了——是发给对端（NOTI 11 的地址）还是发给服务端
    // （即官方架构里"relay 8101-8300"的角色由频道服承担）。这决定"同队进副本"三件老大难
    // （对端角色载体、怪物同步、结算期队员等待）要不要在服务端做 UDP 转发。判定完即删。
    private readonly bool _auditEnabled =
        Environment.GetEnvironmentVariable("DFLEGACY_UDP_AUDIT") == "1";
    private readonly ConcurrentDictionary<string, UdpAudit> _audit = new();

    // 玩法 UDP 中继（诊断开关 DFLEGACY_UDP_RELAY=1）：把 NOTI 11 里"对端的端点"改写成服务端自己的
    // 监听端点，并在收到玩法数据后转发给其它会话 —— 即官方架构里 relay（8101-8300）承担的角色。
    // 起因（2026-10-04 真机）：run #52 的 UDP 审计显示两个客户端之间**只在直连**
    // （100 秒内 `mt=0 protocols=-`，只发 NAT 探测与心跳），服务端既看不见内容也管不着；
    // 而队员端正是在队长放技能后崩在自己代码里（崩点 0x90149F，两轮逐寄存器一致）。
    private readonly bool _relayEnabled =
        Environment.GetEnvironmentVariable("DFLEGACY_UDP_RELAY") == "1";

    // 实验用："把所有对端端点指到 host:port"（优先级高于中继）。指到一个**我们收下就丢**的端口，
    // 就等于把客户端之间的直连**物理切断**，用来一刀判定"崩溃到底靠不靠对端数据"。
    private readonly string? _peerSink =
        Environment.GetEnvironmentVariable("DFLEGACY_UDP_PEER_SINK");
    private UdpClient? _primary;
    private UdpClient? _secondary;

    /// <summary>
    /// 交给客户端的"对端端点"改写目标：`DFLEGACY_UDP_PEER_SINK` 优先，否则中继模式下是
    /// 本服务主监听端点；都没有则 null（调用方原样透传真实端点）。
    /// </summary>
    public IPEndPoint? PeerEndpointOverride =>
        ResolvePeerEndpointOverride(options.GameplayDatagram, _relayEnabled, _peerSink);

    /// <summary>
    /// "对端端点改写"的解析（**纯函数**，单测直接钉）：
    /// * `peerSink` 非空 ⇒ **以它为准**；格式不对就返回 null（不改写），
    ///   **绝不退回中继模式** —— 否则"切断直连"实验会因为一个拼写错误悄悄变成中继实验，
    ///   而且看起来还"跑通了"（本仓最忌讳的静默失败）；
    /// * 否则 `relayEnabled` ⇒ 本服务主监听端点（`AdvertisedHost`/`PrimaryPort`）；
    /// * 都没有 ⇒ null。
    /// </summary>
    internal static IPEndPoint? ResolvePeerEndpointOverride(
        GameplayDatagramOptions datagramOptions,
        bool relayEnabled,
        string? peerSink)
    {
        if (!string.IsNullOrWhiteSpace(peerSink))
        {
            return TryParseEndpoint(peerSink, out var sink) ? sink : null;
        }

        return relayEnabled
            ? new IPEndPoint(
                IPAddress.Parse(datagramOptions.ResolveAdvertisedHost()),
                datagramOptions.PrimaryPort)
            : null;
    }

    private static bool TryParseEndpoint(string text, out IPEndPoint endpoint)
    {
        endpoint = null!;
        var separator = text.LastIndexOf(':');
        if (separator <= 0 || separator == text.Length - 1)
        {
            return false;
        }

        if (!IPAddress.TryParse(text[..separator], out var address)
            || !ushort.TryParse(text[(separator + 1)..], out var port)
            || port == 0)
        {
            return false;
        }

        endpoint = new IPEndPoint(address, port);
        return true;
    }

    /// <summary>
    /// 把一条对端记录改写成"指定的端点"。**收件人自己那条不改**（客户端靠它认出自己），
    /// 且**身份字段（UserId/AccountId/NatType/Mtu）一律原样保留** —— `NOTI 11` 同时决定
    /// 客户端给哪些 uid 建角色（见 `EntranceService` 城镇入场那段注释），动身份字段会波及角色创建。
    /// 纯函数：单测直接钉。
    /// </summary>
    internal static GamePeerInfo ApplyPeerEndpointOverride(
        GamePeerInfo peer,
        bool isSelf,
        IPEndPoint? overrideEndpoint)
    {
        if (isSelf || overrideEndpoint is null)
        {
            return peer;
        }

        return peer with
        {
            LocalAddress = overrideEndpoint.Address,
            PublicAddress = overrideEndpoint.Address,
            PublicPort = checked((ushort)overrideEndpoint.Port)
        };
    }

    /// <summary>
    /// 中继的转发目标：除发送者以外、所有**已登记端点**的会话，同一端点只发一次。
    /// 纯函数：单测直接钉"不回发给发送者 / 跳过没有端点的会话 / 同一端点不重发"。
    /// ⚠️ 当前**不按队伍过滤**（本机双客户端验证够用）；多队伍场景必须接队伍信息，否则串台
    /// —— 这正是它先做成诊断开关、没直接进 `server.json` 的原因。
    /// </summary>
    internal static IReadOnlyList<IPEndPoint> SelectRelayTargets(
        IEnumerable<(Guid Key, IPEndPoint? Endpoint)> sessions,
        Guid? senderKey,
        IPEndPoint sender)
    {
        List<IPEndPoint> targets = [];
        var seen = new HashSet<string>(StringComparer.Ordinal) { EndpointKey(sender) };
        foreach (var (key, endpoint) in sessions)
        {
            if (endpoint is null || (senderKey is { } skip && key == skip))
            {
                continue;
            }

            if (!seen.Add(EndpointKey(endpoint)))
            {
                continue;
            }

            targets.Add(endpoint);
        }

        return targets;
    }

    public void RegisterChannelSession(RuntimeSession runtimeSession, IPAddress tcpAddress)
    {
        ArgumentNullException.ThrowIfNull(runtimeSession);
        ArgumentNullException.ThrowIfNull(tcpAddress);
        _sessions[runtimeSession.Id] = new GameplaySession(
            runtimeSession,
            NormalizeAddress(tcpAddress));
    }

    public void UnregisterChannelSession(Guid sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
        {
            return;
        }

        lock (session.Gate)
        {
            if (session.DatagramEndpoint is not null)
            {
                _sessionsByEndpoint.TryRemove(
                    EndpointKey(session.DatagramEndpoint),
                    out _);
            }
        }
    }

    public void SetNatReport(Guid sessionId, GameNatReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return;
        }

        lock (session.Gate)
        {
            session.NatReport = report;
            var endpoint = SelectDatagramEndpoint(session, report);
            SetDatagramEndpoint(session, endpoint);
        }

        logger.LogInformation(
            "UDP endpoint registered for channel session {SessionId}: nat={NatType} local={LocalAddress} public={PublicAddress}:{PublicPort} mtu={Mtu} target={Target}",
            sessionId,
            report.NatType,
            report.LocalAddress,
            report.PublicAddress,
            report.PublicPort,
            report.Mtu,
            session.DatagramEndpoint);
    }

    public bool TryGetPeerInfo(
        Guid sessionId,
        ushort userId,
        uint accountId,
        out GamePeerInfo peer)
    {
        peer = null!;
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        lock (session.Gate)
        {
            if (session.NatReport is not { } report)
            {
                return false;
            }

            var publicPort = report.PublicPort;
            if (publicPort == 0 && session.DatagramEndpoint is not null)
            {
                publicPort = checked((ushort)session.DatagramEndpoint.Port);
            }

            peer = new GamePeerInfo(
                userId,
                report.LocalAddress.MapToIPv4(),
                report.PublicAddress.MapToIPv4(),
                publicPort,
                accountId,
                report.NatType,
                report.Mtu);
            return true;
        }
    }

    public async ValueTask<bool> SendSetActiveObjectHpAsync(
        Guid sessionId,
        ushort objectType,
        ushort objectId,
        uint hp,
        uint secondaryValue,
        byte senderPartyIndex,
        CancellationToken cancellationToken)
    {
        if (_primary is null
            || !_sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        IPEndPoint endpoint;
        uint sequence;
        lock (session.Gate)
        {
            if (session.DatagramEndpoint is null)
            {
                return false;
            }

            endpoint = session.DatagramEndpoint;
            sequence = session.NextSequence++;
        }

        var application = GameDatagramProtocol.CreateSetActiveObjectHpApplication(
            objectType,
            objectId,
            hp,
            secondaryValue);
        var datagram = GameDatagramProtocol.CreateUnreliableFrame(
            sequence,
            senderPartyIndex,
            flag: 0,
            application);
        await _primary.SendAsync(datagram, endpoint, cancellationToken);
        Interlocked.Increment(ref session.Runtime.SentPackets);
        if (options.EnablePacketTracing)
        {
            runtime.TraceDatagram(
                session.Runtime,
                "TX-UDP",
                datagram,
                options.HexDumpLimit,
                protocolId: (byte)GameDatagramProtocol.SetActiveObjectHpProtocol);
        }

        logger.LogInformation(
            "UDP protocol 12 sent to {Endpoint}: sequence={Sequence} sender={Sender} object={ObjectType}:{ObjectId} hp={Hp} secondary={Secondary}",
            endpoint,
            sequence,
            senderPartyIndex,
            objectType,
            objectId,
            hp,
            secondaryValue);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.GameplayDatagram.Enabled)
        {
            logger.LogWarning("Gameplay datagram listener is disabled.");
            return;
        }

        if (options.GameplayDatagram.PrimaryPort == options.GameplayDatagram.SecondaryPort)
        {
            throw new InvalidOperationException(
                "Gameplay datagram primary and secondary ports must differ.");
        }

        if (options.GameplayDatagram.SenderPartyIndex >= 8)
        {
            throw new InvalidOperationException(
                "Gameplay datagram sender party index must be between zero and seven.");
        }

        var address = IPAddress.Parse(options.GameplayDatagram.Host);
        var primaryEndpoint = new IPEndPoint(
            address,
            options.GameplayDatagram.PrimaryPort);
        var secondaryEndpoint = new IPEndPoint(
            address,
            options.GameplayDatagram.SecondaryPort);
        _primary = DualStackUdp.Bind(primaryEndpoint);
        _secondary = DualStackUdp.Bind(secondaryEndpoint);
        logger.LogInformation(
            "Gameplay UDP primary/secondary listeners ready at {Primary} and {Secondary}",
            primaryEndpoint,
            secondaryEndpoint);

        try
        {
            var loops = new List<Task>
            {
                ReceiveLoopAsync(_primary, "primary", stoppingToken),
                ReceiveLoopAsync(_secondary, "secondary", stoppingToken),
            };
            if (_auditEnabled)
            {
                logger.LogInformation(
                    "Gameplay UDP audit enabled (DFLEGACY_UDP_AUDIT=1): logging only, no forwarding.");
                loops.Add(AuditLoopAsync(stoppingToken));
            }

            if (_relayEnabled)
            {
                logger.LogInformation(
                    "Gameplay UDP relay enabled (DFLEGACY_UDP_RELAY=1): advertising {Endpoint} as every peer endpoint and forwarding gameplay datagrams between sessions.",
                    PeerEndpointOverride);
            }

            if (!string.IsNullOrWhiteSpace(_peerSink))
            {
                if (PeerEndpointOverride is { } sinkEndpoint)
                {
                    logger.LogWarning(
                        "Gameplay UDP peer sink enabled (DFLEGACY_UDP_PEER_SINK={Sink}): every peer endpoint is rewritten to {Endpoint}; direct client-to-client traffic is effectively cut off{Relay}.",
                        _peerSink,
                        sinkEndpoint,
                        _relayEnabled ? " (relay forwarding is ALSO on)" : " (datagrams are dropped)");
                }
                else
                {
                    logger.LogError(
                        "Gameplay UDP peer sink {Sink} is not a valid host:port; NO peer endpoint will be rewritten (the experiment would silently run unmodified).",
                        _peerSink);
                }
            }

            await Task.WhenAll(loops);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _primary.Dispose();
            _secondary.Dispose();
            _primary = null;
            _secondary = null;
            _sessionsByEndpoint.Clear();
            _observedByAddress.Clear();
            _sessions.Clear();
        }
    }

    private async Task ReceiveLoopAsync(
        UdpClient listener,
        string listenerName,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var received = await listener.ReceiveAsync(cancellationToken);
            var remote = received.RemoteEndPoint;
            var datagram = received.Buffer;

            if (GameDatagramProtocol.TryCreateNatProbeResponse(
                    datagram,
                    remote,
                    out var response))
            {
                ObserveEndpoint(remote);
                await listener.SendAsync(response, remote, cancellationToken);
                TraceDatagram(remote, datagram, "RX-UDP-NAT");
                TraceDatagram(remote, response, "TX-UDP-NAT", countReceived: false);
                Audit(remote, datagram, "nat-probe");
                logger.LogDebug(
                    "Handled gameplay UDP NAT probe {ProbeType} on {Listener} from {Remote}",
                    datagram[0],
                    listenerName,
                    remote);
                continue;
            }

            if (GameDatagramProtocol.IsNatKeepAlive(datagram))
            {
                ObserveEndpoint(remote);
                TraceDatagram(remote, datagram, "RX-UDP-NAT-KEEPALIVE");
                Audit(remote, datagram, "nat-keepalive");
                logger.LogDebug(
                    "Handled gameplay UDP NAT keepalive on {Listener} from {Remote}",
                    listenerName,
                    remote);
                continue;
            }

            TraceDatagram(remote, datagram, "RX-UDP");
            var frames = LogMtUdpBlocks(remote, datagram);
            Audit(remote, datagram, "mt", frames);
            if (_relayEnabled)
            {
                await RelayGameplayDatagramAsync(listener, remote, datagram, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 中继：把某个会话发来的玩法数据转给**其它**已登记端点的会话（诊断/补齐官服 relay 角色用）。
    /// 客户端已被 `NOTI 11` 告知"对端在服务端端点"，所以它发来的包目标是本服务，转发给对端即可。
    /// 转发失败只记日志、不打断接收循环（一个对端掉线不该拖垮整条通道）。
    /// </summary>
    private async ValueTask RelayGameplayDatagramAsync(
        UdpClient listener,
        IPEndPoint sender,
        byte[] datagram,
        CancellationToken cancellationToken)
    {
        var senderKey = FindSessionKeyByEndpoint(sender);
        List<(Guid Key, IPEndPoint? Endpoint)> snapshot = [];
        foreach (var (key, session) in _sessions)
        {
            lock (session.Gate)
            {
                snapshot.Add((key, session.DatagramEndpoint));
            }
        }

        var targets = SelectRelayTargets(snapshot, senderKey, sender);
        var forwarded = 0;
        foreach (var target in targets)
        {
            try
            {
                await listener.SendAsync(datagram, target, cancellationToken);
                forwarded++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Gameplay UDP relay could not forward {Bytes} byte(s) from {Sender} to {Target}.",
                    datagram.Length,
                    sender,
                    target);
            }
        }

        logger.LogInformation(
            "Gameplay UDP relay: forwarded {Bytes} byte(s) from {Sender} (session {Session}) to {Forwarded}/{Targets} peer endpoint(s).",
            datagram.Length,
            sender,
            senderKey?.ToString() ?? "unknown",
            forwarded,
            targets.Count);
    }

    /// <summary>按"已登记的玩法端点"反查会话（中继要据此排除发送者自己）。</summary>
    private Guid? FindSessionKeyByEndpoint(IPEndPoint endpoint)
    {
        var key = EndpointKey(endpoint);
        foreach (var (sessionKey, session) in _sessions)
        {
            lock (session.Gate)
            {
                if (session.DatagramEndpoint is { } registered
                    && string.Equals(EndpointKey(registered), key, StringComparison.Ordinal))
                {
                    return sessionKey;
                }
            }
        }

        return null;
    }

    private void ObserveEndpoint(IPEndPoint remote)
    {
        var addressKey = AddressKey(remote.Address);
        var observed = new IPEndPoint(NormalizeAddress(remote.Address), remote.Port);
        _observedByAddress[addressKey] = observed;

        foreach (var session in _sessions.Values)
        {
            lock (session.Gate)
            {
                if (!string.Equals(
                        AddressKey(session.TcpAddress),
                        addressKey,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                SetDatagramEndpoint(session, observed);
            }
        }
    }

    private IPEndPoint? SelectDatagramEndpoint(
        GameplaySession session,
        GameNatReport report)
    {
        if (_observedByAddress.TryGetValue(
                AddressKey(session.TcpAddress),
                out var observed))
        {
            return observed;
        }

        if (report.PublicPort == 0
            || report.PublicAddress.Equals(IPAddress.Any)
            || report.PublicAddress.Equals(IPAddress.None))
        {
            return null;
        }

        return new IPEndPoint(
            NormalizeAddress(report.PublicAddress),
            report.PublicPort);
    }

    private void SetDatagramEndpoint(
        GameplaySession session,
        IPEndPoint? endpoint)
    {
        if (session.DatagramEndpoint is not null)
        {
            _sessionsByEndpoint.TryRemove(
                EndpointKey(session.DatagramEndpoint),
                out _);
        }

        session.DatagramEndpoint = endpoint;
        if (endpoint is not null)
        {
            _sessionsByEndpoint[EndpointKey(endpoint)] = session.Runtime.Id;
        }
    }

    private void TraceDatagram(
        IPEndPoint remote,
        ReadOnlySpan<byte> datagram,
        string direction,
        bool countReceived = true)
    {
        if (!_sessionsByEndpoint.TryGetValue(
                EndpointKey(remote),
                out var sessionId)
            || !_sessions.TryGetValue(sessionId, out var session))
        {
            return;
        }

        if (countReceived)
        {
            runtime.CountPacket(session.Runtime);
        }
        else
        {
            Interlocked.Increment(ref session.Runtime.SentPackets);
        }

        if (options.EnablePacketTracing)
        {
            runtime.TraceDatagram(
                session.Runtime,
                direction,
                datagram,
                options.HexDumpLimit);
        }
    }

    private List<byte> LogMtUdpBlocks(IPEndPoint remote, ReadOnlySpan<byte> datagram)
    {
        var protocols = new List<byte>();
        var offset = 0;
        while (offset < datagram.Length)
        {
            if (!GameDatagramProtocol.TryReadBlock(
                    datagram[offset..],
                    out var frame,
                    out var consumed)
                || consumed <= 0)
            {
                logger.LogWarning(
                    "Ignored malformed gameplay UDP datagram from {Remote} at offset {Offset}; length={Length}",
                    remote,
                    offset,
                    datagram.Length);
                return protocols;
            }

            offset += consumed;
            if (frame is null)
            {
                continue;
            }

            if (GameDatagramProtocol.TryParseApplication(
                    frame.Application,
                    out var application))
            {
                protocols.Add((byte)application.ProtocolId);
                logger.LogDebug(
                    "RX gameplay UDP from {Remote}: block={BlockType} sequence={Sequence} sender={Sender} flag={Flag} protocol={ProtocolId} payload={PayloadLength}",
                    remote,
                    frame.BlockType,
                    frame.Sequence,
                    frame.SenderPartyIndex,
                    frame.Flag,
                    application.ProtocolId,
                    application.Payload.Length);
            }
            else
            {
                logger.LogWarning(
                    "Gameplay UDP application from {Remote} failed CRC or cipher validation.",
                    remote);
            }
        }

        return protocols;
    }

    private void Audit(
        IPEndPoint remote,
        ReadOnlySpan<byte> datagram,
        string kind,
        List<byte>? protocols = null)
    {
        if (!_auditEnabled)
        {
            return;
        }

        var entry = _audit.GetOrAdd(
            $"{remote.Address}:{remote.Port}",
            _ => new UdpAudit());
        entry.Record(kind, datagram, protocols);
    }

    private async Task AuditLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            foreach (var (key, entry) in _audit)
            {
                // **只在"自上次播报以来有新数据"时打**：否则早已静默的端点会被每 10 秒重复播报一次。
                // 2026-10-04 实测：一整夜刷出约 1.1 MB、约 720 行/小时，两个计数器冻在 7 与 18 不动，
                // 把真正的活动淹掉（排查时先被这堆噪声误导过一次）。
                var revision = entry.Revision;
                if (revision == entry.LastReportedRevision)
                {
                    continue;
                }

                entry.LastReportedRevision = revision;
                logger.LogInformation(
                    "UDP audit {Remote}: natProbe={Probe} keepAlive={KeepAlive} mt={Mt} mtBytes={MtBytes} protocols={Protocols} firstPeek={Peek}",
                    key,
                    entry.NatProbes,
                    entry.KeepAlives,
                    entry.MtDatagrams,
                    entry.MtBytes,
                    entry.DescribeProtocols(),
                    entry.FirstPeek);
            }
        }
    }

    private sealed class UdpAudit
    {
        private readonly object _gate = new();
        private readonly Dictionary<byte, long> _protocols = [];
        private long _natProbes;
        private long _keepAlives;
        private long _mtDatagrams;
        private long _mtBytes;

        public string FirstPeek { get; private set; } = string.Empty;

        /// <summary>每次收到数据就 +1：审计循环据此判断"这一轮有没有新东西"。</summary>
        public long Revision { get; private set; }

        /// <summary>上一次播报时的 <see cref="Revision"/>（由审计循环写）。</summary>
        public long LastReportedRevision { get; set; }

        public long NatProbes { get { lock (_gate) { return _natProbes; } } }

        public long KeepAlives { get { lock (_gate) { return _keepAlives; } } }

        public long MtDatagrams { get { lock (_gate) { return _mtDatagrams; } } }

        public long MtBytes { get { lock (_gate) { return _mtBytes; } } }

        public void Record(string kind, ReadOnlySpan<byte> datagram, List<byte>? protocols)
        {
            lock (_gate)
            {
                switch (kind)
                {
                    case "nat-probe":
                        _natProbes++;
                        break;
                    case "nat-keepalive":
                        _keepAlives++;
                        break;
                    default:
                        _mtDatagrams++;
                        _mtBytes += datagram.Length;
                        if (protocols is not null)
                        {
                            foreach (var protocol in protocols)
                            {
                                _protocols[protocol] = _protocols.GetValueOrDefault(protocol) + 1;
                            }
                        }

                        break;
                }

                if (FirstPeek.Length == 0)
                {
                    var peek = datagram.Length > 24 ? datagram[..24] : datagram;
                    FirstPeek = $"{kind}:{Convert.ToHexString(peek)}";
                }

                Revision++;
            }
        }

        public string DescribeProtocols()
        {
            lock (_gate)
            {
                return _protocols.Count == 0
                    ? "-"
                    : string.Join(
                        ",",
                        _protocols.OrderByDescending(pair => pair.Value)
                            .Take(8)
                            .Select(pair => $"{pair.Key}x{pair.Value}"));
            }
        }
    }

    private static string AddressKey(IPAddress address) =>
        NormalizeAddress(address).ToString();

    private static string EndpointKey(IPEndPoint endpoint) =>
        $"{AddressKey(endpoint.Address)}:{endpoint.Port}";

    // An IPv4-mapped IPv6 peer (a real IPv4 client on a dual-stack socket) is the
    // same host as its IPv4 form and keeps collapsing exactly as before, so
    // existing IPv4 deployments are unaffected. A genuine IPv6 peer must stay
    // IPv6: MapToIPv4() keeps only the low 32 bits, which would collide distinct
    // peers onto one key and store a fabricated send target.
    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private sealed class GameplaySession(
        RuntimeSession runtimeSession,
        IPAddress tcpAddress)
    {
        public object Gate { get; } = new();
        public RuntimeSession Runtime { get; } = runtimeSession;
        public IPAddress TcpAddress { get; } = tcpAddress;
        public GameNatReport? NatReport { get; set; }
        public IPEndPoint? DatagramEndpoint { get; set; }
        public uint NextSequence { get; set; }
    }
}
