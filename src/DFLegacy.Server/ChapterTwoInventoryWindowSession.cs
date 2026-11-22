using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed class ChapterTwoInventoryWindowSession
{
    private bool _negotiated;
    private uint _lastRequest;
    private ulong _revision;
    private ChapterTwoInventoryView? _view;

    internal GameServerPacket? Handle(InventoryWindowRequest request, CharacterRecord? current, ItemCatalog catalog)
    {
        if (request.RequestId <= _lastRequest) return null;
        InventoryWindowCell[] cells = [];
        if (request.Operation == InventoryWindowOperation.Hello)
        {
            if (_negotiated) return null;
            _negotiated = true;
        }
        else
        {
            if (!_negotiated || current is null) return null;
            if (!ReferenceEquals(_view?.Character, current))
            {
                _view = new ChapterTwoInventoryView(current);
                _revision++;
            }
            if (!_view!.TryRead(request, out cells)) return null;
        }

        _lastRequest = request.RequestId;
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(byte.MaxValue);
        writer.Write(InventoryWindowRequest.Magic);
        writer.Write(InventoryWindowRequest.Version);
        writer.Write((byte)request.Operation);
        writer.Write(request.RequestId);
        if (request.Operation == InventoryWindowOperation.Read)
        {
            writer.Write((byte)request.Section);
            writer.Write(request.First);
            writer.Write(InventoryAddress.Capacity(request.Section));
            writer.Write(_revision);
            writer.Write((byte)cells.Length);
            foreach (var cell in cells)
            {
                var item = cell.Item;
                var wire = CharacterItemWireProjection.Project(item, catalog);
                writer.Write(cell.Offset);
                writer.Write(item.ItemId);
                writer.Write(item.CountOrValue);
                writer.Write(wire.AddInfo);
                writer.Write(wire.Durability);
                writer.Write(wire.ItemAttr);
                writer.Write(wire.SealState);
                writer.Write(item.EnchantmentCardId);
                writer.Write(item.AvatarAppearanceItemId);
                writer.Write(item.InstanceId.ToByteArray());
            }
        }
        return new(GameProtocolEngine.NotificationPacketType, InventoryWindowRequest.ReplyNotification, payload.ToArray());
    }
}
