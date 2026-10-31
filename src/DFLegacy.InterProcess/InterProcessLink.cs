using DFLegacy.Protocol;

namespace DFLegacy.InterProcess;

/// <summary>
/// 进程间链路的帧包装。帧壳沿用 PacketFrame（10 B 头 + Body[0]=记录数），
/// 线上字节与官方 11 字节头同构：Body[0] 就落在官方 [10] 的位置。
/// 固定参数：category=0x7b，recordCount=1，不带 CRC（spec §5.1）。
/// </summary>
public static class InterProcessLink
{
    public static PacketFrame Create(byte messageId, ReadOnlySpan<byte> payload) =>
        PacketFrame.Create(
            messageId,
            payload,
            type: InterProcessMessageIds.LinkCategory,
            recordCount: 1,
            includeCrc32: false);

    public static bool IsLinkFrame(PacketFrame frame) =>
        frame.Type == InterProcessMessageIds.LinkCategory;

    public static bool TryGetMessageId(PacketFrame frame, out byte messageId)
    {
        messageId = frame.ProtocolId;
        return IsLinkFrame(frame);
    }
}
