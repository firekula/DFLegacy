using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class FallbackRelaySocketSmokeTests
{
    private const uint Alice = 0x11223344, Bob = 0x55667788, Outsider = 0xABCDEF01;

    internal static void Options(Action<bool, string> check)
    {
        var defaults = new ServerOptions();
        defaults.FallbackRelay.Validate(defaults);
        check(defaults.FallbackRelay.ResolveAdvertisedHost(defaults) == "127.0.0.1",
            "native fallback advertisement follows the existing dial-host resolver");
        foreach (var conflict in new Action<ServerOptions>[]
        {
            o => o.FallbackRelay.TcpPort = o.Channel.Port,
            o => o.FallbackRelay.TcpPort = o.Admin.Port,
            o => o.FallbackRelay.TcpPort = o.Entrance.Port,
            o => { o.Entrance.AdditionalPorts = [12345]; o.FallbackRelay.TcpPort = 12345; },
            o => { o.Pvp.Enabled = true; o.FallbackRelay.TcpPort = o.Pvp.Port; },
            o => { o.Processes.Channel.Enabled = true; o.FallbackRelay.TcpPort = o.Processes.Channel.LinkPort; },
            o => o.FallbackRelay.UdpPort = o.CharacterDatagram.Port,
            o => o.FallbackRelay.UdpPort = o.GameplayDatagram.PrimaryPort,
            o => o.FallbackRelay.UdpPort = o.GameplayDatagram.SecondaryPort,
            o => o.FallbackRelay.AdvertisedHost = "::1",
            o => o.FallbackRelay.AdvertisedHost = "0.0.0.0",
            o => o.FallbackRelay.Host = "bad-host",
            o => o.FallbackRelay.Host = "::1",
            o => { o.FallbackRelay.Host = "127.0.0.1"; o.FallbackRelay.AdvertisedHost = "192.168.3.177"; },
            o => o.FallbackRelay.TcpPort = 0,
            o => o.FallbackRelay.UdpPort = 65536
        })
        {
            var options = new ServerOptions(); conflict(options);
            try { options.FallbackRelay.Validate(options); check(false, "fallback rejects invalid or conflicting endpoint"); }
            catch (InvalidOperationException) { check(true, "fallback rejects invalid or conflicting endpoint"); }
        }
        var disabled = new ServerOptions();
        disabled.FallbackRelay.Enabled = false; disabled.FallbackRelay.TcpPort = 0;
        disabled.FallbackRelay.Validate(disabled);
        check(true, "disabled fallback does not claim a transport endpoint");
    }

    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var options = new ServerOptions();
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        options.FallbackRelay.TcpPort = ((IPEndPoint)reservation.LocalEndpoint).Port;
        using (var udpReservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            options.FallbackRelay.UdpPort = ((IPEndPoint)udpReservation.Client.LocalEndPoint!).Port;
        reservation.Stop();
        options.FallbackRelay.Host = "127.0.0.1";
        options.FallbackRelay.AdvertisedHost = "127.0.0.1";
        var runtime = new RuntimeState();
        using var contexts = new GameplayDatagramService(options, runtime, NullLogger<GameplayDatagramService>.Instance);
        var a = runtime.Add("channel:test", "127.0.0.1:41001");
        var b = runtime.Add("channel:test", "127.0.0.1:41002");
        var c = runtime.Add("channel:other", "127.0.0.1:41003");
        foreach (var session in new[] { a, b, c }) contexts.RegisterChannelSession(session, IPAddress.Loopback);
        contexts.AuthenticateRelayAccount(a.Id, Alice);
        contexts.AuthenticateRelayAccount(b.Id, Bob);
        contexts.AuthenticateRelayAccount(c.Id, Outsider);
        contexts.SetRelayContext(a.Id, 7); contexts.SetRelayContext(b.Id, 7); contexts.SetRelayContext(c.Id, 8);
        using var service = new FallbackRelayService(options, contexts, NullLogger<FallbackRelayService>.Instance);
        await service.StartAsync(timeout.Token);
        var clients = new List<TcpClient>();
        try
        {
            check(service.Snapshot().Listening, "native fallback owns both advertised listener endpoints");
            TcpClient alice;
            try { alice = await ConnectAsync(Alice); }
            catch (SocketException) { check(false, "real TCP client can connect to the fallback listener"); return; }
            var bob = await ConnectAsync(Bob);
            await WaitUntilAsync(() => service.Snapshot().RegisteredConnections == 2, timeout.Token);
            byte[] data = Convert.FromHexString("0100110044332211887766556D7400FF7A");
            for (var i = 0; i < 3; i++) await alice.GetStream().WriteAsync(data.AsMemory(i, 1), timeout.Token);
            byte[] second = Frame(1, Alice, Bob, [0x41, 0x42]);
            await alice.GetStream().WriteAsync(data.AsMemory(3).ToArray().Concat(second).ToArray(), timeout.Token);
            await EqualAsync(bob, Convert.FromHexString("0300110044332211887766556D7400FF7A"),
                "real TCP split frame is delivered with only opcode changed", check, timeout.Token);
            await EqualAsync(bob, Frame(3, Alice, Bob, [0x41, 0x42]),
                "following coalesced TCP frame stays separate and ordered", check, timeout.Token);
            await bob.GetStream().WriteAsync(Frame(1, Bob, Alice, [0x81, 0x00, 0xFF]), timeout.Token);
            await EqualAsync(alice, Frame(3, Bob, Alice, [0x81, 0x00, 0xFF]),
                "native TCP relay is bidirectional without an invented registration ACK", check, timeout.Token);

            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var udpTarget = new IPEndPoint(IPAddress.Loopback, options.FallbackRelay.UdpPort);
            await udp.SendAsync(Frame(0, Outsider, Bob, [0xCC]), udpTarget, timeout.Token);
            await udp.SendAsync(Frame(0, Alice, Bob, [0x01, 0x02]), udpTarget, timeout.Token);
            await EqualAsync(bob, Frame(3, Alice, Bob, [0x01, 0x02]),
                "UDP-to-TCP excludes another party and forwards opaque valid bytes", check, timeout.Token);
            byte[] malformedUdp = Frame(0, Alice, Bob, [0xEE]); malformedUdp[2]--;
            await udp.SendAsync(malformedUdp, udpTarget, timeout.Token);
            await udp.SendAsync(Frame(0, Alice, Bob, [0x03]), udpTarget, timeout.Token);
            await EqualAsync(bob, Frame(3, Alice, Bob, [0x03]),
                "UDP trailing bytes are rejected while the listener remains healthy", check, timeout.Token);

            var duplicate = await ConnectAsync(Alice);
            check(await EofAsync(alice, timeout.Token), "duplicate relay registration closes the older TCP socket");
            await bob.GetStream().WriteAsync(Frame(1, Bob, Alice, [0xA1]), timeout.Token);
            await EqualAsync(duplicate, Frame(3, Bob, Alice, [0xA1]),
                "new relay socket survives old socket teardown", check, timeout.Token);

            await duplicate.GetStream().WriteAsync(Frame(1, Outsider, Bob, [0xDD]), timeout.Token);
            check(await EofAsync(duplicate, timeout.Token), "registered TCP sender cannot impersonate another account");
            var recovered = await ConnectAsync(Alice);
            await WaitUntilAsync(() => service.Snapshot().RegisteredConnections == 2, timeout.Token);
            await recovered.GetStream().WriteAsync(Frame(1, Alice, Bob, [0xB1]), timeout.Token);
            await EqualAsync(bob, Frame(3, Alice, Bob, [0xB1]),
                "a rejected spoof does not stop later legitimate transport", check, timeout.Token);

            var unknown = await ConnectAsync(0xFEEDBEEF);
            check(await EofAsync(unknown, timeout.Token), "unknown account cannot register a native relay socket");
            var withoutRegistration = new TcpClient(); clients.Add(withoutRegistration);
            await withoutRegistration.ConnectAsync(IPAddress.Loopback, options.FallbackRelay.TcpPort, timeout.Token);
            await withoutRegistration.GetStream().WriteAsync(Frame(1, Alice, Bob, [0x99]), timeout.Token);
            check(await EofAsync(withoutRegistration, timeout.Token), "data before registration is rejected");
            var malformed = new TcpClient(); clients.Add(malformed);
            await malformed.ConnectAsync(IPAddress.Loopback, options.FallbackRelay.TcpPort, timeout.Token);
            await malformed.GetStream().WriteAsync(new byte[] { 0, 0, 1, 32 }, timeout.Token);
            check(await EofAsync(malformed, timeout.Token), "over-limit TCP header is closed without allocating its body");
            var repeated = await ConnectAsync(Outsider);
            await repeated.GetStream().WriteAsync(Frame(0, Outsider, Outsider, []), timeout.Token);
            check(await EofAsync(repeated, timeout.Token), "a registered socket cannot issue another registration");
            await recovered.GetStream().WriteAsync(Frame(1, Alice, 0xFEEDBEEF, [0xC1]), timeout.Token);
            await recovered.GetStream().WriteAsync(Frame(1, Alice, Bob, [0xC2]), timeout.Token);
            await EqualAsync(bob, Frame(3, Alice, Bob, [0xC2]),
                "missing target is dropped without corrupting subsequent delivery", check, timeout.Token);
            var unfinished = new TcpClient(); clients.Add(unfinished);
            await unfinished.ConnectAsync(IPAddress.Loopback, options.FallbackRelay.TcpPort, timeout.Token);
            var registrationClock = System.Diagnostics.Stopwatch.StartNew();
            await unfinished.GetStream().WriteAsync(new byte[] { 0, 0, 12 }, timeout.Token);
            check(await EofAsync(unfinished, timeout.Token) && registrationClock.Elapsed.TotalSeconds >= 4.5,
                "unfinished native registration expires at the real five-second deadline");
            await bob.GetStream().WriteAsync(Frame(1, Bob, Alice, [0xC3]), timeout.Token);
            await EqualAsync(recovered, Frame(3, Bob, Alice, [0xC3]),
                "registered idle sockets remain usable beyond the registration timeout", check, timeout.Token);
            contexts.UnregisterChannelSession(a.Id);
            check(await EofAsync(recovered, timeout.Token), "game-session retirement closes its owned relay socket");
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
            await service.StopAsync(CancellationToken.None);
        }
        using (var reboundTcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            reboundTcp.Bind(new IPEndPoint(IPAddress.Loopback, options.FallbackRelay.TcpPort));
        using (var reboundUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, options.FallbackRelay.UdpPort)))
            check(true, "host stop releases its owned TCP and UDP listeners");
        foreach (var mode in new[] { "stop", "context", "owner" }) await SlowReceiverAsync(mode, check);

        async Task<TcpClient> ConnectAsync(uint account)
        {
            var client = new TcpClient(); clients.Add(client);
            await client.ConnectAsync(IPAddress.Loopback, options.FallbackRelay.TcpPort, timeout.Token);
            await client.GetStream().WriteAsync(Frame(0, account, account, []), timeout.Token);
            return client;
        }
    }

    private static async Task SlowReceiverAsync(string mode, Action<bool, string> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var options = new ServerOptions();
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        options.FallbackRelay.TcpPort = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using (var udpPort = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            options.FallbackRelay.UdpPort = ((IPEndPoint)udpPort.Client.LocalEndPoint!).Port;
        var runtime = new RuntimeState();
        using var contexts = new GameplayDatagramService(options, runtime, NullLogger<GameplayDatagramService>.Instance);
        Guid senderSession = default;
        foreach (var account in new[] { Alice, Bob, Outsider })
        {
            var session = runtime.Add("channel:test", "127.0.0.1:41000");
            contexts.RegisterChannelSession(session, IPAddress.Loopback);
            contexts.AuthenticateRelayAccount(session.Id, account); contexts.SetRelayContext(session.Id, 7);
            if (account == Alice) senderSession = session.Id;
        }
        using var service = new FallbackRelayService(options, contexts, NullLogger<FallbackRelayService>.Instance);
        await service.StartAsync(timeout.Token);
        using var sender = new TcpClient();
        using var blocked = new TcpClient { ReceiveBufferSize = 1024 };
        using var other = new TcpClient();
        using var replacement = new TcpClient();
        Task flood = Task.CompletedTask;
        bool finishFlood = false;
        try
        {
            await sender.ConnectAsync(IPAddress.Loopback, options.FallbackRelay.TcpPort, timeout.Token);
            await blocked.ConnectAsync(IPAddress.Loopback, options.FallbackRelay.TcpPort, timeout.Token);
            await other.ConnectAsync(IPAddress.Loopback, options.FallbackRelay.TcpPort, timeout.Token);
            await sender.GetStream().WriteAsync(Frame(0, Alice, Alice, []), timeout.Token);
            await blocked.GetStream().WriteAsync(Frame(0, Bob, Bob, []), timeout.Token);
            await other.GetStream().WriteAsync(Frame(0, Outsider, Outsider, []), timeout.Token);
            await WaitUntilAsync(() => service.Snapshot().RegisteredConnections == 3, timeout.Token);
            flood = FloodAsync();
            await WaitUntilAsync(() => service.Snapshot().PendingFrames == 32, timeout.Token);
            check(service.Snapshot().PendingFrames == 32 && !flood.IsCompleted,
                "a real non-reading recipient fills only its bounded queue and backpressures the TCP sender");
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var target = new IPEndPoint(IPAddress.Loopback, options.FallbackRelay.UdpPort);
            await udp.SendAsync(Frame(0, Alice, Bob, [0xD1]), target, timeout.Token);
            await udp.SendAsync(Frame(0, Alice, Outsider, [0xD2]), target, timeout.Token);
            await EqualAsync(other, Frame(3, Alice, Outsider, [0xD2]),
                "a full recipient queue cannot stall UDP delivery to another registered peer", check, timeout.Token);
            if (mode != "stop")
            {
                var droppedBefore = service.Snapshot().DroppedFrames;
                Volatile.Write(ref finishFlood, true);
                TcpClient markerSender = sender;
                if (mode == "context")
                {
                    contexts.SetRelayContext(senderSession, null);
                    contexts.SetRelayContext(senderSession, 7);
                }
                else
                {
                    var next = runtime.Add("channel:replacement", "127.0.0.1:41001");
                    contexts.RegisterChannelSession(next, IPAddress.Loopback);
                    contexts.AuthenticateRelayAccount(next.Id, Alice); contexts.SetRelayContext(next.Id, 7);
                    await replacement.ConnectAsync(IPAddress.Loopback, options.FallbackRelay.TcpPort, timeout.Token);
                    await replacement.GetStream().WriteAsync(Frame(0, Alice, Alice, []), timeout.Token);
                    await WaitUntilAsync(() => service.Snapshot().RegisteredConnections == 3
                        && service.Snapshot().Connections == 3, timeout.Token);
                    markerSender = replacement;
                }
                var drain = DrainToMarkerAsync();
                await flood;
                await markerSender.GetStream().WriteAsync(Frame(1, Alice, Bob, [0x5A]), timeout.Token);
                await drain;
                check(service.Snapshot().DroppedFrames - droppedBefore >= 32,
                    mode == "context" ? "actual queued frames cannot survive party leave/rejoin to the same id"
                        : "actual queued frames from the old game admission cannot reach a replacement incarnation");
            }
        }
        finally
        {
            await service.StopAsync(timeout.Token);
            sender.Dispose(); blocked.Dispose();
            await flood;
        }
        check(service.Snapshot().Connections == 0 && service.Snapshot().PendingFrames == 0,
            "host stop cancels a blocked writer and producer without leaked connections");

        async Task FloodAsync()
        {
            var frame = Frame(1, Alice, Bob, new byte[8192 - 12]);
            try { for (var i = 0; i < 4096 && !Volatile.Read(ref finishFlood); i++) await sender.GetStream().WriteAsync(frame, timeout.Token); }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
        }

        async Task DrainToMarkerAsync()
        {
            while (true)
            {
                var prefix = new byte[4]; await blocked.GetStream().ReadExactlyAsync(prefix, timeout.Token);
                var length = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(2));
                if (BinaryPrimitives.ReadUInt16LittleEndian(prefix) != 3 || length < 12 || length > 8192)
                    throw new InvalidDataException("Corrupted real relay output during queue retirement");
                var rest = new byte[length - 4]; await blocked.GetStream().ReadExactlyAsync(rest, timeout.Token);
                if (length == 13 && rest[^1] == 0x5A) return;
            }
        }
    }

    private static byte[] Frame(ushort opcode, uint sender, uint recipient, byte[] payload)
    {
        var result = new byte[12 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(result, opcode);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), checked((ushort)result.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), sender);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), recipient);
        payload.CopyTo(result, 12);
        return result;
    }

    private static async Task EqualAsync(TcpClient client, byte[] expected, string name,
        Action<bool, string> check, CancellationToken token)
    {
        var actual = new byte[expected.Length];
        await client.GetStream().ReadExactlyAsync(actual, token);
        check(actual.SequenceEqual(expected), name);
    }

    private static async Task<bool> EofAsync(TcpClient client, CancellationToken token)
    {
        try { return await client.GetStream().ReadAsync(new byte[1], token) == 0; }
        catch (IOException) { return true; }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(10, token);
    }
}
