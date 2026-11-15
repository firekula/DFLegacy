namespace DFLegacy.Protocol;

public static partial class GameProtocolEngine
{
    public const byte GuildInviteCommand = 162;
    public const byte GuildInviteResponseCommand = 163;
    public const byte GuildJoinedNotification = 58;
    public const byte GuildInvitationNotification = 159;
    public const byte GuildInvitationResultNotification = 160;

    public static bool TryReadGuildInvitationResponse(byte[] body, out bool accepted)
    {
        accepted = false;
        if (!HasCommandBodyLength(body, 3, allowChapterTwoPadding: true) || body[2] > 1)
            return false;
        accepted = body[2] == 1;
        return true;
    }

    public static GameServerPacket CreateGuildInvitation(byte[] guildName, byte[] inviterName)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        WriteLengthPrefixedBytes(writer, guildName);
        WriteLengthPrefixedBytes(writer, inviterName);
        return new(NotificationPacketType, GuildInvitationNotification, payload.ToArray());
    }

    public static GameServerPacket CreateGuildInvitationResult(byte status, byte[] inviteeName)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(status);
        WriteLengthPrefixedBytes(writer, inviteeName);
        return new(NotificationPacketType, GuildInvitationResultNotification, payload.ToArray());
    }

    public static GameServerPacket CreateGuildJoined(uint guildId, uint characterNo, byte[] guildName, byte[] memberName)
    {
        // The C2 reader consumes both dwords and uses the first as the local guild ID.
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(guildId);
        writer.Write(characterNo);
        WriteLengthPrefixedBytes(writer, guildName);
        WriteLengthPrefixedBytes(writer, memberName);
        return new(NotificationPacketType, GuildJoinedNotification, payload.ToArray());
    }
}
