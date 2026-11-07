using System.Buffers.Binary;

namespace DFLegacy.Protocol;

public sealed record PrivateStorePosition(byte Town, byte Area, short X, short Y);
public sealed record PrivateStoreDisplayRequest(ushort Slot, uint UnitPrice, byte SourceKind, ushort SourceSlot, uint AddInfo);
public sealed record PrivateStoreGoodsRow(ushort Slot, uint UnitPrice, ushort ItemId, uint AddInfo, byte State, ushort Durability);
public sealed record PrivateStoreSummary(ushort OwnerUid, byte[] Title, bool Completed, PrivateStorePosition Position);

public static partial class GameProtocolEngine
{
    public const byte BuyPrivateStoreCommand = 85, EnterPrivateStoreCommand = 86, ExitPrivateStoreCommand = 87,
        CreatePrivateStoreCommand = 88, RemovePrivateStoreCommand = 89, DisplayPrivateStoreCommand = 90;
    public const byte PrivateStoreServiceType = 8;
    // ACT1's CMD90 sender counts and writes occupied slots 0 through 13.
    public const int PrivateStoreMaximumListings = 14;

    private static GameServerPacket StorePacket(byte type, byte id, Action<BinaryWriter> write)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        write(writer);
        return new(type, id, output.ToArray());
    }
    private static void StoreText(BinaryWriter w, byte[] text, int maximum)
    {
        if (text.Length > maximum || text.Contains((byte)0)) throw new ArgumentException("Invalid private-store text.");
        w.Write((uint)text.Length); w.Write(text);
    }
    private static void StorePosition(BinaryWriter w, PrivateStorePosition p)
    { w.Write(p.Town); w.Write(p.Area); w.Write(p.X); w.Write(p.Y); }
    private static void StoreGoods(BinaryWriter w, IReadOnlyList<PrivateStoreGoodsRow> goods)
    {
        if (goods.Count > PrivateStoreMaximumListings) throw new ArgumentOutOfRangeException(nameof(goods));
        w.Write((byte)goods.Count);
        foreach (var g in goods)
        { w.Write(g.Slot); w.Write(g.UnitPrice); w.Write(g.ItemId); w.Write(g.AddInfo); w.Write(g.State); w.Write(g.Durability); }
    }
    public static GameServerPacket CreatePrivateStoreReply(byte command, byte error = 0) =>
        new(CommandPacketType, command, error == 0 ? [1] : [0, error]);
    public static GameServerPacket CreatePrivateStoreCreated(ushort uid, PrivateStorePosition p) =>
        StorePacket(0, 78, w => { w.Write(uid); StorePosition(w, p); });
    public static GameServerPacket CreatePrivateStoreRemoved(ushort uid) =>
        StorePacket(0, 79, w => w.Write(uid));
    public static GameServerPacket CreatePrivateStoreForceClose() => new(0, 80, []);
    public static GameServerPacket CreatePrivateStoreDisplayed(ushort uid, byte[] title) =>
        StorePacket(0, 81, w => { w.Write(uid); StoreText(w, title, 40); });
    public static GameServerPacket CreatePrivateStoreSale(uint gold, ushort itemId, ushort slot, uint quantity,
        byte[] buyer, uint gross, uint fee) => StorePacket(0, 82, w =>
        { w.Write((byte)1); w.Write(gold); w.Write(itemId); w.Write(slot); w.Write(quantity); StoreText(w, buyer, 19); w.Write(gross); w.Write(fee); });
    public static GameServerPacket CreatePrivateStoreSaleFailure(byte error) => new(0, 82, [0, error]);
    public static GameServerPacket CreatePrivateStoreGoodsSold(ushort slot, uint quantity) =>
        StorePacket(0, 83, w => { w.Write(slot); w.Write(quantity); });
    public static GameServerPacket CreatePrivateStoreList(IReadOnlyList<PrivateStoreSummary> stores) =>
        StorePacket(0, 84, w =>
        {
            if (stores.Count > 255) throw new ArgumentOutOfRangeException(nameof(stores));
            w.Write((byte)stores.Count);
            foreach (var s in stores)
            { w.Write(s.OwnerUid); StoreText(w, s.Title, 40); w.Write(s.Completed ? (byte)1 : (byte)0); StorePosition(w, s.Position); }
        });
    public static GameServerPacket CreatePrivateStoreInfo(byte speechFlag, IReadOnlyList<PrivateStoreGoodsRow> goods) =>
        StorePacket(0, 85, w => { w.Write(speechFlag); StoreGoods(w, goods); });
    public static GameServerPacket CreatePrivateStoreEnterReply(ushort uid, IReadOnlyList<PrivateStoreGoodsRow> goods) =>
        StorePacket(1, 86, w => { w.Write((byte)1); w.Write(uid); StoreGoods(w, goods); });
    public static GameServerPacket CreatePrivateStoreEnterFailure() => new(1, 86, [0]);
    public static GameServerPacket CreatePrivateStoreBuyReply(uint gold, uint victoryPoints, uint cash,
        ushort destinationSlot, ushort itemId, uint addInfo, byte state, ushort durability) =>
        StorePacket(1, 85, w =>
        { w.Write((byte)1); w.Write(gold); w.Write(victoryPoints); w.Write(cash); w.Write(destinationSlot); w.Write(itemId); w.Write(addInfo); w.Write(state); w.Write(durability); });

    public static bool TryReadPrivateStoreCreate(byte[] body, out PrivateStorePosition position)
    {
        position = null!;
        if (body.Length != 8) return false;
        position = new(body[2], body[3], BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(4)), BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(6)));
        return true;
    }
    public static bool TryReadPrivateStoreEnter(byte[] body, out uint uid)
    { uid = 0; if (body.Length != 6) return false; uid = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(2)); return true; }
    public static bool TryReadPrivateStoreBuy(byte[] body, out ushort slot, out uint addInfo)
    {
        slot = 0; addInfo = 0;
        if (body.Length != 8) return false;
        slot = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2)); addInfo = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4));
        return addInfo <= int.MaxValue;
    }
    public static bool TryReadPrivateStoreDisplay(byte[] body, out byte[] title, out IReadOnlyList<PrivateStoreDisplayRequest> goods)
    {
        title = []; goods = [];
        if (body.Length < 8) return false;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(2));
        if (length is < 1 or > 40 || length > body.Length - 7) return false;
        var offset = 6 + (int)length;
        var count = body[offset++];
        if (count is < 1 or > PrivateStoreMaximumListings || body.Length != offset + count * 13) return false;
        title = body.AsSpan(6, (int)length).ToArray();
        if (title.Contains((byte)0)) return false;
        var rows = new List<PrivateStoreDisplayRequest>();
        for (var i = 0; i < count; i++, offset += 13)
        {
            var row = body.AsSpan(offset, 13);
            rows.Add(new(BinaryPrimitives.ReadUInt16LittleEndian(row), BinaryPrimitives.ReadUInt32LittleEndian(row[2..]), row[6],
                BinaryPrimitives.ReadUInt16LittleEndian(row[7..]), BinaryPrimitives.ReadUInt32LittleEndian(row[9..])));
        }
        goods = rows; return true;
    }
}
