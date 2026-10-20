using System.Buffers.Binary;
using System.Text;

namespace DFLegacy.InterProcess;

/// <summary>REGISTER：game → channel。38 B。</summary>
public sealed record RegisterPayload(
    ushort ProtocolVersion,
    uint GcNo,
    uint ChannelNo,
    uint MaximumUsers,
    uint CurrentUsers,
    string AdvertisedHost,
    uint GamePort)
{
    public const int Length = 38;
    public const int HostWidth = 16;

    public byte[] Encode()
    {
        var buffer = new byte[Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), ProtocolVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(2, 4), GcNo);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(6, 4), ChannelNo);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(10, 4), MaximumUsers);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(14, 4), CurrentUsers);
        AsciiFixed.Write(buffer.AsSpan(18, HostWidth), AdvertisedHost);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(34, 4), GamePort);
        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out RegisterPayload? payloadValue)
    {
        payloadValue = null;
        if (payload.Length != Length)
        {
            return false;
        }

        payloadValue = new RegisterPayload(
            BinaryPrimitives.ReadUInt16LittleEndian(payload[..2]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(2, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(6, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(10, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(14, 4)),
            AsciiFixed.Read(payload.Slice(18, HostWidth)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(34, 4)));
        return true;
    }
}

/// <summary>REGISTER_ACK：channel → game。30 B。</summary>
public sealed record RegisterAckPayload(
    ushort ProtocolVersion,
    uint Accepted,
    string ChannelName,
    uint ChannelPort)
{
    public const int Length = 30;
    public const int NameWidth = 20;

    public byte[] Encode()
    {
        var buffer = new byte[Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), ProtocolVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(2, 4), Accepted);
        AsciiFixed.Write(buffer.AsSpan(6, NameWidth), ChannelName);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(26, 4), ChannelPort);
        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out RegisterAckPayload? payloadValue)
    {
        payloadValue = null;
        if (payload.Length != Length)
        {
            return false;
        }

        payloadValue = new RegisterAckPayload(
            BinaryPrimitives.ReadUInt16LittleEndian(payload[..2]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(2, 4)),
            AsciiFixed.Read(payload.Slice(6, NameWidth)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(26, 4)));
        return true;
    }
}

/// <summary>STATUS：game → channel，兼心跳。32 B。</summary>
public sealed record StatusPayload(
    ulong UptimeMs,
    uint ActiveSessions,
    uint LoggedInUsers,
    ulong TotalConnections,
    ulong TotalPackets)
{
    public const int Length = 32;

    public byte[] Encode()
    {
        var buffer = new byte[Length];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(0, 8), UptimeMs);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), ActiveSessions);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), LoggedInUsers);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(16, 8), TotalConnections);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(24, 8), TotalPackets);
        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out StatusPayload? payloadValue)
    {
        payloadValue = null;
        if (payload.Length != Length)
        {
            return false;
        }

        payloadValue = new StatusPayload(
            BinaryPrimitives.ReadUInt64LittleEndian(payload[..8]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(12, 4)),
            BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(16, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(24, 8)));
        return true;
    }
}

/// <summary>NOTICE_CHANNEL_SERVER：channel → game。24 B，复用官方 ID 与官方载荷布局。</summary>
public sealed record NoticeChannelServerPayload(int ServerGroup, string AdvertisedHost, int Port)
{
    public const int Length = 24;
    public const int HostWidth = 16;

    public byte[] Encode()
    {
        var buffer = new byte[Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), ServerGroup);
        AsciiFixed.Write(buffer.AsSpan(4, HostWidth), AdvertisedHost);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20, 4), Port);
        return buffer;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out NoticeChannelServerPayload? payloadValue)
    {
        payloadValue = null;
        if (payload.Length != Length)
        {
            return false;
        }

        payloadValue = new NoticeChannelServerPayload(
            BinaryPrimitives.ReadInt32LittleEndian(payload[..4]),
            AsciiFixed.Read(payload.Slice(4, HostWidth)),
            BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(20, 4)));
        return true;
    }
}

/// <summary>定宽 ASCII 字段（与官方 tpServerInfo 的 char[N] 同口径：不足补 0，写入前校验长度）。</summary>
internal static class AsciiFixed
{
    public static void Write(Span<byte> destination, string value)
    {
        var encoded = Encoding.ASCII.GetBytes(value);
        if (encoded.Length >= destination.Length)
        {
            throw new ArgumentException(
                $"ASCII value must be shorter than {destination.Length} bytes: {value}", nameof(value));
        }

        destination.Clear();
        encoded.CopyTo(destination);
    }

    public static string Read(ReadOnlySpan<byte> source)
    {
        var end = source.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? source : source[..end]);
    }
}
