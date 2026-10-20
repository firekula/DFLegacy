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
    Action<string, Exception?> warn)
{
    private readonly List<TcpListener> _listeners = [];

    public IReadOnlyList<int> BoundPorts { get; private set; } = [];

    public Task<int[]> StartAsync(CancellationToken cancellationToken)
    {
        foreach (var port in ports.Where(port => port is >= 0 and <= 65535).Distinct())
        {
            var listener = new TcpListener(address, port);
            listener.Start();
            _listeners.Add(listener);
            log($"Entrance listener ready at {address}:{((IPEndPoint)listener.LocalEndpoint).Port}");
        }

        BoundPorts = _listeners.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port).ToArray();
        return Task.FromResult(BoundPorts.ToArray());
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(_listeners.Select(listener =>
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
        foreach (var listener in _listeners)
        {
            listener.Stop();
        }

        _listeners.Clear();
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
            var session = sink.Begin("entrance", remote);
            try
            {
                using var stream = client.GetStream();
                var state = new EntranceSessionState();
                while (await PacketFrame.ReadAsync(stream, cancellationToken: cancellationToken) is { } request)
                {
                    session.Received(request);
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
