using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 副本实例成员：会话入场时登记自己的发送句柄。Send 走会话的 SerializedWriteStream
/// （`EntranceService.SendTownPacketAsync`，跨线程写入已串行化），所以别的会话可以直接投包。
/// </summary>
internal sealed record DungeonInstanceMember(
    Guid CharacterId,
    ushort AreaUserId,
    byte PartySlot,
    string CharacterName,
    Func<GameServerPacket, ValueTask> Send);

/// <summary>
/// 会话镜像快照：会话在主循环顶部按 <see cref="DungeonInstance.Version"/> 决定要不要刷新自己的
/// currentDungeon* 局部量（房间被别的成员/扫描循环推进时，本会话也能跟上）。
/// </summary>
internal sealed record DungeonInstanceSnapshot(
    long Version,
    ushort DungeonId,
    byte Difficulty,
    byte DungeonOption,
    DungeonLayout? Layout,
    byte RoomX,
    byte RoomY,
    DungeonRoomDefinition? Room,
    byte RoomStateFlag,
    byte HellPartyMode,
    bool ClearEnabled,
    int MemberCount);

/// <summary>进入房间后的发包快照：NOTI 28 的 hell 房坐标 + NOTI 29 的房间/怪物/被动掉落。</summary>
internal sealed record DungeonInstanceRoomSnapshot(
    byte RoomX,
    byte RoomY,
    ushort MapId,
    uint RandomSeed,
    byte RoomStateFlag,
    byte HellPartyMode,
    byte HellRoomX,
    byte HellRoomY,
    DungeonRoomDefinition Room,
    GameDungeonMonster[] Monsters,
    GamePassiveObjectDrop[] PassiveObjectDrops,
    bool FirstVisit,
    bool ReuseClientRoomState);

/// <summary>
/// 一个副本实例（设计稿 §3.1）：每队一份；单人 = 以角色 id 为键的实例（设计稿 §7 决策 1）。
/// 房间/已杀怪/掉落/房间开放集中在这里，用 <see cref="Gate"/> 保护：
/// 锁内改状态并取快照，出锁再发包（沿用 PartyService/ItemTradeService 的并发约定）。
/// 共享集合（`AliveMonsters`/`ReportedBossDeaths`/`BossCascadeMonsterDeaths`/`DestroyedPassiveObjects`/`GroundItems`）
/// 一律 internal：**只允许在 `lock (Gate)` 内访问**（本类内部，或后续 Task 在实例锁内拿它们写实现）；
/// 会话代码不许持裸句柄跨线程读写，只走本类的锁内方法（`SnapshotAliveMonsters`/`GetRoomGroundItems`/…）。
/// </summary>
internal sealed class DungeonInstance(
    ushort partyId,
    Guid soloCharacterId,
    DungeonCatalog catalog,
    ItemCatalog itemCatalog)
{
    private readonly List<DungeonInstanceMember> _members = [];
    private readonly DungeonRunState _run = new(catalog);

    // 被动掉落"登记过"的房间。判据必须是这个集合，而不是"现在还剩几条掉落"：
    // 捡走（Remove）/detach 会让后者归零，而房间定义里的 PassiveItemSpawns 是静态的。
    private readonly HashSet<DungeonRoomPosition> _registeredPassiveRooms = [];

    private DungeonRoomDefinition? _detachedRoom;

    public ushort PartyId { get; } = partyId;
    public Guid SoloCharacterId { get; } = soloCharacterId;

    /// <summary>本局的迷宫布局（`TryStartRun` 记下；教学/空实例为 null）。</summary>
    public DungeonLayout? Layout { get; private set; }

    /// <summary>队伍实例（PartyId != 0）；单人实例以角色 id 为键。</summary>
    public bool IsParty => PartyId != 0;

    /// <summary>实例锁。状态一律走本类的锁内方法改；调用方不要直接改公开集合。</summary>
    public object Gate { get; } = new();

    /// <summary>状态版本号：每次锁内改状态 +1。会话据此刷新镜像（主循环顶部检查）。</summary>
    public long Version { get; private set; }

    public DungeonRunState Run => _run;

    /// <summary>实例的地面掉落。**只允许在 <see cref="Gate"/> 内访问**；会话走 `GetRoomGroundItems`/`AddGroundItem` 等方法。</summary>
    internal DungeonGroundItemState GroundItems { get; } = new();

    /// <summary>本房间还活着的怪（换房间时按新房间重建，与改造前的 aliveDungeonMonsters 同口径）。**只允许在 <see cref="Gate"/> 内访问**。</summary>
    internal HashSet<ushort> AliveMonsters { get; } = [];

    /// <summary>BOSS_DIE_CHECK 已上报的 boss。**只允许在 <see cref="Gate"/> 内访问**；会话走 <see cref="TryReportBossDeath"/>。</summary>
    internal HashSet<ushort> ReportedBossDeaths { get; } = [];

    /// <summary>全部 boss 上报后要一起死的怪（改造前的 bossCascadeMonsterDeaths）。**只允许在 <see cref="Gate"/> 内访问**。</summary>
    internal HashSet<ushort> BossCascadeMonsterDeaths { get; } = [];

    /// <summary>已 destroy 的被动物件 index。**只允许在 <see cref="Gate"/> 内访问**；会话走 <see cref="TryMarkPassiveObjectDestroyed"/>。</summary>
    internal HashSet<byte> DestroyedPassiveObjects { get; } = [];

    public DungeonDoorAssembly Doors { get; } = new();

    public byte DungeonOption { get; private set; }

    /// <summary>房间清空 / 空 boss 房可结算（= 改造前的 dungeonClearEnabled，实例级共享）。</summary>
    public bool ClearEnabled { get; private set; }

    // —— 成员 ——

    public bool AddMember(DungeonInstanceMember member)
    {
        lock (Gate)
        {
            if (_members.Any(existing => existing.CharacterId == member.CharacterId))
            {
                return false;
            }

            _members.Add(member);
            Version++;
            return true;
        }
    }

    public bool RemoveMember(Guid characterId)
    {
        lock (Gate)
        {
            if (_members.RemoveAll(member => member.CharacterId == characterId) == 0)
            {
                return false;
            }

            Version++;
            return true;
        }
    }

    public bool ContainsMember(Guid characterId)
    {
        lock (Gate)
        {
            return _members.Any(member => member.CharacterId == characterId);
        }
    }

    /// <summary>锁内读：调用方拿到的是拷贝，可以出锁遍历发包。</summary>
    public DungeonInstanceMember[] Members
    {
        get
        {
            lock (Gate)
            {
                return [.. _members];
            }
        }
    }

    public int MemberCount
    {
        get
        {
            lock (Gate)
            {
                return _members.Count;
            }
        }
    }

    public DungeonInstanceMember? FindMember(Guid characterId)
    {
        lock (Gate)
        {
            return _members.FirstOrDefault(member => member.CharacterId == characterId);
        }
    }

    // —— 运行态 ——

    /// <summary>入场：建布局、进起始房间、清空共享集合。<paramref name="dungeonOption"/> 原样记录（地狱模式用）。</summary>
    public bool TryStartRun(
        DungeonLayout layout,
        byte difficulty,
        byte dungeonOption,
        out DungeonRoomTransition transition,
        IDropRandomSource? random = null)
    {
        lock (Gate)
        {
            if (!_run.TryStart(layout, difficulty, dungeonOption, out transition, random))
            {
                return false;
            }

            DungeonOption = dungeonOption;
            Layout = layout;
            ClearEnabled = false;
            _detachedRoom = null;
            _registeredPassiveRooms.Clear();
            ResetRoomCollections(transition.Room.Monsters);
            Doors.Reset();
            Version++;
            return true;
        }
    }

    /// <summary>教学副本：布局不是 PVF 迷宫，直接给定房间（`Run` 保持空）。</summary>
    public void EnterDetachedRoom(DungeonRoomDefinition room)
    {
        lock (Gate)
        {
            _run.Stop();
            _detachedRoom = room;
            Layout = null;
            DungeonOption = 0;
            ClearEnabled = false;
            _registeredPassiveRooms.Clear();
            ResetRoomCollections(room.Monsters);
            Doors.Reset();
            Version++;
        }
    }

    public bool TryMoveRoom(
        byte roomX,
        byte roomY,
        out DungeonRoomTransition transition,
        IDropRandomSource? random = null)
    {
        lock (Gate)
        {
            if (!_run.TryMoveRoom(roomX, roomY, out transition, random))
            {
                return false;
            }

            // 与改造前 CMD 48 的行为一致：换房间清掉"本房间"的临时集合与开放标记。
            ClearEnabled = false;
            Doors.Reset();
            ResetRoomCollections(transition.Room.Monsters);
            Version++;
            return true;
        }
    }

    /// <summary>
    /// 怪物死亡：**唯一判定 = 从存活集合里摘掉它**（与改造前 `aliveDungeonMonsters.Remove` 同口径），
    /// 并发上报只会有一次返回 true。返回 false 表示这条死亡是陈旧的（不发 NOTI 38）。
    /// </summary>
    public bool TryTakeMonsterDeath(ushort uniqueId, out bool isBossCascadeDeath)
    {
        lock (Gate)
        {
            isBossCascadeDeath = BossCascadeMonsterDeaths.Remove(uniqueId);
            if (!AliveMonsters.Remove(uniqueId))
            {
                return false;
            }

            _run.MarkMonsterDefeated(uniqueId);
            Version++;
            return true;
        }
    }

    /// <summary>本房间还活着的怪数量（锁内；会话的日志/判定用）。</summary>
    public int AliveMonsterCount
    {
        get
        {
            lock (Gate)
            {
                return AliveMonsters.Count;
            }
        }
    }

    /// <summary>
    /// 存活怪的锁内拷贝：喂 `DungeonRoomClearRules.Evaluate` 与"级联杀"的差集。
    /// 裸 `HashSet` 会给两个会话互相改坏的机会，所以只给拷贝。
    /// </summary>
    public HashSet<ushort> SnapshotAliveMonsters()
    {
        lock (Gate)
        {
            return [.. AliveMonsters];
        }
    }

    /// <summary>
    /// BOSS_DIE_CHECK 上报（改造前 `EntranceService.cs:12333-12341` 的三句）：上报的 boss 覆盖
    /// <paramref name="roomBossIds"/> 全部时，把存活集合里不属于 boss 的怪并进级联死亡集合。
    /// 返回 false = 这个 bossId 之前已上报（幂等：不重复级联、不重复加版本）。
    /// </summary>
    public bool TryReportBossDeath(
        ushort bossId,
        IReadOnlyCollection<ushort> roomBossIds,
        out bool allBossesReported)
    {
        lock (Gate)
        {
            var firstReport = ReportedBossDeaths.Add(bossId);
            allBossesReported = roomBossIds.All(ReportedBossDeaths.Contains);
            if (firstReport && allBossesReported)
            {
                BossCascadeMonsterDeaths.UnionWith(
                    AliveMonsters.Where(monsterId => !roomBossIds.Contains(monsterId)));
            }

            if (firstReport)
            {
                Version++;
            }

            return firstReport;
        }
    }

    /// <summary>已上报的 boss 数（锁内；日志用）。</summary>
    public int ReportedBossDeathCount
    {
        get
        {
            lock (Gate)
            {
                return ReportedBossDeaths.Count;
            }
        }
    }

    public bool TryMarkPassiveObjectDestroyed(ushort objectCode, out byte objectIndex)
    {
        lock (Gate)
        {
            if (!_run.TryMarkPassiveObjectDestroyed(objectCode, out objectIndex))
            {
                return false;
            }

            DestroyedPassiveObjects.Add(objectIndex);
            Version++;
            return true;
        }
    }

    public bool IsHellFiend(ushort uniqueId)
    {
        lock (Gate)
        {
            return _run.IsHellFiend(uniqueId);
        }
    }

    public bool HasClearedMap(ushort mapId)
    {
        lock (Gate)
        {
            return _run.HasClearedMap(mapId);
        }
    }

    public bool IsCurrentHellRoomCleared()
    {
        lock (Gate)
        {
            return _run.IsCurrentHellRoomCleared;
        }
    }

    /// <summary>房间清空 → 开启结算。返回 true 表示"这次真的从关到开"（该给全员发一次 NOTI 31）。</summary>
    public bool TryEnableClear()
    {
        lock (Gate)
        {
            if (ClearEnabled)
            {
                return false;
            }

            ClearEnabled = true;
            Version++;
            return true;
        }
    }

    /// <summary>换房间 / 重开时要清掉的开放标记（不发包，由调用方决定要不要广播）。</summary>
    public void ResetClear()
    {
        lock (Gate)
        {
            if (!ClearEnabled)
            {
                return;
            }

            ClearEnabled = false;
            Version++;
        }
    }

    /// <summary>回城 / 收尾：停 run、清空共享集合与门口集结。</summary>
    public void StopRun()
    {
        lock (Gate)
        {
            _run.Stop();
            _detachedRoom = null;
            Layout = null;
            DungeonOption = 0;
            ClearEnabled = false;
            _registeredPassiveRooms.Clear();
            ResetRoomCollections([]);
            Doors.Reset();
            Version++;
        }
    }

    public DungeonInstanceSnapshot Snapshot()
    {
        lock (Gate)
        {
            return new DungeonInstanceSnapshot(
                Version,
                _run.DungeonId,
                _run.Difficulty,
                DungeonOption,
                Layout,
                _run.IsActive ? _run.RoomX : (byte)0,
                _run.IsActive ? _run.RoomY : (byte)0,
                CurrentRoomLocked(),
                _run.RoomStateFlag,
                _run.CurrentHellPartyMode,
                ClearEnabled,
                _members.Count);
        }
    }

    /// <summary>锁内取"进入某房间后"的发包快照；首次访问时把被动掉落登记进实例的掉落表。</summary>
    public DungeonInstanceRoomSnapshot CaptureRoomSnapshot(
        byte roomX,
        byte roomY,
        in DungeonRoomTransition transition,
        ushort ownerUserId)
    {
        lock (Gate)
        {
            GamePassiveObjectDrop[] drops;
            if (transition.ReuseClientRoomState)
            {
                // 客户端复用上次的 START_MAP 快照，`CreateCachedStartMap` 不吃掉落表。
                drops = [];
            }
            else if (transition.FirstVisit)
            {
                drops = RegisterRoomPassiveDropsLocked(transition.Room, ownerUserId, roomX, roomY);
                Version++;
            }
            else
            {
                drops = GetRoomPassiveDropsLocked(roomX, roomY);
            }

            var (hellX, hellY) = HellRoomPositionLocked();
            return new DungeonInstanceRoomSnapshot(
                roomX,
                roomY,
                transition.Room.MapId,
                transition.Room.ClientRandomSeed,
                _run.RoomStateFlag,
                _run.CurrentHellPartyMode,
                hellX,
                hellY,
                transition.Room,
                transition.Room.Monsters,
                drops,
                transition.FirstVisit,
                transition.ReuseClientRoomState);
        }
    }

    /// <summary>
    /// 加入现有实例（队长/队员进一个"已经在跑的"实例）：取**当前房间**的发包快照，不推进房间。
    /// 当前房间的被动掉落若还没登记过就现在登记（与首次进房同一条路径）。
    /// </summary>
    public DungeonInstanceRoomSnapshot CaptureCurrentRoomSnapshot(ushort ownerUserId)
    {
        lock (Gate)
        {
            var room = CurrentRoomLocked()
                ?? throw new InvalidOperationException("Dungeon instance has no current room.");
            var roomX = _run.IsActive ? _run.RoomX : (byte)0;
            var roomY = _run.IsActive ? _run.RoomY : (byte)0;
            var registered = GetRoomPassiveDropsLocked(roomX, roomY);
            // 判据是"这个房间登记过没有"。用 registered.Length > 0 会在掉落被捡光/detach 后
            // 误判成"没登记过"，把房间定义里的 PassiveItemSpawns 再登记一遍 → 新 GroundId +
            // 客户端多出幽灵被动物件 → 复制道具（后进队的会话正好走这条路径）。
            var drops = _registeredPassiveRooms.Contains(new DungeonRoomPosition(roomX, roomY))
                ? registered
                : RegisterRoomPassiveDropsLocked(room, ownerUserId, roomX, roomY);
            var (hellX, hellY) = HellRoomPositionLocked();
            return new DungeonInstanceRoomSnapshot(
                roomX,
                roomY,
                room.MapId,
                room.ClientRandomSeed,
                _run.RoomStateFlag,
                _run.CurrentHellPartyMode,
                hellX,
                hellY,
                room,
                room.Monsters,
                drops,
                FirstVisit: true,
                ReuseClientRoomState: false);
        }
    }

    /// <summary>地狱房坐标（NOTI 28 的 flagA/flagB）；非地狱模式为 0。</summary>
    private (byte X, byte Y) HellRoomPositionLocked() =>
        _run.HellMode && catalog.TryGetHellRoom(_run.DungeonId, out _, out var hellX, out var hellY)
            ? (hellX, hellY)
            : ((byte)0, (byte)0);

    public GamePassiveObjectDrop[] RegisterRoomPassiveDrops(
        DungeonRoomDefinition room,
        ushort ownerUserId,
        byte roomX,
        byte roomY)
    {
        lock (Gate)
        {
            var drops = RegisterRoomPassiveDropsLocked(room, ownerUserId, roomX, roomY);
            Version++;
            return drops;
        }
    }

    public GamePassiveObjectDrop[] GetRoomPassiveDrops(byte roomX, byte roomY)
    {
        lock (Gate)
        {
            return GetRoomPassiveDropsLocked(roomX, roomY);
        }
    }

    // —— 掉落 ——

    public DungeonGroundItem AddGroundItem(
        DungeonGeneratedDrop drop,
        ushort ownerUserId,
        ushort sourceMonsterUniqueId,
        byte roomX,
        byte roomY,
        short x,
        short y)
    {
        lock (Gate)
        {
            var item = GroundItems.Add(
                drop, ownerUserId, sourceMonsterUniqueId, roomX, roomY, x: x, y: y, catalog: itemCatalog);
            Version++;
            return item;
        }
    }

    /// <summary>玩家主动丢弃（CMD DropItem；改造前 `groundDungeonItems.AddPlayerDropped`，EntranceService.cs:11194）。</summary>
    public DungeonGroundItem AddPlayerDroppedGroundItem(
        CharacterItemRecord droppedItem,
        ushort ownerUserId,
        byte roomX,
        byte roomY,
        short x,
        short y)
    {
        lock (Gate)
        {
            var item = GroundItems.AddPlayerDropped(droppedItem, ownerUserId, roomX, roomY, x, y);
            Version++;
            return item;
        }
    }

    public bool TryGetGroundItem(ushort groundId, byte roomX, byte roomY, out DungeonGroundItem item)
    {
        lock (Gate)
        {
            return GroundItems.TryGet(groundId, roomX, roomY, out item);
        }
    }

    /// <summary>本房间的地面掉落（锁内拷贝；被动掉落 + 玩家丢弃，调用方出锁遍历）。</summary>
    public DungeonGroundItem[] GetRoomGroundItems(byte roomX, byte roomY)
    {
        lock (Gate)
        {
            return [.. GroundItems.GetRoomItems(roomX, roomY)];
        }
    }

    public bool RemoveGroundItem(ushort groundId)
    {
        lock (Gate)
        {
            if (!GroundItems.Remove(groundId))
            {
                return false;
            }

            Version++;
            return true;
        }
    }

    /// <summary>回城 / 重开：清空地面掉落（改造前 `groundDungeonItems.ResetDungeon()`，EntranceService.cs:2630/3483/11773/11976）。</summary>
    public void ResetGroundItems()
    {
        lock (Gate)
        {
            GroundItems.ResetDungeon();
            Version++;
        }
    }

    /// <summary>被动物件被打掉 → 激活它关联的掉落（本房间内）。</summary>
    public int ActivatePassiveObject(byte passiveObjectIndex, byte roomX, byte roomY)
    {
        lock (Gate)
        {
            var activated = GroundItems.ActivatePassiveObject(passiveObjectIndex, roomX, roomY);
            if (activated > 0)
            {
                Version++;
            }

            return activated;
        }
    }

    public int DetachAllPassiveObjectItems(byte roomX, byte roomY)
    {
        lock (Gate)
        {
            var detached = GroundItems.DetachAllPassiveObjectItems(roomX, roomY);
            if (detached > 0)
            {
                Version++;
            }

            return detached;
        }
    }

    private DungeonRoomDefinition? CurrentRoomLocked() =>
        _run.IsActive ? _run.CurrentRoom : _detachedRoom;

    private void ResetRoomCollections(IReadOnlyList<GameDungeonMonster> monsters)
    {
        AliveMonsters.Clear();
        AliveMonsters.UnionWith(monsters.Select(monster => monster.UniqueId));
        ReportedBossDeaths.Clear();
        BossCascadeMonsterDeaths.Clear();
        DestroyedPassiveObjects.Clear();
    }

    private GamePassiveObjectDrop[] RegisterRoomPassiveDropsLocked(
        DungeonRoomDefinition room,
        ushort ownerUserId,
        byte roomX,
        byte roomY)
    {
        // 登记表在添加之前落笔：只要进过这条路径，这个房间的被动掉落就"登记过了"。
        _registeredPassiveRooms.Add(new DungeonRoomPosition(roomX, roomY));
        var result = new List<GamePassiveObjectDrop>(room.PassiveItemSpawns.Length);
        foreach (var spawn in room.PassiveItemSpawns)
        {
            var groundItem = GroundItems.AddPassive(
                spawn.Drop,
                ownerUserId,
                spawn.PassiveObjectIndex,
                spawn.AssociationSlot,
                roomX,
                roomY,
                x: unchecked((short)spawn.X),
                y: unchecked((short)spawn.Y),
                catalog: itemCatalog);
            result.Add(new GamePassiveObjectDrop(
                spawn.AssociationSlot,
                groundItem.GroundId,
                groundItem.ItemId,
                groundItem.GetClientAddInfo(itemCatalog),
                groundItem.OwnerUserId));
        }

        return [.. result];
    }

    private GamePassiveObjectDrop[] GetRoomPassiveDropsLocked(byte roomX, byte roomY) =>
        GroundItems.GetPassiveObjectItems(roomX, roomY)
            .Select(item => new GamePassiveObjectDrop(
                item.PassiveItemSlot,
                item.GroundId,
                item.ItemId,
                item.GetClientAddInfo(itemCatalog),
                item.OwnerUserId))
            .ToArray();
}
