using System.Diagnostics;
using System.Net.Sockets;

namespace DFLegacy.Relay;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!RelayOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(RelayOptions.Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(RelayOptions.Usage);
            return 0;
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        var target = await RelayHost.ResolveTargetAsync(
            options.ServerHost,
            options.Preference == AddressPreference.IPv6,
            Log,
            shutdown.Token);
        if (target is null)
        {
            return 1;
        }

        Log(RelayLogLevel.Info, $"target {options.ServerHost} -> {target}");

        RelayHost relay;
        try
        {
            // Binding every port up front means a clash is reported once, before
            // the relay claims to be ready. A stale instance holding the ports is
            // the usual cause.
            relay = new RelayHost(
                options.BindAddress,
                target,
                options.TcpPorts,
                options.UdpPorts,
                Log);
            relay.Start();
        }
        catch (SocketException exception)
        {
            Log(RelayLogLevel.Error, $"无法绑定本地端口：{exception.Message}");
            Log(RelayLogLevel.Error, "端口可能被另一个中继实例占用；请先结束旧实例再启动本中继。");
            return 1;
        }

        MonitorExitProcess(options.ExitWithProcessId, shutdown);
        MonitorExitImage(options.ExitWithImage, shutdown);
        Log(RelayLogLevel.Info, "按 Ctrl+C 停止。");

        await relay.RunAsync(shutdown.Token);
        return 0;
    }

    private static void Log(RelayLogLevel level, string message)
    {
        var name = level switch
        {
            RelayLogLevel.Warn => "warn",
            RelayLogLevel.Error => "error",
            _ => "info"
        };
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} {name}: {message}");
    }

    private static void MonitorExitProcess(
        int? processId,
        CancellationTokenSource shutdown)
    {
        if (processId is not { } pid)
        {
            return;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            _ = process.WaitForExitAsync().ContinueWith(
                _ =>
                {
                    try
                    {
                        shutdown.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The relay already shut down.
                    }
                },
                TaskScheduler.Default);
            Log(RelayLogLevel.Info, $"process {pid} 退出时本中继也会退出。");
        }
        catch (ArgumentException)
        {
            Log(RelayLogLevel.Warn, $"进程 {pid} 不存在，立即停止。");
            shutdown.Cancel();
        }
    }

    // Waits for the image to appear first, then stops the relay once no process
    // with that name is left. Waiting for the first sighting is what makes this
    // safe to start before the game client.
    private static void MonitorExitImage(
        string? imageName,
        CancellationTokenSource shutdown)
    {
        if (string.IsNullOrWhiteSpace(imageName))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var seen = false;
            try
            {
                while (!shutdown.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), shutdown.Token);
                    var running = IsImageRunning(imageName);
                    if (running)
                    {
                        if (!seen)
                        {
                            seen = true;
                            Log(RelayLogLevel.Info, $"{imageName} 已启动，其退出后本中继也会退出。");
                        }

                        continue;
                    }

                    if (seen)
                    {
                        Log(RelayLogLevel.Info, $"{imageName} 已退出，停止中继。");
                        shutdown.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception exception)
            {
                Log(RelayLogLevel.Warn, $"监视 {imageName} 失败，中继将继续运行：{exception.Message}");
            }
        });
    }

    private static bool IsImageRunning(string imageName)
    {
        var processes = Process.GetProcessesByName(imageName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
