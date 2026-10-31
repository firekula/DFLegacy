using System.Diagnostics;

namespace DFLegacy.Launcher;

internal static class ClientProcessLifetime
{
    internal static async Task RunAsync(Func<Process> create,
        Func<Process, CancellationToken, Task> run, bool handoffOnSuccess,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var client = create();
        var handedOff = false;
        try
        {
            await run(client, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            handedOff = handoffOnSuccess;
        }
        finally
        {
            if (!handedOff && !client.HasExited)
            {
                try { client.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (client.HasExited) { }
                catch (System.ComponentModel.Win32Exception) when (client.HasExited) { }
                await client.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
