using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public enum InventorySection : byte
{
    Equipment, Consumable, Material, Quest, Avatar
}

public enum InventoryWindowOperation : byte
{
    Hello, Read
}

// A stock CMD19 with this prefix has a negative source slot and cannot move an item.
// After the two-byte native sequence the extension occupies two complete cipher blocks.
public readonly record struct InventoryWindowRequest(
    InventoryWindowOperation Operation, InventorySection Section, int First, uint RequestId)
{
    // Ver_OB_216 notification 185 resolves to its default handler in the supported QA image.
    public const byte ReplyNotification = 185;
    public const byte Version = 1;
    public const int BodyLength = 34;
    public static ReadOnlySpan<byte> Magic => "DFIV"u8;

    public byte[] Encode(ushort sequence)
    {
        var bytes = new byte[BodyLength];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, sequence);
        bytes.AsSpan(2, 3).Fill(byte.MaxValue);
        Magic.CopyTo(bytes.AsSpan(5));
        bytes[9] = Version;
        bytes[10] = (byte)Operation;
        bytes[11] = (byte)Section;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), First);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), RequestId);
        return bytes;
    }

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out InventoryWindowRequest request)
    {
        request = default;
        if (bytes.Length != BodyLength || bytes.Slice(2, 3).IndexOfAnyExcept(byte.MaxValue) >= 0
            || !bytes.Slice(5, 4).SequenceEqual(Magic) || bytes[9] != Version
            || bytes[10] > (byte)InventoryWindowOperation.Read || bytes[11] > (byte)InventorySection.Avatar
            || bytes[20..].IndexOfAnyExcept((byte)0) >= 0) return false;
        var operation = (InventoryWindowOperation)bytes[10];
        var first = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        var requestId = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        if (first < 0 || requestId == 0 || operation == InventoryWindowOperation.Hello && (bytes[11] != 0 || first != 0))
            return false;
        request = new(operation, (InventorySection)bytes[11], first, requestId);
        return true;
    }
}
