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
    RelayLog log)
{
    private readonly TcpListener _listener = new(bindAddress, port);

    public void Start() => _listener.Start();

    public void Stop() => _listener.Stop();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        log(
            RelayLogLevel.Info,
            $"TCP  {RelayHost.FormatEndpoint(bindAddress, port)} -> {RelayHost.FormatEndpoint(targetAddress, port)}");
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
            catch (SocketException exception)
            {
                log(RelayLogLevel.Warn, $"TCP {port}: accept failed: {exception.Message}");
                continue;
            }

            _ = RelayAsync(client, cancellationToken);
        }
    }

    private async Task RelayAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (var server = new TcpClient(targetAddress.AddressFamily))
        {
            try
            {
                await server.ConnectAsync(
                    new IPEndPoint(targetAddress, port),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                log(
                    RelayLogLevel.Warn,
                    $"TCP {port}: cannot reach {RelayHost.FormatEndpoint(targetAddress, port)}: {exception.Message}");
                return;
            }

            var clientStream = client.GetStream();
            var serverStream = server.GetStream();
            await Task.WhenAll(
                PumpAsync(clientStream, serverStream, server.Client, cancellationToken),
                PumpAsync(serverStream, clientStream, client.Client, cancellationToken));
        }
    }

    // Copies one direction, then half-closes the far side so it observes EOF
    // instead of waiting for a timeout.
    private async Task PumpAsync(
        NetworkStream from,
        NetworkStream to,
        Socket toSocket,
        CancellationToken cancellationToken)
    {
        try
        {
            await from.CopyToAsync(to, cancellationToken);
        }
        catch (Exception)
        {
            // A reset or a cancelled copy both simply end this direction.
        }
        finally
        {
            try
            {
                toSocket.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                // The far side is already gone.
            }
        }
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
    RelayLog log)
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
                if (consecutiveFailures > MaxConsecutiveReceiveFailures)
                {
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
    RelayLog log)
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
            var forwarder = new TcpForwarder(BindAddress, port, TargetAddress, log);
            forwarder.Start();
            _tcp.Add(forwarder);
        }

        foreach (var port in udpPorts)
        {
            var forwarder = new UdpForwarder(BindAddress, port, TargetAddress, log);
            forwarder.Start();
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
