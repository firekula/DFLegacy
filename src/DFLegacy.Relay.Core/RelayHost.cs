using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DFLegacy.Relay;

public enum RelayLogLevel
{
    Info,
    Warn,
    Error
}

public delegate void RelayLog(RelayLogLevel level, string message);

/// <summary>
/// Forwards one TCP port to a target. Each accepted connection gets its own
/// outgoing socket, which is what lets the listening side stay IPv4 while the
/// outgoing side follows the target's address family.
/// </summary>
internal sealed class TcpForwarder(
    IPAddress bindAddress,
    int port,
    IPAddress targetAddress,
    RelayLog log,
    Action<RelayTermination>? terminated = null)
{
    private static long _nextConnectionId;
    private readonly TcpListener _listener = new(bindAddress, port);

    public void Start() => _listener.Start();

    public void Stop() => _listener.Stop();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        log(
            RelayLogLevel.Info,
            $"TCP  {RelayHost.FormatEndpoint(bindAddress, port)} -> {RelayHost.FormatEndpoint(targetAddress, port)}");
        var connections = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException exception)
                {
                    log(RelayLogLevel.Warn, $"TCP {port}: accept failed: {exception.Message}");
                    continue;
                }

                connections.RemoveAll(task => task.IsCompleted);
                connections.Add(RelayAsync(client, cancellationToken));
            }
        }
        finally
        {
            // Host shutdown is complete only after accepted sockets are released
            // and their terminal diagnostics have been delivered.
            await Task.WhenAll(connections);
        }
    }

    private async Task RelayAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var connectionId = Interlocked.Increment(ref _nextConnectionId);
        var started = Stopwatch.GetTimestamp();
        var phase = "connect";
        var end = new TransferResult(0, "eof");
        long clientToServerBytes = 0;
        long serverToClientBytes = 0;
        try
        {
            using (client)
            using (var server = new TcpClient(targetAddress.AddressFamily))
            using (var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                LogConnection(
                    RelayLogLevel.Info,
                    $"TCP {port}: connection={connectionId} event=connection-start client={client.Client.RemoteEndPoint} target={RelayHost.FormatEndpoint(targetAddress, port)}");
                try
                {
                    using(var connectDeadline=CancellationTokenSource.CreateLinkedTokenSource(connectionCancellation.Token))
                    {
                        connectDeadline.CancelAfter(TimeSpan.FromSeconds(15));
                        try { await server.ConnectAsync(new IPEndPoint(targetAddress, port), connectDeadline.Token); }
                        catch(OperationCanceledException) when(!connectionCancellation.IsCancellationRequested) { throw new TimeoutException("Relay connection deadline elapsed."); }
                    }
                    phase = "transfer";
                    var clientStream = client.GetStream();
                    var serverStream = server.GetStream();
                    var results = await Task.WhenAll(
                        PumpAsync(clientStream, serverStream, server.Client, connectionId, "client->server", connectionCancellation, cancellationToken),
                        PumpAsync(serverStream, clientStream, client.Client, connectionId, "server->client", connectionCancellation, cancellationToken));
                    clientToServerBytes = results[0].Bytes;
                    serverToClientBytes = results[1].Bytes;
                    // Preserve the initiating error instead of replacing it with
                    // the opposite pump's cancellation during cleanup.
                    end = results.FirstOrDefault(result => result.Reason is "socket-error" or "error")
                        ?? results.FirstOrDefault(result => result.Reason == "cancelled")
                        ?? results[0];
                }
                catch (Exception exception)
                {
                    end = DescribeFailure(exception, 0, connectionCancellation.Token, cancellationToken);
                }
            }
        }
        finally
        {
            var reason=end.Reason switch { "eof"=>RelayTerminationReason.Eof,"cancelled"=>RelayTerminationReason.Cancelled,"socket-error"=>RelayTerminationReason.SocketError,"timeout"=>RelayTerminationReason.Timeout,_=>RelayTerminationReason.Error };
            RelayTermination.Deliver(terminated,new(reason,"TCP",phase,port,connectionId,end.Direction ?? "connect",clientToServerBytes,serverToClientBytes,Stopwatch.GetElapsedTime(started).TotalMilliseconds,end.SocketError,end.NativeError));
            LogConnection(
                end.IsError ? RelayLogLevel.Warn : RelayLogLevel.Info,
                $"TCP {port}: connection={connectionId} event=connection-end phase={phase} {end.Details} clientToServerBytes={clientToServerBytes} serverToClientBytes={serverToClientBytes} elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}");
        }
    }

    // A graceful EOF half-closes only the far side's send direction; a reply
    // may still arrive. A failed copy cancels the paired pump so an idle read
    // cannot retain both sockets indefinitely after a reset.
    private async Task<TransferResult> PumpAsync(
        NetworkStream from,
        NetworkStream to,
        Socket toSocket,
        long connectionId,
        string direction,
        CancellationTokenSource connectionCancellation,
        CancellationToken hostCancellation)
    {
        const int bufferSize = 81920;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        long bytes = 0;
        TransferResult result;
        try
        {
            var cancellationToken = connectionCancellation.Token;
            while (true)
            {
                var count = await from.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken);
                if (count == 0) break;
                await to.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                // Count only writes that completed; a failed write can have sent
                // a partial buffer that NetworkStream does not report to us.
                bytes += count;
            }
            toSocket.Shutdown(SocketShutdown.Send);
            result = new TransferResult(bytes, "eof");
        }
        catch (Exception exception)
        {
            result = DescribeFailure(exception, bytes, connectionCancellation.Token, hostCancellation);
            if (result.IsError) connectionCancellation.Cancel();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        result=result with { Direction=direction };
        LogConnection(
            result.IsError ? RelayLogLevel.Warn : RelayLogLevel.Info,
            $"TCP {port}: connection={connectionId} event=direction-end direction={direction} bytes={result.Bytes} {result.Details}");
        return result;
    }

    private void LogConnection(RelayLogLevel level, string message)
    {
        try { log(level, message); }
        catch (Exception)
        {
            // A failed diagnostic sink must not change socket ownership, the
            // bytes forwarded, or the result returned by either transfer pump.
        }
    }

    internal static TransferResult DescribeFailure(
        Exception exception,
        long bytes,
        CancellationToken connectionCancellation,
        CancellationToken hostCancellation)
    {
        var socketException = exception.GetBaseException() as SocketException;
        // A concrete reset/refusal can finish before cancellation, with its await
        // continuation running afterwards. Keep that error instead of masking it
        // merely because the token is now cancelled.
        if (exception is OperationCanceledException
            || (connectionCancellation.IsCancellationRequested
                && (exception is ObjectDisposedException
                    || socketException?.SocketErrorCode is SocketError.OperationAborted or SocketError.Interrupted)))
            return new TransferResult(bytes, "cancelled", hostCancellation.IsCancellationRequested ? "host-stop" : "peer-error");
        return new TransferResult(bytes, exception is TimeoutException ? "timeout" : socketException is null ? "error" : "socket-error", ExceptionType: exception.GetType().Name,
            SocketError: socketException?.SocketErrorCode, NativeError: socketException?.NativeErrorCode);
    }

    internal sealed record TransferResult(
        long Bytes,
        string Reason,
        string? Cancellation = null,
        string? ExceptionType = null,
        SocketError? SocketError = null,
        int? NativeError = null,
        string? Direction = null)
    {
        public bool IsError => Reason is "socket-error" or "error" or "timeout";
        public string Details => $"reason={Reason}"
            + (Cancellation is null ? "" : $" cancellation={Cancellation}")
            + (ExceptionType is null ? "" : $" exception={ExceptionType}")
            + (SocketError is null ? "" : $" socketError={SocketError} socketCode={(int)SocketError.Value} nativeError={NativeError}");
    }
}

/// <summary>
/// Forwards one UDP port to the target. UDP cannot cross address families on a
/// single socket: a socket bound to an IPv4 address cannot send to an IPv6 peer.
/// So this keeps one socket facing the game client and one facing the server and
/// carries datagrams between them.
/// </summary>
internal sealed class UdpForwarder(
    IPAddress bindAddress,
    int port,
    IPAddress targetAddress,
    RelayLog log,
    Action<RelayTermination>? terminated = null)
{
    private const int MaxConsecutiveReceiveFailures = 10;

    private UdpClient? _clientSide;
    private UdpClient? _serverSide;
    private volatile IPEndPoint? _client;

    public void Start()
    {
        // Binding happens here, so a port clash surfaces at startup. The
        // server-facing socket needs an explicit ephemeral bind: ReceiveAsync
        // fails on an unbound UdpClient.
        _clientSide = new UdpClient(new IPEndPoint(bindAddress, port));
        _serverSide = new UdpClient(0, targetAddress.AddressFamily);
    }

    public void Stop()
    {
        _clientSide?.Dispose();
        _serverSide?.Dispose();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clientSide = _clientSide!;
        var serverSide = _serverSide!;
        var target = new IPEndPoint(targetAddress, port);
        log(
            RelayLogLevel.Info,
            $"UDP  {RelayHost.FormatEndpoint(bindAddress, port)} -> {RelayHost.FormatEndpoint(targetAddress, port)}");

        await Task.WhenAll(
            PumpAsync(clientSide, serverSide, target, fromClient: true, cancellationToken),
            PumpAsync(serverSide, clientSide, target, fromClient: false, cancellationToken));
    }

    private async Task PumpAsync(
        UdpClient from,
        UdpClient to,
        IPEndPoint target,
        bool fromClient,
        CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await from.ReceiveAsync(cancellationToken);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // A persistent failure must not spin: log the first few, back off,
                // and give up rather than flooding the log.
                consecutiveFailures++;
                if (cancellationToken.IsCancellationRequested) return;
                if (consecutiveFailures > MaxConsecutiveReceiveFailures)
                {
                    var socket=exception.GetBaseException() as SocketException;
                    RelayTermination.Deliver(terminated,new(RelayTerminationReason.Error,"UDP","receive",port,0,fromClient ? "client->server" : "server->client",0,0,0,socket?.SocketErrorCode,socket?.NativeErrorCode,consecutiveFailures));
                    log(
                        RelayLogLevel.Error,
                        $"UDP {port}: 连续 {consecutiveFailures} 次接收失败，停止该端口（{exception.Message}）。");
                    return;
                }

                if (consecutiveFailures <= 3)
                {
                    log(RelayLogLevel.Warn, $"UDP {port}: receive failed ({exception.Message})");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            IPEndPoint destination;
            if (fromClient)
            {
                // The relay serves a single game client, so the most recent source
                // endpoint is where replies are routed back to.
                _client = received.RemoteEndPoint;
                destination = target;
            }
            else if (_client is { } client)
            {
                destination = client;
            }
            else
            {
                continue;
            }

            try
            {
                await to.SendAsync(received.Buffer, destination, cancellationToken);
            }
            catch (Exception exception)
            {
                if(cancellationToken.IsCancellationRequested) return;
                var socket=exception.GetBaseException() as SocketException;
                RelayTermination.Deliver(terminated,new(RelayTerminationReason.SocketError,"UDP","send",port,0,fromClient ? "client->server" : "server->client",0,0,0,socket?.SocketErrorCode,socket?.NativeErrorCode));
                log(RelayLogLevel.Warn, $"UDP {port}: send failed ({exception.Message})");
            }
        }
    }
}

/// <summary>
/// A local IPv4/IPv6 port relay for one game client: the client keeps talking to
/// <see cref="BindAddress"/> on the usual ports while the relay dials the target.
/// Hosts it either in a dedicated process (<c>DFLegacy.Relay</c>) or inside the
/// launcher, which then stays resident for the duration of the game.
/// </summary>
public sealed class RelayHost(
    IPAddress bindAddress,
    IPAddress targetAddress,
    IReadOnlyCollection<int> tcpPorts,
    IReadOnlyCollection<int> udpPorts,
    RelayLog log,
    Action<RelayTermination>? terminated = null)
{
    private readonly List<TcpForwarder> _tcp = [];
    private readonly List<UdpForwarder> _udp = [];

    public IPAddress BindAddress { get; } = bindAddress;

    public IPAddress TargetAddress { get; } = targetAddress;

    /// <summary>
    /// Binds every port. Throws <see cref="SocketException"/> when a port is
    /// taken, which is normally a stale relay still holding it.
    /// </summary>
    public void Start()
    {
        foreach (var port in tcpPorts)
        {
            var forwarder = new TcpForwarder(BindAddress, port, TargetAddress, log,terminated);
            try { forwarder.Start(); }
            catch(SocketException ex) { RelayTermination.Deliver(terminated,new(RelayTerminationReason.BindFailure,"TCP","bind",port,0,"listen",0,0,0,ex.SocketErrorCode,ex.NativeErrorCode)); Stop(); throw; }
            _tcp.Add(forwarder);
        }

        foreach (var port in udpPorts)
        {
            var forwarder = new UdpForwarder(BindAddress, port, TargetAddress, log,terminated);
            try { forwarder.Start(); }
            catch(SocketException ex) { forwarder.Stop(); RelayTermination.Deliver(terminated,new(RelayTerminationReason.BindFailure,"UDP","bind",port,0,"listen",0,0,0,ex.SocketErrorCode,ex.NativeErrorCode)); Stop(); throw; }
            _udp.Add(forwarder);
        }

        log(
            RelayLogLevel.Info,
            $"relay ready: {_tcp.Count} TCP / {_udp.Count} UDP port(s) on {BindAddress}, target {TargetAddress}.");
    }

    public void Stop()
    {
        foreach (var forwarder in _tcp)
        {
            forwarder.Stop();
        }

        _tcp.Clear();
        foreach (var forwarder in _udp)
        {
            forwarder.Stop();
        }

        _udp.Clear();
    }

    /// <summary>Pumps traffic until the token is cancelled, then stops.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var tasks = _tcp
            .Select(forwarder => forwarder.RunAsync(cancellationToken))
            .Concat(_udp.Select(forwarder => forwarder.RunAsync(cancellationToken)))
            .ToArray();
        try
        {
            await Task.WhenAll(tasks);
        }
        finally
        {
            Stop();
            log(RelayLogLevel.Info, "relay stopped.");
        }
    }

    /// <summary>
    /// Resolves a host name honouring the address-family preference. Literal IPs
    /// (optionally bracketed) are returned as-is.
    /// </summary>
    public static async Task<IPAddress?> ResolveTargetAsync(
        string host,
        bool preferIPv6,
        RelayLog log,
        CancellationToken cancellationToken)
    {
        var literal = host.Trim().Trim('[', ']');
        if (IPAddress.TryParse(literal, out var parsed))
        {
            return parsed;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(literal, cancellationToken);
        }
        catch (Exception exception)
        {
            log(RelayLogLevel.Error, $"无法解析 {literal}：{exception.Message}");
            return null;
        }

        if (addresses.Length == 0)
        {
            log(RelayLogLevel.Error, $"域名 {literal} 没有解析到任何地址。");
            return null;
        }

        var preferred = preferIPv6
            ? AddressFamily.InterNetworkV6
            : AddressFamily.InterNetwork;
        var chosen = Array.Find(addresses, address => address.AddressFamily == preferred);
        if (chosen is null)
        {
            log(
                RelayLogLevel.Warn,
                $"解析结果没有 {(preferIPv6 ? "IPv6" : "IPv4")} 地址，改用 {addresses[0].AddressFamily}。");
            chosen = addresses[0];
        }

        return chosen;
    }

    internal static string FormatEndpoint(IPAddress address, int port) =>
        address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";
}
