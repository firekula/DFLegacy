using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed partial class GameplayDatagramService(
    ServerOptions options,
    RuntimeState runtime,
    ILogger<GameplayDatagramService> logger,
    ListenerReadiness? readiness = null) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, GameplaySession> _sessions = new();

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
    /// 已通过场景筛选后的端点去重：排除发送者、未登记端点和重复端点。
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

    /// <summary>
    /// 玩法中继只连接同队、同场景的会话。城镇/选图使用空实例以便成队握手；
    /// 副本必须使用同一个实例对象（不能只比较可复用的 party id）。回城清实例后，
    /// 即使队伍关系仍保留，也不再收发留在副本中的成员的玩法包。
    /// 未登记队伍的会话默认隔离，NAT 探测和服务端直发不受影响。
    /// </summary>
    internal static IReadOnlyList<IPEndPoint> SelectRelayTargets(
        IEnumerable<(Guid Key, IPEndPoint? Endpoint, ushort? PartyId, object? DungeonInstance)> sessions,
        Guid? senderKey,
        IPEndPoint sender)
    {
        var snapshot = sessions.ToArray();
        var origin = snapshot.FirstOrDefault(session => session.Key == senderKey);
        if (senderKey is null || origin.PartyId is null)
        {
            return [];
        }

        return SelectRelayTargets(
            snapshot.Where(session => session.PartyId == origin.PartyId
                    && ReferenceEquals(session.DungeonInstance, origin.DungeonInstance))
                .Select(session => (session.Key, session.Endpoint)),
            senderKey,
            sender);
    }

    internal sealed record PvpRelayContext(Guid Attachment, Guid Room, Guid? Match);

    internal bool SetPvpContext(Guid sessionId, Guid attachment, Guid room, Guid? match)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return false;
        lock (session.Gate)
        {
            if (session.PvpContext is { } existing && existing.Attachment != attachment) return false;
            var next = new PvpRelayContext(attachment, room, match);
            if (session.PvpContext != next || session.RelayPartyId is not null
                || session.RelayDungeonInstance is not null) session.RelayContextRevision++;
            session.PvpContext = next;
            session.RelayPartyId = null; session.RelayDungeonInstance = null;
            return true;
        }
    }

    internal void ClearPvpContext(Guid sessionId, Guid attachment, Guid room)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;
        lock (session.Gate)
        {
            if (session.PvpContext is { } context && context.Attachment == attachment && context.Room == room)
            {
                session.PvpContext = null;
                session.RelayContextRevision++;
            }
        }
    }

    internal bool HasUniqueEndpoint(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return false;
        IPEndPoint? endpoint;
        lock (session.Gate) endpoint = session.DatagramEndpoint;
        return endpoint is not null && FindSessionKeyByEndpoint(endpoint) == sessionId;
    }

    internal IReadOnlyList<IPEndPoint> PvpRelayTargets(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var origin)) return [];
        PvpRelayContext? context; IPEndPoint? endpoint;
        lock (origin.Gate) { context = origin.PvpContext; endpoint = origin.DatagramEndpoint; }
        if (context is null || endpoint is null || FindSessionKeyByEndpoint(endpoint) != sessionId) return [];
        var candidates = new List<(Guid Key, IPEndPoint? Endpoint)>();
        foreach (var (key, session) in _sessions)
            lock (session.Gate)
                if (session.PvpContext is { } other && other.Room == context.Room && other.Match == context.Match
                    && session.DatagramEndpoint is { } target)
                    candidates.Add((key, target));
        return SelectRelayTargets(candidates.Where(c => c.Endpoint is { } target && FindSessionKeyByEndpoint(target) == c.Key), sessionId, endpoint);
    }

    public void SetRelayContext(Guid sessionId, ushort? partyId, object? dungeonInstance = null)
    {
        UpdateRelayContext(sessionId, partyId, dungeonInstance, updateParty: true, updateDungeon: true);
    }

    /// <summary>组队名册变化时更新队伍，不覆盖会话正在使用的副本实例。</summary>
    public void SetRelayParty(Guid sessionId, ushort? partyId)
    {
        UpdateRelayContext(sessionId, partyId, null, updateParty: true, updateDungeon: false);
    }

    /// <summary>进入/离开副本时更新实例，不覆盖名册维护的队伍；回城在下发城镇状态前传 null。</summary>
    public void SetRelayDungeonInstance(Guid sessionId, object? dungeonInstance)
    {
        UpdateRelayContext(sessionId, null, dungeonInstance, updateParty: false, updateDungeon: true);
    }

    private void UpdateRelayContext(
        Guid sessionId, ushort? partyId, object? dungeonInstance, bool updateParty, bool updateDungeon)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return;
        }

        ushort? currentParty;
        bool inDungeon;
        lock (session.Gate)
        {
            if (session.PvpContext is not null) return;
            var changed = (updateParty && session.RelayPartyId != partyId)
                || (updateDungeon && !ReferenceEquals(session.RelayDungeonInstance, dungeonInstance));
            if (!changed)
            {
                return;
            }

            if (updateParty) session.RelayPartyId = partyId;
            if (updateDungeon) session.RelayDungeonInstance = dungeonInstance;
            session.RelayContextRevision++;
            currentParty = session.RelayPartyId;
            inDungeon = session.RelayDungeonInstance is not null;
        }

        logger.LogInformation(
            "Gameplay UDP relay context updated for channel session {SessionId}: party={PartyId} dungeon={InDungeon}.",
            sessionId, currentParty, inDungeon);
    }

    public void RegisterChannelSession(RuntimeSession runtimeSession, IPAddress tcpAddress)
    {
        ArgumentNullException.ThrowIfNull(runtimeSession);
        ArgumentNullException.ThrowIfNull(tcpAddress);
        RevokeRelayAccount(runtimeSession.Id);
        _sessions[runtimeSession.Id] = new GameplaySession(runtimeSession, tcpAddress);
    }

    public void UnregisterChannelSession(Guid sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        RevokeRelayAccount(sessionId);
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
            session.DatagramEndpoint = SelectDatagramEndpoint(report);
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

        return TryGetPeerInfo(session, userId, accountId, out peer);
    }

    private static bool TryGetPeerInfo(GameplaySession session, ushort userId, uint accountId, out GamePeerInfo peer)
    {
        peer = null!;
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
            if (session.DatagramEndpoint is null || session.PvpContext is not null)
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
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        try
        {
            _primary = DualStackUdp.Bind(primaryEndpoint);
            _secondary = DualStackUdp.Bind(secondaryEndpoint);
            readiness?.MarkReady("gameplay");
            logger.LogInformation(
                "Gameplay UDP primary/secondary listeners ready at {Primary} and {Secondary}",
                primaryEndpoint,
                secondaryEndpoint);

            var loops = new List<Task>
            {
                OwnLoopAsync("primary", token => ReceiveLoopAsync(_primary, "primary", token), lifetime),
                OwnLoopAsync("secondary", token => ReceiveLoopAsync(_secondary, "secondary", token), lifetime),
            };
            if (_auditEnabled)
            {
                logger.LogInformation(
                    "Gameplay UDP audit enabled (DFLEGACY_UDP_AUDIT=1): logging only, no forwarding.");
                loops.Add(OwnLoopAsync("audit", AuditLoopAsync, lifetime));
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
            lifetime.Cancel();
            readiness?.MarkStopped("gameplay");
            _primary?.Dispose();
            _secondary?.Dispose();
            _primary = null;
            _secondary = null;
            _sessions.Clear();
        }
    }

    private async Task OwnLoopAsync(string listenerName, Func<CancellationToken, Task> loop,
        CancellationTokenSource lifetime)
    {
        try { await loop(lifetime.Token); }
        catch (Exception ex) when (lifetime.IsCancellationRequested
            && ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        catch (Exception ex)
        {
            // A failed listener must not leave its sibling or the audit timer
            // keeping WhenAll pending while readiness still advertises service.
            lifetime.Cancel();
            readiness?.MarkStopped("gameplay");
            _primary?.Dispose();
            _secondary?.Dispose();
            logger.LogError(ex, "Gameplay UDP {Listener} loop failed; stopping gameplay datagram listeners.", listenerName);
            throw;
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

            // The secondary reflector receives NAT keepalives. The primary
            // endpoint is advertised for gameplay: its ten-byte type-zero
            // selective ACKs must reach the peer, even when their remaining
            // bytes happen to match the reflector's keepalive signature.
            if (ReferenceEquals(listener, _secondary)
                && GameDatagramProtocol.IsNatKeepAlive(datagram))
            {
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
        if (senderKey is null)
        {
            logger.LogDebug(
                "Ignored gameplay UDP relay datagram from unregistered or ambiguous endpoint {Sender}.",
                sender);
            return;
        }
        List<(Guid Key, IPEndPoint? Endpoint, ushort? PartyId, object? DungeonInstance)> snapshot = [];
        foreach (var (key, session) in _sessions)
        {
            lock (session.Gate)
            {
                if (session.PvpContext is null) snapshot.Add((key, session.DatagramEndpoint, session.RelayPartyId, session.RelayDungeonInstance));
            }
        }

        var targets = PvpRelayTargets(senderKey.Value);
        if (targets.Count == 0) targets = SelectRelayTargets(snapshot, senderKey, sender);
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
        Guid? match = null;
        foreach (var (sessionKey, session) in _sessions)
        {
            lock (session.Gate)
            {
                if (session.DatagramEndpoint is { } registered
                    && string.Equals(EndpointKey(registered), key, StringComparison.Ordinal))
                {
                    // A shared or stale report must not attribute a packet to an arbitrary
                    // session. Only a unique complete IP:port identifies the sender.
                    if (match is not null)
                    {
                        return null;
                    }

                    match = sessionKey;
                }
            }
        }

        return match;
    }

    // CMD 2 arrives on the owning TCP session and supplies the complete reflected
    // endpoint. Anonymous UDP probes/keepalives cannot identify a session: learning
    // by IP makes two clients behind the same address overwrite each other.
    // A NAT rebind therefore requires another CMD 2, never an IP-only guess.
    private static IPEndPoint? SelectDatagramEndpoint(GameNatReport report)
    {
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

    private void TraceDatagram(
        IPEndPoint remote,
        ReadOnlySpan<byte> datagram,
        string direction,
        bool countReceived = true)
    {
        if (FindSessionKeyByEndpoint(remote) is not { } sessionId
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

    private sealed class GameplaySession(RuntimeSession runtimeSession, IPAddress tcpAddress)
    {
        public object Gate { get; } = new();
        public RuntimeSession Runtime { get; } = runtimeSession;
        public IPAddress TcpAddress { get; } = NormalizeAddress(tcpAddress);
        public uint AccountUid { get; set; }
        public GameNatReport? NatReport { get; set; }
        public IPEndPoint? DatagramEndpoint { get; set; }
        public PvpRelayContext? PvpContext { get; set; }
        public ushort? RelayPartyId { get; set; }
        public object? RelayDungeonInstance { get; set; }
        public long RelayContextRevision { get; set; }
        public uint NextSequence { get; set; }
    }
}
