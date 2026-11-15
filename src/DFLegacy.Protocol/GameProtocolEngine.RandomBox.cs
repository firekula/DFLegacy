using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public readonly record struct GameRandomBoxCommand(ushort Sequence, ushort BoxSlot, ushort HammerSlot);
public sealed record GameRandomBoxReward(ushort DestinationSlot, ushort ItemId, uint CountOrValue,
    ushort InstanceValue, byte Attribute);

public static partial class GameProtocolEngine
{
    public const byte UseRandomBoxCommand = 191;
    public const byte UseSamsungRandomBoxCommand = 192;
    public static bool TryReadRandomBoxCommand(ReadOnlySpan<byte> body, out GameRandomBoxCommand command)
    {
        command = default;
        const int logicalLength = 6;
        if (body.Length < logicalLength) return false;
        var padding = body[logicalLength..];
        if (!padding.IsEmpty && (body.Length != 18 || padding.ContainsAnyExcept((byte)0))) return false;
        command = new(BinaryPrimitives.ReadUInt16LittleEndian(body),
            BinaryPrimitives.ReadUInt16LittleEndian(body[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(body[4..]));
        return true;
    }

    public static GameServerPacket CreateRandomBoxReply(ushort boxSlot, ushort hammerSlot,
        IReadOnlyList<GameRandomBoxReward> rewards, byte protocolId = UseRandomBoxCommand)
    {
        if (rewards.Count > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(rewards));
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        // The 191 and 192 readers close their own window and then consume
        // the same two slot fields, reward count and 11-byte reward rows.
        writer.Write((byte)1);
        writer.Write(boxSlot);
        writer.Write(hammerSlot);
        writer.Write((ushort)rewards.Count);
        foreach (var reward in rewards)
        {
            writer.Write(reward.DestinationSlot);
            writer.Write(reward.ItemId);
            writer.Write(reward.CountOrValue);
            writer.Write(reward.InstanceValue);
            writer.Write(reward.Attribute);
        }
        return new(CommandPacketType, protocolId, payload.ToArray());
    }

    public static GameServerPacket CreateRandomBoxError(byte error, byte protocolId = UseRandomBoxCommand) =>
        new(CommandPacketType, protocolId, [0, error]);
}
