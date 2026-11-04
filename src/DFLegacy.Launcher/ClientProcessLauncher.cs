using System.Diagnostics;

namespace DFLegacy.Launcher;

internal static class ClientProcessLauncher
{
    internal static Task<Process> StartAsync(string clientPath, string launcherData, ClientPackageManifest manifest, CancellationToken cancellationToken, string? paymentUrl = null)
        => ClientBootstrapLauncher.StartAsync(clientPath,launcherData,manifest,cancellationToken,paymentUrl);
}
