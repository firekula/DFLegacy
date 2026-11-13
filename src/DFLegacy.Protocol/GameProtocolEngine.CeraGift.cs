using System.Buffers.Binary;
using System.Text;

namespace DFLegacy.Protocol;

public readonly record struct GameCeraShopPurchaseItem(byte ProductType, ushort AttributeValue, uint CommodityNo)
{
    // ACT1 record+16 is the selected zero-based avatar ability, not category.
    // Retain ProductType construction for existing physical-product callers.
    public byte AvatarAbilityIndex => ProductType;
}
public sealed record GameCeraShopPurchaseRequest(bool IsGift, string RecipientName, GameCeraShopPurchaseItem[] Products);

public static partial class GameProtocolEngine
{
    public const byte QueryCeraGiftRecipientCommand = 146;
    private static Encoding CeraGiftEncoding
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
    }

    public static bool TryReadCeraGiftRecipient(byte[] body, out string name,
        bool allowChapterTwoPadding = false)
    {
        if (!TryReadGiftName(body.AsSpan(Math.Min(2, body.Length)), out name, out var consumed))
            return false;
        var suffix = body.AsSpan(sizeof(ushort) + consumed);
        // C2 pads the payload after the sequence to a 16-byte boundary.
        // Keep ACT1 strict and accept only the minimal all-zero suffix.
        return suffix.IsEmpty || (allowChapterTwoPadding
            && (body.Length - sizeof(ushort)) % 16 == 0
            && suffix.Length < 16 && !suffix.ContainsAnyExcept((byte)0));
    }

    private static bool TryReadGiftName(ReadOnlySpan<byte> body, out string name, out int consumed)
    {
        name = string.Empty;
        consumed = 0;
        if (body.Length < 4) return false;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(body);
        if (length is 0 or > 30 || length > body.Length - 4) return false;
        try { name = CeraGiftEncoding.GetString(body.Slice(4, (int)length)); }
        catch (DecoderFallbackException) { return false; }
        consumed = 4 + (int)length;
        return !name.Contains('\0') && name.Trim() == name;
    }

    public static bool TryReadCeraShopPurchase(byte[] body, out GameCeraShopPurchaseRequest request,
        bool chapterTwoClient = false)
    {
        request = null!;
        if (body.Length < 4 || body[2] > 1) return false;
        var gift = body[2] == 1;
        var offset = 3;
        var name = string.Empty;
        if (gift)
        {
            if (!TryReadGiftName(body.AsSpan(offset), out name, out var consumed)) return false;
            offset += consumed;
        }
        if (offset >= body.Length) return false;
        var count = body[offset++];
        // C2 94B770 appends a package selection-count byte to every row.
        // Current catalog supports fixed rewards only; do not silently drop
        // selections from a legacy selectable package.
        var rowSize = chapterTwoClient ? 8 : 7;
        var logicalLength = offset + count * rowSize;
        if (count is 0 or > 32 || logicalLength > body.Length) return false;
        var padding = body.AsSpan(logicalLength);
        if (!padding.IsEmpty && (!chapterTwoClient
            || (body.Length - sizeof(ushort)) % 16 != 0
            || padding.Length >= 16 || padding.ContainsAnyExcept((byte)0)))
        {
            return false;
        }
        var products = new GameCeraShopPurchaseItem[count];
        for (var i = 0; i < count; i++, offset += rowSize)
        {
            if (chapterTwoClient && body[offset + 7] != 0) return false;
            var commodity = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset + 3));
            if (commodity == 0) return false;
            products[i] = new(body[offset], BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(offset + 1)), commodity);
        }
        request = new(gift, name, products);
        return true;
    }

    public static GameServerPacket CreateCeraGiftRecipientReply(string name, ushort level, byte job,
        byte growth, byte gender, byte receivedCount)
    {
        var bytes = CeraGiftEncoding.GetBytes(name);
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((byte)1);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Write(level);
        writer.Write(job);
        writer.Write(growth);
        writer.Write(gender);
        writer.Write(receivedCount);
        return new(CommandPacketType, QueryCeraGiftRecipientCommand, payload.ToArray());
    }

    public static GameServerPacket CreateCeraGiftRecipientError(byte error) =>
        new(CommandPacketType, QueryCeraGiftRecipientCommand, [0, error]);
}
