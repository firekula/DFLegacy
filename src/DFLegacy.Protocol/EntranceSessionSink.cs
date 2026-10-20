namespace DFLegacy.Protocol;

/// <summary>
/// 握手宿主的记账出口。宿主本身不认识 RuntimeState（那是 server 进程的东西）；
/// 两个进程各自实现本接口：server 进程映射到 RuntimeState，channel 进程映射到自己的计数/日志。
/// </summary>
public interface IEntranceSessionSink
{
    IEntranceSession Begin(string service, string remote);
}

public interface IEntranceSession : IDisposable
{
    void Received(PacketFrame frame);

    void Sent(PacketFrame frame);
}
