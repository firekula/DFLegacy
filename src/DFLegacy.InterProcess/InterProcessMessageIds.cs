namespace DFLegacy.InterProcess;

/// <summary>
/// 进程间链路的报文号与常量。只有 NoticeChannelServer 复用官方 ID（其载荷布局已有抓包双证）；
/// 其余走 >= 128 的私有号段，避免与官方 1..14 的通道服注册表相撞。
/// 证据：docs/superpowers/sdd/2026-10-05-official-login-channel/dual-evidence.md:88-91,229
/// </summary>
public static class InterProcessMessageIds
{
    /// <summary>官方 channel↔bridge 链路的 category 字节，实测 0x7b。</summary>
    public const byte LinkCategory = 0x7b;

    /// <summary>本链路自己的协议版本。任一侧不匹配时 REGISTER_ACK.accepted 回 0。</summary>
    public const ushort LinkProtocolVersion = 2;

    public const byte NoticeChannelServer = 4;
    public const byte Register = 128;
    public const byte RegisterAck = 129;
    public const byte Status = 130;
}
