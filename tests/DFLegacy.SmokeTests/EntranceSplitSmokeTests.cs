using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class EntranceSplitSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        var (entrance, channel) = EntrancePortFamilies.Split(2311, [7001, 8080], 7001);
        check(entrance.SequenceEqual([2311, 8080]) && channel.SequenceEqual([7001]),
            "entrance family = Entrance.Port + AdditionalPorts minus Channel.Port");
        check(EntrancePortFamilies.Split(2311, [7001, 8080], 7001).EntrancePorts
                .All(port => port != 7001),
            "the channel port never lands in the entrance family");
        var (deduped, _) = EntrancePortFamilies.Split(2311, [2311, 0, 70000, 8080, 8080], 7001);
        check(deduped.SequenceEqual([2311, 8080]),
            "duplicates and out-of-range ports are dropped while order is preserved");
        // 配置里 AdditionalPorts 不保证升序；声明序必须原样保留（排序实现会在这里变红）。
        var (ordered, _) = EntrancePortFamilies.Split(2311, [1000, 8080], 7001);
        check(ordered.SequenceEqual([2311, 1000, 8080]),
            "entrance ports keep declaration order (2311, 1000, 8080) instead of being sorted");
        var (single, singleChannel) = EntrancePortFamilies.Split(8080, [], 7001);
        check(single.SequenceEqual([8080]) && singleChannel.SequenceEqual([7001]),
            "defaults (Entrance.Port=8080, no AdditionalPorts) still yield both families");

        var monolithic = new ProcessOptions();
        monolithic.Validate();
        check(!monolithic.Channel.Enabled && monolithic.Game.EntranceBinding
                && monolithic.Channel.LinkPort == 7004,
            "defaults describe today's single-process mode with link port 7004");

        var both = new ProcessOptions();
        both.Channel.Enabled = true;
        both.Game.EntranceBinding = true;
        var threw = false;
        try { both.Validate(); }
        catch (InvalidOperationException) { threw = true; }
        check(threw,
            "enabling the channel process while the game process still binds the entrance ports is rejected");

        var zeroLinkPort = new ProcessOptions();
        zeroLinkPort.Channel.LinkPort = 0;
        var zeroLinkPortThrew = false;
        try { zeroLinkPort.Validate(); }
        catch (InvalidOperationException) { zeroLinkPortThrew = true; }
        check(zeroLinkPortThrew,
            "LinkPort=0 is rejected: the link port must be within 1..65535");

        var oversizedLinkPort = new ProcessOptions();
        oversizedLinkPort.Channel.LinkPort = 70000;
        var oversizedLinkPortThrew = false;
        try { oversizedLinkPort.Validate(); }
        catch (InvalidOperationException) { oversizedLinkPortThrew = true; }
        check(oversizedLinkPortThrew,
            "LinkPort=70000 is rejected: the link port must be within 1..65535");

        var split = new ProcessOptions();
        split.Channel.Enabled = true;
        split.Game.EntranceBinding = false;
        var splitAccepted = true;
        try { split.Validate(); }
        catch (InvalidOperationException) { splitAccepted = false; }
        check(splitAccepted,
            "a valid split layout (Channel.Enabled=true, Game.EntranceBinding=false) is accepted by Validate()");

        check(EntranceDialHost.Resolve("0.0.0.0", "127.0.0.1", "1.2.3.4") == "127.0.0.1",
            "a wildcard advertised host falls back to Entrance.Host");
        check(EntranceDialHost.Resolve("", "", "1.2.3.4") == "1.2.3.4",
            "with nothing advertised, a concrete Channel.Host is used as the dial target");
        check(EntranceDialHost.Resolve("192.168.1.5", "127.0.0.1", "0.0.0.0") == "192.168.1.5",
            "an advertised gameplay host wins over the entrance host");
        check(EntranceDialHost.IsDialable("example.local"),
            "a non-IP hostname is dialable, mirroring EntranceService.IsDialableHost");
        check(EntranceDialHost.IsDialable("255.255.255.255"),
            "the broadcast address is dialable, matching the existing helper that never excluded it");
        check(!EntranceDialHost.IsDialable("0.0.0.0"),
            "the IPv4 wildcard 0.0.0.0 is not dialable");
        check(!EntranceDialHost.IsDialable("::"),
            "the IPv6 wildcard :: is not dialable");
        check(!EntranceDialHost.IsDialable("  "),
            "a whitespace host is not dialable");
        check(EntranceDialHost.Resolve("example.local", "127.0.0.1", "0.0.0.0") == "example.local",
            "a hostname advertised host wins over a loopback entrance host");
    }

    public static async Task RunHandshakeServerAsync(Action<bool, string> check)
    {
        var content = new EntranceContent(null, [1, 2, 3, 4]);
        var sink = new RecordingSink();
        var server = new EntranceHandshakeServer(
            System.Net.IPAddress.Loopback,
            [0],
            () => content,
            sink,
            _ => { },
            (_, _) => { });
        var ports = await server.StartAsync(CancellationToken.None);
        using var stop = new CancellationTokenSource();
        var run = server.RunAsync(stop.Token);
        using (var client = new System.Net.Sockets.TcpClient())
        {
            await client.ConnectAsync(System.Net.IPAddress.Loopback, ports[0]);
            var stream = client.GetStream();
            var request = PacketFrame.Create(11, new byte[32]);
            await request.WriteAsync(stream);
            var reply = await PacketFrame.ReadAsync(stream);
            check(reply is not null && reply.ProtocolId == 12 && reply.Body.Length == 37,
                "protocol 11 (0x0b) is answered by protocol 12 with a 36-byte payload plus record count");
            check(sink.Last is { ReceivedCount: 1, SentCount: 1 },
                "the session sink sees one received and one sent frame");
        }

        stop.Cancel();
        await run;
        server.Stop();
    }

    private sealed class RecordingSink : IEntranceSessionSink
    {
        public RecordingSession? Last { get; private set; }

        public IEntranceSession Begin(string service, string remote) => Last = new RecordingSession();
    }

    private sealed class RecordingSession : IEntranceSession
    {
        public int ReceivedCount { get; private set; }
        public int SentCount { get; private set; }

        public void Received(PacketFrame frame) => ReceivedCount++;

        public void Sent(PacketFrame frame) => SentCount++;

        public void Dispose()
        {
        }
    }
}
