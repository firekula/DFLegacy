using System.Buffers.Binary;

namespace DFLegacy.Server;

internal enum DungeonDebugDropResult : byte { None, Applied, Denied, Stale }
internal sealed record DungeonDebugDropState(bool Allowed, DungeonDebugDropResult Result, long RequestId);

// Local debug mailbox only; these bytes are never sent over the gameplay protocol.
internal sealed record DungeonDebugDropRequest(long RequestId, long RunId, ushort UserId, bool Enabled)
{
    internal const uint Magic = 0x31504444; // DDP1
    internal const int Size = 32;
    internal byte[] Encode()
    {
        var bytes = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), RequestId);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16), RunId);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(24), UserId);
        bytes[30] = Enabled ? (byte)1 : (byte)0;
        return bytes;
    }

    internal static bool TryDecode(ReadOnlySpan<byte> bytes, out DungeonDebugDropRequest request)
    {
        request = null!;
        if (bytes.Length != Size || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 1
            || bytes[30] > 1 || bytes[31] != 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[26..]) != 0)
            return false;
        var id = BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]);
        var run = BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]);
        var user = BinaryPrimitives.ReadUInt16LittleEndian(bytes[24..]);
        if (id <= 0 || run <= 0 || user == 0) return false;
        request = new(id, run, user, bytes[30] != 0);
        return true;
    }
}
