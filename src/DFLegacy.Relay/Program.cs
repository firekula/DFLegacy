using System.Diagnostics;
using System.Net;
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

        var target = await ResolveTargetAsync(options, shutdown.Token);
        if (target is null)
        {
            return 1;
        }

        Log.Info($"target {options.ServerHost} -> {target}");
        var preferred = options.Preference == AddressPreference.IPv6
            ? AddressFamily.InterNetworkV6
            : AddressFamily.InterNetwork;
        if (IPAddress.TryParse(options.ServerHost.Trim('[', ']'), out _)
            is false
            && target.AddressFamily != preferred)
        {
            Log.Warn(
                $"解析结果没有 {(preferred == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4")} 地址，改用 {target.AddressFamily}。");
        }

        // Bind every port up front so a clash is reported once, before the relay
        // claims to be ready. A stale instance holding the ports is the usual cause.
        var tcp = new List<TcpForwarder>();
        var udp = new List<UdpForwarder>();
        try
        {
            foreach (var port in options.TcpPorts)
            {
                var forwarder = new TcpForwarder(options.BindAddress, port, target);
                forwarder.Start();
                tcp.Add(forwarder);
            }

            foreach (var port in options.UdpPorts)
            {
                var forwarder = new UdpForwarder(options.BindAddress, port, target);
                forwarder.Start();
                udp.Add(forwarder);
            }
        }
        catch (SocketException exception)
        {
            Log.Error($"无法绑定本地端口：{exception.Message}");
            Log.Error("端口可能被另一个中继实例占用；请先结束旧实例再启动本中继。");
            StopAll(tcp, udp);
            return 1;
        }

        MonitorExitProcess(options.ExitWithProcessId, shutdown);
        MonitorExitImage(options.ExitWithImage, shutdown);

        var tasks = tcp
            .Select(forwarder => forwarder.RunAsync(shutdown.Token))
            .Concat(udp.Select(forwarder => forwarder.RunAsync(shutdown.Token)))
            .ToArray();

        Log.Info(
            $"relay ready: {tcp.Count} TCP / {udp.Count} UDP port(s) on {options.BindAddress}. 按 Ctrl+C 停止。");

        try
        {
            await Task.WhenAll(tasks);
        }
        finally
        {
            StopAll(tcp, udp);
        }

        Log.Info("relay stopped.");
        return 0;
    }

    private static void StopAll(
        IEnumerable<TcpForwarder> tcp,
        IEnumerable<UdpForwarder> udp)
    {
        foreach (var forwarder in tcp)
        {
            forwarder.Stop();
        }

        foreach (var forwarder in udp)
        {
            forwarder.Stop();
        }
    }

    private static async Task<IPAddress?> ResolveTargetAsync(
        RelayOptions options,
        CancellationToken cancellationToken)
    {
        var host = options.ServerHost.Trim('[', ']');
        if (IPAddress.TryParse(host, out var literal))
        {
            return literal;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (Exception exception)
        {
            Log.Error($"无法解析 {host}：{exception.Message}");
            return null;
        }

        if (addresses.Length == 0)
        {
            Log.Error($"域名 {host} 没有解析到任何地址。");
            return null;
        }

        var preferred = options.Preference == AddressPreference.IPv6
            ? AddressFamily.InterNetworkV6
            : AddressFamily.InterNetwork;
        return Array.Find(addresses, address => address.AddressFamily == preferred)
            ?? addresses[0];
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
            Log.Info($"process {pid} 退出时本中继也会退出。");
        }
        catch (ArgumentException)
        {
            Log.Warn($"进程 {pid} 不存在，立即停止。");
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
                            Log.Info($"{imageName} 已启动，其退出后本中继也会退出。");
                        }

                        continue;
                    }

                    if (seen)
                    {
                        Log.Info($"{imageName} 已退出，停止中继。");
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
                Log.Warn($"监视 {imageName} 失败，中继将继续运行：{exception.Message}");
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
