using System.Diagnostics;
using System.Net.Sockets;

namespace DFLegacy.Launcher;

internal sealed record ServerProbeResult(bool Reachable, TimeSpan? Elapsed);

internal static class ServerConnectionProbe
{
    // Measures entrance TCP connection setup, including DNS resolution; it is not game-protocol RTT.
    internal static async Task<ServerProbeResult> ProbeAsync(LocalServerChoice choice, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var client = new TcpClient();
        var clock = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(choice.Host, choice.Port, deadline.Token);
            cancellationToken.ThrowIfCancellationRequested();
            return new(true, clock.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, null); }
        catch (SocketException) { return new(false, null); }
    }
}
