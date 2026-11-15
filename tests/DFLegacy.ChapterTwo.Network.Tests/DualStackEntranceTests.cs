using System.Net;
using System.Net.Sockets;
using DFLegacy.Protocol;

internal static class DualStackEntranceTests
{
    internal static async Task<int> Run()
    {
        // Accept is unnecessary: the listening socket itself must accept both address families.
        var server = new EntranceHandshakeServer(IPAddress.IPv6Any, [0], () => null!, null!, _ => { }, (_, _) => { });
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var ports = await server.StartAsync(timeout.Token);
            foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
            {
                using var connection = new TcpClient(address.AddressFamily);
                await connection.ConnectAsync(address, ports.Single(), timeout.Token);
            }
            Console.WriteLine("PASS wildcard entrance accepts actual IPv4 and IPv6 connections");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL dual-stack entrance: " + error.Message); return 1; }
        finally { server.Stop(); }
    }
}
