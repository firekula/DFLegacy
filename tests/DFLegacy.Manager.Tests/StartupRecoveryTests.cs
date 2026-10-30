using System.Net;
using System.Net.Sockets;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class StartupRecoveryTests
{
    internal static async Task<int> RunAsync()
    {
        var failures = 0;
        void Check(bool passed, string name) { Console.WriteLine((passed ? "PASS: " : "FAIL: ") + name); if (!passed) failures++; }
        static bool CanBind(int port, SocketType type)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, type, type == SocketType.Stream ? ProtocolType.Tcp : ProtocolType.Udp);
            socket.ExclusiveAddressUse = true;
            try { socket.Bind(new IPEndPoint(IPAddress.Loopback, port)); return true; }
            catch (SocketException) { return false; }
        }
        using (var first = new TcpListener(IPAddress.Loopback, 0))
        using (var conflict = new TcpListener(IPAddress.Loopback, 0))
        {
            first.Start(); var port = ((IPEndPoint)first.LocalEndpoint).Port; first.Stop();
            conflict.Start(); var conflictingPort = ((IPEndPoint)conflict.LocalEndpoint).Port;
            var server = new EntranceHandshakeServer(IPAddress.Loopback, [port, conflictingPort],
                () => throw new InvalidOperationException("No client traffic expected"), new UnusedSink(), _ => { }, (_, _) => { });
            try
            {
                try { await server.StartAsync(CancellationToken.None); Check(false, "later TCP conflict rejects startup"); }
                catch (SocketException) { Check(true, "later TCP conflict rejects startup"); }
                Check(CanBind(port, SocketType.Stream), "failed handshake startup releases its earlier TCP listener");
                Check(!CanBind(conflictingPort, SocketType.Stream), "failed startup leaves the unrelated TCP owner untouched");
            }
            finally { server.Stop(); }
        }
        using (var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var conflict = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            var port = ((IPEndPoint)first.Client.LocalEndPoint!).Port; first.Dispose();
            conflict.ExclusiveAddressUse = true; conflict.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var conflictingPort = ((IPEndPoint)conflict.LocalEndPoint!).Port;
            var options = new ServerOptions(); options.GameplayDatagram.PrimaryPort = port; options.GameplayDatagram.SecondaryPort = conflictingPort;
            using var service = new GameplayDatagramService(options, new RuntimeState(), NullLogger<GameplayDatagramService>.Instance);
            try { await service.StartAsync(CancellationToken.None); await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); Check(false, "later UDP conflict rejects startup"); }
            catch (SocketException) { Check(true, "later UDP conflict rejects startup"); }
            Check(CanBind(port, SocketType.Dgram), "failed gameplay startup releases its earlier UDP listener");
            Check(!CanBind(conflictingPort, SocketType.Dgram), "failed startup leaves the unrelated UDP owner untouched");
        }
        var readiness = new ListenerReadiness(new ServerOptions());
        Check(!readiness.IsReady, "HTTP startup alone cannot imply gameplay readiness");
        foreach (var name in readiness.Pending.Where(name => name != "gameplay").ToArray()) readiness.MarkReady(name);
        Check(!readiness.IsReady && readiness.Pending.SequenceEqual(["gameplay"]), "every enabled listener must report ready");
        readiness.MarkReady("gameplay");
        Check(readiness.IsReady, "all enabled listeners complete readiness");
        readiness.MarkStopped("gameplay");
        Check(!readiness.IsReady, "a stopped listener immediately revokes readiness");
        Console.WriteLine($"Startup recovery: {failures} failures");
        return failures == 0 ? 0 : 1;
    }

    private sealed class UnusedSink : IEntranceSessionSink
    {
        public IEntranceSession Begin(string service, string remote) => throw new InvalidOperationException("No client traffic expected");
    }
}
