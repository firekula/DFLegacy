using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public sealed record GameGuildMember(byte[] Name, ushort Level, byte Job, byte GrowType,
    byte AwakeningType, byte Rank, bool Online, uint OfflineSeconds = 0);

public static partial class GameProtocolEngine
{
    public const byte GuildMembersCommand = 70;
    public const byte GuildCreateCommand = 71;
    public const byte GuildUpgradeCommand = 76;
    public const byte GuildInfoCommand = 77;
    public const byte GuildAllMembersCommand = 150;
    public const byte GuildMembersNextCommand = 151;
    public const byte GuildAnnouncementNotification = 153;
    public const byte GuildSetAnnouncementCommand = 165;
    public const byte GuildCheckNameCommand = 167;
    public const byte GuildCheckAddressCommand = 168;
    public const byte GuildOpenCreateCommand = 169;
    public const byte GuildDissolveCommand = 193;
    public const byte GuildDissolvedNotification = 187;
    public const int GuildAnnouncementMaxBytes = 100;

    public static GameServerPacket CreateGuildDissolved(bool success)
    {
        // C2 has no CMD193 reply entry. Its NOTI187 reader takes a u32 status,
        // where zero clears the recipient's local guild identity.
        var payload = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, success ? 0u : 1u);
        return new(NotificationPacketType, GuildDissolvedNotification, payload);
    }

    public static bool TryReadGuildText(byte[] body, out byte[] text) =>
        TryReadGuildText(body, 30, out text);

    public static bool TryReadGuildAnnouncement(byte[] body, out byte[] text) =>
        TryReadGuildText(body, GuildAnnouncementMaxBytes, out text);

    private static bool TryReadGuildText(byte[] body, int maximumLength, out byte[] text)
    {
        text = [];
        if (body.Length < 6) return false;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(2));
        if (length == 0 || length > maximumLength || !HasCommandBodyLength(body, 6 + (int)length, true))
            return false;
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

    public static GameServerPacket CreateGuildAnnouncement(byte[] announcement)
    {
        var payload = new byte[sizeof(uint) + announcement.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)announcement.Length);
        announcement.CopyTo(payload, sizeof(uint));
        return new(NotificationPacketType, GuildAnnouncementNotification, payload);
    }

    public static GameServerPacket CreateGuildInfo(byte[] name, byte rank, ushort memberCount,
        byte[]? announcement = null, byte guildLevel = 1, uint experience = 0,
        ushort skillPoints = 0, IReadOnlyList<GameSkillEntry>? skills = null)
    {
        // C2 NOTI70, native reader 0042ADFB. Level is zero-based on the wire.
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        WriteLengthPrefixedBytes(writer, name);
        writer.Write(rank == 1 ? (byte)1 : (byte)0); // founder flag
        writer.Write((byte)(Math.Max(1, (int)guildLevel) - 1));
        writer.Write((byte)0);
        writer.Write(memberCount);
        writer.Write((byte)2); // completed guild creation right
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write(experience);
        writer.Write(skillPoints);
        writer.Write(checked((byte)(skills?.Count ?? 0)));
        if (skills is not null)
            foreach (var skill in skills)
            {
                writer.Write((uint)skill.SkillId);
                writer.Write(skill.Level);
            }
        WriteLengthPrefixedBytes(writer, announcement ?? []);
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
            writer.Write((byte)1); // Use the original awakening job title.
            writer.Write((byte)0);
            // The C2 roster constructor writes the last byte to its rank
            // field at +0xC0, the same field updated by NOTI152.
            writer.Write(member.Rank);
            writer.Write(member.OfflineSeconds);
        }
        return new(CommandPacketType, command, payload.ToArray());
    }
}
