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

    public static void RunPortOwnership(Action<bool, string> check)
    {
        // The shipped server.json: Entrance.Port=2311, AdditionalPorts=[7001,8080], Channel.Port=7001.
        // I1 acceptance ② is "2311/7001/8080 are all still listening in one pid", so the two
        // hosted services must partition exactly those three ports between them.
        var monolithic = new ServerOptions();
        var shipped = EntrancePortFamilies.Split(2311, [7001, 8080], 7001);
        check(shipped.EntrancePorts.SequenceEqual([2311, 8080]) && shipped.ChannelPorts.SequenceEqual([7001]),
            "shipped config: the handshake host owns 2311+8080 and the game listener owns 7001");
        check(!shipped.EntrancePorts.Contains(7001),
            "shipped config: the channel port is never bound by the handshake host (that would fail the bind)");
        check(shipped.EntrancePorts.Intersect(shipped.ChannelPorts).Count() == 0
                && shipped.EntrancePorts.Union(shipped.ChannelPorts).OrderBy(port => port)
                    .SequenceEqual([2311, 7001, 8080]),
            "shipped config: the two families are disjoint and together own every configured port exactly once");

        var defaults = EntrancePortFamilies.Split(
            monolithic.Entrance.Port,
            monolithic.Entrance.AdditionalPorts,
            monolithic.Channel.Port);
        check(defaults.EntrancePorts.SequenceEqual([monolithic.Entrance.Port])
                && defaults.ChannelPorts.SequenceEqual([monolithic.Channel.Port]),
            "ServerOptions defaults: the families are Entrance.Port and Channel.Port "
            + "(before the split the shared listener bound only Entrance.Port under these defaults)");
    }

    /// <summary>
    /// The sink is what makes the split invisible to admin: /api/sessions must keep showing
    /// <c>entrance:2311</c> and /api/status.TotalPackets must keep counting handshake packets.
    /// </summary>
    public static void RunRuntimeSinkAccounting(Action<bool, string> check)
    {
        var runtime = new RuntimeState();
        var sink = new RuntimeEntranceSink(runtime, tracePackets: false, hexDumpLimit: 0);
        var session = sink.Begin("entrance:2311", "127.0.0.1:50000");
        try
        {
            session.Received(PacketFrame.Create(11, new byte[32]));
            var snapshot = runtime.Sessions().Single();
            check(snapshot.Service == "entrance:2311",
                "the sink keeps the host's per-port label so /api/sessions can tell entrance:2311 from entrance:8080");
            check(snapshot.ReceivedPackets == 1 && snapshot.LastProtocolId == 11,
                "Received increments the session counter and records the protocol id");
            check(runtime.TotalPackets == 1,
                "Received goes through RuntimeState.CountPacket: /api/status.TotalPackets counts handshake packets "
                + "(a bare Interlocked.Increment on the session field would leave this at 0)");

            session.Sent(PacketFrame.Create(12, new byte[36]));
            snapshot = runtime.Sessions().Single();
            check(snapshot.SentPackets == 1 && runtime.TotalPackets == 1,
                "Sent increments the session's sent counter without inflating the received total");
        }
        finally
        {
            session.Dispose();
        }

        check(runtime.Sessions().Length == 0,
            "Dispose removes the session, so /api/sessions does not leak closed handshakes");
    }

    /// <summary>
    /// EntranceContentFactory has to reproduce what EntranceService.LoadContent (EntranceService.cs:17612-17660)
    /// assembles. The oracle is not our own builder: it is the metadata a real client actually received,
    /// captured in runs/20261018T091951Z-2724238-town-two-clients/*/netdump-*.log. That capture used
    /// Channel.Name="Local Channel", Port=7001, Number=1, MaximumUsers=100, CurrentUsers=1 and told the
    /// client to dial 192.168.3.177; decrypting the recorded protocol 3 reply yields 652 bytes
    /// (the last 4 bytes of the decrypted block are AES padding), re-encrypting reproduces the recorded
    /// ciphertext exactly. Reproduced on 5 netdump files with 4 different session keys.
    /// </summary>
    public static void RunContentFactory(Action<bool, string> check)
    {
        var channel = new EntranceChannel("Local Channel", "192.168.3.177", 7001, 1, 100, 1);
        const string recordedMetadataSha256 = "8E059F3A8BEF0DBC34F446007C259A60EE90C4A5DEBCA3981D490B72A0EE3493";
        var script = Enumerable.Range(0, 4096).Select(index => (byte)(index * 31 % 251)).ToArray();
        var reads = 0;
        var factory = new EntranceContentFactory(
            () =>
            {
                reads++;
                return script;
            },
            channel);

        var first = factory.Get();
        check(first.Metadata.Length == 652,
            "the factory assembles all nine server groups plus one channel record (652 bytes)");
        check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(first.Metadata))
                == recordedMetadataSha256,
            "the metadata bytes are byte-identical to what a real client received before the split "
            + "(recorded netdump protocol 3 plaintext)");
        check(first.ChannelScript is not null && first.ChannelScript.SequenceEqual(script),
            "the channel script is passed through unchanged, so protocol 9 stays byte-identical");

        var second = factory.Get();
        check(ReferenceEquals(first, second) && reads == 1,
            "the content is assembled once and cached afterwards, like EntranceService.LoadContent");
    }

    /// <summary>
    /// End-to-end replay of the recorded client handshake against the host that now serves it:
    /// the four request frames the client actually sent (11, 5, 9, 1) go in, and the replies are
    /// decrypted with the key the host handed out, then compared with the recorded plaintext.
    /// </summary>
    public static async Task RunHandshakeReplayAsync(Action<bool, string> check)
    {
        var channel = new EntranceChannel("Local Channel", "192.168.3.177", 7001, 1, 100, 1);
        const string recordedMetadataBlockSha256 =
            "59FC373C8527F02F3392A88344B93F8EA160B47F309655D1C5B9871317DB08CD";
        const string recordedCachePlaintext =
            "0000000064666C732D6C6F63616C000000000000000000000000000000000000";
        var script = Enumerable.Range(0, 4096).Select(index => (byte)(index * 31 % 251)).ToArray();
        var content = new EntranceContentFactory(() => script, channel);
        var sink = new RecordingSink();
        var server = new EntranceHandshakeServer(
            System.Net.IPAddress.Loopback,
            [0],
            content.Get,
            sink,
            _ => { },
            (_, _) => { });
        var ports = await server.StartAsync(CancellationToken.None);
        using var stop = new CancellationTokenSource();
        var run = server.RunAsync(stop.Token);
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Loopback, ports[0]);
            var stream = client.GetStream();

            // Recorded client request order and payload sizes: 11 with 32 bytes, 5 with 16, 9 empty, 1 empty.
            await PacketFrame.Create(11, new byte[32]).WriteAsync(stream);
            var keyReply = await TryReadFrameAsync(stream, TimeSpan.FromSeconds(5));
            check(keyReply is { ProtocolId: 12 } && keyReply.Body.Length == 37,
                "replay: protocol 11 is answered by 12 with a 36-byte payload (record count included)");
            if (keyReply is null || keyReply.ProtocolId != 12 || keyReply.Body.Length != 37)
            {
                return;
            }

            check(sink.LastService == $"entrance:{ports[0]}",
                "replay: the host labels the session with its local port, so /api/sessions keeps entrance:<port>");

            // Body[0] is the record count; the 36-byte payload is int32(0) followed by the 32-byte key material.
            var key = keyReply.Body[5..37];

            await PacketFrame.Create(5, new byte[16]).WriteAsync(stream);
            var cacheReply = await TryReadFrameAsync(stream, TimeSpan.FromSeconds(5));
            check(cacheReply is { ProtocolId: 6 },
                "replay: protocol 5 is answered by 6");
            if (cacheReply is not null)
            {
                var plain = EntranceCrypto.DecryptBlocks(cacheReply.Body[1..], key);
                check(Convert.ToHexString(plain) == recordedCachePlaintext,
                    "replay: protocol 6 carries the same cache decision and session token the client saw before the split");
            }

            await PacketFrame.Create(9, []).WriteAsync(stream);
            var scriptReply = await TryReadFrameAsync(stream, TimeSpan.FromSeconds(5));
            check(scriptReply is { ProtocolId: 10 },
                "replay: protocol 9 is answered by 10");
            if (scriptReply is not null)
            {
                // EncryptCompressed == Compress(EncryptBlocks(...)), so the inverse decompresses first.
                var plain = EntranceCrypto.DecryptBlocks(EntranceCrypto.Decompress(scriptReply.Body[1..]), key);
                check(plain.SequenceEqual(script),
                    "replay: protocol 10 carries the channel script unchanged");
            }

            await PacketFrame.Create(1, []).WriteAsync(stream);
            var metadataReply = await TryReadFrameAsync(stream, TimeSpan.FromSeconds(5));
            check(metadataReply is { ProtocolId: 3 },
                "replay: protocol 1 is answered by 3");
            if (metadataReply is not null)
            {
                // The decrypted block is 656 bytes = 652 metadata + 4 zero bytes of AES block padding,
                // which is exactly what the recorded client received (recorded block sha256 59FC37...).
                var plain = EntranceCrypto.DecryptBlocks(EntranceCrypto.Decompress(metadataReply.Body[1..]), key);
                check(plain.Length == 656
                        && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(plain))
                            == recordedMetadataBlockSha256
                        && plain.AsSpan(0, 652).SequenceEqual(content.Get().Metadata),
                    "replay: the metadata the host sends is byte-identical to the recorded real-client plaintext");
            }
        }
        finally
        {
            server.Stop();
            await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(2)));
            stop.Cancel();
            await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(2)));
        }
    }

    /// <summary>
    /// The old shared read loop honoured <c>options.RejectBadCrc32</c> (HEAD EntranceService.cs:4432)
    /// before dispatching to either protocol family, and read frames with <c>options.MaximumPacketLength</c>.
    /// The new host has to keep that: with the flag on, a frame whose declared CRC32 is wrong must end
    /// the session without a reply; with the flag off (the shipped value) the same frame is still answered.
    /// </summary>
    public static async Task RunCrcRejectionAsync(Action<bool, string> check)
    {
        var content = new EntranceContent(null, [1, 2, 3, 4]);
        // A protocol 11 request that carries a deliberately wrong CRC32.
        var correct = PacketFrame.Create(11, new byte[32], includeCrc32: true);
        var badCrc = new PacketFrame(
            correct.Type,
            correct.ProtocolId,
            correct.DeclaredCrc32 ^ 0xFFFFFFFFu,
            correct.Body);

        var rejecting = await ProbeAsync(rejectBadCrc32: true);
        check(rejecting is { Reply: null, Warned: true },
            "with RejectBadCrc32 on, a wrong-CRC32 frame gets no reply and is warned about "
            + "(the old shared read loop rejected it at EntranceService.cs:4432)");

        var accepting = await ProbeAsync(rejectBadCrc32: false);
        check(accepting.Reply is { ProtocolId: 12 },
            "with RejectBadCrc32 off (the shipped value) the same frame is still answered, "
            + "so the flag is wired in both directions");

        // The read cap must be wired too: a cap smaller than the frame makes ReadAsync throw and the
        // session end, so the frame is never answered (the old loop passed options.MaximumPacketLength).
        var capped = await ProbeAsync(rejectBadCrc32: false, maximumPacketLength: 20);
        check(capped.Reply is null,
            "a frame larger than the configured maximum packet length is not answered");

        async Task<(PacketFrame? Reply, bool Warned)> ProbeAsync(
            bool rejectBadCrc32,
            int maximumPacketLength = PacketFrame.DefaultMaximumLength)
        {
            var warned = 0;
            var server = new EntranceHandshakeServer(
                System.Net.IPAddress.Loopback,
                [0],
                () => content,
                new RecordingSink(),
                _ => { },
                (_, _) => Interlocked.Increment(ref warned),
                maximumPacketLength,
                rejectBadCrc32);
            var ports = await server.StartAsync(CancellationToken.None);
            using var stop = new CancellationTokenSource();
            var run = server.RunAsync(stop.Token);
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                await client.ConnectAsync(System.Net.IPAddress.Loopback, ports[0]);
                var stream = client.GetStream();
                await badCrc.WriteAsync(stream);
                var reply = await TryReadFrameAsync(stream, TimeSpan.FromSeconds(5));
                return (reply, Volatile.Read(ref warned) > 0);
            }
            finally
            {
                server.Stop();
                await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(2)));
                stop.Cancel();
                await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(2)));
            }
        }
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
        Exception? runFault = null;
        if (run.IsCompleted)
        {
            try
            {
                await run;
            }
            catch (Exception exception)
            {
                runFault = exception;
            }
        }
        else
        {
            runFault = new TimeoutException("RunAsync neither completed nor faulted after Stop()");
        }

        check(runFault is null,
            "RunAsync completes without faulting after Stop(): a pending accept is interrupted as SocketException");
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
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException)
        {
            // The peer closed (FIN) or reset (RST) the connection. A reset happens when the host
            // abandons a session with unread bytes still buffered — e.g. the oversized-frame case.
            // That is "no reply" and must fail the check by name, not crash the whole suite.
            return null;
        }
    }

    private sealed class RecordingSink : IEntranceSessionSink
    {
        private readonly List<RecordingSession> _sessions = [];

        public RecordingSession? Last { get; private set; }

        public string? LastService { get; private set; }

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
            LastService = service;
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
