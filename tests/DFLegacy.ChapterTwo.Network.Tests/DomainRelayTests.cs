using System.Net;
using System.Net.Sockets;
using DFLegacy.Relay;

internal static class DomainRelayTests
{
    internal static async Task<int> Run()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var token = timeout.Token;
            using var first = new EchoHost("127.0.0.80", 0, 41, token);
            using var second = new EchoHost("127.0.0.81", first.Port, 42, token);
            var lookups = 0;
            var addresses = new[] { IPAddress.IPv6Loopback, first.Address };
            Task<IPAddress[]> Resolve(string host, CancellationToken ct)
            { ct.ThrowIfCancellationRequested(); lookups++; return Task.FromResult(addresses); }
            await using var a = await DomainRelaySession.StartAsync("moving.example.test", true,
                [first.Port], [first.Port], first.Port, (_, _) => { }, token, Resolve);
            Require(a.TargetAddress.Equals(first.Address), "unreachable IPv6 falls back to available IPv4");
            addresses = [second.Address];
            await using var b = await DomainRelaySession.StartAsync("moving.example.test", true,
                [first.Port], [first.Port], first.Port, (_, _) => { }, token, Resolve);
            Require(lookups == 2 && b.TargetAddress.Equals(second.Address), "new login resolves a changed domain again");
            Require(!a.BindAddress.Equals(b.BindAddress), "two live sessions have separate local addresses");
            await Task.WhenAll(Exchange(a.BindAddress, first.Port, 41, token), Exchange(b.BindAddress, first.Port, 42, token));
            var released = a.BindAddress;
            await a.DisposeAsync();
            using var rebound = new TcpListener(released, first.Port);
            rebound.Start();
            using var reboundUdp = new UdpClient(new IPEndPoint(released, first.Port));
            Console.WriteLine("PASS domain change, address-family fallback, dual-session TCP/UDP isolation and released listeners");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                await using var ignored = await DomainRelaySession.StartAsync("moving.example.test", true,
                    [first.Port], [first.Port], first.Port, (_, _) => { }, cancelled.Token, Resolve);
                throw new InvalidOperationException("cancelled start unexpectedly succeeded");
            }
            catch (OperationCanceledException) { Console.WriteLine("PASS cancelled start does not start a relay"); }
            using var v6 = new EchoHost("::1", 0, 43, token);
            await using var ipv6 = await DomainRelaySession.StartAsync("ipv6.example.test", true,
                [v6.Port], [v6.Port], v6.Port, (_, _) => { }, token, (_, _) => Task.FromResult(new[] { IPAddress.IPv6Loopback }));
            await Exchange(ipv6.BindAddress, v6.Port, 43, token);
            Console.WriteLine("PASS IPv4 game-facing TCP/UDP reaches IPv6-only server");
            try
            {
                await using var missing = await DomainRelaySession.StartAsync("missing.example.test", true,
                    [first.Port], [first.Port], first.Port, (_, _) => { }, token, (_, _) => Task.FromResult(Array.Empty<IPAddress>()));
                throw new InvalidOperationException("empty DNS answer accepted");
            }
            catch (IOException) { Console.WriteLine("PASS missing DNS answer fails without starting game relay"); }
            var crowded = Enumerable.Range(1, 16).Select(i => IPAddress.Parse($"::{i:x}")).Append(first.Address).ToArray();
            await using var fallback = await DomainRelaySession.StartAsync("crowded.example.test", true,
                [first.Port], [first.Port], first.Port, (_, _) => { }, token, (_, _) => Task.FromResult(crowded));
            Require(fallback.TargetAddress.Equals(first.Address), "preferred-family addresses must not starve fallback family");
            await Exchange(fallback.BindAddress, first.Port, 41, token);
            Console.WriteLine("PASS many preferred addresses retain a reachable other-family fallback");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL domain relay: " + error); return 1; }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static async Task Exchange(IPAddress address, int port, byte expected, CancellationToken token)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(address, port, token);
        await tcp.GetStream().WriteAsync(new byte[] { 7 }, token);
        var reply = new byte[1];
        await tcp.GetStream().ReadExactlyAsync(reply, token);
        Require(reply[0] == expected, "TCP reached wrong server");
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await udp.SendAsync(new byte[] { 8 }, new IPEndPoint(address, port), token);
        var received = await udp.ReceiveAsync(token);
        Require(received.Buffer.SequenceEqual(new byte[] { expected, 8 }), "UDP reply crossed sessions or changed bytes");
    }

    private sealed class EchoHost : IDisposable
    {
        private readonly TcpListener _tcp;
        private readonly UdpClient _udp;
        public IPAddress Address { get; }
        public int Port { get; }
        public EchoHost(string address, int port, byte id, CancellationToken token)
        {
            Address = IPAddress.Parse(address);
            _tcp = new TcpListener(Address, port); _tcp.Start();
            Port = ((IPEndPoint)_tcp.LocalEndpoint).Port;
            _udp = new UdpClient(new IPEndPoint(Address, Port));
            _ = Accept(id, token); _ = Datagram(id, token);
        }
        private async Task Accept(byte id, CancellationToken token)
        {
            try { while (!token.IsCancellationRequested) { var connection = await _tcp.AcceptTcpClientAsync(token); _ = Reply(connection, id, token); } }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }
        private static async Task Reply(TcpClient client, byte id, CancellationToken token)
        {
            using (client) try
            {
                var b = new byte[1];
                if (await client.GetStream().ReadAsync(b, token) > 0) await client.GetStream().WriteAsync(new byte[] { id }, token);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException) { }
        }
        private async Task Datagram(byte id, CancellationToken token)
        {
            try { while (!token.IsCancellationRequested) { var data = await _udp.ReceiveAsync(token); await _udp.SendAsync(new byte[] { id }.Concat(data.Buffer).ToArray(), data.RemoteEndPoint, token); } }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }
        public void Dispose() { _tcp.Stop(); _udp.Dispose(); }
    }
}
