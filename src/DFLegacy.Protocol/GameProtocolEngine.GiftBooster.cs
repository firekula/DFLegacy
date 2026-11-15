using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public readonly record struct GameGiftBoosterCommand(ushort Sequence, ushort SourceSlot);

public static partial class GameProtocolEngine
{
    public const byte OpenGiftBoosterCommand = 171;

    public static bool TryReadGiftBoosterCommand(ReadOnlySpan<byte> body, out GameGiftBoosterCommand command)
    {
        command = default;
        if (!HasCommandBodyLength(body, 4, allowChapterTwoPadding: true)) return false;
        command = new(BinaryPrimitives.ReadUInt16LittleEndian(body),
            BinaryPrimitives.ReadUInt16LittleEndian(body[2..]));
        return true;
    }

    public static GameServerPacket CreateGiftBoosterReply(ushort slot, uint remainingCount,
        IReadOnlyList<(ushort ItemId, uint Count)> rewards)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        // Ver_OB_216 reads these two fields before the reward count and shows the reward rows.
        writer.Write(slot);
        writer.Write(remainingCount);
        writer.Write(checked((ushort)rewards.Count));
        foreach (var reward in rewards)
        {
            writer.Write(reward.ItemId);
            writer.Write(reward.Count);
        }
        return new(CommandPacketType, OpenGiftBoosterCommand, payload.ToArray());
    }

    public static GameServerPacket CreateGiftBoosterError(byte error) =>
        new(CommandPacketType, OpenGiftBoosterCommand, [0, error]);
}
