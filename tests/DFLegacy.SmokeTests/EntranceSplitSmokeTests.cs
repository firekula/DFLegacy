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
        var warnCount = 0;
        var server = new EntranceHandshakeServer(
            System.Net.IPAddress.Loopback,
            [0],
            () => content,
            sink,
            _ => { },
            (_, _) => Interlocked.Increment(ref warnCount));
        var ports = await server.StartAsync(CancellationToken.None);
        using var stop = new CancellationTokenSource();
        var run = server.RunAsync(stop.Token);

        using (var client = new System.Net.Sockets.TcpClient())
        {
            await client.ConnectAsync(System.Net.IPAddress.Loopback, ports[0]);
            var stream = client.GetStream();
            var request = PacketFrame.Create(11, new byte[32]);
            await request.WriteAsync(stream);
            // A host that stops replying must fail this check by name within five
            // seconds; reading without a token would hang the whole --s2-ipc run.
            var reply = await TryReadFrameAsync(stream, TimeSpan.FromSeconds(5));
            check(reply is not null,
                "the host answers protocol 11 within five seconds (a silent host fails by name, it does not hang)");
            check(reply is not null && reply.ProtocolId == 12 && reply.Body.Length == 37,
                "protocol 11 (0x0b) is answered by protocol 12 with a 36-byte payload plus record count");
            var sinkObserved = false;
            for (var attempt = 0; attempt < 50 && !sinkObserved; attempt++)
            {
                sinkObserved = sink.Last is { ReceivedCount: 1, SentCount: 1 };
                if (!sinkObserved)
                {
                    await Task.Delay(20);
                }
            }

            check(sinkObserved, "the session sink sees one received and one sent frame");
        }

        using (var silent = new System.Net.Sockets.TcpClient())
        {
            await silent.ConnectAsync(System.Net.IPAddress.Loopback, ports[0]);
            var silentStream = silent.GetStream();
            await PacketFrame.Create(0x7f, new byte[8]).WriteAsync(silentStream);
            var unexpected = await TryReadFrameAsync(silentStream, TimeSpan.FromSeconds(5));
            check(unexpected is null, "an unknown protocol (0x7f) gets no reply within five seconds");
            // Checked while this connection is still open: closing it must not be
            // what produces the warn under test.
            check(Volatile.Read(ref warnCount) == 1,
                "the unknown protocol triggers exactly one warn callback");
        }

        using (var truncated = new System.Net.Sockets.TcpClient())
        {
            await truncated.ConnectAsync(System.Net.IPAddress.Loopback, ports[0]);
            var truncatedStream = truncated.GetStream();
            // Declares a 100-byte frame (10-byte header + record count + 89-byte body)
            // but sends only 12 bytes, so the host hits EndOfStream mid-body.
            var partialFrame = PacketFrame.Create(5, new byte[89]).Encode();
            await truncatedStream.WriteAsync(partialFrame.AsMemory(0, 12));
            await truncatedStream.FlushAsync();
        }

        var disposedTruncatedSession = false;
        for (var attempt = 0; attempt < 50 && !disposedTruncatedSession; attempt++)
        {
            var sessions = sink.Sessions;
            disposedTruncatedSession = sessions.Length == 3 && sessions[2].DisposedCount == 1;
            if (!disposedTruncatedSession)
            {
                await Task.Delay(20);
            }
        }

        check(disposedTruncatedSession,
            "a truncated frame ends the session through the exception path and still runs Dispose");

        server.Stop();
        var stopped = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(2)));
        check(stopped == run,
            "Stop() ends the accept loops so RunAsync completes within two seconds");

        stop.Cancel();
        await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(2)));
        if (run.IsFaulted)
        {
            // Measured on .NET 10: Stop() aborts a pending accept with
            // SocketException(OperationAborted), which AcceptLoopAsync does not catch,
            // so RunAsync faults after Stop(). Recorded in task-3-report.md §7; the
            // plan promises completion here, which is what the check above pins.
            _ = run.Exception;
        }
    }

    /// <summary>
    /// Reads one frame, but gives up after <paramref name="timeout"/>. Returns null both for
    /// a clean end-of-stream and for a timeout so a silent host is a named check failure
    /// instead of an uncaught exception or an indefinite hang.
    /// </summary>
    private static async Task<PacketFrame?> TryReadFrameAsync(
        System.Net.Sockets.NetworkStream stream,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            return await PacketFrame.ReadAsync(stream, cancellationToken: timeoutSource.Token);
        }
        catch (Exception exception) when (timeoutSource.IsCancellationRequested
                                          && exception is OperationCanceledException or IOException)
        {
            return null;
        }
    }

    private sealed class RecordingSink : IEntranceSessionSink
    {
        private readonly List<RecordingSession> _sessions = [];

        public RecordingSession? Last { get; private set; }

        public RecordingSession[] Sessions
        {
            get
            {
                lock (_sessions)
                {
                    return [.. _sessions];
                }
            }
        }

        public IEntranceSession Begin(string service, string remote)
        {
            var session = new RecordingSession();
            lock (_sessions)
            {
                _sessions.Add(session);
            }

            Last = session;
            return session;
        }
    }

    private sealed class RecordingSession : IEntranceSession
    {
        private int _receivedCount;
        private int _sentCount;
        private int _disposedCount;

        public int ReceivedCount => Volatile.Read(ref _receivedCount);
        public int SentCount => Volatile.Read(ref _sentCount);
        public int DisposedCount => Volatile.Read(ref _disposedCount);

        public void Received(PacketFrame frame) => Interlocked.Increment(ref _receivedCount);

        public void Sent(PacketFrame frame) => Interlocked.Increment(ref _sentCount);

        public void Dispose() => Interlocked.Increment(ref _disposedCount);
    }
}
