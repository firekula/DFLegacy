using DFLegacy.Protocol;
using System.Net;
using System.Net.Sockets;

internal static class FallbackRelayProtocolSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        // Literal from ACT1 sub_40F290's complete normal-login read sequence.
        // The generic serializer has no authenticated account/relay endpoint;
        // the live channel must replace those fields with its verified identity.
        byte[] expected =
        [
            1, 0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            1, 0
        ];
        var response = GameProtocolEngine.Handle(new PacketFrame(
            GameProtocolEngine.CommandPacketType, GameProtocolEngine.LoginCommand, 0, [0, 0]));
        check(response is not null,
            "a sequence-bearing login reaches the normal reply serializer");
        check(response is { Payload.Length: 23 },
            "ACT1 normal login includes all fields consumed after the four flags");
        check(response?.Payload.SequenceEqual(expected) == true,
            "ACT1 unavailable relay still has explicit UID, address length, ports and profile bytes");
        LoginFixtures(check);
        FrameFixtures(check);
        await StreamFramesAsync(check);
    }

    private static void LoginFixtures(Action<bool, string> check)
    {
        check(GameProtocolEngine.CreateChannelLoginSuccess(0x11223344, "127.0.0.1", 7005, 7005)
                .Payload.SequenceEqual(Convert.FromHexString(
                    "010000000044332211090000003132372E302E302E315D1B00005D1B00000100")),
            "normal login literal preserves authenticated account and native relay endpoint layout");
        check(GameProtocolEngine.CreatePvpLoginSuccess(0x11223344, "127.0.0.1", 7005, 7005)
                .Payload.SequenceEqual(Convert.FromHexString(
                    "010000040044332211090000003132372E302E302E315D1B00005D1B00000100")),
            "complete PvP login keeps its original mode4 wire bytes");
        foreach (var host in new[] { "::1", "not-an-ip", "127.0.0.1:7005" })
            Rejects<ArgumentException>(() => GameProtocolEngine.CreateChannelLoginSuccess(1, host, 7005, 7005),
                check, "native login rejects a non-IPv4 relay address");
        Rejects<ArgumentException>(() => GameProtocolEngine.CreateChannelLoginSuccess(1, "", 7005, 0),
            check, "unavailable relay cannot advertise a nonzero port");
        Rejects<ArgumentException>(() => GameProtocolEngine.CreateChannelLoginSuccess(1, "127.0.0.1", 0, 7005),
            check, "advertised relay requires both valid transport ports");
        Rejects<ArgumentException>(() => GameProtocolEngine.CreateChannelLoginSuccess(1, "127.0.0.1", 65536, 7005),
            check, "native relay port cannot exceed ushort range");
    }

    private static void FrameFixtures(Action<bool, string> check)
    {
        byte[] registration = Convert.FromHexString("00000C004433221144332211");
        check(NativeFallbackRelayProtocol.TryReadHeader(registration, out var registrationHeader)
                && registrationHeader == new NativeFallbackRelayHeader(0, 12, 0x11223344, 0x11223344),
            "native registration fixture carries sender and recipient at offsets4 and8");
        byte[] tcp = Convert.FromHexString("0100110044332211887766556D7400FF7A");
        byte[] udp = Convert.FromHexString("0000110044332211887766556D7400FF7A");
        byte[] delivered = Convert.FromHexString("0300110044332211887766556D7400FF7A");
        check(NativeFallbackRelayProtocol.CreateDelivery(tcp).SequenceEqual(delivered),
            "TCP relay changes only the native opcode1 to receive3");
        check(NativeFallbackRelayProtocol.CreateDelivery(udp).SequenceEqual(delivered),
            "UDP relay changes only the native opcode0 to receive3");
        check(tcp.SequenceEqual(Convert.FromHexString("0100110044332211887766556D7400FF7A"))
                && udp.SequenceEqual(Convert.FromHexString("0000110044332211887766556D7400FF7A")),
            "native relay conversion leaves caller-owned input bytes untouched");
        check(NativeFallbackRelayProtocol.TryReadHeader(delivered, out var header)
                && header == new NativeFallbackRelayHeader(3, 17, 0x11223344, 0x55667788),
            "received frame exposes its original sender and target identities");
        foreach (var bad in new[]
        {
            Array.Empty<byte>(), new byte[11],
            Convert.FromHexString("010000004433221188776655"),
            Convert.FromHexString("010001204433221188776655"),
            Convert.FromHexString("02000C004433221188776655")
        })
            check(!NativeFallbackRelayProtocol.TryReadHeader(bad, out _),
                "native relay rejects short, zero, over-limit and unknown-opcode headers");
        Rejects<InvalidDataException>(() => NativeFallbackRelayProtocol.CreateDelivery(tcp[..^1]),
            check, "relay cannot forward a truncated declared frame");
        Rejects<InvalidDataException>(() => NativeFallbackRelayProtocol.CreateDelivery([.. tcp, 0xAA]),
            check, "relay cannot forward trailing bytes from a second UDP datagram");
        Rejects<InvalidDataException>(() => NativeFallbackRelayProtocol.CreateDelivery(delivered),
            check, "client input cannot relay an already-delivered server opcode");
    }

    private static async Task StreamFramesAsync(Action<bool, string> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var sender = new TcpClient();
            var connect = sender.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
            using var receiver = await listener.AcceptTcpClientAsync(timeout.Token);
            await connect;
            var input = receiver.GetStream();
            var output = sender.GetStream();
            byte[] registration = Convert.FromHexString("00000C004433221144332211");
            byte[] data = Convert.FromHexString("0100110044332211887766556D7400FF7A");
            var firstRead = NativeFallbackRelayProtocol.ReadTcpFrameAsync(input, timeout.Token);
            for (var i = 0; i < 5; i++)
                await output.WriteAsync(registration.AsMemory(i, 1), timeout.Token);
            await output.WriteAsync(new byte[] { 0x33, 0x22, 0x11, 0x44, 0x33, 0x22, 0x11 }
                .Concat(data).ToArray(), timeout.Token);
            check((await firstRead)?.SequenceEqual(registration) == true,
                "native TCP framing survives a split length and split account fields");
            check((await NativeFallbackRelayProtocol.ReadTcpFrameAsync(input, timeout.Token))?.SequenceEqual(data) == true,
                "native TCP framing retains a coalesced following packet");
            sender.Client.Shutdown(SocketShutdown.Send);
            check(await NativeFallbackRelayProtocol.ReadTcpFrameAsync(input, timeout.Token) is null,
                "clean TCP EOF has no fabricated empty frame");
        }
        finally { listener.Stop(); }

        foreach (var bad in new[] { new byte[] { 1, 0 }, Convert.FromHexString("0100110044332211") })
        {
            try
            {
                using var input = new MemoryStream(bad);
                await NativeFallbackRelayProtocol.ReadTcpFrameAsync(input, timeout.Token);
                check(false, "partial TCP EOF must reject an incomplete frame");
            }
            catch (EndOfStreamException) { check(true, "partial TCP EOF rejects incomplete frame"); }
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            using var input = new MemoryStream(Convert.FromHexString("00000C004433221144332211"));
            await NativeFallbackRelayProtocol.ReadTcpFrameAsync(input, cancelled.Token);
            check(false, "cancelled framing read must honor host cancellation");
        }
        catch (OperationCanceledException) { check(true, "framing read honors host cancellation"); }
    }

    private static void Rejects<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); check(false, name); }
        catch (T) { check(true, name); }
    }
}
