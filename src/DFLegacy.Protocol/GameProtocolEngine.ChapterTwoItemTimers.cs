using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public static partial class GameProtocolEngine
{
    public const byte ChapterTwoItemCooldownNotification = 181;
    public const byte ChapterTwoItemEffectNotification = 182;

    public static GameServerPacket CreateChapterTwoItemCooldownNotification(ushort itemId, uint seconds) =>
        CreateChapterTwoItemTimerNotification(ChapterTwoItemCooldownNotification, itemId, seconds);

    public static GameServerPacket CreateChapterTwoItemEffectNotification(ushort itemId, uint seconds) =>
        CreateChapterTwoItemTimerNotification(ChapterTwoItemEffectNotification, itemId, seconds);

    private static GameServerPacket CreateChapterTwoItemTimerNotification(byte notification, ushort itemId, uint seconds)
    {
        var payload = new byte[9];
        payload[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), itemId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5), seconds);
        return new GameServerPacket(NotificationPacketType, notification, payload);
    }
}
