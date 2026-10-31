using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DFLegacy.InterProcess;
using DFLegacy.Protocol;

/// <summary>
/// Task 6（S2 / I3 前半）：链路客户端 + spec §7.1 Tier 1 的第 1/2/3/4 项。
/// </summary>
/// <remarks>
/// 分工：编解码的精确字节（REGISTER 49 B 的固定 hex、各载荷字段位置）已由
/// <see cref="InterProcessCodecSmokeTests"/> 覆盖，本组补的是**客户端真的把那些字节写到 socket 上**、
/// 心跳重复、对端 STATUS 的捕获口径，以及 accepted=0 / 静默判死两条失败语义。
/// 每个读、每次等退出都有超时：静默的对端必须变成具名 FAIL，不能把整套 smoke 挂住。
/// 不声称：Task 7 的服务端、端到端两进程连通、真机。
/// </remarks>
internal static class InterProcessLinkSmokeTests
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>心跳间隔。静默判死窗口 = StatusInterval × MissedStatusLimit = 300 ms，够宽以免调度抖动误判。</summary>
    private static readonly TimeSpan StatusInterval = TimeSpan.FromMilliseconds(100);

    private const int MissedStatusLimit = 3;

    /// <summary>49 B = 10 B 头 + 1 B 记录数 + 38 B REGISTER 载荷（spec §7.1 第 1 项）。</summary>
    private const int RegisterFrameLength = PacketFrame.HeaderLength + 1 + RegisterPayload.Length;

    /// <summary>43 B = 10 B 头 + 1 B 记录数 + 32 B STATUS 载荷。</summary>
    private const int StatusFrameLength = PacketFrame.HeaderLength + 1 + StatusPayload.Length;

    private static RegisterPayload Register => new(
        ProtocolVersion: InterProcessMessageIds.LinkProtocolVersion,
        GcNo: 3011,
        ChannelNo: 11,
        MaximumUsers: 100,
        CurrentUsers: 1,
        AdvertisedHost: "127.0.0.1",
        GamePort: 7001);

    public static async Task RunAsync(Action<bool, string> check)
    {
        await RunHappyPathAsync(check);
        await RunRejectedAckAsync(check);
        await RunSilentPeerAsync(check);
        await RunServerEndToEndAsync(check);
        await RunServerRejectsVersionAsync(check);
    }

    /// <summary>
    /// Task 7：真实服务端 + 真实客户端（同一进程、两个 socket）的端到端连通，双向都验。
    /// </summary>
    private static async Task RunServerEndToEndAsync(Action<bool, string> check)
    {
        // 两个方向刻意给不同的值：把「对端发来的」和「自己发的」写反时必红。
        var gameStatus = new StatusPayload(UptimeMs: 5, ActiveSessions: 1, LoggedInUsers: 0, TotalConnections: 2, TotalPackets: 4);
        var channelStatus = new StatusPayload(UptimeMs: 77, ActiveSessions: 3, LoggedInUsers: 1, TotalConnections: 9, TotalPackets: 11);
        var ack = new RegisterAckPayload(InterProcessMessageIds.LinkProtocolVersion, 1, "Local Channel 1", 7001);
        var logs = new List<string>();
        var server = new InterProcessLinkServer(
            IPAddress.Loopback,
            0,
            ack,
            () => channelStatus,
            StatusInterval,
            message =>
            {
                lock (logs)
                {
                    logs.Add(message);
                }
            });

        var linkPort = await server.StartAsync(CancellationToken.None);
        check(linkPort > 0, "the link server binds an ephemeral port when asked for port 0");
        using var serverStop = new CancellationTokenSource();
        var serverRun = server.RunAsync(serverStop.Token);
        using var clientStop = new CancellationTokenSource();
        InterProcessLinkClient? client = null;
        Task? clientRun = null;
        try
        {
            client = CreateClient(linkPort, gameStatus, null, _ => { });
            clientRun = client.RunAsync(clientStop.Token);

            var up = await PollAsync(() => client.Connected && server.PeerUp, WaitTimeout);
            check(up, "client and server agree the link is up after REGISTER / REGISTER_ACK");
            check(server.LastRegister is { GcNo: 3011, ChannelNo: 11, GamePort: 7001 },
                "the server decodes gc_no / channel_no / game_port out of REGISTER");
            check(client.LastAck is { Accepted: 1, ChannelPort: 7001, ChannelName: "Local Channel 1" },
                "the client decodes the channel identity out of REGISTER_ACK");

            check(await PollAsync(() => server.LastStatus == gameStatus, WaitTimeout),
                "game→channel STATUS is decoded and stored by the server");
            check(server.LastStatusAt is not null,
                "the server stamps LastStatusAt, so a silent game process is observable");
            check(await PollAsync(() => client.LastRemoteStatus == channelStatus, WaitTimeout),
                "channel→game STATUS reaches the client, so end-to-end is bidirectional");

            // game 侧断开后，服务端必须自己降下来（不是等 Stop）。
            clientStop.Cancel();
            var clientStopped = await AwaitQuietlyAsync(clientRun, WaitTimeout);
            check(clientStopped.Completed && clientStopped.Fault is null,
                $"the link client stops cleanly{Describe(clientStopped.Fault)}");
            check(await PollAsync(() => !server.PeerUp, WaitTimeout),
                "when the game side disconnects the server reports PeerUp=false on its own");
        }
        finally
        {
            clientStop.Cancel();
            if (clientRun is not null)
            {
                await AwaitQuietlyAsync(clientRun, WaitTimeout);
            }

            if (client is not null)
            {
                await client.DisposeAsync();
            }

            serverStop.Cancel();
            await AwaitQuietlyAsync(serverRun, WaitTimeout);
            server.Stop();
        }

        check(CanBind(linkPort), "the link server releases its port after RunAsync completes and Stop() runs");
        lock (logs)
        {
            check(logs.Any(line => line.Contains("Game process registered from", StringComparison.Ordinal)),
                "the server logs the registration together with the decoded ids");
        }
    }

    /// <summary>Task 7：版本不匹配的 REGISTER 必须回 accepted=0，双方都不建链。</summary>
    private static async Task RunServerRejectsVersionAsync(Action<bool, string> check)
    {
        var ack = new RegisterAckPayload(InterProcessMessageIds.LinkProtocolVersion, 1, "Local Channel 1", 7001);
        var logs = new List<string>();
        var server = new InterProcessLinkServer(
            IPAddress.Loopback,
            0,
            ack,
            () => new StatusPayload(1, 0, 0, 0, 0),
            StatusInterval,
            message =>
            {
                lock (logs)
                {
                    logs.Add(message);
                }
            });

        var linkPort = await server.StartAsync(CancellationToken.None);
        using var serverStop = new CancellationTokenSource();
        var serverRun = server.RunAsync(serverStop.Token);
        using var clientStop = new CancellationTokenSource();
        InterProcessLinkClient? client = null;
        Task? clientRun = null;
        try
        {
            var newerRegister = Register with
            {
                ProtocolVersion = (ushort)(InterProcessMessageIds.LinkProtocolVersion + 1),
            };
            client = CreateClient(linkPort, new StatusPayload(1, 0, 0, 0, 0), null, _ => { }, newerRegister);
            clientRun = client.RunAsync(clientStop.Token);

            var rejected = await PollAsync(
                () => client.LastAck is { Accepted: 0 } && !client.Connected && !server.PeerUp,
                WaitTimeout);
            check(rejected,
                "a REGISTER with a newer link protocol version is answered with accepted=0 and neither side links up");
            check(server.LastRegister is { ProtocolVersion: InterProcessMessageIds.LinkProtocolVersion + 1 },
                "the server still records the attempted version from the rejected REGISTER (config-drift evidence)");
            lock (logs)
            {
                check(logs.Any(line => line.Contains("rejected", StringComparison.OrdinalIgnoreCase)),
                    "the rejection is logged with the reason");
            }
        }
        finally
        {
            clientStop.Cancel();
            if (clientRun is not null)
            {
                await AwaitQuietlyAsync(clientRun, WaitTimeout);
            }

            if (client is not null)
            {
                await client.DisposeAsync();
            }

            serverStop.Cancel();
            await AwaitQuietlyAsync(serverRun, WaitTimeout);
            server.Stop();
        }
    }

    private static bool CanBind(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// 正常链路：拨号 → REGISTER（线上字节）→ ACK → Connected/PeerUp → 周期 STATUS（心跳）→
    /// 捕获对端 STATUS。同时钉住「TCP 连上 ≠ 链路 up」这条口径。
    /// </summary>
    private static async Task RunHappyPathAsync(Action<bool, string> check)
    {
        // 自己发的 STATUS 与对端要发的刻意不同：口径写反（把自发的当成对端的）时必然变红。
        var ownStatus = new StatusPayload(UptimeMs: 12, ActiveSessions: 7, LoggedInUsers: 3, TotalConnections: 4, TotalPackets: 5);
        var peerStatus = new StatusPayload(UptimeMs: 99, ActiveSessions: 2, LoggedInUsers: 0, TotalConnections: 5, TotalPackets: 6);
        var ups = 0;
        var downs = new List<string>();

        using var peer = FakeLinkPeer.Start();
        await using var client = CreateClient(peer.Port, ownStatus,
            () => Interlocked.Increment(ref ups),
            reason =>
            {
                lock (downs)
                {
                    downs.Add(reason);
                }
            });
        using var stop = new CancellationTokenSource();
        var run = client.RunAsync(stop.Token);
        var peerPulse = new CancellationTokenSource();
        Task? pulse = null;
        try
        {
            using var accepted = await peer.AcceptAsync(WaitTimeout);
            check(accepted is not null, "the link client dials the configured link port");
            if (accepted is null)
            {
                return;
            }

            var stream = accepted.GetStream();

            // ---- Tier 1 第 1/2 项：客户端写到线上的 REGISTER 就是那个固定布局 ----
            var register = await TryReadRawAsync(stream, ReadTimeout);
            check(register is not null, "the first frame arrives within the read timeout");
            if (register is null)
            {
                return;
            }

            check(register.Header[0] == InterProcessMessageIds.LinkCategory
                    && register.Header[1] == InterProcessMessageIds.Register,
                "on the wire: [0]=0x7b category and [1]=0x80 REGISTER, not only inside the codec");
            check(register.DeclaredLength == RegisterFrameLength
                    && register.DeclaredLength == 11 + RegisterPayload.Length,
                $"on the wire: size={RegisterFrameLength} = 11 + 38 payload bytes (spec §7.1 item 2)");
            check(register.Body.Length == 1 + RegisterPayload.Length,
                "on the wire: the declared size really matches the bytes that followed it");
            check(register.Header.AsSpan(6, 4).IndexOfAnyExcept((byte)0) < 0,
                "on the wire: [6..9] CRC32 field stays zero (the link frame never carries a CRC)");
            check(register.RecordCount == 1,
                "on the wire: [10] is the record count and it is 1");
            check(register.Body.Length > 0
                    && RegisterPayload.TryDecode(register.Body.AsSpan(1), out var decodedRegister)
                    && decodedRegister == Register,
                "on the wire: the 38-byte REGISTER payload decodes back to the configured record (field order pinned)");

            check(!client.Connected,
                "a TCP connection alone does not mark the link up: Connected stays false until REGISTER_ACK is accepted");
            var nothingElse = await TryReadRawAsync(stream, TimeSpan.FromMilliseconds(200));
            check(nothingElse is null,
                "the client writes exactly one REGISTER frame and then waits for the ACK (no duplicate, no preface)");

            // ---- Tier 1 第 3 项的正向分支：accepted=1 ⇒ up ----
            var ack = new RegisterAckPayload(InterProcessMessageIds.LinkProtocolVersion, 1, "Local Channel 1", 7001);
            check(await TryWriteFrameAsync(stream, InterProcessLink.Create(InterProcessMessageIds.RegisterAck, ack.Encode()), ReadTimeout),
                "the fake peer could write REGISTER_ACK back to the client");
            var upSeen = await PollAsync(() => client.Connected && Volatile.Read(ref ups) == 1, WaitTimeout);
            check(upSeen, "an accepted REGISTER_ACK flips Connected and fires PeerUp exactly once");
            check(client.LastAck is { Accepted: 1, ChannelPort: 7001, ChannelName: "Local Channel 1" },
                "LastAck keeps the decoded REGISTER_ACK for admin/reporting");

            // ---- 心跳：稳态周期发 43 B STATUS，内容是调用方给的快照 ----
            var firstStatus = await TryReadRawAsync(stream, ReadTimeout);
            check(firstStatus is not null
                    && firstStatus.ProtocolId == InterProcessMessageIds.Status
                    && firstStatus.DeclaredLength == StatusFrameLength,
                $"steady state sends a {StatusFrameLength}-byte STATUS (11 + 32) as the heartbeat");
            if (firstStatus is not null && firstStatus.Body.Length > 0)
            {
                check(StatusPayload.TryDecode(firstStatus.Body.AsSpan(1), out var sent) && sent == ownStatus,
                    "the heartbeat carries the caller's STATUS snapshot, not a zero-filled placeholder");
            }

            var secondStatus = await TryReadRawAsync(stream, ReadTimeout);
            check(secondStatus is not null && secondStatus.ProtocolId == InterProcessMessageIds.Status,
                "STATUS repeats on the configured interval while the link stays up (single heartbeat needs no reply)");

            // ---- 对端 STATUS 的捕获口径 ----
            // 对端像真 channel 一样持续发 STATUS：否则客户端自己的静默窗口会在断言期间到期，
            // 把「健康链路」误判成 down（这也是测试脆性来源，特意在这里做对）。
            pulse = WriteStatusLoopAsync(stream, peerStatus, peerPulse.Token);
            var remoteSeen = await PollAsync(() => client.LastRemoteStatus == peerStatus, WaitTimeout);
            check(remoteSeen,
                "LastRemoteStatus is the peer's STATUS (admin merges that one into /api/status, not the one this client sends)");
            check(client.LastStatusAt is not null,
                "LastStatusAt is stamped when the peer's STATUS arrives, so a stale peer is observable");

            // 先停客户端、再让假对端关 socket：反过来的话「对端关闭」会被如实记成 PeerDown，
            // 「健康链路没有 down」这条断言就变成在测自己的收尾顺序了。
            stop.Cancel();
            var completed = await AwaitQuietlyAsync(run, WaitTimeout);
            check(completed.Completed && completed.Fault is null,
                $"RunAsync returns without faulting after cancellation{Describe(completed.Fault)}");
            check(!client.Connected, "cancelling the link client leaves Connected false");
            lock (downs)
            {
                check(downs.Count == 0,
                    $"a healthy link reports no PeerDown at all (got: {string.Join(" | ", downs)})");
            }
        }
        finally
        {
            // 收尾只做清理，不再断言：走到这里时对端 socket 可能已经关了。
            peerPulse.Cancel();
            if (pulse is not null)
            {
                await AwaitQuietlyAsync(pulse, TimeSpan.FromSeconds(2));
            }

            peerPulse.Dispose();
            stop.Cancel();
            await AwaitQuietlyAsync(run, WaitTimeout);
        }
    }

    /// <summary>
    /// Tier 1 第 3 项：accepted=0 ⇒ 不建链、不崩、退避重试；版本不匹配同样不建链。
    /// </summary>
    private static async Task RunRejectedAckAsync(Action<bool, string> check)
    {
        var ownStatus = new StatusPayload(1, 0, 0, 0, 0);
        var downs = new List<string>();
        using var peer = FakeLinkPeer.Start();
        await using var client = CreateClient(peer.Port, ownStatus, null, reason =>
        {
            lock (downs)
            {
                downs.Add(reason);
            }
        });
        using var stop = new CancellationTokenSource();
        var run = client.RunAsync(stop.Token);
        try
        {
            using (var first = await peer.AcceptAsync(WaitTimeout))
            {
                check(first is not null, "the rejected-ACK scenario connects at all");
                if (first is null)
                {
                    return;
                }

                var stream = first.GetStream();
                await TryReadRawAsync(stream, ReadTimeout);
                var rejected = new RegisterAckPayload(InterProcessMessageIds.LinkProtocolVersion, 0, "x", 7001);
                await TryWriteFrameAsync(stream, InterProcessLink.Create(InterProcessMessageIds.RegisterAck, rejected.Encode()), ReadTimeout);
                // 一次 poll 同时等「状态」与「PeerDown 已发出」：LastAck 先写、PeerDown 后发，
                // 分成两次会让后者偶发读不到（实测过一次偶发失败）。
                var observed = await PollAsync(
                    () => client.LastAck is { Accepted: 0 }
                          && !client.Connected
                          && HasDownReason(downs, "accepted=0"),
                    WaitTimeout);
                check(observed,
                    "accepted=0 never marks the link up, but LastAck still records the rejection (spec §5.3) and PeerDown reports it");
            }

            // 退避后必须再拨一次 —— Tier 1 第 3 项的「game 退避重试」。
            using (var second = await peer.AcceptAsync(WaitTimeout))
            {
                check(second is not null, "a rejected REGISTER_ACK is retried after the backoff instead of giving up");
                if (second is null)
                {
                    return;
                }

                var stream = second.GetStream();
                await TryReadRawAsync(stream, ReadTimeout);
                var wrongVersion = new RegisterAckPayload(
                    (ushort)(InterProcessMessageIds.LinkProtocolVersion + 1), 1, "x", 7001);
                await TryWriteFrameAsync(stream, InterProcessLink.Create(InterProcessMessageIds.RegisterAck, wrongVersion.Encode()), ReadTimeout);
                var mismatchSeen = await PollAsync(
                    () => client.LastAck is { Accepted: 1 } ack
                          && ack.ProtocolVersion != InterProcessMessageIds.LinkProtocolVersion
                          && !client.Connected
                          && HasDownReason(downs, "version mismatch"),
                    WaitTimeout);
                check(mismatchSeen,
                    "accepted=1 with a different link protocol version is still rejected (a half-upgraded pair must not link) "
                    + "and PeerDown says why");
            }
        }
        finally
        {
            stop.Cancel();
            var completed = await AwaitQuietlyAsync(run, WaitTimeout);
            check(completed.Completed && completed.Fault is null,
                $"the retrying client stops cleanly on cancellation{Describe(completed.Fault)}");
        }
    }

    /// <summary>
    /// Tier 1 第 4 项：对端静默 <see cref="MissedStatusLimit"/> 个心跳周期 ⇒ 标记 down 并重连，
    /// 且客户端自己继续跑（不因链路问题退出）。
    /// </summary>
    private static async Task RunSilentPeerAsync(Action<bool, string> check)
    {
        var ownStatus = new StatusPayload(1, 0, 0, 0, 0);
        var downs = new List<string>();
        using var peer = FakeLinkPeer.Start();
        await using var client = CreateClient(peer.Port, ownStatus, null, reason =>
        {
            lock (downs)
            {
                downs.Add(reason);
            }
        });
        using var stop = new CancellationTokenSource();
        var run = client.RunAsync(stop.Token);
        try
        {
            using (var first = await peer.AcceptAsync(WaitTimeout))
            {
                check(first is not null, "the silent-peer scenario connects at all");
                if (first is null)
                {
                    return;
                }

                var stream = first.GetStream();
                await TryReadRawAsync(stream, ReadTimeout);
                var ack = new RegisterAckPayload(InterProcessMessageIds.LinkProtocolVersion, 1, "Local Channel 1", 7001);
                await TryWriteFrameAsync(stream, InterProcessLink.Create(InterProcessMessageIds.RegisterAck, ack.Encode()), ReadTimeout);
                var upSeen = await PollAsync(() => client.Connected, WaitTimeout);
                check(upSeen, "the silent-peer scenario reaches the up state first");

                // 从这一刻起对端一个字节都不发：客户端应该在自己的静默窗口到期后判 down。
                var declared = await PollAsync(
                    () => !client.Connected && HasDownReason(downs, "sent nothing"),
                    WaitTimeout);
                check(declared,
                    $"{MissedStatusLimit} missed status intervals of complete silence mark the link down with a reason");
            }

            using var second = await peer.AcceptAsync(WaitTimeout);
            check(second is not null,
                "after declaring the peer down the client reconnects on its own (the game process keeps serving clients)");
        }
        finally
        {
            stop.Cancel();
            var completed = await AwaitQuietlyAsync(run, WaitTimeout);
            check(completed.Completed && completed.Fault is null,
                $"the client survives the down/reconnect cycle and stops cleanly on cancellation{Describe(completed.Fault)}");
        }
    }

    private static InterProcessLinkClient CreateClient(
        int port,
        StatusPayload ownStatus,
        Action? onUp,
        Action<string> onDown,
        RegisterPayload? register = null) =>
        new(new InterProcessLinkClientOptions(
            "127.0.0.1", port, StatusInterval, MissedStatusLimit, register ?? Register, () => ownStatus))
        {
            PeerUp = onUp,
            PeerDown = onDown,
        };

    /// <summary>假对端的心跳：像真 channel 一样周期性发 STATUS，直到取消。</summary>
    private static async Task WriteStatusLoopAsync(NetworkStream stream, StatusPayload status, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await InterProcessLink.Create(InterProcessMessageIds.Status, status.Encode())
                    .WriteAsync(stream, token);
                await Task.Delay(StatusInterval, token);
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException
                                              or ObjectDisposedException or OperationCanceledException)
        {
            // 对端 socket 关了/取消了：本辅助循环安静退出，由断言决定测试成败。
        }
    }

    private static bool HasDownReason(List<string> downs, string fragment)
    {
        lock (downs)
        {
            return downs.Any(reason => reason.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed record AwaitOutcome(bool Completed, Exception? Fault);

    private static string Describe(Exception? fault) =>
        fault is null ? string.Empty : $" ({fault.GetType().Name}: {fault.Message})";

    /// <summary>等一个任务在超时内结束；返回是否结束以及（若有）故障，不把故障抛给整套 smoke。</summary>
    private static async Task<AwaitOutcome> AwaitQuietlyAsync(Task task, TimeSpan timeout)
    {
        var finished = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(finished, task))
        {
            return new AwaitOutcome(false, null);
        }

        try
        {
            await task;
            return new AwaitOutcome(true, null);
        }
        catch (Exception exception)
        {
            return new AwaitOutcome(true, exception);
        }
    }

    private static async Task<bool> PollAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return predicate();
    }

    private sealed record RawFrame(byte[] Header, byte[] Body)
    {
        public byte ProtocolId => Header[1];

        public uint DeclaredLength => BinaryPrimitives.ReadUInt32LittleEndian(Header.AsSpan(2, 4));

        public byte RecordCount => Body.Length == 0 ? (byte)0 : Body[0];
    }

    /// <summary>读一个 10 B 头 + 声明长度的载荷；超时或断链返回 null（不抛）。</summary>
    private static async Task<RawFrame?> TryReadRawAsync(NetworkStream stream, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            var header = new byte[PacketFrame.HeaderLength];
            if (!await TryReadExactAsync(stream, header, cancellation.Token))
            {
                return null;
            }

            var declared = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));
            if (declared < PacketFrame.HeaderLength || declared > PacketFrame.DefaultMaximumLength)
            {
                return new RawFrame(header, []);
            }

            var body = new byte[declared - PacketFrame.HeaderLength];
            return body.Length == 0 || await TryReadExactAsync(stream, body, cancellation.Token)
                ? new RawFrame(header, body)
                : null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static async Task<bool> TryWriteFrameAsync(NetworkStream stream, PacketFrame frame, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await frame.WriteAsync(stream, cancellation.Token);
            return true;
        }
        catch (Exception exception) when (exception is IOException or SocketException
                                              or ObjectDisposedException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> TryReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), token);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private sealed class FakeLinkPeer : IDisposable
    {
        private readonly TcpListener _listener;

        private FakeLinkPeer(TcpListener listener)
        {
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public static FakeLinkPeer Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new FakeLinkPeer(listener);
        }

        /// <summary>接受下一个连接；超时/监听已停返回 null。（只拉本组自己的 socket。）</summary>
        public async Task<TcpClient?> AcceptAsync(TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                return await _listener.AcceptTcpClientAsync(cancellation.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
            }
        }
    }
}
