namespace DFLegacy.Protocol;

public sealed record MentorListEntry(byte Channel, byte Level, byte[] Name,
    byte Blocked = 0, byte RelationshipLevel = 1, uint Experience = 0, uint NextExperience = 3);

public static partial class GameProtocolEngine
{
    public const byte RequestMentorCommand = 79;
    public const byte ReplyMentorCommand = 80;
    public const byte RemoveMentorCommand = 81;
    public const byte MentorListCommand = 82;

    private static void WriteMentorName(BinaryWriter writer, byte[] name)
    {
        writer.Write((uint)name.Length);
        writer.Write(name);
    }

    private static GameServerPacket MentorPacket(byte type, byte id, Action<BinaryWriter> write)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        write(writer);
        return new(type, id, output.ToArray());
    }

    public static GameServerPacket CreateMentorInvitation(ushort level, byte[] name)
        => MentorPacket(NotificationPacketType, 73, writer => { writer.Write(level); WriteMentorName(writer, name); });

    public static GameServerPacket CreateMentorRequestReply(byte[] name, byte error = 0) =>
        MentorPacket(CommandPacketType, RequestMentorCommand, writer =>
        {
            writer.Write(error == 0 ? (byte)1 : (byte)0);
            WriteMentorName(writer, name);
            if (error != 0) writer.Write(error);
        });

    public static GameServerPacket CreateMentorEstablished(byte status, byte peerRole, byte level, byte[] name) =>
        MentorPacket(NotificationPacketType, 74, writer =>
        { writer.Write(status); writer.Write(peerRole); writer.Write(level); WriteMentorName(writer, name); });

    public static GameServerPacket CreateMentorReply(byte peerRole, byte level, byte[] name) =>
        MentorPacket(CommandPacketType, ReplyMentorCommand, writer =>
        { writer.Write((byte)1); writer.Write(peerRole); writer.Write(level); WriteMentorName(writer, name); });

    public static GameServerPacket CreateMentorRemoved(byte[] name, bool success = true) =>
        MentorPacket(CommandPacketType, RemoveMentorCommand, writer =>
        { writer.Write(success ? (byte)1 : (byte)0); if (success) WriteMentorName(writer, name); });

    public static GameServerPacket CreateMentorRemovalNotice(byte[] name) =>
        MentorPacket(NotificationPacketType, 77, writer => WriteMentorName(writer, name));

    public static GameServerPacket CreateMentorReset() => new(NotificationPacketType, 75, []);

    public static GameServerPacket CreateMentorGoldAward(uint actualGold, byte[] sourceName)
    {
        ArgumentNullException.ThrowIfNull(sourceName);
        if (sourceName.Length is < 1 or >= 50) throw new ArgumentOutOfRangeException(nameof(sourceName));
        return MentorPacket(NotificationPacketType, 76, writer =>
        {
            writer.Write(actualGold);
            writer.Write((short)0);
            WriteMentorName(writer, sourceName);
        });
    }

    public static GameServerPacket CreateMentorPresence(bool online, byte channel, byte level, byte[] name) =>
        MentorPacket(NotificationPacketType, 72, writer =>
        { writer.Write(online ? (byte)1 : (byte)0); writer.Write(channel); writer.Write(level); WriteMentorName(writer, name); });

    public static GameServerPacket CreateMentorListReply(MentorListEntry? master, IReadOnlyList<MentorListEntry> apprentices)
    {
        if (apprentices.Count > 10) throw new ArgumentOutOfRangeException(nameof(apprentices));
        return MentorPacket(CommandPacketType, MentorListCommand, writer =>
        {
            void Entry(MentorListEntry entry)
            {
                writer.Write(entry.Channel); writer.Write(entry.Level); WriteMentorName(writer, entry.Name);
                writer.Write(entry.Blocked); writer.Write(entry.RelationshipLevel);
                writer.Write(entry.Experience); writer.Write(entry.NextExperience);
            }
            writer.Write((byte)1);
            Entry(master ?? new(0, 0, [], 0, 0, 0, 0));
            writer.Write((byte)apprentices.Count);
            foreach (var child in apprentices) Entry(child);
        });
    }

    public static bool TryReadMentorName(byte[] body, out byte[] name) => TryReadLengthPrefixedName(body, out name);

    public static bool TryReadMentorReply(byte[] body, out byte reply)
    {
        reply = 0;
        if (body.Length != 3 || body[2] is not (1 or 2)) return false;
        reply = body[2];
        return true;
    }
}
