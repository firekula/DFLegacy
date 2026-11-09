using System.Diagnostics;

namespace DFLegacy.Launcher;

internal static class ClientStartupMonitor
{
    internal static async Task ObserveAsync(Process client, TimeSpan observation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(observation);
        try { await client.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!client.HasExited) return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var code = unchecked((uint)client.ExitCode);
        throw new InvalidOperationException(
            $"游戏进程在启动阶段已退出（代码 0x{code:X8}）。" +
            "请查看 Logs/DFLegacy/launcher.log 和客户端错误提示，检查客户端版本、资源及系统依赖；" +
            "创建进程成功不代表游戏已经启动。");
    }
}
