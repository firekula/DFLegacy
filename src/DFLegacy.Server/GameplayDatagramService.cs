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
    private UdpClient? _primary;
    private UdpClient? _secondary;

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
        }
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
