using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public static partial class GameProtocolEngine
{
    public const byte GuildSetMemberRankCommand = 136;
    public const byte GuildSecedeCommand = 164;
    public const byte GuildDelegateMasterCommand = 166;
    public const byte GuildMemberDepartureNotification = 59;
    public const byte GuildSelfRankNotification = 139;
    public const byte GuildMemberRankNotification = 152;
    public const byte GuildMasterDelegatedNotification = 161;
    public const byte GuildMasterAppointedNotification = 162;
    public const byte GuildDepartureNotification = 163;

    public static bool TryReadGuildMembershipTarget(
        byte[] body, bool allowSelf, bool hasRank, out byte[] name, out byte rank)
    {
        name = [];
        rank = 0;
        const int nameOffset = sizeof(ushort) + sizeof(uint);
        if (body.Length < nameOffset) return false;

        var length = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(sizeof(ushort)));
        if (length > 30 || (length == 0 && !allowSelf)) return false;

        var endOfName = nameOffset + (int)length;
        if (!HasCommandBodyLength(body, endOfName + (hasRank ? 1 : 0), true)) return false;

        name = body.AsSpan(nameOffset, (int)length).ToArray();
        if (name.AsSpan().Contains((byte)0)) return false;
        if (hasRank) rank = body[endOfName];
        return true;
    }

    public static GameServerPacket CreateGuildSecedeReply(byte mode, byte[] guildName, byte[] characterName) =>
        CreateGuildMembershipPacket(CommandPacketType, GuildSecedeCommand, writer =>
        {
            writer.Write((byte)1);
            writer.Write(mode);
            WriteLengthPrefixedBytes(writer, guildName);
            WriteLengthPrefixedBytes(writer, characterName);
        });

    public static GameServerPacket CreateGuildMemberDeparture(
        uint guildId, uint characterNo, byte mode, byte[] guildName, byte[] characterName) =>
        CreateGuildMembershipPacket(NotificationPacketType, GuildMemberDepartureNotification, writer =>
        {
            writer.Write(guildId);
            writer.Write(characterNo);
            writer.Write(mode);
            WriteLengthPrefixedBytes(writer, guildName);
            WriteLengthPrefixedBytes(writer, characterName);
        });

    public static GameServerPacket CreateGuildDeparture(byte mode) =>
        new(NotificationPacketType, GuildDepartureNotification, [mode]);

    public static GameServerPacket CreateGuildMemberRankReply(byte[] characterName, byte rank) =>
        CreateGuildMembershipPacket(CommandPacketType, GuildSetMemberRankCommand, writer =>
        {
            writer.Write((byte)1);
            writer.Write((byte)0); // the native success result code
            WriteLengthPrefixedBytes(writer, characterName);
            writer.Write(rank);
        });

    public static GameServerPacket CreateGuildMemberRank(byte rank) =>
        new(NotificationPacketType, GuildMemberRankNotification, [rank]);

    public static GameServerPacket CreateGuildSelfRank(byte rank) =>
        new(NotificationPacketType, GuildSelfRankNotification, [rank]);

    public static GameServerPacket CreateGuildMasterDelegated(byte[] characterName) =>
        CreateGuildMembershipPacket(NotificationPacketType, GuildMasterDelegatedNotification,
            writer => WriteLengthPrefixedBytes(writer, characterName));

    public static GameServerPacket CreateGuildMasterAppointed() =>
        new(NotificationPacketType, GuildMasterAppointedNotification, []);

    private static GameServerPacket CreateGuildMembershipPacket(byte type, byte protocol, Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        write(writer);
        return new(type, protocol, buffer.ToArray());
    }

}
