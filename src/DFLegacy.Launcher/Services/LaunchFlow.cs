using System.Diagnostics;
using DFLegacy.Relay;

namespace DFLegacy.Launcher;

internal static class LaunchFlow
{
    public static async Task RunAsync(
        LauncherOptions options,
        LauncherAccountProfile account,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Program.ValidateOptions(options);
        Program.ValidateLauncherField(account.Account, "Account");
        Program.ValidateLauncherField(account.Password, "Password");

        var clientPath = Program.ResolveClientPath(options.ClientPath);
        Program.ValidateClient(clientPath);
        ClientBootstrapInspector.ValidateForLaunch(clientPath);
        Program.ValidateScriptPvf(clientPath);
        await Program.EnsureD3D9AvailableAsync(options);
        cancellationToken.ThrowIfCancellationRequested();
        var ijl15 = Ijl15ReplacementInstaller.Install(clientPath, options.ResolveAccountServiceUrl());
        if (ijl15.Installed)
            Program.WriteLauncherLog(
                $"Installed x86 ijl15 replacement at {ijl15.TargetPath}; sha256={ijl15.Sha256}; backup={ijl15.BackupPath ?? "(none)"}.");

        var relay = await Program.StartRelayIfConfiguredAsync(options);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task relayTask = Task.CompletedTask;
        try
        {
            if (relay is null || Program.IsLoopbackHost(options.EntranceHost))
                await Program.EnsureServerReadyAsync(options);
            cancellationToken.ThrowIfCancellationRequested();
            if (relay is not null) relayTask = relay.RunAsync(shutdown.Token);
            var entranceHost = options.Relay.Enabled ? Program.RelayListenAddress : options.EntranceHost;
            var launcherData = string.Join('?',
            [
                "99", entranceHost,
                options.EntrancePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                account.Account, account.Password,
                "0","0","0","0","0","0","0","0"
            ]);
            await ClientProcessLifetime.RunAsync(
                () => ClientProcessLauncher.Start(clientPath, launcherData),
                async (client, token) =>
                {
                    Program.TryRaisePriority(client);
                    Program.WriteLauncherLog(
                        $"Created DFLegacy client process {client.Id}; entrance={entranceHost}:{options.EntrancePort}" +
                        (relay is null ? "." : $"; relay target={relay.TargetAddress}."));
                    await ClientStartupMonitor.ObserveAsync(client, TimeSpan.FromSeconds(5), token);
                    Program.WriteLauncherLog($"Client process {client.Id} remained running through startup observation.");
                    if (relay is null) return;
                    await client.WaitForExitAsync(token);
                    shutdown.Cancel();
                    await relayTask;
                    Program.WriteLauncherLog($"Client process {client.Id} exited; launcher stopping.");
                }, handoffOnSuccess: relay is null, cancellationToken);
        }
        finally
        {
            shutdown.Cancel();
            try { await relayTask; }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            finally { relay?.Stop(); }
        }
    }
}
