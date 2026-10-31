using System.Net;
using System.Net.Sockets;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

internal static class FallbackRelayLifecycleSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        Disposal(check);
        await RetirementAsync(check);
        foreach (var inherit in new[] { true, false }) await DualStackAsync(inherit, check);
    }

    private static void Disposal(Action<bool, string> check)
    {
        var options = new ServerOptions(); var runtime = new RuntimeState();
        using var contexts = new GameplayDatagramService(options, runtime, NullLogger<GameplayDatagramService>.Instance);
        var services = new ServiceCollection();
        services.AddSingleton(_ => new FallbackRelayService(options, contexts, NullLogger<FallbackRelayService>.Instance));
        services.AddHostedService(sp => sp.GetRequiredService<FallbackRelayService>());
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IEnumerable<IHostedService>>().ToArray();
        try { provider.Dispose(); check(true, "production singleton plus hosted registration disposes without throwing"); }
        catch (ObjectDisposedException) { check(false, "production singleton plus hosted registration disposes without throwing"); }
    }

    private static async Task RetirementAsync(Action<bool, string> check)
    {
        var options = new ServerOptions(); var runtime = new RuntimeState();
        using var contexts = new GameplayDatagramService(options, runtime, NullLogger<GameplayDatagramService>.Instance);
        var session = runtime.Add("channel:test", "127.0.0.1:41000");
        contexts.RegisterChannelSession(session, IPAddress.Loopback);
        var admission = contexts.AuthenticateRelayAccount(session.Id, 123)!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retirement = contexts.RetireChannelSessionAsync(session.Id, () => release.Task);
        check(!contexts.IsCurrentRelayAdmission(admission), "closed channel revokes relay authority before a stalled world cleanup completes");
        release.SetResult(); await retirement;
        var failing = runtime.Add("channel:test", "127.0.0.1:41001");
        contexts.RegisterChannelSession(failing, IPAddress.Loopback);
        contexts.AuthenticateRelayAccount(failing.Id, 456);
        try { await contexts.RetireChannelSessionAsync(failing.Id, () => throw new IOException("controlled cleanup failure")); }
        catch (IOException) { }
        check(contexts.FindRelayAdmission(456, IPAddress.Loopback) is null
            && contexts.AuthenticateRelayAccount(failing.Id, 456) is null,
            "throwing world cleanup cannot retain or recreate the retired channel admission");
    }

    private static async Task DualStackAsync(bool inherit, Action<bool, string> check)
    {
        var options = new ServerOptions(); options.Entrance.Host = "::";
        options.GameplayDatagram.AdvertisedHost = "127.0.0.2";
        if (!inherit) options.FallbackRelay.Host = "::";
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        options.FallbackRelay.TcpPort = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using (var udpPort = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            options.FallbackRelay.UdpPort = ((IPEndPoint)udpPort.Client.LocalEndPoint!).Port;
        var runtime = new RuntimeState();
        using var contexts = new GameplayDatagramService(options, runtime, NullLogger<GameplayDatagramService>.Instance);
        var session = runtime.Add("channel:test", "127.0.0.1:41000");
        contexts.RegisterChannelSession(session, IPAddress.Loopback); contexts.AuthenticateRelayAccount(session.Id, 123);
        using var service = new FallbackRelayService(options, contexts, NullLogger<FallbackRelayService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.StartAsync(timeout.Token);
        try
        {
            using var client = new TcpClient();
            try { await client.ConnectAsync(IPAddress.Parse("127.0.0.2"), options.FallbackRelay.TcpPort, timeout.Token); }
            catch (SocketException) { check(false, inherit ? "legacy configuration inherits the actual wildcard game bind" : "IPv6 wildcard relay TCP accepts native IPv4 clients"); return; }
            await client.GetStream().WriteAsync(Convert.FromHexString("00000C007B0000007B000000"), timeout.Token);
            while (service.Snapshot().RegisteredConnections != 1) await Task.Delay(10, timeout.Token);
            check(true, inherit ? "legacy configuration inherits the actual wildcard game bind" : "IPv6 wildcard relay TCP accepts native IPv4 clients");
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }
}
