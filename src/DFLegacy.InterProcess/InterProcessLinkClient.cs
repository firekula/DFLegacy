using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.InterProcess;

/// <summary>game 侧链路客户端的参数（spec §5.3/§5.6）。</summary>
/// <param name="Host">链路对端地址（channel 进程）。</param>
/// <param name="Port">链路端口，默认 7004（`Processes.Channel.LinkPort`）。</param>
/// <param name="StatusInterval">稳态下发送 STATUS 的间隔，同时是心跳的计量单位。</param>
/// <param name="MissedStatusLimit">连续多少个 <paramref name="StatusInterval"/> 没收到任何帧就判对端 down。</param>
/// <param name="Register">REGISTER 载荷（38 B，见 RegisterPayload）。</param>
/// <param name="Status">每次发 STATUS 前取一次快照，由调用方决定统计口径。</param>
public sealed record InterProcessLinkClientOptions(
    string Host,
    int Port,
    TimeSpan StatusInterval,
    int MissedStatusLimit,
    RegisterPayload Register,
    Func<StatusPayload> Status)
{
    /// <summary>
    /// 等 REGISTER_ACK 的上限。它与稳态的静默判死不是同一个尺度：注册阶段允许宽一些，
    /// 否则对端慢启动/调度抖动会被误判成「注册失败」并进入无谓的重连循环；
    /// 稳态心跳仍按 <see cref="StatusInterval"/> × <see cref="MissedStatusLimit"/> 判死。
    /// </summary>
    public TimeSpan AckTimeout
    {
        get
        {
            var scaled = StatusInterval * MissedStatusLimit;
            return scaled < MinimumAckTimeout ? MinimumAckTimeout : scaled;
        }
    }

    private static readonly TimeSpan MinimumAckTimeout = TimeSpan.FromSeconds(2);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Link host must not be empty.", nameof(Host));
        }

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Link port must be in 1..65535.");
        }

        if (StatusInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StatusInterval), StatusInterval, "Status interval must be positive.");
        }

        if (MissedStatusLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MissedStatusLimit), MissedStatusLimit, "Missed status limit must be at least 1.");
        }
    }
}

/// <summary>
/// game 侧链路客户端：拨号 → REGISTER → 等 REGISTER_ACK → 周期性 STATUS（兼心跳）→
/// 连续 <see cref="InterProcessLinkClientOptions.MissedStatusLimit"/> 个间隔没有收到任何帧就判对端 down，
/// 退避后重连。失败一律不抛给调用方：game 进程不因链路问题停止服务客户端（spec §5.3）。
/// </summary>
/// <remarks>
/// 三条口径容易写错，改之前先看这里：
/// <list type="number">
/// <item><b><see cref="Connected"/> = 已注册成功</b>，不是「TCP 连上了」。spec §5.3 规定
/// <c>REGISTER_ACK.accepted=0</c> 不建链，所以 ACK 之前的 TCP 连接不算 up。</item>
/// <item><b><see cref="LastRemoteStatus"/> 是对端发来的那条</b>，不是自己发出去的那条 ——
/// admin 合并要用对端的计数（Task 8 的口径）。</item>
/// <item><b>判死只看收</b>：单向心跳没有回包可等，若按「STATUS 有没有被回应」判，健康链路会被误杀；
/// 所以只有当连续若干个间隔<b>一帧都没收到</b>时才判 down。</item>
/// </list>
/// 线程模型：属性由 RunAsync 的循环写、由调用方（admin/测试）读，全部用 volatile/Interlocked，
/// 不提供跨线程的 happens-before 之外的更强保证。
/// </remarks>
public sealed class InterProcessLinkClient(InterProcessLinkClientOptions options) : IAsyncDisposable
{
    private readonly InterProcessLinkClientOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    private CancellationTokenSource? _stop;
    private int _running;
    private volatile bool _connected;
    private RegisterAckPayload? _lastAck;
    private StatusPayload? _lastRemoteStatus;
    private long _lastStatusAtUtcTicks = -1;

    /// <summary>已注册成功（`accepted=1` 且版本一致）。TCP 连上但还没 ACK 时仍是 false。</summary>
    public bool Connected => _connected;

    /// <summary>最近一次收到的 REGISTER_ACK；被拒绝时也保留（`Accepted=0` 可观测）。</summary>
    public RegisterAckPayload? LastAck => Volatile.Read(ref _lastAck);

    /// <summary>对端最近一次发来的 STATUS。**不是**我们自己发出去的那条。</summary>
    public StatusPayload? LastRemoteStatus => Volatile.Read(ref _lastRemoteStatus);

    /// <summary>最近一次收到对端 STATUS 的时刻；从未收到为 null。</summary>
    public DateTimeOffset? LastStatusAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastStatusAtUtcTicks);
            return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>链路进入 up（只在对端接受注册后触发）。</summary>
    public Action? PeerUp { get; init; }

    /// <summary>链路断开或注册被拒，参数是原因文本（给日志/admin 用，不抛）。</summary>
    public Action<string>? PeerDown { get; init; }

    /// <summary>
    /// 跑链路直到 <paramref name="cancellationToken"/> 取消。永不向调用方抛异常（除取消本身与
    /// 重复调用的编程错误）。CTS 由本方法持有：退出时才释放，所以 DisposeAsync 取消它不会撞到已释放对象。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _options.Validate();
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            throw new InvalidOperationException("InterProcessLinkClient.RunAsync is already running.");
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _stop = stop;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var tcp = new TcpClient();
                    await tcp.ConnectAsync(_options.Host, _options.Port, stop.Token).ConfigureAwait(false);
                    using var stream = tcp.GetStream();
                    if (!await TryRegisterAsync(stream, stop.Token).ConfigureAwait(false))
                    {
                        await DelayBeforeRetryAsync(stop.Token).ConfigureAwait(false);
                        continue;
                    }

                    await RunSteadyStateAsync(stream, stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    MarkDown($"link failed: {exception.Message}");
                    await DelayBeforeRetryAsync(stop.Token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _connected = false;
            _stop = null;
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>REGISTER → 等 ACK。返回 false 表示这一轮没建链（原因已经通过 PeerDown 报过）。</summary>
    private async Task<bool> TryRegisterAsync(NetworkStream stream, CancellationToken token)
    {
        await InterProcessLink.Create(InterProcessMessageIds.Register, _options.Register.Encode())
            .WriteAsync(stream, token)
            .ConfigureAwait(false);

        // 对端可能接了连接却不回 ACK：必须有自己的超时，否则重连循环永远停在这一步。
        using var ackWindow = CancellationTokenSource.CreateLinkedTokenSource(token);
        ackWindow.CancelAfter(_options.AckTimeout);
        PacketFrame? reply;
        try
        {
            reply = await PacketFrame.ReadAsync(stream, cancellationToken: ackWindow.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return Reject("no REGISTER_ACK within the ack window");
        }

        if (reply is null)
        {
            return Reject("peer closed the link before REGISTER_ACK");
        }

        if (!InterProcessLink.TryGetMessageId(reply, out var messageId)
            || messageId != InterProcessMessageIds.RegisterAck)
        {
            return Reject(
                $"expected REGISTER_ACK (0x{InterProcessMessageIds.RegisterAck:x2}), got protocol 0x{reply.ProtocolId:x2}");
        }

        if (!RegisterAckPayload.TryDecode(reply.Body.AsSpan(1), out var ack) || ack is null)
        {
            return Reject($"malformed REGISTER_ACK ({reply.Body.Length - 1} payload bytes)");
        }

        // 先存再判：被拒绝的 ACK 也要可观测（admin/测试靠它区分「没回」与「回了但拒绝」）。
        Volatile.Write(ref _lastAck, ack);
        if (ack.Accepted == 0)
        {
            return Reject($"REGISTER_ACK rejected by the peer (accepted=0, version={ack.ProtocolVersion})");
        }

        if (ack.ProtocolVersion != InterProcessMessageIds.LinkProtocolVersion)
        {
            return Reject(
                $"link protocol version mismatch: peer={ack.ProtocolVersion}, local={InterProcessMessageIds.LinkProtocolVersion}");
        }

        _connected = true;
        PeerUp?.Invoke();
        return true;
    }

    /// <summary>稳态：一个写循环（STATUS/心跳）+ 一个读循环（收对端帧），任一先结束就把整轮拉倒。</summary>
    private async Task RunSteadyStateAsync(NetworkStream stream, CancellationToken token)
    {
        using var loop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var writer = WriteLoopAsync(stream, loop.Token);
        var reader = ReadLoopAsync(stream, loop.Token);
        var finished = await Task.WhenAny(writer, reader).ConfigureAwait(false);
        loop.Cancel();
        try
        {
            await Task.WhenAll(writer, reader).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 另一个循环是被我们自己取消的，正常。
        }

        // 把「谁先结束」的原因抛出去：读循环会抛 IOException（静默/断链），外层据此判 down 并重连。
        await finished.ConfigureAwait(false);
    }

    private async Task WriteLoopAsync(NetworkStream stream, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var status = _options.Status();
            await InterProcessLink.Create(InterProcessMessageIds.Status, status.Encode())
                .WriteAsync(stream, token)
                .ConfigureAwait(false);
            await Task.Delay(_options.StatusInterval, token).ConfigureAwait(false);
        }
    }

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var silence = CancellationTokenSource.CreateLinkedTokenSource(token);
            silence.CancelAfter(_options.StatusInterval * _options.MissedStatusLimit);
            PacketFrame? frame;
            try
            {
                frame = await PacketFrame.ReadAsync(stream, cancellationToken: silence.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new IOException(
                    $"peer sent nothing for {_options.MissedStatusLimit} status intervals");
            }

            if (frame is null)
            {
                throw new IOException("link peer closed the connection");
            }

            if (InterProcessLink.TryGetMessageId(frame, out var messageId)
                && messageId == InterProcessMessageIds.Status
                && StatusPayload.TryDecode(frame.Body.AsSpan(1), out var status)
                && status is not null)
            {
                Volatile.Write(ref _lastRemoteStatus, status);
                Interlocked.Exchange(ref _lastStatusAtUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
            }
        }
    }

    private bool Reject(string reason)
    {
        MarkDown(reason);
        return false;
    }

    private void MarkDown(string reason)
    {
        _connected = false;
        PeerDown?.Invoke(reason);
    }

    /// <summary>重连退避。取消时不抛：调用方靠 while 条件与 finally 收尾。</summary>
    private async Task DelayBeforeRetryAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_options.StatusInterval, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// 只<b>请求</b>停止，不等待、也不释放 CTS —— CTS 归 RunAsync 的 using 所有，退出时才释放；
    /// 这里若释放，跑动中的读/写循环会拿到已释放的 token 源。
    /// 需要「等它真的停下来」的调用方应当 await 自己持有的 <see cref="RunAsync"/> 任务。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        try
        {
            _stop?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经退出了。
        }

        return ValueTask.CompletedTask;
    }
}
