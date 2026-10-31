using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed record FallbackRelayStatus(bool Enabled, bool Listening, int Connections, int PendingFrames,
    int RegisteredConnections, long TcpFramesForwarded, long UdpFramesForwarded, long DroppedFrames);

/// <summary>ACT1 native transport. The game session owns identity and party/PvP scope.</summary>
public sealed class FallbackRelayService(ServerOptions options, GameplayDatagramService contexts,
    ILogger<FallbackRelayService> logger, ListenerReadiness? readiness = null) : BackgroundService
{
    private const int MaximumConnections = 1000;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<Guid, RelayConnection> _connections = new();
    private readonly object _gate = new();
    private readonly Dictionary<Guid, RelayConnection> _registered = [];
    private TcpListener? _tcp;
    private UdpClient? _udp;
    private volatile bool _listening;
    private long _tcpForwarded, _udpForwarded, _dropped;
    private int _disposed;

    public FallbackRelayStatus Snapshot()
    {
        lock (_gate)
            return new(options.FallbackRelay.Enabled, _listening, _connections.Count,
                _connections.Values.Sum(c => c.Outbound.Reader.Count), _registered.Count,
                Interlocked.Read(ref _tcpForwarded), Interlocked.Read(ref _udpForwarded), Interlocked.Read(ref _dropped));
    }

    internal (string Host, uint TcpPort, uint UdpPort)? AdvertisedEndpoint => _listening
        ? (options.FallbackRelay.ResolveAdvertisedHost(options), (uint)options.FallbackRelay.TcpPort,
            (uint)options.FallbackRelay.UdpPort) : null;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        options.FallbackRelay.Validate(options);
        if (options.FallbackRelay.Enabled)
        {
            var ip = IPAddress.Parse(options.FallbackRelay.ResolveBindHost(options));
            try
            {
                _tcp = new TcpListener(ip, options.FallbackRelay.TcpPort);
                if (ip.AddressFamily == AddressFamily.InterNetworkV6) _tcp.Server.DualMode = true;
                _tcp.Start();
                _udp = DualStackUdp.Bind(new IPEndPoint(ip, options.FallbackRelay.UdpPort));
                _listening = true;
                readiness?.MarkReady("fallback");
                logger.LogInformation("Native fallback relay ready: TCP {TcpPort}, UDP {UdpPort}, advertised {Host}",
                    options.FallbackRelay.TcpPort, options.FallbackRelay.UdpPort,
                    options.FallbackRelay.ResolveAdvertisedHost(options));
            }
            catch { CloseListeners(); throw; }
        }
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.FallbackRelay.Enabled) return;
        using var registration = stoppingToken.Register(() => _shutdown.Cancel());
        try
        {
            await Task.WhenAll(OwnLoopAsync(AcceptAsync), OwnLoopAsync(ReceiveUdpAsync), OwnLoopAsync(ReapAsync));
        }
        finally
        {
            CloseListeners();
            foreach (var connection in _connections.Values) connection.Close();
            await Task.WhenAll(_connections.Values.Select(c => c.Completion));
        }
    }

    private async Task OwnLoopAsync(Func<CancellationToken, Task> loop)
    {
        try { await loop(_shutdown.Token); }
        catch (Exception ex) when (_shutdown.IsCancellationRequested
            && ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        catch { _shutdown.Cancel(); CloseListeners(); throw; }
    }

    private async Task AcceptAsync(CancellationToken token)
    {
        while (true)
        {
            var client = await _tcp!.AcceptTcpClientAsync(token);
            if (_connections.Count >= MaximumConnections) { client.Dispose(); Drop(); continue; }
            client.NoDelay = true;
            var connection = new RelayConnection(client, token);
            _connections[connection.Id] = connection;
            connection.Completion = HandleConnectionAsync(connection);
        }
    }

    private async Task HandleConnectionAsync(RelayConnection connection)
    {
        Task writer = Task.CompletedTask;
        try
        {
            byte[]? first;
            using (var registrationTimeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Token))
            {
                registrationTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                first = await NativeFallbackRelayProtocol.ReadTcpFrameAsync(connection.Stream, registrationTimeout.Token);
            }
            if (first is null || !NativeFallbackRelayProtocol.TryReadHeader(first, out var registration)
                || registration.Opcode != 0 || registration.Length != NativeFallbackRelayProtocol.HeaderLength
                || registration.SenderAccountUid != registration.RecipientAccountUid) { Drop(); return; }
            var address = ((IPEndPoint)connection.Client.Client.RemoteEndPoint!).Address;
            var admission = contexts.FindRelayAdmission(registration.SenderAccountUid, address);
            if (admission is null) { Drop(); return; }
            RelayConnection? previous;
            lock (_gate)
            {
                if (!contexts.IsCurrentRelayAdmission(admission)) { Drop(); return; }
                connection.Admission = admission;
                _registered.TryGetValue(admission.Id, out previous);
                _registered[admission.Id] = connection;
            }
            previous?.Close();
            logger.LogInformation("Native fallback registered account {AccountUid} game {SessionId} relay {ConnectionId}",
                admission.AccountUid, admission.SessionId, connection.Id);
            writer = WriteAsync(connection);
            while (await NativeFallbackRelayProtocol.ReadTcpFrameAsync(connection.Stream, connection.Token) is { } frame)
            {
                if (!NativeFallbackRelayProtocol.TryReadHeader(frame, out var header) || header.Opcode != 1
                    || header.SenderAccountUid != admission.AccountUid || !IsActive(connection)) { Drop(); break; }
                if (!TryCreateDelivery(frame, admission, connection, false, out var target, out var delivery))
                { Drop(); continue; }
                try { await target!.Outbound.Writer.WriteAsync(delivery!, connection.Token); }
                catch (ChannelClosedException) { Drop(); }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException
            or ObjectDisposedException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Native fallback connection {ConnectionId} failed", connection.Id);
        }
        finally
        {
            connection.Close();
            lock (_gate)
            {
                if (connection.Admission is { } admission
                    && _registered.TryGetValue(admission.Id, out var current) && ReferenceEquals(current, connection))
                    _registered.Remove(admission.Id);
            }
            await writer;
            _connections.TryRemove(connection.Id, out _);
            connection.Dispose();
        }
    }

    private async Task ReceiveUdpAsync(CancellationToken token)
    {
        while (true)
        {
            var packet = await _udp!.ReceiveAsync(token);
            if (!NativeFallbackRelayProtocol.TryReadHeader(packet.Buffer, out var header)
                || header.Length != packet.Buffer.Length || header.Opcode != 0) { Drop(); continue; }
            var admission = contexts.FindRelayAdmission(header.SenderAccountUid, packet.RemoteEndPoint.Address);
            if (admission is null || !TryCreateDelivery(packet.Buffer, admission, null, true,
                    out var target, out var delivery) || !target!.Outbound.Writer.TryWrite(delivery!)) Drop();
        }
    }

    private bool TryCreateDelivery(byte[] frame, RelayAccountAdmission sender, RelayConnection? source,
        bool udp, out RelayConnection? target, out Delivery? delivery)
    {
        target = null; delivery = null;
        if (!NativeFallbackRelayProtocol.TryReadHeader(frame, out var header)
            || !contexts.TryCaptureRelayRoute(sender, header.RecipientAccountUid, out var route)) return false;
        lock (_gate)
            if (!_registered.TryGetValue(route!.Recipient.Id, out target) || !IsActiveUnderLock(target)) return false;
        delivery = new(route!, source, NativeFallbackRelayProtocol.CreateDelivery(frame), udp);
        return true;
    }

    private async Task WriteAsync(RelayConnection connection)
    {
        try
        {
            await foreach (var delivery in connection.Outbound.Reader.ReadAllAsync(connection.Token))
            {
                if (!IsActive(connection) || (delivery.Source is { } source && !IsActive(source))
                    || !contexts.IsCurrentRelayRoute(delivery.Route)) { Drop(); continue; }
                // Use the recipient lifetime token: cancelling a sender must not leave half a TCP frame.
                await connection.Stream.WriteAsync(delivery.Frame, connection.Token);
                if (delivery.Udp) Interlocked.Increment(ref _udpForwarded);
                else Interlocked.Increment(ref _tcpForwarded);
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Native fallback delivered {Transport} {Sender}->{Recipient} length {Length} sha256 {Hash}",
                        delivery.Udp ? "UDP" : "TCP", delivery.Route.Sender.AccountUid,
                        delivery.Route.Recipient.AccountUid, delivery.Frame.Length,
                        Convert.ToHexString(SHA256.HashData(delivery.Frame)));
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException
            or ObjectDisposedException) { connection.Close(); }
    }

    private bool IsActive(RelayConnection connection)
    {
        lock (_gate) return IsActiveUnderLock(connection);
    }

    private bool IsActiveUnderLock(RelayConnection connection) =>
        !connection.IsClosed && connection.Admission is { } admission && contexts.IsCurrentRelayAdmission(admission)
        && _registered.TryGetValue(admission.Id, out var current) && ReferenceEquals(connection, current);

    private async Task ReapAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(token))
            foreach (var connection in _connections.Values)
                if (connection.Admission is not null && !IsActive(connection)) connection.Close();
    }

    private void Drop() => Interlocked.Increment(ref _dropped);

    private void CloseListeners()
    {
        readiness?.MarkStopped("fallback");
        _listening = false;
        _tcp?.Stop();
        _udp?.Dispose();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdown.Cancel();
        CloseListeners();
        foreach (var connection in _connections.Values) connection.Close();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        CloseListeners();
        foreach (var connection in _connections.Values) connection.Close();
        base.Dispose();
        _shutdown.Dispose();
    }

    private sealed record Delivery(FallbackRelayRoute Route, RelayConnection? Source, byte[] Frame, bool Udp);

    private sealed class RelayConnection : IDisposable
    {
        private readonly CancellationTokenSource _lifetime;
        private int _closed;
        internal RelayConnection(TcpClient client, CancellationToken token)
        {
            Client = client; Stream = client.GetStream();
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        }
        internal Guid Id { get; } = Guid.NewGuid();
        internal TcpClient Client { get; }
        internal NetworkStream Stream { get; }
        internal CancellationToken Token => _lifetime.Token;
        internal RelayAccountAdmission? Admission { get; set; }
        internal Task Completion { get; set; } = Task.CompletedTask;
        internal bool IsClosed => Volatile.Read(ref _closed) != 0;
        internal Channel<Delivery> Outbound { get; } = Channel.CreateBounded<Delivery>(new BoundedChannelOptions(32)
            { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        internal void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            Outbound.Writer.TryComplete();
            _lifetime.Cancel(); Client.Dispose();
        }
        public void Dispose() => _lifetime.Dispose();
    }
}
