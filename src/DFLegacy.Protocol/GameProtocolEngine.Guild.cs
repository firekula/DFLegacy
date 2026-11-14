using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public sealed record GameGuildMember(byte[] Name, ushort Level, byte Job, byte GrowType,
    byte AwakeningType, byte Rank, bool Online, uint OfflineSeconds = 0);

public static partial class GameProtocolEngine
{
    public const byte GuildMembersCommand = 70;
    public const byte GuildCreateCommand = 71;
    public const byte GuildInfoCommand = 77;
    public const byte GuildAllMembersCommand = 150;
    public const byte GuildMembersNextCommand = 151;
    public const byte GuildCheckNameCommand = 167;
    public const byte GuildCheckAddressCommand = 168;
    public const byte GuildOpenCreateCommand = 169;

    public static bool TryReadGuildText(byte[] body, out byte[] text)
    {
        text = [];
        if (body.Length < 6) return false;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(2));
        if (length is 0 or > 30 || !HasCommandBodyLength(body, 6 + (int)length, true)) return false;
        text = body.AsSpan(6, (int)length).ToArray();
        return true;
    }

    public static GameServerPacket CreateGuildCreated(uint id, byte[] name)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(id);
        WriteLengthPrefixedBytes(writer, name);
        return new(NotificationPacketType, 71, payload.ToArray());
    }

    public static GameServerPacket CreateGuildInfo(byte[] name, byte rank, ushort memberCount)
    {
        // C2 NOTI70, native reader 0042ADFB. Level is zero-based on the wire.
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        WriteLengthPrefixedBytes(writer, name);
        writer.Write(rank == 1 ? (byte)1 : (byte)0); // founder flag
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(memberCount);
        writer.Write((byte)2); // completed guild creation right
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write((ushort)0);
        writer.Write((byte)0); // no guild skills have been purchased
        WriteLengthPrefixedBytes(writer, []); // guild notice
        WriteLengthPrefixedBytes(writer, []); // member display name
        return new(NotificationPacketType, 70, payload.ToArray());
    }

    public static GameServerPacket CreateGuildMembers(byte command, uint id, byte[] name,
        IReadOnlyList<GameGuildMember> members)
    {
        if (members.Count > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(members));
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(id);
        if (command is GuildMembersCommand or GuildAllMembersCommand)
        {
            WriteLengthPrefixedBytes(writer, name);
            writer.Write(0u);
            writer.Write((ushort)members.Count);
        }
        writer.Write((byte)members.Count);
        foreach (var member in members)
        {
            WriteLengthPrefixedBytes(writer, member.Name);
            WriteLengthPrefixedBytes(writer, []);
            writer.Write(member.Level);
            writer.Write(member.Job);
            writer.Write((byte)((member.AwakeningType << 4) | member.GrowType));
            writer.Write(member.Online ? (byte)0 : byte.MaxValue);
            writer.Write(member.Rank);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(member.OfflineSeconds);
        }
        return new(CommandPacketType, command, payload.ToArray());
    }
}
