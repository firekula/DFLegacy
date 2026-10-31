using DFLegacy.Protocol;

namespace DFLegacy.Channel;

/// <summary>
/// channel 进程自己的记账出口（C5-SINK/2）。宿主只认 <see cref="IEntranceSessionSink"/>
/// （EntranceSessionSink.cs:7-17），server 进程把它映射到 RuntimeState，这里映射到独立计数 + 日志。
/// </summary>
/// <remarks>
/// 两条口径必须保持：
/// <list type="bullet">
/// <item>会话标签原样保留宿主给的 <c>entrance:{port}</c>（EntranceHandshakeServer.cs:136-138）。
/// 两个入口端口在同一进程里，只有这条标签能把它们区分开；换成硬编码 "entrance" 会丢掉这个能力。</item>
/// <item>收/发/会话计数各自独立：<see cref="ReceivedPackets"/> 只由收包路径推进，
/// <see cref="SentPackets"/> 只由发包路径推进，<see cref="ActiveSessions"/> 在会话关闭后归零。
/// 不要把 Received+Sent 直接当成 server 的 TotalPackets —— 那是 server 侧收包口径
/// （RuntimeEntranceSink.cs:23-33），合并方式留给 Task 7/8 复核（needs-refresh）。</item>
/// </list>
/// </remarks>
public sealed class ChannelSessionSink(Action<string> log) : IEntranceSessionSink
{
    private long _receivedPackets;
    private long _sentPackets;
    private long _totalSessions;
    private int _activeSessions;

    /// <summary>累计收到的帧数（只统计成功读出并交给宿主的帧）。</summary>
    public long ReceivedPackets => Interlocked.Read(ref _receivedPackets);

    /// <summary>累计发出的帧数。</summary>
    public long SentPackets => Interlocked.Read(ref _sentPackets);

    /// <summary>累计建立的会话数（含已关闭的）。</summary>
    public long TotalSessions => Interlocked.Read(ref _totalSessions);

    /// <summary>当前活跃会话数；会话 Dispose 后归零。</summary>
    public int ActiveSessions => Volatile.Read(ref _activeSessions);

    public IEntranceSession Begin(string service, string remote)
    {
        Interlocked.Increment(ref _totalSessions);
        Interlocked.Increment(ref _activeSessions);
        // 标签（entrance:2311 / entrance:8080）与远端都进日志：channel 侧目前没有 admin 面，
        // 运维和接线测试都靠这一行判断「哪个入口端口接的、会话有没有回收」。
        log($"Entrance session {service} opened from {remote}");
        return new Session(this, service);
    }

    private void CountReceived() => Interlocked.Increment(ref _receivedPackets);

    private void CountSent() => Interlocked.Increment(ref _sentPackets);

    private void EndSession(string service)
    {
        if (Interlocked.Decrement(ref _activeSessions) == 0)
        {
            log($"Entrance session {service} closed; no active entrance sessions remain");
        }
    }

    private sealed class Session(ChannelSessionSink owner, string service) : IEntranceSession
    {
        private int _disposed;

        public void Received(PacketFrame frame) => owner.CountReceived();

        public void Sent(PacketFrame frame) => owner.CountSent();

        public void Dispose()
        {
            // 宿主在 finally 里恰好调一次（EntranceHandshakeServer.cs:170-173）；这里再守一层，
            // 免得将来多调一次就把 ActiveSessions 打成负数。
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.EndSession(service);
            }
        }
    }
}
