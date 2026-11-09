using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 副本实例成员：会话入场时登记自己的发送句柄。Send 走会话的 SerializedWriteStream
/// （`EntranceService.SendTownPacketAsync`，跨线程写入已串行化），所以别的会话可以直接投包。
/// SendConditional 必须在同一写门内检查谓词；主动掉落不能退回普通 Send 绕过这一门控。
/// </summary>
internal sealed record DungeonInstanceMember(
    Guid CharacterId,
    ushort AreaUserId,
    byte PartySlot,
    string CharacterName,
    Func<GameServerPacket, ValueTask> Send,
    string AccountName = "",
    Guid MentorGoldIncarnation = default,
    Func<GameServerPacket, Func<bool>, ValueTask>? SendConditional = null);

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

/// <summary>
/// 队长开局的结果（<see cref="DungeonInstance.TryStartRunForEntry"/>，终审 P5）：
/// <see cref="AlreadyActive"/> = 这支队的 run 已经被另一个会话开好，调用方必须改走 live-join。
/// </summary>
public enum DungeonRunEntryDecision
{
    Started,
    AlreadyActive,
    StartFailed
}

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
internal sealed partial class DungeonInstance(
    ushort partyId,
    Guid soloCharacterId,
    DungeonCatalog catalog,
    ItemCatalog itemCatalog,
    TimeSpan? partyCardAgentTimeout = null)
{
    private readonly List<DungeonInstanceMember> _members = [];
    private readonly DungeonRunState _run = new(catalog);
    private long _debugRunId;

    internal DungeonDebugOverlaySnapshot? CaptureDebugOverlay()
    {
        lock (Gate)
        {
            if (!_run.IsActive || Layout is null) return null;
            var room = _run.PrepareBossRoomForDebug(out var defeated, out var destroyed);
            if (room is null) return null;
            var atBoss = _run.RoomX == Layout.BossX && _run.RoomY == Layout.BossY;
            var hidden = room.Monsters.Where(monster => monster.IsBoxMonster != 0).Select(monster =>
            {
                var container = room.PassiveObjects.FirstOrDefault(value => value.ObjectIndex == monster.BoxIndex);
                byte state = defeated.Contains(monster.UniqueId) ? (byte)3
                    : destroyed.Contains(monster.BoxIndex) ? (byte)2
                    : atBoss ? (byte)1 : (byte)0;
                return new DungeonDebugHiddenMonster(monster.UniqueId, monster.MonsterIndex, monster.Level,
                    monster.BoxIndex, state, container?.X ?? 0, container?.Y ?? 0);
            }).ToArray();
            return new(_debugRunId, _members.Select(member => member.AreaUserId).ToArray(),
                _run.DungeonId, _run.CurrentRoom?.MapId ?? 0, room.MapId,
                _run.RoomX, _run.RoomY, Layout.BossX, Layout.BossY, ClearEnabled, atBoss, hidden,
                _dropDebugAll, CaptureDropTestStatesLocked());
        }
    }
    private readonly PartyCardSelectionState _partyCards = new();
    private readonly Dictionary<(PartyCardType Type, byte Column, byte Slot), PartyCardContent> _partyCardContent = [];
    private long _partyContentGeneration;
    private readonly HashSet<(PartyCardType Type, byte Column, byte Slot)> _partyPublishedOwners = [];
    private readonly Dictionary<Guid, byte> _partySettlementSlots = [];
    private readonly Dictionary<Guid, IPartyCardRewardAgent> _partyCardAgents = [];
    private readonly HashSet<(long Generation, PartyCardType Type, byte Column, Guid CharacterId)> _partyCardAttempts = [];
    private readonly List<PartyCardAuditEntry> _partyCardAudit = [];
    // "这一局结算的翻牌代次已经开着"：会话侧 EnsurePartyCardSettlement 的幂等闸门。
    // 决不能让同一局里第二个到达的会话重开代次（会清掉已发布的归属）。
    private bool _partyCardSettlementOpen;
    // 代理在一次归属预留里的等待上限：超过即视为 Unknown（不发布、不重试、留审计）。
    // 它只用来**别把发包会话钉死在一个卡住的代理上**；正常本地提交是毫秒级。
    private readonly TimeSpan _partyCardAgentTimeout = partyCardAgentTimeout ?? TimeSpan.FromSeconds(5);

    /// <summary>取走并清空审计（会话在出锁后写日志；测试用它断言"不重试、不发布、留痕"）。</summary>
    internal IReadOnlyList<PartyCardAuditEntry> DrainPartyCardAudit()
    {
        lock (Gate)
        {
            if (_partyCardAudit.Count == 0)
            {
                return [];
            }

            var drained = _partyCardAudit.ToArray();
            _partyCardAudit.Clear();
            return drained;
        }
    }

    /// <summary>
    /// 审计条目必须写**产生它的那一刻**的结算标识：迟到结果（超时之后才回来）可能已经跨了一次
    /// 结算重开，此时读"当前值"会把新结算的 id 配上旧代次（评审 N2）。
    /// </summary>
    private void NotePartyCardAudit(Guid settlementId, long generation, PartyCardType type, byte column, Guid characterId, string reason)
        => _partyCardAudit.Add(new PartyCardAuditEntry(settlementId, generation, type, column, characterId, reason));

    internal void RegisterPartyCardRewardAgent(Guid characterId, IPartyCardRewardAgent agent)
    {
        lock (Gate)
        {
            if (IsParty && _members.Any(x => x.CharacterId == characterId)) _partyCardAgents[characterId] = agent;
        }
    }

    internal async Task SettleOwnPartyCardAsync(Guid characterId, PartyCardType type, byte column, long generation, CancellationToken ct)
    {
        IPartyCardRewardAgent? agent;
        byte slot;
        Guid settlementId;
        lock (Gate)
        {
            if (generation != _partyCards.Generation || !_members.Any(x => x.CharacterId == characterId)) return;
            var owners = _partyCards.Snapshot();
            slot = _partySettlementSlots.GetValueOrDefault(characterId, byte.MaxValue);
            var owner = type == PartyCardType.Free ? owners.FreeOwners.ElementAtOrDefault(column) : owners.PaidOwners.ElementAtOrDefault(column);
            // 归属校验在**调用代理之前**：只有这一列的归属成员自己的会话能推进它（R1′）。
            if (column >= 4 || owner != slot) return;
            // 结算标识与代次**在同一把锁里**取：会话拿它算幂等键，审计条目也用它，
            // 绝不会把"局 A 的代次"配上"局 B 的标识"。
            settlementId = _partyCards.SettlementId;
            // 一次预留只尝试一次：失败（异常/取消/超时/Unknown）绝不重试（R4）。
            if (!_partyCardAttempts.Add((generation, type, column, characterId))) return;
            if (!_partyCardAgents.TryGetValue(characterId, out agent))
            {
                NotePartyCardAudit(settlementId, generation, type, column, characterId, "agent-missing");
                return;
            }
        }

        PartyCardContent content;
        try
        {
            content = agent.RollOwnColumn(settlementId, type, column, generation);
        }
        catch (Exception)
        {
            lock (Gate) NotePartyCardAudit(settlementId, generation, type, column, characterId, "agent-exception");
            return;
        }

        if (content is null
            || content.Type != type
            || content.Column != column
            || content.PartySlot != slot)
        {
            lock (Gate) NotePartyCardAudit(settlementId, generation, type, column, characterId, "agent-content-mismatch");
            return;
        }

        PartyCardCommitOutcome outcome;
        try
        {
            // 同步抛出与"返回故障 Task"都算代理异常：两种都要挡住（本地代理是会话实现的，未定义行为不可信）。
            var commit = agent.CommitOwnColumnAsync(settlementId, type, column, generation, content, ct);
            if (commit is null)
            {
                lock (Gate) NotePartyCardAudit(settlementId, generation, type, column, characterId, "agent-exception");
                return;
            }

            // 有界等待：代理卡住不能把发包会话一起钉死；超时按 Unknown 处理（不发布、不重试、留审计）。
            var finished = await Task.WhenAny(commit, Task.Delay(_partyCardAgentTimeout));
            if (finished != commit)
            {
                lock (Gate) NotePartyCardAudit(settlementId, generation, type, column, characterId, "agent-timeout");
                // 超时**不等于没落库**：会话那一侧仍在等这次提交，它可能已经把奖励写进存档并采用结果。
                // 放弃等待但**不放弃结果**：迟到完成时照常上报（代次/归属会在上报里再校验一次），
                // 并留一条带真实结果的审计 —— 否则审计写着"超时未提交"、存档里却已经发了奖励，
                // 运维照审计去补发就会发出第二份。
                ObserveLateAgentCommit(commit, settlementId, characterId, type, column, generation, content);
                return;
            }

            outcome = await commit;
        }
        catch (OperationCanceledException)
        {
            lock (Gate) NotePartyCardAudit(settlementId, generation, type, column, characterId, "agent-cancelled");
            return;
        }
        catch (Exception)
        {
            lock (Gate) NotePartyCardAudit(settlementId, generation, type, column, characterId, "agent-exception");
            return;
        }

        ReportPartyCardCommitFromAgent(settlementId, characterId, type, column, generation, content, outcome);
    }

    /// <summary>
    /// 超时之后接管在飞的那次代理提交：完成后按**真实结果**上报（代次/归属仍会被校验，
    /// 已失效的结算只会留下 <c>report-rejected</c>），并留一条 <c>agent-timeout-late-*</c> 审计。
    /// 这条路径不是"重试"：它只观察那一次已经发生的提交。
    /// </summary>
    private void ObserveLateAgentCommit(
        Task<PartyCardCommitOutcome> commit,
        Guid settlementId,
        Guid characterId,
        PartyCardType type,
        byte column,
        long generation,
        PartyCardContent content)
    {
        _ = ObserveAsync();

        async Task ObserveAsync()
        {
            PartyCardCommitOutcome lateOutcome;
            try
            {
                lateOutcome = await commit;
            }
            catch (OperationCanceledException)
            {
                lock (Gate) NotePartyCardAudit(settlementId, generation, type, column, characterId, "agent-timeout-late-cancelled");
                return;
            }
            catch (Exception exception)
            {
                lock (Gate)
                {
                    NotePartyCardAudit(
                        settlementId,
                        generation,
                        type,
                        column,
                        characterId,
                        $"agent-timeout-late-failure:{exception.GetType().Name}");
                }
                return;
            }

            lock (Gate)
            {
                NotePartyCardAudit(
                    settlementId,
                    generation,
                    type,
                    column,
                    characterId,
                    $"agent-timeout-late-{lateOutcome.Status.ToString().ToLowerInvariant()}");
            }
            ReportPartyCardCommitFromAgent(settlementId, characterId, type, column, generation, content, lateOutcome);
        }
    }

    /// <summary>强制开新代次（模型/测试用；会话侧一律走幂等的 <see cref="EnsurePartyCardSettlement"/>）。</summary>
    internal long BeginPartyCardSettlement()
    {
        lock (Gate)
        {
            if (!IsParty) return 0;
            return OpenPartyCardSettlementLocked();
        }
    }

    private bool ReportPartyCardCommitFromAgent(Guid settlementId, Guid characterId, PartyCardType type, byte column, long generation, PartyCardContent content, PartyCardCommitOutcome outcome)
    {
        lock (Gate)
        {
            if (generation != _partyContentGeneration || generation != _partyCards.Generation || !_members.Any(m => m.CharacterId == characterId))
            {
                NotePartyCardAudit(settlementId, generation, type, column, characterId, "report-rejected");
                return false;
            }

            var owners = _partyCards.Snapshot();
            if (!_partySettlementSlots.TryGetValue(characterId, out var memberSlot))
            {
                NotePartyCardAudit(settlementId, generation, type, column, characterId, "report-rejected");
                return false;
            }

            var owner = type switch
            {
                PartyCardType.Free when column < 4 => owners.FreeOwners[column],
                PartyCardType.Paid when column < 4 => owners.PaidOwners[column],
                _ => PartyCardSelectionState.Unassigned
            };
            if (owner == PartyCardSelectionState.Unassigned || owner != memberSlot)
            {
                NotePartyCardAudit(settlementId, generation, type, column, characterId, "report-rejected");
                return false;
            }

            var key = (type, column, owner);
            var commitStatus = outcome.Status switch
            {
                DungeonRewardLedgerStatus.Committed => PartyCardCommitStatus.Committed,
                DungeonRewardLedgerStatus.Rejected => PartyCardCommitStatus.Rejected,
                _ => PartyCardCommitStatus.Unknown
            };
            if (_partyCardContent.ContainsKey(key)
                || content.Type != type
                || content.Column != column
                || content.PartySlot != owner)
            {
                NotePartyCardAudit(settlementId, generation, type, column, characterId, "report-rejected");
                return false;
            }

            if (commitStatus == PartyCardCommitStatus.Rejected)
            {
                NotePartyCardAudit(settlementId, generation, type, column, characterId, "commit-rejected");
            }
            else if (commitStatus == PartyCardCommitStatus.Unknown)
            {
                NotePartyCardAudit(settlementId, generation, type, column, characterId, "commit-unknown");
            }

            _partyCardContent[key] = content with { Status = commitStatus };
            return true;
        }
    }

    internal bool TryTakePartyCardPublication(long generation, out PartyCardPublishedSnapshot? snapshot)
    {
        lock (Gate)
        {
            snapshot = null;
            if (generation != _partyContentGeneration || generation != _partyCards.Generation) return false;
            var owners = _partyCards.Snapshot();
            var free = (byte[])owners.FreeOwners.Clone();
            var paid = (byte[])owners.PaidOwners.Clone();
            var newlyPublished = new List<(PartyCardType Type, byte Column, byte Slot)>();
            for (byte column = 0; column < 4; column++)
            {
                foreach (var type in new[] { PartyCardType.Free, PartyCardType.Paid })
                {
                    var slot = type == PartyCardType.Free ? free[column] : paid[column];
                    if (slot == PartyCardSelectionState.Unassigned || _partyPublishedOwners.Contains((type, column, slot))) continue;
                    var key = (type, column, slot);
                    if (!_partyCardContent.TryGetValue(key, out var content) || content.Status is PartyCardCommitStatus.Pending or PartyCardCommitStatus.Unknown)
                    {
                        if (type == PartyCardType.Free) free[column] = PartyCardSelectionState.Unassigned; else paid[column] = PartyCardSelectionState.Unassigned;
                        continue;
                    }
                    newlyPublished.Add(key);
                }
            }
            if (newlyPublished.Count == 0) return false;
            foreach (var key in newlyPublished) _partyPublishedOwners.Add(key);
            snapshot = new PartyCardPublishedSnapshot(generation, new PartyCardSelectionSnapshot(generation, owners.AutoCompleted, free, paid, owners.MemberSlots), _partyCardContent.Values.ToArray());
            return true;
        }
    }

    /// <summary>
    /// 会话侧入口：确保"这一局结算"的翻牌代次已经打开，返回该代次（单人 → 0 = 模型不参与）。
    /// **幂等**：同一局里第二个到达的会话拿到同一代次，绝不重开（重开会清空归属与内容，
    /// 而客户端的卡位 owner 是首写不覆盖 —— 已发布的列被清掉就永久错位）。
    /// 生命周期失效（开局/换房/停局）后再调用会开新代次。
    /// </summary>
    internal long EnsurePartyCardSettlement()
    {
        lock (Gate)
        {
            if (!IsParty) return 0;
            if (_partyCardSettlementOpen) return _partyCards.Generation;
            return OpenPartyCardSettlementLocked();
        }
    }

    /// <summary>
    /// 开新代次（调用方必须已持 <see cref="Gate"/>）：清空归属/内容/已发布集合/尝试集合与槽位映射。
    /// 与 <see cref="BeginPartyCardSettlement"/> 同一份语义，只是多了"这一局已经开着"的标志。
    /// </summary>
    private long OpenPartyCardSettlementLocked()
    {
        _partyContentGeneration = _partyCards.Begin(_members);
        _partyCardContent.Clear();
        _partyClearRewards.Clear();
        _partySettlementSlots.Clear();
        foreach (var member in _members) _partySettlementSlots[member.CharacterId] = member.PartySlot;
        _partyPublishedOwners.Clear();
        _partyCardAttempts.Clear();
        _partyCardSettlementOpen = true;
        return _partyContentGeneration;
    }

    /// <summary>
    /// 开局 / 换房 / 停局的失效点：把"这一局已经开着"的标志一并清掉，
    /// 否则下一局的 <see cref="EnsurePartyCardSettlement"/> 会拿到一个已经作废的代次。
    /// </summary>
    private void InvalidatePartyCardSettlementLocked()
    {
        _partyCards.Invalidate();
        _partyCardSettlementOpen = false;
    }

    /// <summary>
    /// 本局结算的全局唯一 id（幂等键的一部分）：代次号只在实例内有意义，跨局会重复。
    /// </summary>
    internal Guid PartyCardSettlementId
    {
        get
        {
            lock (Gate)
            {
                return _partyCards.SettlementId;
            }
        }
    }

    /// <summary>本会话（这个角色）在指定卡型上的归属列；没有归属 → false（会话据此审计，绝不自己挑列）。</summary>
    internal bool TryGetOwnPartyCardColumn(Guid characterId, PartyCardType type, out byte column)
    {
        lock (Gate)
        {
            column = PartyCardSelectionState.Unassigned;
            if (type is not (PartyCardType.Free or PartyCardType.Paid)
                || !_partySettlementSlots.TryGetValue(characterId, out var slot)
                || !_members.Any(member => member.CharacterId == characterId))
            {
                return false;
            }

            var owners = _partyCards.Snapshot();
            var table = type == PartyCardType.Free ? owners.FreeOwners : owners.PaidOwners;
            for (byte index = 0; index < 4; index++)
            {
                if (table[index] == slot)
                {
                    column = index;
                    return true;
                }
            }

            return false;
        }
    }

    internal PartyCardSelectionOutcome TryReservePartyCard(Guid characterId, PartyCardType type, byte column, long generation)
    {
        lock (Gate) return _partyCards.TryReserve(characterId, type, column, generation);
    }

    internal PartyCardSelectionOutcome CompletePartyCardSelection(long generation)
    {
        lock (Gate) return _partyCards.CompleteAuto(generation);
    }

    internal PartyCardSelectionSnapshot SnapshotPartyCards()
    {
        lock (Gate) return _partyCards.Snapshot().Copy();
    }

    // 本房间"已加载"的成员（官方 `CParty::loaded[]`，party+892+i 的等价物）：
    // 过图 / 入场时清零，成员发 CMD 40（FINISH_LOADING）时置位；**全员齐了才向全队广播 NOTI 30**
    // （官方 `CParty::check_allmember_loading` 的语义，2026-09-30 静态实证）。
    // 只在 `lock (Gate)` 内读写。
    private readonly HashSet<Guid> _loadedMembers = [];

    // 官服 CParty::die_user 先广播生死，再检查全灭；send_dungeon_fail_msg 只在全灭后
    // 开 10 秒计时（0x085A7A10 / 0x085B35FC）。不能给每个死者各开单人回城计时。
    private readonly HashSet<Guid> _deadMembers = [];
    private DateTimeOffset? _partyWipeDeadline;
    private bool _partyWipeReturnIssued;
    private bool _partyWipeReturnStarted;
    private readonly HashSet<Guid> _partyRevivalReservations = [];
    private DateTimeOffset? _partyRevivalInterruptedWipeDeadline;
    internal static readonly TimeSpan PartyWipeReturnDelay = TimeSpan.FromSeconds(10);

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

    private GameDungeonMonster[] _roomMonsters = [];
    private long _roomGeneration;
    private bool _bossDeathCascadeStarted;
    private bool _bossDeathCascadeAnnounced;
    private int _monsterDeathSettlementsPending;

    internal long RoomGeneration { get { lock (Gate) { return _roomGeneration; } } }

    internal ushort? BossDeathCascadeBossId
    {
        get { lock (Gate) { return _roomMonsters.FirstOrDefault(monster => monster.Type == GameDungeonMonsterTypes.Boss)?.UniqueId; } }
    }

    internal HashSet<ushort> SnapshotRoomBossIds()
    {
        lock (Gate)
        {
            return _roomMonsters.Where(monster => monster.Type == GameDungeonMonsterTypes.Boss)
                .Select(monster => monster.UniqueId).ToHashSet();
        }
    }

    internal bool TryBeginMonsterDeath(
        ushort uniqueId, long? expectedRoomGeneration,
        out GameDungeonMonster defeatedMonster, out sbyte championRewardOrdinal)
    {
        lock (Gate)
        {
            defeatedMonster = _roomMonsters.FirstOrDefault(monster => monster.UniqueId == uniqueId)!;
            championRewardOrdinal = GameProtocolEngine.NoChampionRewardOrdinal;
            if (defeatedMonster is null || !TryTakeMonsterDeath(uniqueId, out _, expectedRoomGeneration))
            {
                return false;
            }
            championRewardOrdinal = RegisterMonsterKill(defeatedMonster.Type);
            _monsterDeathSettlementsPending++;
            return true;
        }
    }

    internal bool CompleteMonsterDeath(long roomGeneration)
    {
        lock (Gate)
        {
            if (roomGeneration != _roomGeneration)
            {
                return false;
            }
            if (_monsterDeathSettlementsPending > 0)
            {
                _monsterDeathSettlementsPending--;
            }
            return true;
        }
    }

    internal bool TryAnnounceBossDeathCascade()
    {
        lock (Gate)
        {
            if (_bossDeathCascadeAnnounced)
            {
                return false;
            }
            _bossDeathCascadeAnnounced = true;
            return true;
        }
    }

    internal bool TryStartBossDeathCascade(out bool firstStart, long? expectedRoomGeneration = null)
    {
        lock (Gate)
        {
            firstStart = false;
            if (expectedRoomGeneration is { } expected && expected != _roomGeneration
                || CurrentRoomLocked()?.MapType != DungeonMapType.Boss)
            {
                return false;
            }

            var bosses = _roomMonsters.Where(monster => monster.Type == GameDungeonMonsterTypes.Boss).ToArray();
            if (bosses.Length == 0 || bosses.Any(monster => AliveMonsters.Contains(monster.UniqueId))
                && !bosses.All(monster => ReportedBossDeaths.Contains(monster.UniqueId)))
            {
                return false;
            }

            firstStart = !_bossDeathCascadeStarted;
            _bossDeathCascadeStarted = true;
            return true;
        }
    }

    internal ushort[] SnapshotBossDeathCascadeTargets()
    {
        lock (Gate)
        {
            return !_bossDeathCascadeStarted ? [] : _roomMonsters
                .Where(monster => monster.Type != GameDungeonMonsterTypes.Boss
                    && AliveMonsters.Contains(monster.UniqueId)
                    && (monster.IsBoxMonster == 0 || DestroyedPassiveObjects.Contains(monster.BoxIndex)))
                .Select(monster => monster.UniqueId).Order().ToArray();
        }
    }

    internal DungeonRoomClearDecision EvaluateRoomClear(
        GameDungeonMonster defeatedMonster, long? expectedRoomGeneration = null)
    {
        lock (Gate)
        {
            if (_monsterDeathSettlementsPending != 0
                || expectedRoomGeneration is { } expected && expected != _roomGeneration)
            {
                return DungeonRoomClearDecision.None;
            }
            var room = CurrentRoomLocked();
            return room is null ? DungeonRoomClearDecision.None : DungeonRoomClearRules.Evaluate(
                room with { Monsters = _roomMonsters }, defeatedMonster, AliveMonsters, DestroyedPassiveObjects);
        }
    }

    public byte DungeonOption { get; private set; }

    /// <summary>房间清空 / 空 boss 房可结算（= 改造前的 dungeonClearEnabled，实例级共享）。</summary>
    public bool ClearEnabled { get; private set; }

    /// <summary>
    /// 最近一次进房是不是**首次访问**（`transition.FirstVisit` 的口径）。
    /// 单人只对首访扣房间疲劳（重访走客户端缓存快照）；组队的换房由**别的会话**推进时，本会话
    /// 只能在镜像追赶里补扣，需要这个标记才知道该不该扣（设计稿 §7 决策 6"每人各自扣"）。
    /// 锁内写（TryStartRun/TryMoveRoom/EnterDetachedRoom/StopRun）、锁内只读。
    /// </summary>
    public bool LastRoomEntryWasFirstVisit { get; private set; }

    // 击杀计数**实例级共享**（真机 2026-09-29，server-party3.log：改造前是会话局部，
    // 同队两人各按自己杀的那几只算 → 两个客户端的通关评分/奖励完全不同：
    // user 2 记 normalKills=17、user 1 记 normalKills=4+bossKills=1）。评分/奖励的输入必须全队一致。
    public int NormalMonsterKills { get; private set; }

    public int ChampionMonsterKills { get; private set; }

    public int BossMonsterKills { get; private set; }

    /// <summary>Champion/SuperChampion 奖励动画的进度（每局至多 11 次，全队共享同一序号）。</summary>
    public int ChampionRewardOrdinal { get; private set; }

    /// <summary>
    /// 记一只怪的死（锁内）：累计计数并给出该次击杀的奖励动画序号。
    /// 并发上报只会进来一次（唯一判定是 <see cref="TryTakeMonsterDeath"/> 的返回值）。
    /// </summary>
    public sbyte RegisterMonsterKill(byte type)
    {
        lock (Gate)
        {
            Version++;
            switch (type)
            {
                case GameDungeonMonsterTypes.Champion:
                case GameDungeonMonsterTypes.SuperChampion:
                    ChampionMonsterKills = Math.Min(ushort.MaxValue, ChampionMonsterKills + 1);
                    if (ChampionRewardOrdinal < GameProtocolEngine.MaximumChampionRewardOrdinal)
                    {
                        ChampionRewardOrdinal++;
                        return checked((sbyte)ChampionRewardOrdinal);
                    }

                    return GameProtocolEngine.NoChampionRewardOrdinal;
                case GameDungeonMonsterTypes.Boss:
                    BossMonsterKills = Math.Min(ushort.MaxValue, BossMonsterKills + 1);
                    return GameProtocolEngine.NoChampionRewardOrdinal;
                default:
                    NormalMonsterKills = Math.Min(ushort.MaxValue, NormalMonsterKills + 1);
                    return GameProtocolEngine.NoChampionRewardOrdinal;
            }
        }
    }

    // —— 成员 ——

    public bool AddMember(DungeonInstanceMember member)
    {
        lock (Gate)
        {
            if (_partyWipeReturnStarted
                || _members.Any(existing => existing.CharacterId == member.CharacterId))
            {
                return false;
            }

            _members.Add(member);
            // 新成员默认"未加载"：要等他自己发 CMD 40（FINISH_LOADING）才算这一房间加载完成。
            _loadedMembers.Remove(member.CharacterId);
            _deadMembers.Remove(member.CharacterId);
            _goldIneligibleMembers.Remove(member.CharacterId);
            _partyRevivalInterruptedWipeDeadline = null;
            UpdatePartyWipeDeadlineLocked(DateTimeOffset.UtcNow);
            Version++;
            return true;
        }
    }

    /// <summary>入场的幂等登记：全灭回城封口后不许先摘掉原成员再尝试重挂。</summary>
    internal bool TryRegisterMemberForEntry(DungeonInstanceMember member)
    {
        lock (Gate)
        {
            if (_partyWipeReturnStarted)
            {
                return false;
            }

            RemoveMember(member.CharacterId);
            return AddMember(member);
        }
    }

    /// <summary>加入运行中的房间：取快照和登记同锁，拒绝时不改变成员或场景。</summary>
    internal bool TryJoinRunForEntry(
        DungeonInstanceMember member,
        out DungeonInstanceRoomSnapshot room)
    {
        lock (Gate)
        {
            room = null!;
            return CanAcceptJoin
                && TryCaptureCurrentRoomSnapshot(member.AreaUserId, out room)
                && TryRegisterMemberForEntry(member);
        }
    }

    public bool RemoveMember(Guid characterId, DateTimeOffset? now = null)
    {
        lock (Gate)
        {
            if (_members.RemoveAll(member => member.CharacterId == characterId) == 0)
            {
                return false;
            }

            _loadedMembers.Remove(characterId);
            _deadMembers.Remove(characterId);
            _goldIneligibleMembers.Remove(characterId);
            _cancelledLoot.Remove(characterId);
            foreach (var pair in _pendingLoot.Where(pair => pair.Value.Winner == characterId).ToArray())
            {
                QueueLootCancellationLocked(pair.Value.Picker);
                _pendingLoot.Remove(pair.Key);
            }
            _partyRevivalReservations.Remove(characterId);
            // 最后一个活人离开后，留下的死者也要能结束这一局；已在计时/回城的全灭
            // 不因某个成员先解绑而重开期限，否则全队不能按同一个到点时刻回城。
            RestorePartyWipeAfterReservationLocked(now ?? DateTimeOffset.UtcNow);
            _partyCards.RemoveMember(characterId);
            _partyCardAgents.Remove(characterId);
            _partyClearRewards.Remove(characterId);
            _settlementReturnDeadlines.Remove(characterId);
            _announcedSettlementCountdowns.Remove(characterId);
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

    /// <summary>只受理本实例的组队成员；单人继续由会话使用原来的死亡计时。</summary>
    internal bool TrySetPartyMemberAlive(Guid characterId, bool isAlive, DateTimeOffset now)
    {
        lock (Gate)
        {
            if (!IsParty || !_members.Any(member => member.CharacterId == characterId)
                || isAlive && _partyWipeReturnStarted)
            {
                return false;
            }

            if (isAlive)
            {
                _goldIneligibleMembers.Remove(characterId);
                // 成功落库后的提交。任何一次真正复活都让之前的全灭期限作废；
                // 其它并发预留若失败，不能把已经被成功复活打断的旧期限恢复回来。
                _partyRevivalReservations.Remove(characterId);
                _partyRevivalInterruptedWipeDeadline = null;
            }

            var changed = isAlive ? _deadMembers.Remove(characterId) : _deadMembers.Add(characterId);
            UpdatePartyWipeDeadlineLocked(now);
            if (changed)
            {
                Version++;
            }

            return true;
        }
    }

    internal DateTimeOffset? PartyWipeDeadline
    {
        get
        {
            lock (Gate)
            {
                return _partyWipeDeadline;
            }
        }
    }

    /// <summary>全灭到期重新检查存活状态，一次取出全队；重复扫描不重复投递。</summary>
    internal Guid[] TakeExpiredPartyWipeReturns(DateTimeOffset now)
    {
        lock (Gate)
        {
            if (_partyWipeReturnIssued || _partyWipeDeadline is not { } deadline
                || now < deadline || !AllPartyMembersDeadLocked())
            {
                return [];
            }

            _partyWipeReturnIssued = true;
            return _members.Select(member => member.CharacterId).ToArray();
        }
    }

    /// <summary>
    /// 只读资格快照；实际回城必须使用 TryBeginPartyWipeReturn 原子认领。
    /// 扫描投递后有人用币复活，尚未取走的旧邮件不能把他或队友送回城。
    /// 首个成员回城解绑后，其余成员仍在这轮已到期的全灭中，不重新计时。
    /// </summary>
    internal bool ShouldReturnForPartyWipe(Guid characterId, DateTimeOffset now)
    {
        lock (Gate)
        {
            return _partyWipeReturnIssued
                && _partyWipeDeadline is { } deadline && now >= deadline
                && _members.Any(member => member.CharacterId == characterId)
                && AllPartyMembersDeadLocked();
        }
    }

    /// <summary>
    /// 首个会话在开始异步回城之前认领整队失败。与花币前的复活预留同锁仲裁，
    /// 避免一个会话已回城、另一个会话在落库等待中复活而留本。
    /// </summary>
    internal bool TryBeginPartyWipeReturn(Guid characterId, DateTimeOffset now)
    {
        lock (Gate)
        {
            if (!ShouldReturnForPartyWipe(characterId, now))
            {
                return false;
            }

            _partyWipeReturnStarted = true;
            return true;
        }
    }

    /// <summary>USE_COIN 验证通过后、花币落库之前预留；失败不应消费复活币。</summary>
    internal bool TryBeginPartyRevival(Guid characterId, DateTimeOffset now)
    {
        lock (Gate)
        {
            if (!IsParty || _partyWipeReturnStarted
                || !_members.Any(member => member.CharacterId == characterId)
                || !_deadMembers.Contains(characterId)
                || !_partyRevivalReservations.Add(characterId))
            {
                return false;
            }

            _partyRevivalInterruptedWipeDeadline ??= _partyWipeDeadline;
            _deadMembers.Remove(characterId);
            UpdatePartyWipeDeadlineLocked(now);
            Version++;
            return true;
        }
    }

    /// <summary>复活落库失败/异常时撤销预留，恢复原死亡期限；不要再多等十秒。</summary>
    internal void CancelPartyRevival(Guid characterId, DateTimeOffset now)
    {
        lock (Gate)
        {
            if (!_partyRevivalReservations.Remove(characterId))
            {
                return;
            }

            if (_members.Any(member => member.CharacterId == characterId))
            {
                _deadMembers.Add(characterId);
            }

            RestorePartyWipeAfterReservationLocked(now);
            Version++;
        }
    }

    private void RestorePartyWipeAfterReservationLocked(DateTimeOffset now)
    {
        UpdatePartyWipeDeadlineLocked(now);
        if (AllPartyMembersDeadLocked() && _partyRevivalInterruptedWipeDeadline is { } interrupted)
        {
            _partyWipeDeadline = interrupted;
            // 已投的旧邮件可能在复活落库等待期间被会话丢弃，因此恢复后允许再投递。
            _partyWipeReturnIssued = false;
        }

        if (_partyRevivalReservations.Count == 0)
        {
            _partyRevivalInterruptedWipeDeadline = null;
        }
    }

    private bool AllPartyMembersDeadLocked() =>
        IsParty && _members.Count > 0 && _members.All(member => _deadMembers.Contains(member.CharacterId));

    private void UpdatePartyWipeDeadlineLocked(DateTimeOffset now)
    {
        if (AllPartyMembersDeadLocked())
        {
            _partyWipeDeadline ??= now.Add(PartyWipeReturnDelay);
        }
        else
        {
            _partyWipeDeadline = null;
            _partyWipeReturnIssued = false;
            if (_members.Count == 0)
            {
                _partyWipeReturnStarted = false;
            }
        }
    }

    private void ResetPartyMemberLivesLocked()
    {
        _deadMembers.Clear();
        _goldIneligibleMembers.Clear();
        _partyWipeDeadline = null;
        _partyWipeReturnIssued = false;
        _partyWipeReturnStarted = false;
        _partyRevivalReservations.Clear();
        _partyRevivalInterruptedWipeDeadline = null;
    }

    // —— 全员加载门槛（官方 `CParty::resetFinishLoadingVar` / `CParty::check_allmember_loading`）——

    /// <summary>
    /// 换房间时清空"已加载"标志（官方 `CParty::resetFinishLoadingVar`：`for (i=0..3) loaded[i]=0`）。
    /// **必须在 <see cref="TryMoveRoom"/> 同一把锁内调用**；单人实例没有集结，由调用方按 IsParty 早退。
    /// </summary>
    internal void ResetMemberLoadingForRoomChange()
    {
        lock (Gate)
        {
            _loadedMembers.Clear();
            // 换房即新房间：上一间房排队等着"强制回城"的到点时间一并作废。
            _settlementReturnDeadlines.Clear();
            _announcedSettlementCountdowns.Clear();
        }
    }

    /// <summary>
    /// 标记某成员"本房间加载完成"（CMD 40）。返回 true = **此刻全员已加载**
    /// （官方 `check_allmember_loading`：服务端据此广播 NOTI 30）。
    /// 注意：**重复上报也必须照常返回 true**——本服务端有一条"区域条目后再重载一次房间"的补包路径
    /// （为了让客户端地图加载器给外来角色建外观载体），客户端重载地图后会**再报一次 CMD 40**；
    /// 若把重复上报按官方 `if (loaded[i]) return;` 的幂等早退处理，就再也不会放行 NOTI 30，
    /// 两个客户端会一起停在黑屏（真机 2026-09-30 08:55:30 实测）。
    /// 门槛本身不变：只有**全员**都标记过才返回 true。
    /// </summary>
    internal bool TryMarkMemberLoaded(Guid characterId)
    {
        lock (Gate)
        {
            if (!_members.Any(member => member.CharacterId == characterId))
            {
                return false;
            }

            _loadedMembers.Add(characterId);
            return _members.Count > 0
                && _members.All(member => _loadedMembers.Contains(member.CharacterId));
        }
    }

    /// <summary>锁内查询：当前是否全员已加载（成员离开后重新评估用）。</summary>
    internal bool AllMembersLoaded
    {
        get
        {
            lock (Gate)
            {
                return _members.Count > 0
                    && _members.All(member => _loadedMembers.Contains(member.CharacterId));
            }
        }
    }

    // —— 结算期"EPLP 超时"（官服：结算包发出后由服务端定时把队伍送回城）——
    // 参考实现日志逐字：`WARN town return: eplp timer 20 party_index=0`。真机 2026-09-30：
    // 队员在结算页卡死、一个包都不发（客户端"未响应"），只认客户端 EPLP 就永远回不去。
    // 只在 `lock (Gate)` 内读写。
    private readonly Dictionary<Guid, DateTimeOffset> _settlementReturnDeadlines = [];

    /// <summary>结算包（NOTI 34/35）发出时登记：到点仍没收到该成员的 EPLP 就强制送他回城。</summary>
    internal void ArmSettlementReturn(Guid characterId, DateTimeOffset deadline)
    {
        lock (Gate)
        {
            if (_members.Any(member => member.CharacterId == characterId))
            {
                _settlementReturnDeadlines[characterId] = deadline;
                // 重新登记 = 新的一次结算期：把"上一个播报过的整秒"清掉，倒计时从头再播。
                _announcedSettlementCountdowns.Remove(characterId);
            }
        }
    }

    /// <summary>
    /// 结算期最后 N 秒的逐秒播报（用户 2026-09-30 报的原版行为：自动返回前 10 秒在聊天框发系统消息）。
    /// 记录"这个成员上一个播报过的整秒"，保证 10..1 每个数只播一次；deadline 被取消/移除时同步清掉。
    /// 只在 <see cref="Gate"/> 内读写。
    /// </summary>
    private readonly Dictionary<Guid, int> _announcedSettlementCountdowns = [];

    /// <summary>
    /// 本秒要播报的结算倒计时（10..1 各一次）：返回 (成员, 剩余整秒)，调用方负责投系统消息。
    /// 到点的（remaining &lt;= 0）**不在这里**——那条走 <see cref="TakeExpiredSettlementReturns"/> 的强制回城。
    /// </summary>
    internal (Guid CharacterId, int RemainingSeconds)[] TakeSettlementCountdowns(
        DateTimeOffset now,
        int windowSeconds)
    {
        lock (Gate)
        {
            if (_settlementReturnDeadlines.Count == 0)
            {
                return [];
            }

            List<(Guid, int)>? announcements = null;
            foreach (var pair in _settlementReturnDeadlines)
            {
                // 向上取整 = "还差几秒"，因此同一整秒只会命中一次（扫描抖动不会重复播报）；
                // 到点（remaining <= 0）不在这里：那条走 TakeExpiredSettlementReturns 的强制回城。
                var remaining = (int)Math.Ceiling((pair.Value - now).TotalSeconds);
                if (remaining is <= 0 || remaining > windowSeconds)
                {
                    continue;
                }

                if (_announcedSettlementCountdowns.TryGetValue(pair.Key, out var announced) && announced == remaining)
                {
                    continue;
                }

                _announcedSettlementCountdowns[pair.Key] = remaining;
                (announcements ??= []).Add((pair.Key, remaining));
            }

            return announcements is null ? [] : [.. announcements];
        }
    }

    /// <summary>取走所有已到点的成员（非空即代表这些会话该被送回城）；调用方负责投信箱。</summary>
    internal Guid[] TakeExpiredSettlementReturns(DateTimeOffset now)
    {
        lock (Gate)
        {
            if (_settlementReturnDeadlines.Count == 0)
            {
                return [];
            }

            List<Guid>? expired = null;
            foreach (var pair in _settlementReturnDeadlines)
            {
                if (pair.Value <= now)
                {
                    (expired ??= []).Add(pair.Key);
                }
            }

            if (expired is null)
            {
                return [];
            }

            foreach (var characterId in expired)
            {
                _settlementReturnDeadlines.Remove(characterId);
            }

            return [.. expired];
        }
    }

    /// <summary>
    /// 可以"加入现有实例"（设计稿 §3.1）：**已有成员且 run 还活着**。
    /// 入场中止路径（`TryStartRun` 失败 / 地狱票被拒 / 落库失败）会留下"有成员、run 已停"的实例
    /// （Task 2 评审遗留项）；那种实例没有当前房间，按成员数命中就会在取快照处炸掉。
    /// 门槛收在这里，会话代码与冒烟测试用同一个判据。
    /// </summary>
    public bool CanAcceptJoin
    {
        get
        {
            lock (Gate)
            {
                return _members.Count > 0 && _run.IsActive && !_partyWipeReturnStarted;
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
            return StartRunLocked(layout, difficulty, dungeonOption, out transition, random);
        }
    }

    /// <summary>
    /// 队长开局的仲裁点（终审 P5）：`TryGetForParty(...).CanAcceptJoin` 与 `TryStartRun` 之间
    /// 有窗口——两个成员会话可能都通过门槛，第二个的 `TryStartRun` 会先 `Stop()` 掉先到者的 run
    /// （重掷迷宫 → 两人房间几何不一致、先到者的 NOTI 29 已经发出去）。这里把"判定 + 开局"
    /// 收进**同一次锁内**：run 已经活了 → <see cref="DungeonRunEntryDecision.AlreadyActive"/>，
    /// 调用方必须改走"加入现有实例"（live-join），绝不重掷对方的迷宫。
    /// </summary>
    public DungeonRunEntryDecision TryStartRunForEntry(
        DungeonLayout layout,
        byte difficulty,
        byte dungeonOption,
        out DungeonRoomTransition transition)
    {
        lock (Gate)
        {
            if (_partyWipeReturnStarted)
            {
                transition = null!;
                return DungeonRunEntryDecision.StartFailed;
            }

            if (_run.IsActive)
            {
                transition = null!;
                return DungeonRunEntryDecision.AlreadyActive;
            }

            return StartRunLocked(layout, difficulty, dungeonOption, out transition, random: null)
                ? DungeonRunEntryDecision.Started
                : DungeonRunEntryDecision.StartFailed;
        }
    }

    private bool StartRunLocked(
        DungeonLayout layout,
        byte difficulty,
        byte dungeonOption,
        out DungeonRoomTransition transition,
        IDropRandomSource? random)
    {
        if (!_run.TryStart(layout, difficulty, dungeonOption, out transition, random))
        {
            return false;
        }

        InvalidatePartyCardSettlementLocked();
        ResetPartyMemberLivesLocked();
        DungeonOption = dungeonOption;
        Layout = layout;
        _debugRunId = DateTime.UtcNow.Ticks;
        _dropDebugAll = false;
        _debugDropReplies.Clear();
        _debugDropApplied.Clear();
        ClearEnabled = false;
        LastRoomEntryWasFirstVisit = transition.FirstVisit;
        _detachedRoom = null;
        _registeredPassiveRooms.Clear();
        ResetRoomCollections(transition.Room.Monsters);
        ResetKillCounters();
        Version++;
        return true;
    }

    /// <summary>教学副本：布局不是 PVF 迷宫，直接给定房间（`Run` 保持空）。</summary>
    public void EnterDetachedRoom(DungeonRoomDefinition room)
    {
        lock (Gate)
        {
            _run.Stop();
            _dropDebugAll = false;
            _debugDropReplies.Clear();
            _debugDropApplied.Clear();
            InvalidatePartyCardSettlementLocked();
            ResetPartyMemberLivesLocked();
            _detachedRoom = room;
            Layout = null;
            DungeonOption = 0;
            ClearEnabled = false;
            // 教学副本的合成房间也算"首次进房"（没有迷宫/重访概念）。
            LastRoomEntryWasFirstVisit = true;
            _registeredPassiveRooms.Clear();
            ResetRoomCollections(room.Monsters);
            ResetKillCounters();
            Version++;
        }
    }

    /// <summary>
    /// 换房间的**唯一入口**，官服 `CParty::move_map` @0x085a8f4e(987) 的语义（伪码
    /// `official/ida_targeted_out.txt:362-454`）：**一次合法请求推进整队**——官方那个函数里
    /// **根本没有"全员到门口"这种条件**，推进与否只看 `CBattle_Field::MoveMap(field, msg[13], msg[14], &out)`
    /// 对**请求者自己**目标坐标的判定；成功后由会话侧广播 `NOTI 29` 给全队并清零全员 loaded。
    ///
    /// 失败返回错误码（沿用改造前 CMD 48 的约定）：目标就是当前房间 / 目标非法 → **4**。
    ///
    /// 官服另有两道状态门：
    /// ① 活动副本状态由会话的 currentDungeonLayout 和 TryMoveRoom 的 run 校验保证；
    /// ② 最终 boss 已死亡/上报，或结算已开放 → 错误 19。死亡掉落和级联仍在等待时也要保留原房间；
    ///    普通房清怪、地狱遭遇以及尚有 boss 存活的房间仍可换房。当前目录未实现 IsEnterEachMap 特殊模式。
    /// </summary>
    internal bool TryAdvanceRoom(
        byte roomX,
        byte roomY,
        out DungeonRoomTransition? transition,
        out byte errorCode)
    {
        lock (Gate)
        {
            if (IsFinalRoomTransitionBlockedLocked())
            {
                transition = null;
                errorCode = 19;
                return false;
            }

            if (roomX == _run.RoomX && roomY == _run.RoomY)
            {
                transition = null;
                errorCode = 4;
                return false;
            }

            if (!TryMoveRoom(roomX, roomY, out var moved))
            {
                transition = null;
                errorCode = 4;
                return false;
            }

            transition = moved;
            errorCode = 0;
            return true;
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
            // Reject before touching the run, death generation, drops or card settlement.
            // Every direct/party/delayed caller must retain the completed boss room.
            if (IsFinalRoomTransitionBlockedLocked())
            {
                transition = null!;
                return false;
            }

            var previousRoomX = _run.RoomX;
            var previousRoomY = _run.RoomY;
            if (!_run.TryMoveRoom(roomX, roomY, out transition, random))
            {
                return false;
            }

            InvalidatePartyCardSettlementLocked();
            // 与改造前 CMD 48 的行为一致：换房间清掉"本房间"的临时集合与开放标记。
            _goldIneligibleMembers.UnionWith(_deadMembers);
            _goldIneligibleMembers.UnionWith(_partyRevivalReservations);
            ClearEnabled = false;
            LastRoomEntryWasFirstVisit = transition.FirstVisit;
            ResetRoomCollections(transition.Room.Monsters);
            // 地狱房的被动掉落 detach（终审 I3，从单人的 CMD 48 分支集中到这里）：
            // ①退出**未清**的地狱房（`CompletedHellRoomOnExit`，客户端场景要重建为空的地图房）
            //    → 离开的那间房的被动掉落永远不可达；
            // ②进入**已清**的地狱房（客户端重建空场景）→ 目标房间那次遭遇留下的被动掉落不该存在。
            // 单人的调用点原来在会话里（报文之前/之后各一处），搬进来后三条换房路径
            // （单人立即推进 / 组队自己推进 / 倒计时强制推进）共用同一份，且与房间状态同锁。
            if (transition.CompletedHellRoomOnExit)
            {
                GroundItems.DetachAllPassiveObjectItems(previousRoomX, previousRoomY);
            }

            if (_run.IsCurrentHellRoomCleared)
            {
                GroundItems.DetachAllPassiveObjectItems(roomX, roomY);
            }

            Version++;
            return true;
        }
    }

    private bool IsFinalRoomTransitionBlockedLocked()
    {
        if (ClearEnabled || _bossDeathCascadeStarted) return true;
        if (CurrentRoomLocked()?.MapType != DungeonMapType.Boss) return false;

        // BOSS_DIE_CHECK precedes native all-die, and TryBeginMonsterDeath removes
        // a boss before awaited reward work can start the cascade. Either report
        // is enough for that boss, but every boss must be finished before blocking.
        var bosses = _roomMonsters.Where(monster => monster.Type == GameDungeonMonsterTypes.Boss).ToArray();
        return bosses.Length > 0 && bosses.All(monster =>
            !AliveMonsters.Contains(monster.UniqueId) || ReportedBossDeaths.Contains(monster.UniqueId));
    }

    /// <summary>
    /// 怪物死亡：**唯一判定 = 从存活集合里摘掉它**（与改造前 `aliveDungeonMonsters.Remove` 同口径），
    /// 并发上报只会有一次返回 true。返回 false 表示这条死亡是陈旧的（不发 NOTI 38）。
    /// </summary>
    public bool TryTakeMonsterDeath(
        ushort uniqueId, out bool isBossCascadeDeath, long? expectedRoomGeneration = null)
    {
        lock (Gate)
        {
            if (expectedRoomGeneration is { } expected && expected != _roomGeneration)
            {
                isBossCascadeDeath = false;
                return false;
            }
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

    /// <summary>
    /// 直接移除存活记录的状态工具，不执行掉落、经验或死亡通知。
    /// BOSS 清场必须走 TryBeginMonsterDeath / CompleteMonsterDeath 的正常结算流程，
    /// 不能用本方法提前移除怪物，否则正常死亡上报会失去掉落资格。
    /// </summary>
    public void ForceAllMonstersDead(IReadOnlyCollection<ushort>? knownAliveIds = null)
    {
        lock (Gate)
        {
            if (knownAliveIds is not null)
            {
                foreach (var uniqueId in knownAliveIds)
                {
                    if (AliveMonsters.Remove(uniqueId))
                    {
                        _run.MarkMonsterDefeated(uniqueId);
                    }
                }
            }
            else
            {
                foreach (var uniqueId in AliveMonsters)
                {
                    _run.MarkMonsterDefeated(uniqueId);
                }

                AliveMonsters.Clear();
            }

            Version++;
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

    /// <summary>锁内读地狱模式标记（终审 P6：会话的票务/NOTI 28 判定用，别裸读 `_run`）。</summary>
    public bool HellMode
    {
        get
        {
            lock (Gate)
            {
                return _run.HellMode;
            }
        }
    }

    /// <summary>
    /// 锁内读"run 的当前房间"（终审 P6；run 未活时为 null——与教学副本的 detached room 无关，
    /// 那是 <see cref="Snapshot"/> 的口径）。会话不能裸读 <c>_run.CurrentRoom</c>。
    /// </summary>
    public DungeonRoomDefinition? RunCurrentRoom
    {
        get
        {
            lock (Gate)
            {
                return _run.IsActive ? _run.CurrentRoom : null;
            }
        }
    }

    /// <summary>房间清空 → 开启结算。返回 true 表示"这次真的从关到开"（该给全员发一次 NOTI 31）。</summary>
    public bool TryEnableClear(long? expectedRoomGeneration = null)
    {
        lock (Gate)
        {
            if (ClearEnabled || _monsterDeathSettlementsPending != 0
                || expectedRoomGeneration is { } expected && expected != _roomGeneration)
            {
                return false;
            }

            ClearEnabled = true;
            _run.MarkCurrentRoomCleared();
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
            InvalidatePartyCardSettlementLocked();
            ResetPartyMemberLivesLocked();
            _detachedRoom = null;
            Layout = null;
            DungeonOption = 0;
            ClearEnabled = false;
            // 没有当前房间了：标记回到"未进房"，别让收尾后的会话按陈旧标记补扣房间疲劳。
            LastRoomEntryWasFirstVisit = false;
            _registeredPassiveRooms.Clear();
            ResetRoomCollections([]);
            ResetKillCounters();
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
    /// 实例没有当前房间时抛 <see cref="InvalidOperationException"/>；会话路径要用不抛的
    /// <see cref="TryCaptureCurrentRoomSnapshot"/>。
    /// </summary>
    public DungeonInstanceRoomSnapshot CaptureCurrentRoomSnapshot(ushort ownerUserId) =>
        TryCaptureCurrentRoomSnapshot(ownerUserId, out var room)
            ? room
            : throw new InvalidOperationException("Dungeon instance has no current room.");

    /// <summary>
    /// 不抛的"取当前房间快照"（加入现有实例的门槛）：实例的 run 已停 / 从未开局时返回 false。
    /// 会话据此中止入场（不入场、不绑定），而不是让异常逃到外层 catch 把会话打断。
    /// </summary>
    public bool TryCaptureCurrentRoomSnapshot(
        ushort ownerUserId,
        out DungeonInstanceRoomSnapshot room)
    {
        lock (Gate)
        {
            var current = CurrentRoomLocked();
            if (current is null)
            {
                room = null!;
                return false;
            }

            var roomX = _run.IsActive ? _run.RoomX : (byte)0;
            var roomY = _run.IsActive ? _run.RoomY : (byte)0;
            var registered = GetRoomPassiveDropsLocked(roomX, roomY);
            // 判据是"这个房间登记过没有"。用 registered.Length > 0 会在掉落被捡光/detach 后
            // 误判成"没登记过"，把房间定义里的 PassiveItemSpawns 再登记一遍 → 新 GroundId +
            // 客户端多出幽灵被动物件 → 复制道具（后进队的会话正好走这条路径）。
            var drops = _registeredPassiveRooms.Contains(new DungeonRoomPosition(roomX, roomY))
                ? registered
                : RegisterRoomPassiveDropsLocked(current, ownerUserId, roomX, roomY);
            var (hellX, hellY) = HellRoomPositionLocked();
            room = new DungeonInstanceRoomSnapshot(
                roomX,
                roomY,
                current.MapId,
                current.ClientRandomSeed,
                _run.RoomStateFlag,
                _run.CurrentHellPartyMode,
                hellX,
                hellY,
                current,
                current.Monsters,
                drops,
                FirstVisit: true,
                ReuseClientRoomState: false);
            return true;
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
        short y,
        CharacterItemRecord? preservedItem = null,
        byte routingSource = 1)
    {
        lock (Gate)
        {
            var item = GroundItems.Add(
                drop,
                ownerUserId,
                sourceMonsterUniqueId,
                roomX,
                roomY,
                preservedItem,
                x: x,
                y: y,
                catalog: itemCatalog,
                routingSource: routingSource);
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
        short y,
        bool isGold = false,
        bool activated = true)
    {
        lock (Gate)
        {
            var item = GroundItems.AddPlayerDropped(droppedItem, ownerUserId, roomX, roomY, x, y, isGold, activated);
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
            return [.. GroundItems.GetRoomItems(roomX, roomY).Where(item => item.Activated)];
        }
    }

    public bool RemoveGroundItem(ushort groundId, DungeonGroundItem? expectedItem = null)
    {
        lock (Gate)
        {
            if (!GroundItems.Remove(groundId, expectedItem))
            {
                return false;
            }

            _pendingLoot.Remove(groundId);
            Version++;
            return true;
        }
    }

    internal DungeonGroundItem? ActivatePlayerDroppedGroundItem(DungeonGroundItem expected, out long roomGeneration)
    {
        lock (Gate)
        {
            roomGeneration = _roomGeneration;
            var active = GroundItems.Activate(expected);
            if (active is not null) Version++;
            return active;
        }
    }

    internal bool IsGroundItemInCurrentRoom(DungeonGroundItem expected, long? roomGeneration = null)
    {
        lock (Gate)
        {
            return (roomGeneration is null || roomGeneration == _roomGeneration)
                && CurrentRoomLocked() is not null
                && (_run.IsActive
                    ? _run.RoomX == expected.RoomX && _run.RoomY == expected.RoomY
                    : expected.RoomX == 0 && expected.RoomY == 0)
                && GroundItems.TryGet(expected.GroundId, expected.RoomX, expected.RoomY, out var current)
                && ReferenceEquals(current, expected);
        }
    }

    /// <summary>获奖信箱绑定原实例与原地面对象；旧局复用 GroundId 时不许领取新物品。</summary>
    internal bool TryGetGroundItemForLootAttempt(
        DungeonLootPickupAttempt attempt,
        byte roomX,
        byte roomY,
        out DungeonGroundItem item)
    {
        lock (Gate)
        {
            if (ReferenceEquals(attempt.Instance, this)
                && _members.Any(member => member.CharacterId == attempt.WinnerCharacterId)
                && _pendingLoot.TryGetValue(attempt.GroundId, out var pending)
                && pending.PickupId == attempt.PickupId && pending.Winner == attempt.WinnerCharacterId
                && GroundItems.TryGet(attempt.GroundId, roomX, roomY, out item)
                && ReferenceEquals(attempt.GroundItem, item))
                return true;
            item = null!;
            return false;
        }
    }

    /// <summary>
    /// 拾取仲裁（设计稿 §7 决策 5：全队共享、**先到先得**）：锁内"取快照 + 摘除"一次完成，
    /// 返回值就是"我赢下了这个掉落实体"（并发/重复上报只有一个 true，输的一方拿到的 item 为 null）。
    /// **准入不看 <see cref="DungeonGroundItem.OwnerUserId"/>**——归属只做客户端显示
    /// （无主掉落 OwnerUserId=0 同样能捡）；准入只要求"这个房间里的这个实体还在"。
    /// 调用方赢下之后才可落库/发包；落库失败必须走 <see cref="TryReAddGroundItem"/> 放回。
    /// </summary>
    public bool TryClaimGroundItem(
        ushort groundId,
        byte roomX,
        byte roomY,
        out DungeonGroundItem item,
        DungeonGroundItem? expectedItem = null,
        Guid pickupId = default)
    {
        lock (Gate)
        {
            if ((_run.IsActive && (_run.RoomX != roomX || _run.RoomY != roomY))
                || !GroundItems.TryGet(groundId, roomX, roomY, out item)
                || (expectedItem is not null && !ReferenceEquals(expectedItem, item))
                || (pickupId != Guid.Empty && (!_pendingLoot.TryGetValue(groundId, out var pending) || pending.PickupId != pickupId))
                || !GroundItems.Remove(groundId))
            {
                item = null!;
                return false;
            }

            _pendingLoot.Remove(groundId);
            Version++;
            return true;
        }
    }

    /// <summary>
    /// 拾取落库失败的"原样放回"：把 <see cref="TryClaimGroundItem"/> 摘掉的那条记录按原 GroundId 放回
    /// （改造前口径"落库失败 → 掉落保留在原地"）。放回后别人仍可捡（先到先得）。
    /// 返回 false = 放不回去（id 已被占用，不许静默丢件，调用方要记错误）。
    /// </summary>
    public bool TryReAddGroundItem(DungeonGroundItem item)
    {
        lock (Gate)
        {
            if (!GroundItems.TryReAdd(item))
            {
                return false;
            }

            Version++;
            return true;
        }
    }

    // —— 调试用"满掉落"（真机测试，S5） ——

    /// <summary>
    /// 本实例是否处于"调试满掉落"（由 debug 面板或 `掉落测试` 聊天命令开关，见
    /// <see cref="EntranceService"/> 的 `CMD 17 SEND_MESSAGE` 分支）。
    /// **只为真机测试存在**：开启后本实例内每次怪死都按"金币必掉 + 4 类物品**每类只掉最稀有的
    /// 那一件** + 世界掉落必掉"满额掉（封顶 `DungeonDropGenerator.MaxDebugDrops`，候选池里没有的不掉）。
    /// **作用域 = 这个实例的本局副本**（换房保留；回城/新一局失效），且**只有 `test` 账号**能触发。
    /// 测试做完就删（台账里按"临时测试能力/已知缺口"记着）。
    /// </summary>
    private bool _dropDebugAll;

    /// <summary>调试满掉落开关的当前状态（诊断/测试用）。</summary>
    internal bool DropDebugAll
    {
        get
        {
            lock (Gate)
            {
                return _dropDebugAll;
            }
        }
    }

    /// <summary>翻转调试满掉落开关并返回翻转后的状态（同一把锁内，避免并发下"开了又关"）。</summary>
    internal bool ToggleDropDebugAll()
    {
        lock (Gate)
        {
            _dropDebugAll = !_dropDebugAll;
            return _dropDebugAll;
        }
    }

    // —— routing 归属裁决（S5/T3b） ——

    /// <summary>
    /// routing 的**轮转下标**（官服 `CParty::get_item` 里的 `party+324`）：
    /// 下一轮谁拿那 +40 的顺位。只在 <see cref="Gate"/> 内读写。
    /// </summary>
    private int _routingRoundRobinIndex;
    private bool _routingRotationInitialized;
    private readonly Dictionary<ushort, (DungeonGroundItem Item, Guid Winner, Guid PickupId, Guid Picker)> _pendingLoot = [];
    private readonly Dictionary<Guid, Queue<GameServerPacket>> _cancelledLoot = [];

    /// <summary>routing 轮转下标（诊断/测试用；对应官服 `party+324`）。</summary>
    internal int RoutingRoundRobinIndex
    {
        get
        {
            lock (Gate)
            {
                return _routingRoundRobinIndex;
            }
        }
    }

    /// <summary>
    /// 走一次 routing 归属裁决（官服 `get_item` 的掷骰分支）。纯规则在
    /// <see cref="LootRouting.Decide"/>，这里只负责"**读旧顺位 → 跑规则 → 写新顺位**在同一把锁里"，
    /// 否则两个成员同时点拾取可能拿到同一个 +40 顺位（官服是单线程对象引用，没这个问题）。
    ///
    /// `routingActive` 恒传 false：官服的 `CParty::SetStartRouting` 由 `StartRouting` 触发、并会先发
    /// **`NOTI 281`**——那条通知在本客户端**超出跳转表**（>0x97）根本不会被处理，`CMD 301` 真机 0 次
    /// ⇒ 我们**没有** routing 生命周期（PD-3008/3009 的裁决）。`LootRouting.Decide` 里那条
    /// `RoutingActive → 0xD8` 分支因此由单测覆盖，生产不可达。
    /// </summary>
    public LootRoutingDecision DecidePickupRouting(
        Guid pickerCharacterId,
        bool routable,
        IReadOnlyList<LootRoutingCandidate> candidates,
        Func<int, int> nextRandom,
        Guid forcedOwnerCharacterId = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(nextRandom);

        lock (Gate)
        {
            var eligible = candidates.Where(candidate =>
                _members.Any(member => member.CharacterId == candidate.CharacterId
                    && member.PartySlot == candidate.PartySlot)
                && !_deadMembers.Contains(candidate.CharacterId)
                && !_partyRevivalReservations.Contains(candidate.CharacterId))
                .OrderBy(candidate => candidate.PartySlot).ToArray();
            // 初始化延至成员入场完成后的首次分配，避免开局时仅队长已登记。
            if (!_routingRotationInitialized && routable && _members.Count > 1)
            {
                _routingRoundRobinIndex = nextRandom(_members.Count);
                _routingRotationInitialized = true;
            }
            return LootRouting.Decide(
                itemAvailable: true,
                routingActive: false,
                routable,
                pickerCharacterId,
                eligible,
                _routingRoundRobinIndex,
                nextRandom,
                out _routingRoundRobinIndex,
                partyMemberCount: _members.Count,
                forcedOwnerCharacterId: forcedOwnerCharacterId);
        }
    }

    internal bool TryBeginLootPickup(Guid pickerId, DungeonGroundItem expectedItem, bool routable,
        IReadOnlyList<LootRoutingCandidate> candidates, Func<int, int> nextRandom, out LootRoutingDecision decision)
    {
        lock (Gate)
        {
            decision = null!;
            if (!_members.Any(member => member.CharacterId == pickerId)
                || (_run.IsActive && (_run.RoomX != expectedItem.RoomX || _run.RoomY != expectedItem.RoomY))
                || !GroundItems.TryGet(expectedItem.GroundId, expectedItem.RoomX, expectedItem.RoomY, out var item)
                || !ReferenceEquals(item, expectedItem)) return false;
            if (_pendingLoot.ContainsKey(expectedItem.GroundId))
            {
                decision = new LootRoutingDecision(LootPickupOutcome.PickupInProgress, Guid.Empty, new byte[4], 21);
                return false;
            }
            decision = DecidePickupRouting(pickerId, routable, candidates, nextRandom, expectedItem.ForcedOwnerCharacterId);
            if (decision.Outcome is LootPickupOutcome.Routed or LootPickupOutcome.Granted)
            {
                decision = decision with { PickupId = Guid.NewGuid() };
                _pendingLoot.Add(item.GroundId, (item, decision.WinnerCharacterId, decision.PickupId, pickerId));
            }
            return true;
        }
    }

    internal void ReleaseLootPickup(DungeonGroundItem item, Guid pickupId = default, bool notifyPicker = false)
    {
        lock (Gate)
            if (_pendingLoot.TryGetValue(item.GroundId, out var pending) && ReferenceEquals(pending.Item, item)
                && (pickupId == Guid.Empty || pickupId == pending.PickupId))
            {
                if (notifyPicker) QueueLootCancellationLocked(pending.Picker);
                _pendingLoot.Remove(item.GroundId);
            }
    }

    private void QueueLootCancellationLocked(Guid picker)
    {
        if (!_members.Any(member => member.CharacterId == picker)) return;
        if (!_cancelledLoot.TryGetValue(picker, out var queue)) _cancelledLoot[picker] = queue = new();
        queue.Enqueue(GameProtocolEngine.CreateGetItemError(21));
    }

    internal bool HasCancelledLootPickup(Guid picker)
    {
        lock (Gate) return _cancelledLoot.TryGetValue(picker, out var queue) && queue.Count != 0;
    }

    internal bool TryTakeCancelledLootPickup(Guid picker, out GameServerPacket packet)
    {
        lock (Gate)
        {
            if (_cancelledLoot.TryGetValue(picker, out var queue) && queue.TryDequeue(out packet!))
            {
                if (queue.Count == 0) _cancelledLoot.Remove(picker);
                return true;
            }
            packet = null!;
            return false;
        }
    }

    /// <summary>回城 / 重开：清空地面掉落（改造前 `groundDungeonItems.ResetDungeon()`，EntranceService.cs:2630/3483/11773/11976）。</summary>
    public void ResetGroundItems()
    {
        lock (Gate)
        {
            GroundItems.ResetDungeon();
            _pendingLoot.Clear();
            _cancelledLoot.Clear();
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
        _pendingLoot.Clear();
        _cancelledLoot.Clear();
        _roomGeneration++;
        _roomMonsters = [.. monsters];
        _bossDeathCascadeStarted = false;
        _bossDeathCascadeAnnounced = false;
        _monsterDeathSettlementsPending = 0;
        AliveMonsters.Clear();
        AliveMonsters.UnionWith(monsters.Select(monster => monster.UniqueId));
        ReportedBossDeaths.Clear();
        BossCascadeMonsterDeaths.Clear();
        DestroyedPassiveObjects.Clear();
    }

    /// <summary>开新局/教学局时清掉本局的击杀统计（每局一份，全队共享）。</summary>
    private void ResetKillCounters()
    {
        _routingRoundRobinIndex = 0;
        _routingRotationInitialized = false;
        NormalMonsterKills = 0;
        ChampionMonsterKills = 0;
        BossMonsterKills = 0;
        ChampionRewardOrdinal = 0;
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
