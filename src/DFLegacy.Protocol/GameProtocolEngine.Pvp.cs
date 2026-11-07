using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DFLegacy.Protocol;

public sealed record PvpCreateRequest(byte NameSelection, byte[] Name, ushort RequestedMap);
public sealed record PvpSeatInfo(byte Seat, byte State, ushort Uid);
public sealed record PvpRoomInfo(ushort RoomId, byte NameSelection, byte[] Name, byte State,
    byte HostSeat, ushort RequestedMap, byte Mode, IReadOnlyList<PvpSeatInfo> Seats, bool PasswordProtected = false);
public sealed record PvpRelayOrder(byte Seat, ushort Priority);
public sealed record PvpRelayTeam(byte NextSeat, IReadOnlyList<PvpRelayOrder> Order);
public sealed record PvpRankReport(ushort[] Stats, uint[] Scores, uint TotalScore);
public sealed record PvpResultRankEntry(ushort Uid, uint TotalScore, byte Grade, uint TotalPvpExp,
    uint CurrentGradeThreshold, uint NextGradeThreshold);

public static partial class GameProtocolEngine
{
    public const byte MakePvpRoomCommand = 53;
    public const byte EnterPvpRoomCommand = 54;
    public const byte SetPvpSeatCommand = 55;
    public const byte SetPvpReadyCommand = 56;
    public const byte SetPvpModeCommand = 57;
    public const byte PvpDeathCommand = 58;
    public const byte PvpTimeoutCommand = 59;
    public const byte PvpResultAckCommand = 60;
    public const byte PvpRankCommand = 61;
    public const byte SetPvpMapCommand = 62;
    public const byte PvpQuickJoinCommand = 101;
    public const byte PvpHeartbeatCommand = 120;
    public const byte PvpRequestFightCommand = 122;

    // The bounded implementation supports the native 16 presets and short custom names.
    // The byte bound is local input hardening, not a claim about a later server's limit.
    private const int MaximumPvpRoomNameBytes = 64;

    private static GameServerPacket PvpPacket(byte type, byte id, Action<BinaryWriter> write)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        write(writer);
        return new(type, id, output.ToArray());
    }

    /// <summary>ACT1's complete type4 login tail. Empty relay means unavailable locally,
    /// not a proven native disable flag; this endpoint is distinct from gameplay UDP.</summary>
    public static GameServerPacket CreatePvpLoginSuccess(uint accountUid, string relayIPv4,
        uint relayTcpPort, uint relayUdpPort) =>
        CreateLoginSuccessCore(accountUid, 4, relayIPv4, relayTcpPort, relayUdpPort);

    public static GameServerPacket CreatePvpRoomList(IReadOnlyList<PvpRoomInfo> rooms)
    {
        if (rooms.Count > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(rooms));
        foreach (var room in rooms)
        {
            ValidatePvpRoomState(room.State, room.HostSeat, room.Mode);
            if (room.NameSelection > 16 || (room.NameSelection == 0 && !IsValidPvpName(room.Name)))
                throw new ArgumentException("Invalid PvP room name.", nameof(rooms));
            if (room.Seats.Count != 8 || room.Seats.Select(s => s.Seat).Distinct().Count() != 8)
                throw new ArgumentException("A native room contains exactly eight distinct seats.", nameof(rooms));
            foreach (var seat in room.Seats) ValidatePvpSeat(seat);
            if (room.PasswordProtected) throw new ArgumentException("Password rooms are outside the bounded ACT1 implementation.", nameof(rooms));
        }
        return PvpPacket(NotificationPacketType, 41, writer =>
        {
            writer.Write((ushort)rooms.Count);
            foreach (var room in rooms)
            {
                writer.Write(room.RoomId); writer.Write(room.NameSelection);
                if (room.NameSelection == 0) { writer.Write((uint)room.Name.Length); writer.Write(room.Name); }
                writer.Write(room.State); writer.Write(room.HostSeat); writer.Write(room.RequestedMap); writer.Write(room.Mode);
                foreach (var seat in room.Seats.OrderBy(s => s.Seat)) { writer.Write(seat.State); writer.Write(seat.Uid); }
                writer.Write((byte)0);
            }
        });
    }

    public static GameServerPacket CreatePvpRoomState(ushort roomId, byte state, byte hostSeat, ushort requestedMap, byte mode = 0)
    {
        ValidatePvpRoomState(state, hostSeat, mode);
        return PvpPacket(NotificationPacketType, 42, writer =>
        { writer.Write(roomId); writer.Write(state); writer.Write(hostSeat); writer.Write(requestedMap); writer.Write(mode); });
    }

    public static GameServerPacket CreatePvpSeatState(ushort roomId, IReadOnlyList<PvpSeatInfo> seats, byte mode = 0)
    {
        if (mode > 2 || seats.Count is < 1 or > 8 || seats.Select(s => s.Seat).Distinct().Count() != seats.Count)
            throw new ArgumentException("Invalid PvP seat update.", nameof(seats));
        foreach (var seat in seats) ValidatePvpSeat(seat);
        return PvpPacket(NotificationPacketType, 43, writer =>
        {
            writer.Write(roomId); writer.Write(mode); writer.Write((byte)seats.Count);
            foreach (var seat in seats) { writer.Write(seat.Seat); writer.Write(seat.State); writer.Write(seat.Uid); }
        });
    }

    public static GameServerPacket CreatePvpReadyState(byte seat, bool ready)
    {
        if (seat > 7) throw new ArgumentOutOfRangeException(nameof(seat));
        return new(NotificationPacketType, 44, [seat, ready ? (byte)1 : (byte)0]);
    }

    public static GameServerPacket CreatePvpStart(byte selectedMap, byte mode = 0)
    {
        if (selectedMap == 0 || mode > 2) throw new ArgumentException("Start requires a selected native map and a supported mode.");
        return new(NotificationPacketType, 45, [selectedMap, mode]);
    }

    public static GameServerPacket CreatePvpDeath(byte deadSeat, byte killerSeat)
    {
        if (deadSeat > 7 || (killerSeat > 7 && killerSeat != 255)) throw new ArgumentOutOfRangeException(nameof(deadSeat));
        return new(NotificationPacketType, 46, [deadSeat, killerSeat]);
    }

    public static GameServerPacket CreatePvpRelayTurn(IReadOnlyList<PvpRelayTeam> teams)
    {
        if (teams.Count != 2 || teams.Any(t => (t.NextSeat > 7 && t.NextSeat != 255) || t.Order.Count > 4)
            || teams.SelectMany(t => t.Order).Any(o => o.Seat > 7 || o.Priority == 0)
            || teams.SelectMany(t => t.Order).Select(o => o.Seat).Distinct().Count() != teams.Sum(t => t.Order.Count))
            throw new ArgumentException("Invalid ACT1 relay turn.", nameof(teams));
        return PvpPacket(NotificationPacketType, 124, writer =>
        {
            writer.Write((byte)2);
            foreach (var team in teams)
            {
                writer.Write(team.NextSeat); writer.Write((byte)team.Order.Count);
                foreach (var order in team.Order) { writer.Write(order.Seat); writer.Write(order.Priority); }
            }
        });
    }

    public static GameServerPacket CreatePvpFightRequest(byte seat)
    {
        if (seat > 7) throw new ArgumentOutOfRangeException(nameof(seat));
        return new(NotificationPacketType, 125, [seat]);
    }

    public static GameServerPacket CreatePvpRankRequest() => new(NotificationPacketType, 49, []);

    public static GameServerPacket CreatePvpEnterReply(IReadOnlyList<bool> ready)
    {
        if (ready.Count != 8) throw new ArgumentException("Enter reply requires eight ready flags.", nameof(ready));
        return new(CommandPacketType, EnterPvpRoomCommand, [(byte)1, ..ready.Select(r => r ? (byte)1 : (byte)0)]);
    }

    /// <summary>ACT1 common result: winning representative seat for all three modes.</summary>
    public static GameServerPacket CreatePvpResult(byte winningSeat, uint recipientVictoryPoints,
        byte recipientGrade, int recipientExpDelta, IReadOnlyList<PvpResultRankEntry> ranks, ushort aceUid, bool draw, ushort allKillUid = ushort.MaxValue)
    {
        if (winningSeat > 7 || recipientGrade > 30 || ranks.Count is < 1 or > 8
            || ranks.Any(r => r.Uid == ushort.MaxValue || r.Grade > 30)
            || ranks.Select(r => r.Uid).Distinct().Count() != ranks.Count)
            throw new ArgumentException("Invalid PvP result.");
        return PvpPacket(NotificationPacketType, 47, writer =>
        {
            writer.Write(winningSeat); writer.Write(recipientVictoryPoints); writer.Write(recipientGrade); writer.Write(recipientExpDelta);
            writer.Write((byte)ranks.Count);
            foreach (var rank in ranks)
            {
                writer.Write(rank.Uid); writer.Write(rank.TotalScore); writer.Write(rank.Grade); writer.Write(rank.TotalPvpExp);
                writer.Write(rank.CurrentGradeThreshold); writer.Write(rank.NextGradeThreshold);
                writer.Write(0u); writer.Write(0u); // Deathmatch counters are excluded in individual mode.
            }
            writer.Write(aceUid); writer.Write(allKillUid);
            writer.Write(0u); // Ordinary individual does not grant character EXP.
            writer.Write(draw ? (byte)1 : (byte)0);
        });
    }

    // All command readers include the existing u16 transport/session sequence prefix.
    public static bool TryReadPvpCreate(byte[] body, out PvpCreateRequest? request)
    {
        request = null;
        if (body.Length < 6) return false;
        var nameSelection = body[2];
        if (nameSelection > 16) return false;
        var cursor = 3;
        byte[] name = [];
        if (nameSelection == 0)
        {
            if (body.Length < cursor + 4) return false;
            var length = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(cursor)); cursor += 4;
            if (length is 0 or > MaximumPvpRoomNameBytes || length > body.Length - cursor) return false;
            name = body.AsSpan(cursor, (int)length).ToArray(); cursor += (int)length;
            if (!IsValidPvpName(name)) return false;
        }
        if (body.Length != cursor + 3 || body[cursor + 2] != 0) return false;
        request = new(nameSelection, name, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(cursor)));
        return true;
    }

    public static bool TryReadPvpEnter(byte[] body, out ushort roomId)
    {
        roomId = 0;
        if (body.Length != 5 || body[4] != 0) return false;
        roomId = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2)); return true;
    }

    public static bool TryReadPvpSeat(byte[] body, out byte seat, out byte state)
    {
        seat = state = 0;
        if (body.Length != 4 || body[2] > 7 || body[3] is not (0 or 1 or 2 or 254 or 255)) return false;
        seat = body[2]; state = body[3]; return true;
    }

    public static bool TryReadPvpReady(byte[] body, out bool ready)
    {
        ready = false;
        if (body.Length != 3 || body[2] > 1) return false;
        ready = body[2] == 1; return true;
    }

    public static bool TryReadPvpMode(byte[] body, out byte mode)
    {
        mode = 0;
        if (body.Length != 3 || body[2] > 2) return false;
        mode = body[2]; return true;
    }

    public static bool TryReadPvpMap(byte[] body, out ushort requestedMap)
    {
        requestedMap = 0;
        if (body.Length != 4) return false;
        requestedMap = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2)); return true;
    }

    public static bool TryReadPvpDeath(byte[] body, out ushort killerUid) => TryReadPvpMap(body, out killerUid);

    public static bool TryReadPvpTimeout(byte[] body, out uint[] hpDecreases)
    {
        hpDecreases = [];
        if (body.Length != 34) return false;
        hpDecreases = new uint[8];
        for (var i = 0; i < 8; i++) hpDecreases[i] = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(2 + i * 4));
        return true;
    }

    public static bool TryReadPvpRank(byte[] body, out PvpRankReport? report)
    {
        report = null;
        if (body.Length != 56) return false;
        var stats = new ushort[11]; var scores = new uint[8]; ulong total = 0;
        for (var i = 0; i < stats.Length; i++) stats[i] = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2 + i * 2));
        for (var i = 0; i < scores.Length; i++) { scores[i] = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(24 + i * 4)); total += scores[i]; }
        if (total > uint.MaxValue) return false;
        report = new(stats, scores, (uint)total); return true;
    }

    public static bool TryReadPvpAck(byte[] body) => body.Length == 2;
    public static bool TryReadPvpHeartbeat(byte[] body) => body.Length == 2;

    private static bool IsValidPvpName(byte[] name)
    {
        if (name.Length is 0 or > MaximumPvpRoomNameBytes || name.Any(b => b < 32 || b == 127)) return false;
        // Validate GBK lead/trail pairs without substituting malformed client bytes.
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] < 0x80 || name[i] == 0x80) continue;
            if (name[i] == 0xFF || ++i == name.Length || name[i] < 0x40 || name[i] == 0x7F || name[i] == 0xFF) return false;
        }
        return true;
    }

    private static void ValidatePvpRoomState(byte state, byte hostSeat, byte mode)
    {
        if (state > 2 || hostSeat > 7 || mode > 2) throw new ArgumentException("Invalid PvP room state.");
    }

    private static void ValidatePvpSeat(PvpSeatInfo seat)
    {
        if (seat.Seat > 7 || (seat.State <= 2 ? seat.Uid == ushort.MaxValue
            : seat.State is not (254 or 255) || seat.Uid != ushort.MaxValue))
            throw new ArgumentException("Invalid PvP seat.", nameof(seat));
    }
}
