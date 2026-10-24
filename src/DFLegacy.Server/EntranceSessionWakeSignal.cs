namespace DFLegacy.Server;

/// <summary>
/// 会话状态变化的版本信号。读循环必须先 Capture，再读取队伍/邮箱状态：
/// 此后发生的变化会完成捕获到的任务，而下一轮捕获的新版本仍保持未完成。
/// 与取消网络读取不同，Pulse 只唤醒调度，不会丢弃或重复已有 TCP 读取。
/// </summary>
internal sealed class EntranceSessionWakeSignal
{
    private TaskCompletionSource _version = CreateVersion();

    internal Task Capture() => Volatile.Read(ref _version).Task;

    internal void Pulse() =>
        Interlocked.Exchange(ref _version, CreateVersion()).TrySetResult();

    private static TaskCompletionSource CreateVersion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
