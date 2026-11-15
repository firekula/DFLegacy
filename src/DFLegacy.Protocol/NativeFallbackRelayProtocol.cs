using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public readonly record struct NativeFallbackRelayHeader(
    ushort Opcode, ushort Length, uint SenderAccountUid, uint RecipientAccountUid);

/// <summary>
/// Native relay transport shared by ACT1 and the inspected relay sample.
/// Its twelve-byte header is separate from game TCP and MT application framing.
/// Authentication, direction and current party/PvP scope belong to the service.
/// </summary>
public static class NativeFallbackRelayProtocol
{
    public const int HeaderLength = 12;
    public const int MaximumFrameLength = 8192;

    public static bool TryReadHeader(ReadOnlySpan<byte> bytes, out NativeFallbackRelayHeader header)
    {
        header = default;
        if (bytes.Length < HeaderLength) return false;
        var opcode = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]);
        if (opcode is not (0 or 1 or 3) || length is < HeaderLength or > MaximumFrameLength)
            return false;
        header = new(opcode, length,
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]));
        return true;
    }

    public static byte[] CreateDelivery(ReadOnlySpan<byte> frame)
    {
        if (!TryReadHeader(frame, out var header) || header.Length != frame.Length
            || header.Opcode is not (0 or 1))
            throw new InvalidDataException("Invalid native relay input frame.");
        var output = frame.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(output, 3);
        return output;
    }

    public static async Task<byte[]?> ReadTcpFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var prefix = new byte[4];
        if (await stream.ReadAsync(prefix.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) == 0)
            return null;
        await stream.ReadExactlyAsync(prefix.AsMemory(1, 3), cancellationToken).ConfigureAwait(false);
        var opcode = BinaryPrimitives.ReadUInt16LittleEndian(prefix);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(2));
        if (opcode is not (0 or 1 or 3) || length is < HeaderLength or > MaximumFrameLength)
            throw new InvalidDataException("Invalid native relay stream header.");
        var frame = new byte[length];
        prefix.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(4), cancellationToken).ConfigureAwait(false);
        return frame;
    }
}
