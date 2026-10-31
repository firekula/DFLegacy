using System.Net;
using System.Net.Sockets;

namespace DFLegacy.Protocol;

/// <summary>
/// 入口握手族的监听与读循环（协议 11/5/9/1 → 12/6/10/3）。
/// 无 ASP.NET 依赖：日志与记账都通过构造参数注入，因此同一份实现可以跑在
/// DFLegacy.Server（单体模式）或 DFLegacy.Channel（拆分模式）里。
/// </summary>
public sealed class EntranceHandshakeServer(
    IPAddress address,
    IReadOnlyList<int> ports,
    Func<EntranceContent> content,
    IEntranceSessionSink sink,
    Action<string> log,
    Action<string, Exception?> warn,
    int maximumPacketLength = PacketFrame.DefaultMaximumLength,
    bool rejectBadCrc32 = false,
    bool noDelay = true)
{
    private readonly List<TcpListener> _listeners = [];
    private readonly object _listenersGate = new();

    public IReadOnlyList<int> BoundPorts { get; private set; } = [];

    public Task<int[]> StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var port in ports.Where(port => port is >= 0 and <= 65535).Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var listener = new TcpListener(address, port);
                try { listener.Start(); }
                catch { listener.Stop(); throw; }
                lock (_listenersGate)
                {
                    _listeners.Add(listener);
                }

                log($"Entrance listener ready at {address}:{((IPEndPoint)listener.LocalEndpoint).Port}");
            }

            int[] bound;
            lock (_listenersGate)
            {
                bound = _listeners.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port).ToArray();
            }

            BoundPorts = bound;
            return Task.FromResult(bound.ToArray());
        }
        catch { Stop(); BoundPorts = []; throw; }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        TcpListener[] listeners;
        lock (_listenersGate)
        {
            listeners = [.. _listeners];
        }

        try
        {
            await Task.WhenAll(listeners.Select(listener =>
                AcceptLoopAsync(listener, cancellationToken)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Stop();
        }
    }

    public void Stop()
    {
        // Stop() 会被两方并发调用：宿主显式 Stop()，以及 RunAsync 的 finally。两者都枚举并清空
        // 同一个 List，不设门就会抛 "Collection was modified"（实测：--s2-ipc 随机红在 Stop 里）。
        // 取快照后释放锁再 Stop，避免持锁调用阻塞的 socket 操作；重复 Stop 现在是幂等的。
        TcpListener[] listeners;
        lock (_listenersGate)
        {
            listeners = [.. _listeners];
            _listeners.Clear();
        }

        foreach (var listener in listeners)
        {
            listener.Stop();
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
                return;   // 只吞 Stop() 造成的那一种
            }
            catch (SocketException exception)
            {
                // 真实的 accept 错误不能静默停掉监听：打 warn 后继续 accept
                // （与 src/DFLegacy.Relay.Core/RelayHost.cs:48-52 同形）。
                warn($"Accept failed: {exception.SocketErrorCode}", exception);
                continue;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        using (client)
        {
            // 这三个 socket/读参数必须与旧共用读循环一致（HEAD：client.NoDelay 在 EntranceService.cs:207、
            // 读长度 options.MaximumPacketLength 在 :4201、坏 CRC 拒绝在 :4432），否则拆分后入口族的
            // 收包语义会悄悄变：NoDelay 影响时延，读长度影响可接受的最大帧，RejectBadCrc32 影响是否断会话。
            client.NoDelay = noDelay;
            // 会话标签带本地端口（entrance:2311 / entrance:8080），与既有 EntranceService 的
            // 标签口径一致（HEAD EntranceService.cs:221；工作区因未提交改动在 :224）。admin 的
            // /api/sessions 直接显示它，只写 "entrance" 会让两个入口端口在管理面上不可区分。
            var localPort = (client.Client.LocalEndPoint as IPEndPoint)?.Port;
            var service = localPort is { } port ? $"entrance:{port}" : "entrance";
            var session = sink.Begin(service, remote);
            try
            {
                using var stream = client.GetStream();
                var state = new EntranceSessionState();
                while (await PacketFrame.ReadAsync(stream, maximumPacketLength, cancellationToken) is { } request)
                {
                    session.Received(request);
                    if (rejectBadCrc32 && !request.HasValidCrc32)
                    {
                        // 与旧共用读循环同位置、同语义：计数与 LastProtocolId 已经记过，然后断会话。
                        warn($"Rejected frame with bad CRC32 from {remote}", null);
                        break;
                    }

                    var reply = EntranceProtocolEngine.Handle(request, state, content());
                    if (reply is null)
                    {
                        warn(
                            $"No entrance handler for packet type {request.Type}, protocol {request.ProtocolId}; frame retained in logs.",
                            null);
                        continue;
                    }

                    await reply.WriteAsync(stream, cancellationToken);
                    session.Sent(reply);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warn($"Entrance session {remote} ended.", exception);
            }
            finally
            {
                session.Dispose();
            }
        }
    }
}
