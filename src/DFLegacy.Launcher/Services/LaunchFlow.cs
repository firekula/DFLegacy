using System.Diagnostics;
using DFLegacy.Relay;

namespace DFLegacy.Launcher;

internal static class LaunchFlow
{
    public static async Task RunAsync(
        LauncherOptions options,
        LauncherAccountProfile account,
        CancellationToken cancellationToken = default,
        Func<IjlMigrationInspection,bool>? confirmMigration = null,
        Action<string>? reportWarning = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Program.ValidateOptions(options);
        Program.ValidateLauncherField(account.Account, "Account");
        Program.ValidateLauncherField(account.Password, "Password");

        var clientPath = Program.ResolveClientPath(options.ClientPath);
        Program.ValidateClient(clientPath);
        Program.LogClientPath = clientPath;
        ClientFileIdentity.RequireSameDirectory(AppContext.BaseDirectory,Path.GetDirectoryName(clientPath)!);
        var manifest = ClientPackageManifest.LoadAndValidate(Path.GetDirectoryName(clientPath)!);
        ClientBootstrapInspector.ValidateForLaunch(clientPath);
        Program.ValidateScriptPvf(clientPath);
        await Program.EnsureD3D9AvailableAsync(options);
        cancellationToken.ThrowIfCancellationRequested();
        var migration = ClientIjlMigration.Inspect(clientPath);
        if(migration.State != IjlMigrationState.Original)
        {
            if(migration.State != IjlMigrationState.RestoreRequired || migration.CandidatePath is null)
                throw new InvalidDataException("客户端需要校验通过的原版 ijl15.dll。未找到可信原版备份；未知文件不会被覆盖。请手动恢复你持有的原版文件。");
            if(confirmMigration?.Invoke(migration)!=true)
                throw new InvalidDataException("已取消原版 IJL 恢复，游戏未启动。请在登录器中确认一次性迁移后重试。");
            cancellationToken.ThrowIfCancellationRequested();
            ClientIjlMigration.RestoreConfirmed(migration,true);
            Program.WriteLauncherLog("Confirmed legacy IJL migration completed; original hash verified and old patch backed up.");
        }
        StandaloneClientInstaller.Configure(clientPath,options.ResolveAccountServiceUrl());
        ClientLogPaths.ArchiveLegacy(clientPath);
        if(!ClientLogPaths.TryWrite(clientPath,"Preparing standalone client bootstrap."))
        {
            Program.LogWarning = "客户端 Logs/DFLegacy 无法写入，诊断日志不可用。请检查目录权限。";
            if(reportWarning is not null) reportWarning(Program.LogWarning);
            else Console.Error.WriteLine(Program.LogWarning);
        }

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
                () => ClientProcessLauncher.StartAsync(clientPath, launcherData,manifest,cancellationToken),
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
