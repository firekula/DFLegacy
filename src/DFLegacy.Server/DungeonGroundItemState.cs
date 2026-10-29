namespace DFLegacy.Server;

public sealed record DungeonGroundItem(
    ushort GroundId,
    ushort ItemId,
    uint CountOrValue,
    bool IsGold,
    ushort OwnerUserId,
    ushort SourceMonsterId,
    byte RoomX,
    byte RoomY,
    byte PassiveObjectIndex = byte.MaxValue,
    byte PassiveItemSlot = byte.MaxValue,
    bool Activated = true,
    CharacterItemRecord? PreservedItem = null,
    short X = 0,
    short Y = 0,
    byte RoutingSource = 1,
    Guid ForcedOwnerCharacterId = default)
{
    public uint GetClientAddInfo(ItemCatalog catalog)
    {
        if (IsGold)
        {
            return CountOrValue;
        }

        return CharacterItemWireProjection.Project(
            GetProjectionItem(catalog),
            catalog).AddInfo;
    }

    public byte GetClientItemAttr(ItemCatalog catalog) =>
        CharacterItemWireProjection.Project(
            GetProjectionItem(catalog),
            catalog).ItemAttr;

    public ushort GetClientDurability(ItemCatalog catalog) =>
        CharacterItemWireProjection.Project(
            GetProjectionItem(catalog),
            catalog).Durability;

    private CharacterItemRecord GetProjectionItem(ItemCatalog catalog) =>
        PreservedItem ?? new CharacterItemRecord(
            ushort.MaxValue,
            ItemId,
            CountOrValue,
            Durability: catalog.GetInitialDurability(ItemId),
            SealState: CharacterItemSealing.GetInitialSealState(catalog, ItemId));
}

public sealed class DungeonGroundItemState
{
    private readonly Dictionary<ushort, DungeonGroundItem> _items = [];
    private ushort _nextGroundId = 1;

    public int Count => _items.Count;

    public DungeonGroundItem Add(
        DungeonGeneratedDrop drop,
        ushort ownerUserId,
        ushort sourceMonsterId,
        byte roomX,
        byte roomY,
        CharacterItemRecord? preservedItem = null,
        short x = 0,
        short y = 0,
        ItemCatalog? catalog = null,
        byte routingSource = 1)
    {
        var groundId = AllocateGroundId();
        preservedItem ??= CreateGeneratedItem(drop, catalog);
        var item = new DungeonGroundItem(
            groundId,
            drop.ItemId,
            drop.CountOrValue,
            drop.IsGold,
            ownerUserId,
            sourceMonsterId,
            roomX,
            roomY,
            PreservedItem: preservedItem,
            X: x,
            Y: y,
            RoutingSource: routingSource);
        _items.Add(groundId, item);
        return item;
    }

    public DungeonGroundItem AddPassive(
        DungeonGeneratedDrop drop,
        ushort ownerUserId,
        byte passiveObjectIndex,
        byte passiveItemSlot,
        byte roomX,
        byte roomY,
        short x = 0,
        short y = 0,
        ItemCatalog? catalog = null)
    {
        var groundId = AllocateGroundId();
        var preservedItem = CreateGeneratedItem(drop, catalog);
        var item = new DungeonGroundItem(
            groundId,
            drop.ItemId,
            drop.CountOrValue,
            drop.IsGold,
            ownerUserId,
            SourceMonsterId: ushort.MaxValue,
            roomX,
            roomY,
            passiveObjectIndex,
            passiveItemSlot,
            // DFLegacy reveals START_MAP passive associations locally when the
            // object breaks, then sends GET_ITEM directly. It does not reliably
            // send a separate passive-destruction command that the server can
            // use as an activation gate.
            Activated: true,
            PreservedItem: preservedItem,
            X: x,
            Y: y);
        _items.Add(groundId, item);
        return item;
    }

    public DungeonGroundItem AddPlayerDropped(
        CharacterItemRecord droppedItem,
        ushort ownerUserId,
        byte roomX,
        byte roomY,
        short x = 0,
        short y = 0,
        bool isGold = false,
        bool activated = true)
    {
        ArgumentNullException.ThrowIfNull(droppedItem);
        var groundId = AllocateGroundId();
        var item = new DungeonGroundItem(
            groundId,
            droppedItem.ItemId,
            droppedItem.CountOrValue,
            IsGold: isGold,
            ownerUserId,
            SourceMonsterId: ushort.MaxValue,
            roomX,
            roomY,
            Activated: activated,
            PreservedItem: isGold ? null : droppedItem with { Slot = ushort.MaxValue },
            X: x,
            Y: y,
            RoutingSource: 0);
        _items.Add(groundId, item);
        return item;
    }

    public bool TryGet(
        ushort groundId,
        byte roomX,
        byte roomY,
        out DungeonGroundItem item)
    {
        if (_items.TryGetValue(groundId, out item!)
            && item.RoomX == roomX
            && item.RoomY == roomY
            && item.Activated)
        {
            return true;
        }

        item = null!;
        return false;
    }

    public int ActivatePassiveObject(
        byte passiveObjectIndex,
        byte roomX,
        byte roomY)
    {
        var activated = 0;
        foreach (var entry in _items
                     .Where(entry =>
                         entry.Value.RoomX == roomX
                         && entry.Value.RoomY == roomY
                         && entry.Value.PassiveObjectIndex == passiveObjectIndex
                         && !entry.Value.Activated)
                     .ToArray())
        {
            _items[entry.Key] = entry.Value with { Activated = true };
            activated++;
        }

        return activated;
    }

    public bool Remove(ushort groundId, DungeonGroundItem? expected = null) =>
        (expected is null || _items.TryGetValue(groundId, out var item) && ReferenceEquals(item, expected))
        && _items.Remove(groundId);

    public DungeonGroundItem? Activate(DungeonGroundItem expected)
    {
        if (!_items.TryGetValue(expected.GroundId, out var item) || !ReferenceEquals(item, expected))
            return null;
        var active = item with { Activated = true };
        _items[item.GroundId] = active;
        return active;
    }

    /// <summary>
    /// 落库失败时把刚摘掉的掉落**按原 GroundId 原样放回**（记录里的房间/归属/被动物件关联/耐久/坐标
    /// 全在快照里，直接放回即可）。GroundId 若已被别人占用（理论上的 id 回绕）返回 false，
    /// 调用方要记错误日志——不许静默丢件。
    /// </summary>
    internal void Relocate(DungeonGroundItem item, short x, short y)
    {
        if (!_items.TryGetValue(item.GroundId, out var current) || !ReferenceEquals(current, item))
            throw new InvalidOperationException("Ground relocation requires the original record.");
        _items[item.GroundId] = item with { X = x, Y = y };
    }

    public bool TryReAdd(DungeonGroundItem item) => _items.TryAdd(item.GroundId, item);

    public IReadOnlyList<DungeonGroundItem> GetRoomItems(byte roomX, byte roomY) =>
        _items.Values
            .Where(item => item.RoomX == roomX && item.RoomY == roomY)
            .OrderBy(item => item.GroundId)
            .ToArray();

    public IReadOnlyList<DungeonGroundItem> GetPassiveObjectItems(
        byte roomX,
        byte roomY) =>
        GetRoomItems(roomX, roomY)
            .Where(item => item.PassiveObjectIndex != byte.MaxValue)
            .ToArray();

    public int DetachPassiveObjectItems(
        byte passiveObjectIndex,
        byte roomX,
        byte roomY)
    {
        var detached = 0;
        foreach (var entry in _items
                     .Where(entry =>
                         entry.Value.RoomX == roomX
                         && entry.Value.RoomY == roomY
                         && entry.Value.PassiveObjectIndex == passiveObjectIndex)
                     .ToArray())
        {
            _items[entry.Key] = entry.Value with
            {
                PassiveObjectIndex = byte.MaxValue,
                PassiveItemSlot = byte.MaxValue,
                Activated = true
            };
            detached++;
        }

        return detached;
    }

    public int DetachAllPassiveObjectItems(byte roomX, byte roomY)
    {
        var detached = 0;
        foreach (var passiveObjectIndex in GetPassiveObjectItems(roomX, roomY)
                     .Select(item => item.PassiveObjectIndex)
                     .Distinct()
                     .ToArray())
        {
            detached += DetachPassiveObjectItems(passiveObjectIndex, roomX, roomY);
        }

        return detached;
    }

    public void ResetDungeon()
    {
        _items.Clear();
        _nextGroundId = 1;
    }

    private static CharacterItemRecord? CreateGeneratedItem(
        DungeonGeneratedDrop drop,
        ItemCatalog? catalog)
    {
        if (drop.IsGold
            || catalog is null
            || !catalog.TryGetDefinition(drop.ItemId, out var definition)
            || !CharacterItemIdentity.RequiresInstanceId(definition))
        {
            return null;
        }

        return new CharacterItemRecord(
            Slot: ushort.MaxValue,
            ItemId: drop.ItemId,
            CountOrValue: drop.CountOrValue,
            Durability: catalog.GetInitialDurability(drop.ItemId),
            SealState: CharacterItemSealing.GetInitialSealState(definition),
            InstanceId: CharacterItemIdentity.CreateInstanceId());
    }

    private ushort AllocateGroundId()
    {
        for (var attempt = 0; attempt < ushort.MaxValue - 1; attempt++)
        {
            var candidate = _nextGroundId++;
            if (_nextGroundId == 0 || _nextGroundId == ushort.MaxValue)
            {
                _nextGroundId = 1;
            }

            if (candidate is not 0 and not ushort.MaxValue
                && !_items.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No ground-item ids remain in this dungeon session.");
    }
}
