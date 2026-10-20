using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 把握手宿主的记账接到 <see cref="RuntimeState"/> 上，保留 admin 的 /api/sessions 与
/// /api/status 口径：会话标签、每会话收发计数、全局 TotalPackets、可选抓包环形缓冲。
/// </summary>
public sealed class RuntimeEntranceSink(
    RuntimeState runtime,
    bool tracePackets,
    int hexDumpLimit) : IEntranceSessionSink
{
    public IEntranceSession Begin(string service, string remote) =>
        new Session(runtime.Add(service, remote), runtime, tracePackets, hexDumpLimit);

    private sealed class Session(
        RuntimeSession session,
        RuntimeState runtime,
        bool tracePackets,
        int hexDumpLimit) : IEntranceSession
    {
        public void Received(PacketFrame frame)
        {
            // 必须走 runtime.CountPacket：它同时累加会话级与全局计数（RuntimeState.cs:71-75）。
            // 手写 Interlocked.Increment 只会动会话字段，/api/status.TotalPackets 会丢掉全部握手包。
            runtime.CountPacket(session);
            session.LastProtocolId = frame.ProtocolId;
            if (tracePackets)
            {
                runtime.TracePacket(session, "RX", frame, hexDumpLimit);
            }
        }

        public void Sent(PacketFrame frame)
        {
            Interlocked.Increment(ref session.SentPackets);
            if (tracePackets)
            {
                runtime.TracePacket(session, "TX", frame, hexDumpLimit);
            }
        }

        public void Dispose() => runtime.Remove(session.Id);
    }
}
