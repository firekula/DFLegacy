using System.Net;
using System.Net.Sockets;

namespace DFLegacy.Relay;

public sealed class DomainRelaySession : IAsyncDisposable
{
    private readonly RelayHost _relay;
    private readonly CancellationTokenSource _stop;
    private readonly Task _run;
    private int _disposed;
    public IPAddress BindAddress => _relay.BindAddress;
    public IPAddress TargetAddress => _relay.TargetAddress;

    private DomainRelaySession(RelayHost relay, CancellationToken token)
    {
        _relay = relay;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        _run = relay.RunAsync(_stop.Token);
    }

    public static async Task<DomainRelaySession> StartAsync(string host, bool preferIPv6,
        IReadOnlyCollection<int> tcpPorts, IReadOnlyCollection<int> udpPorts, int probePort,
        RelayLog log, CancellationToken token,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        token.ThrowIfCancellationRequested();
        if (!tcpPorts.Contains(probePort) || tcpPorts.Concat(udpPorts).Any(port => port is < 1 or > 65535))
            throw new ArgumentException("Invalid relay port set.");
        void Log(RelayLogLevel level, string text) { try { log(level, text); } catch { } }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        IPAddress? target = null;
        try
        {
            var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? [literal]
                : await (resolve ?? ((name, ct) => Dns.GetHostAddressesAsync(name, ct)))(host, deadline.Token);
            var preferred = preferIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            foreach (var address in AlternateFamilies(addresses, preferred).Take(16))
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                attempt.CancelAfter(TimeSpan.FromSeconds(3));
                using var probe = new TcpClient(address.AddressFamily);
                try
                {
                    await probe.ConnectAsync(address, probePort, attempt.Token);
                    target = address;
                    break;
                }
                catch (Exception error) when (error is SocketException or OperationCanceledException && !deadline.IsCancellationRequested)
                { Log(RelayLogLevel.Warn, $"入口 {RelayHost.FormatEndpoint(address, probePort)} 不可达，尝试下一个地址。"); }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("解析或连接服务器超时，请检查域名及服务器状态。"); }
        if (target is null) throw new IOException($"域名 {host} 没有可连接的入口地址，请检查服务器状态后重新登录。");
        Log(RelayLogLevel.Info, $"Resolved this session: {host} -> {target}.");
        // Each launcher owns its loopback address, including its UDP return path.
        // No machine-wide hosts/DNS changes or shared resident forwarder.
        for (var attempt = 0; attempt < 32; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var bind = IPAddress.Parse($"127.73.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}");
            if (bind.Equals(target)) continue;
            var relay = new RelayHost(bind, target, tcpPorts.Distinct().ToArray(), udpPorts.Distinct().ToArray(), Log);
            try
            {
                relay.Start();
                token.ThrowIfCancellationRequested();
                return new DomainRelaySession(relay, token);
            }
            catch (SocketException error) when (error.SocketErrorCode == SocketError.AddressAlreadyInUse) { relay.Stop(); }
            catch { relay.Stop(); throw; }
        }
        throw new IOException("无法为此游戏实例分配本机转发端口。");
    }

    private static bool Usable(IPAddress address) => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
        && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any) && !address.IsIPv6Multicast
        && !address.IsIPv6LinkLocal && !address.IsIPv4MappedToIPv6
        && (address.AddressFamily != AddressFamily.InterNetwork || address.GetAddressBytes()[0] is > 0 and < 224);

    private static IEnumerable<IPAddress> AlternateFamilies(IEnumerable<IPAddress> addresses, AddressFamily preferred)
    {
        var candidates = addresses.Distinct().Where(Usable).ToArray();
        var first = new Queue<IPAddress>(candidates.Where(ip => ip.AddressFamily == preferred));
        var other = new Queue<IPAddress>(candidates.Where(ip => ip.AddressFamily != preferred));
        // Try both families before repeated timeouts can consume the shared deadline.
        while (first.Count > 0 || other.Count > 0)
        {
            if (first.TryDequeue(out var a)) yield return a;
            if (other.TryDequeue(out var b)) yield return b;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        try { await _run; }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _relay.Stop(); _stop.Dispose(); }
    }
}
