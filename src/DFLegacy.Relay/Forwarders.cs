using System.Net;
using System.Net.Sockets;

namespace DFLegacy.Relay;

internal static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("info", message);

    public static void Warn(string message) => Write("warn", message);

    public static void Error(string message) => Write("error", message);

    public static string Format(IPAddress address, int port) =>
        address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} {level}: {message}");
        }
    }
}

/// <summary>
/// Forwards one TCP port to the server. Each accepted connection gets its own
/// outgoing socket, which is what lets the listening side stay IPv4 while the
/// outgoing side follows the target's address family.
/// </summary>
internal sealed class TcpForwarder(IPAddress bindAddress, int port, IPAddress targetAddress)
{
    private readonly TcpListener _listener = new(bindAddress, port);

    public void Start() => _listener.Start();

    public void Stop() => _listener.Stop();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Log.Info($"TCP  {Log.Format(bindAddress, port)} -> {Log.Format(targetAddress, port)}");
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
                Log.Warn($"TCP {port}: accept failed: {exception.Message}");
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
                Log.Warn(
                    $"TCP {port}: cannot reach {Log.Format(targetAddress, port)}: {exception.Message}");
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
    private static async Task PumpAsync(
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
/// Forwards one UDP port to the server. UDP cannot cross address families on a
/// single socket: a socket bound to an IPv4 address cannot send to an IPv6 peer.
/// So this keeps one socket facing the game client and one facing the server and
/// carries datagrams between them.
/// </summary>
internal sealed class UdpForwarder(IPAddress bindAddress, int port, IPAddress targetAddress)
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
        Log.Info($"UDP  {Log.Format(bindAddress, port)} -> {Log.Format(targetAddress, port)}");

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
                // and give up rather than flooding the console.
                consecutiveFailures++;
                if (consecutiveFailures > MaxConsecutiveReceiveFailures)
                {
                    Log.Error(
                        $"UDP {port}: 连续 {consecutiveFailures} 次接收失败，停止该端口（{exception.Message}）。");
                    return;
                }

                if (consecutiveFailures <= 3)
                {
                    Log.Warn($"UDP {port}: receive failed ({exception.Message})");
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(500),
                        cancellationToken);
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
                Log.Warn($"UDP {port}: send failed ({exception.Message})");
            }
        }
    }
}
