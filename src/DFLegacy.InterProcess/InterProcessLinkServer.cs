using System.Net;
using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.InterProcess;

/// <summary>
/// channel 侧链路服务端：接受 game 的 REGISTER，回 REGISTER_ACK，之后双向跑 STATUS（心跳）。
/// 只做校验与记账，不碰任何存档 —— 本刀链路不承载落盘变更（spec §5.4）。
/// </summary>
/// <remarks>
/// 三条容易写错的地方：
/// <list type="number">
/// <item><b><see cref="PeerUp"/> 由「已注册会话数」推导，不是一个布尔位。</b>game 重连时旧会话的收尾
/// 不能把新会话刚建立的 up 打回 down。</item>
/// <item><b><see cref="Stop"/> 幂等且要取快照</b>：它会被调用方和 <see cref="RunAsync"/> 的 finally 两方并发调用，
/// 直接枚举并清空同一个 List 会抛 "Collection was modified"（Task 4 在
/// <c>EntranceHandshakeServer.cs:73-89</c> 修过同一个坑）。</item>
/// <item><b>读包带上限</b>：链路帧最大 49 B（REGISTER），不设上限就等于允许对端用一个长度字段
/// 换来 4 MiB 的缓冲。</item>
/// </list>
/// </remarks>
public sealed class InterProcessLinkServer(
    IPAddress address,
    int port,
    RegisterAckPayload ack,
    Func<StatusPayload> status,
    TimeSpan statusInterval,
    Action<string> log)
{
    /// <summary>链路帧上限：最大的 REGISTER 是 49 B，给到 4 KiB 已经很宽松。</summary>
    public const int MaximumFrameLength = 4 * 1024;

    private readonly List<TcpListener> _listeners = [];
    private readonly HashSet<TcpClient> _clients = [];
    private readonly object _gate = new();
    private int _registeredSessions;
    private RegisterPayload? _lastRegister;
    private StatusPayload? _lastStatus;
    private long _lastStatusAtUtcTicks = -1;

    /// <summary>至少有一个 game 进程已完成注册（`accepted=1`）。</summary>
    public bool PeerUp => Volatile.Read(ref _registeredSessions) > 0;

    /// <summary>
    /// 最近一次成功解码的 REGISTER（**无论是否被接受**）：被拒时它仍是定位配置漂移的线索。
    /// </summary>
    public RegisterPayload? LastRegister => Volatile.Read(ref _lastRegister);

    /// <summary>game 进程最近一次发来的 STATUS（与客户端的 `LastRemoteStatus` 对称）。</summary>
    public StatusPayload? LastStatus => Volatile.Read(ref _lastStatus);

    /// <summary>最近一次收到 game STATUS 的时刻；从未收到为 null。</summary>
    public DateTimeOffset? LastStatusAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastStatusAtUtcTicks);
            return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>绑定链路端口，返回**实际**端口（传 0 时由内核分配）。</summary>
    public Task<int> StartAsync(CancellationToken cancellationToken)
    {
        // token 与共享握手宿主同样不参与绑定（本仓库既有形状），生命周期由调用方串行管理。
        var listener = new TcpListener(address, port);
        listener.Start();
        lock (_gate)
        {
            _listeners.Add(listener);
        }

        var bound = ((IPEndPoint)listener.LocalEndpoint).Port;
        log($"Inter-process link listening at {address}:{bound}");
        return Task.FromResult(bound);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        TcpListener[] listeners;
        lock (_gate)
        {
            listeners = [.. _listeners];
        }

        try
        {
            await Task.WhenAll(listeners.Select(listener => AcceptLoopAsync(listener, cancellationToken)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Stop();
        }
    }

    /// <summary>幂等：停监听 + 关掉已建立的会话（不留悬挂 handler）。</summary>
    public void Stop()
    {
        TcpListener[] listeners;
        TcpClient[] clients;
        lock (_gate)
        {
            listeners = [.. _listeners];
            _listeners.Clear();
            clients = [.. _clients];
            _clients.Clear();
        }

        foreach (var listener in listeners)
        {
            listener.Stop();
        }

        foreach (var client in clients)
        {
            try
            {
                client.Close();
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
            }
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.OperationAborted)
            {
                return;
            }
            catch (SocketException exception)
            {
                // 真实的 accept 错误不能静默停掉监听（与 EntranceHandshakeServer.cs:112-118 同形）。
                log($"Link accept failed: {exception.SocketErrorCode}");
                continue;
            }

            lock (_gate)
            {
                _clients.Add(client);
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var registered = false;
        using var sessionStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? statusWriter = null;
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                while (await PacketFrame.ReadAsync(
                           stream,
                           MaximumFrameLength,
                           sessionStop.Token) is { } frame)
                {
                    if (!InterProcessLink.TryGetMessageId(frame, out var messageId))
                    {
                        // 非链路 category 的帧直接丢弃：这条 socket 只承载链路报文。
                        continue;
                    }

                    switch (messageId)
                    {
                        case InterProcessMessageIds.Register:
                            if (registered)
                            {
                                // 同一连接上重复 REGISTER 只会让两个写者抢同一个 stream，忽略。
                                continue;
                            }

                            if (!RegisterPayload.TryDecode(frame.Body.AsSpan(1), out var register)
                                || register is null)
                            {
                                log($"Malformed REGISTER from {remote} ({frame.Body.Length - 1} payload bytes)");
                                continue;
                            }

                            Volatile.Write(ref _lastRegister, register);
                            var versionMatches = register.ProtocolVersion == InterProcessMessageIds.LinkProtocolVersion;
                            var reply = versionMatches
                                ? ack
                                : ack with { ProtocolVersion = InterProcessMessageIds.LinkProtocolVersion, Accepted = 0 };
                            await InterProcessLink.Create(InterProcessMessageIds.RegisterAck, reply.Encode())
                                .WriteAsync(stream, sessionStop.Token);
                            if (!versionMatches)
                            {
                                log(
                                    $"Game process {remote} rejected: link protocol {register.ProtocolVersion} != "
                                    + $"{InterProcessMessageIds.LinkProtocolVersion}");
                                break;
                            }

                            registered = true;
                            Interlocked.Increment(ref _registeredSessions);
                            log(
                                $"Game process registered from {remote} "
                                + $"(gc_no={register.GcNo}, channel_no={register.ChannelNo}, game_port={register.GamePort})");
                            // 双向心跳：对端靠「收得到」判活，本侧也按同一间隔发 STATUS。
                            statusWriter = StatusLoopAsync(stream, sessionStop.Token, remote);
                            break;
                        case InterProcessMessageIds.Status:
                            if (StatusPayload.TryDecode(frame.Body.AsSpan(1), out var status) && status is not null)
                            {
                                Volatile.Write(ref _lastStatus, status);
                                Interlocked.Exchange(ref _lastStatusAtUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
                            }

                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (sessionStop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            log($"Link peer {remote} dropped: {exception.Message}");
        }
        finally
        {
            sessionStop.Cancel();
            if (statusWriter is not null)
            {
                await QuietlyAsync(statusWriter);
            }

            if (registered)
            {
                Interlocked.Decrement(ref _registeredSessions);
            }

            lock (_gate)
            {
                _clients.Remove(client);
            }
        }
    }

    /// <summary>注册成功后本侧周期发 STATUS，直到会话结束（对端断开/本侧停止）。</summary>
    private async Task StatusLoopAsync(NetworkStream stream, CancellationToken token, string remote)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await InterProcessLink.Create(InterProcessMessageIds.Status, status().Encode())
                    .WriteAsync(stream, token);
                await Task.Delay(statusInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            log($"Link status writer for {remote} stopped: {exception.Message}");
        }
    }

    private static async Task QuietlyAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception) when (exception is IOException or SocketException
                                              or ObjectDisposedException or OperationCanceledException)
        {
        }
    }
}
