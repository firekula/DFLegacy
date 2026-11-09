using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DFLegacy.Server;
using Microsoft.Extensions.Logging;

internal static class GameplayDatagramLifecycleSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var previousAudit = Environment.GetEnvironmentVariable("DFLEGACY_UDP_AUDIT");
        Environment.SetEnvironmentVariable("DFLEGACY_UDP_AUDIT", "1");
        try
        {
            await HealthyAndStopAsync(check);
            foreach (var listener in new[] { "primary", "secondary" })
                await ListenerFailureAsync(listener, check);
        }
        finally { Environment.SetEnvironmentVariable("DFLEGACY_UDP_AUDIT", previousAudit); }
    }

    private static async Task HealthyAndStopAsync(Action<bool, string> check)
    {
        var options = Options();
        var readiness = new ListenerReadiness(options);
        var logger = new ListenerLogger();
        using var service = new GameplayDatagramService(options, new RuntimeState(), logger, readiness);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(timeout.Token);
        try
        {
            await logger.Ready.Task.WaitAsync(timeout.Token);
            using var client = BindClient();
            foreach (var port in new[] { options.GameplayDatagram.PrimaryPort, options.GameplayDatagram.SecondaryPort })
            {
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                // Literal truncated MT data is not a listener failure. A later
                // real reflector response proves this same receive loop lives.
                await client.SendAsync(new byte[] { 0x7F }, endpoint, timeout.Token);
                await client.SendAsync(new byte[] { 1, 0 }, endpoint, timeout.Token);
                await ProbeAsync(client, endpoint, check, timeout.Token);
            }
            await Task.Delay(80, timeout.Token);
            check(!service.ExecuteTask!.IsCompleted && !readiness.Pending.Contains("gameplay")
                    && logger.Errors.IsEmpty,
                "malformed datagrams and a silent interval leave both gameplay UDP listeners ready");
        }
        finally { await service.StopAsync(timeout.Token); }
        check(service.ExecuteTask!.IsCompletedSuccessfully && readiness.Pending.Contains("gameplay")
                && logger.Errors.IsEmpty,
            "ordinary host stop cancels gameplay receives and audit without reporting a listener fault");
        CheckPortsReleased(options, check, "ordinary host stop");
    }

    private static async Task ListenerFailureAsync(string listener, Action<bool, string> check)
    {
        var options = Options();
        var readiness = new ListenerReadiness(options);
        var logger = new ListenerLogger();
        using var service = new GameplayDatagramService(options, new RuntimeState(), logger, readiness);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        await service.StartAsync(timeout.Token);
        try
        {
            await logger.Ready.Task.WaitAsync(timeout.Token);
            using var client = BindClient();
            var port = listener == "primary" ? options.GameplayDatagram.PrimaryPort : options.GameplayDatagram.SecondaryPort;
            await ProbeAsync(client, new IPEndPoint(IPAddress.Loopback, port), check, timeout.Token);
            // Break a real owned socket, not the protocol or global host token.
            // Reflection is confined to fault injection in this test.
            var socket = (UdpClient)typeof(GameplayDatagramService)
                .GetField("_" + listener, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
            socket.Dispose();

            Exception? failure = null;
            try { await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2), timeout.Token); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { failure = ex; }
            catch (TimeoutException) { }
            check(failure is not null && service.ExecuteTask!.IsFaulted,
                $"{listener} receive failure surfaces without waiting for the live sibling or audit loop");
            check(readiness.Pending.Contains("gameplay"),
                $"{listener} receive failure withdraws gameplay readiness before host shutdown");
            check(logger.Errors.Count == 1 && logger.Errors.TryPeek(out var error)
                    && error.Listener == listener && ReferenceEquals(error.Exception, failure),
                $"{listener} receive failure records its listener and original exception once");
            CheckPortsReleased(options, check, $"{listener} receive failure before host shutdown");
        }
        finally
        {
            await service.StopAsync(timeout.Token);
            // Observe the failure even on the RED path, where StopAsync is the
            // only operation that unblocks the otherwise healthy sibling.
            try { await service.ExecuteTask!; }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
        }
    }

    private static ServerOptions Options()
    {
        var options = new ServerOptions();
        options.GameplayDatagram.Host = "127.0.0.1";
        // Reserve both at once to guarantee distinct ephemeral loopback ports.
        using var primary = BindClient();
        using var secondary = BindClient();
        options.GameplayDatagram.PrimaryPort = ((IPEndPoint)primary.Client.LocalEndPoint!).Port;
        options.GameplayDatagram.SecondaryPort = ((IPEndPoint)secondary.Client.LocalEndPoint!).Port;
        return options;
    }

    private static UdpClient BindClient() => new(new IPEndPoint(IPAddress.Loopback, 0));

    private static async Task ProbeAsync(UdpClient client, IPEndPoint target,
        Action<bool, string> check, CancellationToken token)
    {
        await client.SendAsync(new byte[] { 1 }, target, token);
        var response = await client.ReceiveAsync(token);
        var port = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
        byte[] expected = [2, 1, 0, 0, 127, (byte)port, (byte)(port >> 8)];
        check(response.RemoteEndPoint.Equals(target) && response.Buffer.SequenceEqual(expected),
            "gameplay lifecycle fixture receives the original NAT reflection on the requested listener");
    }

    private static void CheckPortsReleased(ServerOptions options, Action<bool, string> check, string context)
    {
        foreach (var port in new[] { options.GameplayDatagram.PrimaryPort, options.GameplayDatagram.SecondaryPort })
        {
            try
            {
                using var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                check(true, $"{context} releases gameplay UDP port {port}");
            }
            catch (SocketException) { check(false, $"{context} releases gameplay UDP port {port}"); }
        }
    }

    private sealed class ListenerLogger : ILogger<GameplayDatagramService>
    {
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<(string? Listener, Exception? Exception)> Errors { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).StartsWith("Gameplay UDP primary/secondary listeners ready", StringComparison.Ordinal))
                Ready.TrySetResult();
            if (logLevel >= LogLevel.Error)
            {
                var values = state as IEnumerable<KeyValuePair<string, object?>>;
                Errors.Enqueue((values?.FirstOrDefault(value => value.Key == "Listener").Value?.ToString(), exception));
            }
        }
    }
}
