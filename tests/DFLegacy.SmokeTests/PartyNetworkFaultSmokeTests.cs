using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PartyNetworkFaultSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var ct = deadline.Token;
        var reserve = new TcpListener(IPAddress.Loopback, 0);
        reserve.Start();
        var tcpPort = ((IPEndPoint)reserve.LocalEndpoint).Port;
        reserve.Stop();
        using var udpReserve = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var udpPort = ((IPEndPoint)udpReserve.Client.LocalEndPoint!).Port;
        udpReserve.Dispose();
        var options = new ServerOptions { EnableConnectionDiagnostics = true };
        options.FallbackRelay.TcpPort = tcpPort;
        options.FallbackRelay.UdpPort = udpPort;
        var runtime = new RuntimeState();
        using var contexts = new GameplayDatagramService(options, runtime, NullLogger<GameplayDatagramService>.Instance);
        var log = new CaptureLogger<FallbackRelayService>();
        using var service = new FallbackRelayService(options, contexts, log);
        var sessions = new Dictionary<uint, Guid>();
        var combatInstance = new object();
        foreach (uint id in new uint[] { 101, 102, 103, 104 })
        {
            var session = runtime.Add("channel:fault-test", "127.0.0.1:1");
            sessions[id] = session.Id;
            contexts.RegisterChannelSession(session, IPAddress.Loopback);
            contexts.AuthenticateRelayAccount(session.Id, id);
            contexts.SetRelayContext(session.Id, 7, combatInstance);
        }
        await service.StartAsync(ct);
        var clients = new List<TcpClient>();
        Task flood = Task.CompletedTask;
        using var floodLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var a = await Connect(101);
            var b = await Connect(102, 1024);
            var c = await Connect(103);
            await Until(() => service.Snapshot().RegisteredConnections == 3, ct);
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var target = new IPEndPoint(IPAddress.Loopback, udpPort);
            // Controlled loss of 1/4/7; delayed, duplicate and reordered survivors.
            // Opaque relay must preserve what arrives, not invent retransmissions.
            int[] arrivals = [2, 0, 2, 5, 3, 8, 6, 9];
            foreach (var sequence in arrivals)
            {
                await Task.Delay(sequence % 3 * 7, ct);
                var payload = BitConverter.GetBytes(sequence);
                await udp.SendAsync(Frame(0, 101, 102, payload), target, ct);
                var delivered = await NativeFallbackRelayProtocol.ReadTcpFrameAsync(b.GetStream(), ct);
                check(delivered is not null && delivered.AsSpan().SequenceEqual(Frame(3, 101, 102, payload)),
                    $"UDP fault simulation preserves surviving sequence {sequence} without changing bytes");
            }
            await Until(() => service.Snapshot().UdpFramesForwarded == arrivals.Length, ct);
            check(service.Snapshot().UdpFramesForwarded == arrivals.Length,
                "omitted UDP packets are not fabricated by the fallback relay");
            for (var i = 0; i < 64; i++)
                await udp.SendAsync(new byte[] { 0, 0, 1 }, target, ct);
            await Until(() => service.Snapshot().DroppedFrames >= 64, ct);
            check(log.HasReason("malformed-udp"), "diagnostics distinguish malformed UDP from upstream packet loss");
            if (!log.HasReason("malformed-udp")) return;
            check(log.Events.Count(e => e.Name == "RelayDrop") <= 7,
                "a malformed UDP burst produces logarithmically bounded diagnostics");

            var frame = Frame(1, 101, 103, [0x61, 0x62, 0x63]);
            foreach (var chunk in frame.Chunk(3)) await a.GetStream().WriteAsync(chunk, ct);
            check((await NativeFallbackRelayProtocol.ReadTcpFrameAsync(c.GetStream(), ct))!
                .AsSpan().SequenceEqual(Frame(3, 101, 103, [0x61, 0x62, 0x63])),
                "TCP fragmentation after dropped/malformed UDP still delivers exactly one complete frame");

            await VerifyDuplexBurstAsync(a, c, ct, check);

            // A non-reading peer fills its bounded queue; diagnose, don't silently drop TCP.
            var large = Frame(1, 101, 102, new byte[8000]);
            flood = Task.Run(async () =>
            {
                try { while (true) await a.GetStream().WriteAsync(large, floodLifetime.Token); }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
            }, ct);
            await Until(() => service.Snapshot().PendingFrames == 32, ct);
            await Until(() => log.Events.Any(e => e.Name == "RelayStall"), ct);
            check(log.Events.Any(e => e.Name == "RelayStall"),
                "real non-reading peer produces a stalled connection diagnostic before disconnection");
            await udp.SendAsync(Frame(0, 101, 103, [0x77]), target, ct);
            check((await NativeFallbackRelayProtocol.ReadTcpFrameAsync(c.GetStream(), ct))!
                .AsSpan().SequenceEqual(Frame(3, 101, 103, [0x77])),
                "a stalled TCP destination does not stop UDP forwarding to a healthy party member");
            var d = await Connect(104);
            await Until(() => service.Snapshot().RegisteredConnections == 4, ct);
            var blockedThenHealthy = Frame(1, 104, 102, [0xA1])
                .Concat(Frame(1, 104, 103, [0xA2])).ToArray();
            await d.GetStream().WriteAsync(blockedThenHealthy, ct);
            var pendingHealthy = NativeFallbackRelayProtocol.ReadTcpFrameAsync(c.GetStream(), ct);
            await Task.WhenAny(pendingHealthy, Task.Delay(300, ct));
            check(!pendingHealthy.IsCompleted,
                "characterization: a full target queue delays the same sender's later TCP frame to a healthy peer");
            contexts.RevokeRelayAccount(sessions[102]);
            b.Dispose();
            var recoveredHealthy = await pendingHealthy;
            check(recoveredHealthy is not null && recoveredHealthy.AsSpan().SequenceEqual(Frame(3, 104, 103, [0xA2])),
                "retiring the blocked recipient releases the queued sender and preserves the following healthy frame");
            floodLifetime.Cancel();
            contexts.RevokeRelayAccount(sessions[101]);
            a.Dispose();
            await flood;
            await Until(() => service.Snapshot().RegisteredConnections == 2, ct);
            contexts.AuthenticateRelayAccount(sessions[101], 101);
            var replacement = await Connect(101);
            await Until(() => service.Snapshot().RegisteredConnections == 3, ct);
            await c.GetStream().WriteAsync(Frame(1, 103, 101, [0x88]), ct);
            check((await NativeFallbackRelayProtocol.ReadTcpFrameAsync(replacement.GetStream(), ct))!
                .AsSpan().SequenceEqual(Frame(3, 103, 101, [0x88])),
                "new connection recovers after a blocked incarnation is retired");
            check(log.Events.Any(e => e.Name == "RelayClosed"),
                "relay end diagnostics retain a connection id and reason for correlation");
            options.EnableConnectionDiagnostics = false;
            var diagnosticDrops = log.Events.Count(e => e.Name == "RelayDrop");
            var droppedBefore = service.Snapshot().DroppedFrames;
            for (var i = 0; i < 128; i++) await udp.SendAsync(new byte[] { 0, 0, 1 }, target, ct);
            await Until(() => service.Snapshot().DroppedFrames >= droppedBefore + 128, ct);
            check(log.Events.Count(e => e.Name == "RelayDrop") == diagnosticDrops,
                "disabled diagnostics suppress drop logs while malformed traffic is still rejected");
            options.EnableConnectionDiagnostics = true;
        }
        finally
        {
            floodLifetime.Cancel();
            try { await service.StopAsync(CancellationToken.None); }
            finally { foreach (var client in clients) client.Dispose(); }
            await flood;
        }
        check(log.HasReason("server-stop"), "host shutdown is diagnosed as server-stop instead of a read/write failure");

        async Task<TcpClient> Connect(uint id, int receiveBuffer = 65536)
        {
            var client = new TcpClient { ReceiveBufferSize = receiveBuffer };
            clients.Add(client);
            await client.ConnectAsync(IPAddress.Loopback, tcpPort, ct);
            await client.GetStream().WriteAsync(Frame(0, id, id, []), ct);
            return client;
        }
    }

    private static byte[] Frame(ushort opcode, uint sender, uint target, byte[] data)
    {
        var result = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(result, opcode);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)result.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), sender);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), target);
        data.CopyTo(result, 12);
        return result;
    }

    // Synthetic opaque frames, not replayed gameplay commands. Exercises the
    // transport used inside the same dungeon without claiming native acceptance.
    private static async Task VerifyDuplexBurstAsync(TcpClient a, TcpClient c,
        CancellationToken ct, Action<bool, string> check)
    {
        const int count = 512;
        a.NoDelay = c.NoDelay = true;
        await Task.WhenAll(Send(a, 101, 103), Send(c, 103, 101),
            Receive(a, 103, 101, false), Receive(c, 101, 103, true));

        static byte[] Payload(int sequence)
        {
            var payload = new byte[64 + sequence % 8 * 137];
            BinaryPrimitives.WriteInt32LittleEndian(payload, sequence);
            for (var i = 4; i < payload.Length; i++) payload[i] = (byte)(sequence * 17 + i);
            return payload;
        }
        async Task Send(TcpClient client, uint from, uint to)
        {
            for (var i = 0; i < count; i++)
                await client.GetStream().WriteAsync(Frame(1, from, to, Payload(i)), ct);
        }
        async Task Receive(TcpClient client, uint from, uint to, bool pause)
        {
            var intact = true;
            for (var i = 0; i < count; i++)
            {
                if (pause && i == 64) await Task.Delay(350, ct);
                var actual = await NativeFallbackRelayProtocol.ReadTcpFrameAsync(client.GetStream(), ct);
                if (actual is null) { intact = false; break; }
                intact &= actual.AsSpan().SequenceEqual(Frame(3, from, to, Payload(i)));
            }
            check(intact, $"same-dungeon {from}->{to} burst preserves all {count} frames and ordering after a 350ms receiver pause");
        }
    }

    private static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        while (!condition()) await Task.Delay(10, ct);
    }

    internal sealed class CaptureLogger<T> : ILogger<T>
    {
        internal ConcurrentQueue<(string? Name, string? Reason, string Message)> Events { get; } = new();
        internal bool HasReason(string reason) => Events.Any(e => e.Reason == reason);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state as IEnumerable<KeyValuePair<string, object?>>;
            Events.Enqueue((id.Name, fields?.FirstOrDefault(p => p.Key == "Reason").Value?.ToString(), formatter(state, exception)));
        }
    }
}
