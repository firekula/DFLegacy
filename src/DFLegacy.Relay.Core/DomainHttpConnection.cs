using System.Net;
using System.Net.Sockets;

namespace DFLegacy.Relay;

/// <summary>Race only connection establishment; the caller sends its request once on the winning stream.</summary>
public static class DomainHttpConnection
{
    public static async ValueTask<Stream> ConnectAsync(string host, int port, CancellationToken token,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        stop.CancelAfter(TimeSpan.FromSeconds(8));
        var addresses = await (resolve ?? ((name, ct) => Dns.GetHostAddressesAsync(name, ct)))(host, stop.Token);
        var candidates = addresses.Distinct().Where(a =>
            a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
            && !a.IsIPv6LinkLocal && !a.IsIPv6Multicast && !a.IsIPv4MappedToIPv6
            && !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)
            && (a.AddressFamily != AddressFamily.InterNetwork || a.GetAddressBytes()[0] is > 0 and < 224)).ToArray();
        var ipv6 = new Queue<IPAddress>(candidates.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6));
        var ipv4 = new Queue<IPAddress>(candidates.Where(a => a.AddressFamily == AddressFamily.InterNetwork));
        var ordered = new List<IPAddress>();
        while (ordered.Count < 16 && (ipv6.Count > 0 || ipv4.Count > 0))
        {
            if (ipv6.TryDequeue(out var v6)) ordered.Add(v6);
            if (ipv4.TryDequeue(out var v4)) ordered.Add(v4);
        }
        var pending = ordered.Take(16).Select((address, index) => Dial(address, port, index, stop.Token)).ToList();
        Exception? last = null;
        try
        {
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                try
                {
                    var socket = await completed;
                    if (token.IsCancellationRequested) { socket.Dispose(); token.ThrowIfCancellationRequested(); }
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception error) when (error is SocketException or OperationCanceledException)
                { last = error; token.ThrowIfCancellationRequested(); }
            }
            throw new IOException("无法连接账号服务的解析地址。", last);
        }
        finally
        {
            stop.Cancel();
            foreach (var task in pending)
            {
                try { (await task).Dispose(); }
                catch (Exception error) when (error is SocketException or OperationCanceledException) { }
            }
        }
    }

    private static async Task<Socket> Dial(IPAddress address, int port, int index, CancellationToken token)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(index * 200), token);
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
            attempt.CancelAfter(TimeSpan.FromSeconds(3));
            await socket.ConnectAsync(new IPEndPoint(address, port), attempt.Token);
            return socket;
        }
        catch { socket.Dispose(); throw; }
    }
}
