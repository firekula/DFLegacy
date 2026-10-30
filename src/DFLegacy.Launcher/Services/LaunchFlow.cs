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
        Program.ValidateOptions(options);
        Program.ValidateLauncherField(account.Account, "Account");
        Program.ValidateLauncherField(account.Password, "Password");

        await Program.EnsureD3D9AvailableAsync(options);
        var clientPath = Program.ResolveClientPath(options.ClientPath);
        Program.ValidateClient(clientPath);
        Program.ValidateScriptPvf(clientPath);
        var ijl15 = Ijl15ReplacementInstaller.Install(clientPath, options.ResolveAccountServiceUrl());
        if (ijl15.Installed)
            Program.WriteLauncherLog(
                $"Installed x86 ijl15 replacement at {ijl15.TargetPath}; sha256={ijl15.Sha256}; backup={ijl15.BackupPath ?? "(none)"}.");

        var relay = await Program.StartRelayIfConfiguredAsync(options);
        if (relay is null || Program.IsLoopbackHost(options.EntranceHost))
            await Program.EnsureServerReadyAsync(options);

        var entranceHost = options.Relay.Enabled ? Program.RelayListenAddress : options.EntranceHost;
        var launcherData = string.Join('?',
        [
            "99", entranceHost,
            options.EntrancePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            account.Account, account.Password,
            "0","0","0","0","0","0","0","0"
        ]);

        var client = ClientProcessLauncher.Start(clientPath, launcherData);
        Program.TryRaisePriority(client);
        Program.WriteLauncherLog(
            $"Started DFLegacy client process {client.Id}; entrance={entranceHost}:{options.EntrancePort}" +
            (relay is null ? "." : $"; relay target={relay.TargetAddress}."));

        if (relay is null)
            return;

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = client.WaitForExitAsync().ContinueWith(_ =>
        {
            try { shutdown.Cancel(); } catch (ObjectDisposedException) { }
        }, TaskScheduler.Default);
        await relay.RunAsync(shutdown.Token);
        Program.WriteLauncherLog($"Client process {client.Id} exited; launcher stopping.");
    }
}
