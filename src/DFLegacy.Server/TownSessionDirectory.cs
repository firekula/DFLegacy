using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 目录成员注册请求。外观包由成员所在会话构建（它手里才有装备/宠物状态），
/// 目录只保存与重放，不读存档、不反向访问别的会话状态。
/// Peer 是该成员登录后上报的 UDP 端点（可能为 null，例如没有 NAT 上报的大厅会话）；
/// 目录用它拼镇内对端表，客户端只为表内的 uid 建立同镇角色。
/// </summary>
public sealed record TownEntryRequest(
    byte TownId,
    byte AreaId,
    ushort AreaUserId,
    Guid CharacterId,
    uint CharacterNo,
    byte[] CharacterNameBytes,
    string DisplayName,
    GamePeerInfo? Peer,
    short X,
    short Y,
    byte Direction,
    Func<GameServerPacket, CancellationToken, ValueTask> SendAsync,
    Func<bool>? IsInTown = null);

/// <summary>目录成员的只读快照（日志与后台页用）。</summary>
public sealed record TownMemberSnapshot(
    ushort AreaUserId,
    Guid CharacterId,
    uint CharacterNo,
    string DisplayName,
    byte TownId,
    byte AreaId,
    short X,
    short Y,
    byte Direction);

/// <summary>成员进入区域后拿到的凭据；刷新外观、更新位置、离开都要带它。</summary>
public sealed class TownMemberTicket
{
    internal TownMemberTicket(TownMember member) => Member = member;

    internal TownMember Member { get; }

    public ushort AreaUserId => Member.AreaUserId;
    public Guid CharacterId => Member.CharacterId;
    public byte TownId => Member.TownId;
    public byte AreaId => Member.AreaId;
}

internal sealed record TownAppearanceSnapshot(GameServerPacket Basic, GameServerPacket? Detail);

internal sealed class TownMember
{
    public required ushort AreaUserId { get; init; }
    public required Guid CharacterId { get; init; }
    public required uint CharacterNo { get; init; }
    public required byte[] CharacterNameBytes { get; init; }
    public required string DisplayName { get; init; }
    public required Func<GameServerPacket, CancellationToken, ValueTask> SendAsync { get; init; }
    public GamePeerInfo? Peer { get; init; }
    public Func<bool>? IsInTown { get; init; }
    public TownAppearanceSnapshot? Appearance { get; set; }
    // Recovery notifications and entry snapshots can run on different sessions.
    // Keep each subject's basic/detail pair and later recovery updates ordered.
    public SemaphoreSlim AppearanceDeliveryGate { get; } = new(1, 1);
    public short X { get; set; }
    public short Y { get; set; }
    public byte Direction { get; set; }
    public byte TownId { get; set; }
    public byte AreaId { get; set; }

    /// <summary>
    /// 区域实例号：共享区域恒为 0；私密区域（<see cref="TownSyncOptions.PrivateAreas"/>）取成员
    /// 自己的 AreaUserId（在线角色唯一），同一 (城镇, 区域) 因此按成员各成一份名单。
    /// </summary>
    public required int Instance { get; init; }

    public long PositionWindowStartMs { get; set; }
    public int PositionForwardsInWindow { get; set; }

    /// <summary>
    /// 是否已把自己的加入增量（USER_AREA active=1）发给当时的同区成员。客户端只为已有
    /// USERINFO 记录的 uid 建立同镇角色，这条增量必须排在该成员的外观之后（见
    /// RefreshAppearanceAsync），且每人只介绍一次。成员作用域：迁移会新建实例、自动归零，
    /// 迁移后的新同区成员因此仍能拿到一次「外观 → 加入」。
    /// </summary>
    public bool JoinAnnounced { get; set; }

    // NOTI 11 silently ignores a uid until its USERINFO has created a CNUser.
    // Keep recipient-specific delivery knowledge: another entry may register
    // while this recipient is still receiving its initial appearance snapshot.
    // The member reference also distinguishes re-entry / reuse of the same uid.
    public ConcurrentDictionary<ushort, TownMember> KnownAppearances { get; } = new();
    public ConcurrentDictionary<ushort, GameServerPacket> KnownCombatDetails { get; } = new();
    // This records server AREA_USERS/USER_AREA delivery order only. It does
    // not establish the native scene: returning clients fade asynchronously.
    // Native pending-detail handling completes initialization at actual Town1.
    public volatile bool CombatDetailsReady;

    public GameTownUser ToTownUser() => new(AreaUserId, X, Y, Direction);
}

/// <summary>
/// 城镇区域在线表：按 (城镇, 区域, 实例) 维护成员，负责名单、外观、位置与消息的区域投递。
/// 共享区域的实例恒为 0（与改造前的 (城镇, 区域) 分桶逐字节一致）；配置为私密区域
/// 时每个成员各占一个实例，彼此看不到也听不到。五个城镇的赛丽亚房间始终私密。公开的
/// <see cref="GetMemberCount"/> / <see cref="Snapshot"/> 按区域聚合全部实例。
/// 客户端按 uid 认人，成员 id 必须由 AreaUserIdPool 分配且唯一（见 EntranceService）。
/// </summary>
public sealed class TownSessionDirectory(
    ServerOptions options,
    TimeProvider timeProvider,
    ILogger<TownSessionDirectory> logger)
{
    private sealed class AreaRoster
    {
        public readonly object Gate = new();
        public readonly List<TownMember> Members = [];
    }

    /// <summary>名单键：共享区域 Instance=0，私密区域 Instance=成员 AreaUserId（见 <see cref="TownMember.Instance"/>）。</summary>
    private readonly record struct AreaKey(byte TownId, byte AreaId, int Instance);

    private readonly ConcurrentDictionary<AreaKey, AreaRoster> _areas = new();
    private readonly ConcurrentDictionary<Guid, TownMember> _membersByCharacter = new();

    /// <summary>
    /// 私密区域集合，构造时从配置解析一次：条目必须是 "城镇:区域"，两侧 1..255；
    /// 非法条目告警跳过，绝不让服务因配置笔误起不来。
    /// </summary>
    private readonly HashSet<(byte TownId, byte AreaId)> _privateAreas =
        ParsePrivateAreas(options.TownSync.PrivateAreas, logger);

    /// <summary>
    /// 镇内对端表也要跟着"对端端点改写"（中继/黑洞实验）走：**只在镇内表里写真实端点，
    /// 客户端就会在城镇阶段拿到并缓存对端地址**，之后队伍表里那份改写过的它不再采用
    /// —— 2026-10-04 真机实测就是这样：队伍表已改写成服务端，审计里 `mt` 仍然是 0。
    /// 改写目标与 `GameplayDatagramService` 完全同一套解析（同一批环境变量）。
    /// </summary>
    private readonly IPEndPoint? _peerEndpointOverride =
        GameplayDatagramService.ResolvePeerEndpointOverride(
            options.GameplayDatagram,
            Environment.GetEnvironmentVariable("DFLEGACY_UDP_RELAY") == "1",
            Environment.GetEnvironmentVariable("DFLEGACY_UDP_PEER_SINK"));

    // 结构锁把「读旧位置 → 摘除 → 登记 → 空区回收」串成一个原子段：同一角色的并发进入
    // 退化成先后两次迁移，任何时刻只属于一个区域；空区回收也不会与登记交错出孤儿成员。
    // 锁内只做记账与取快照，网络发送一律留到出锁之后。
    private readonly SemaphoreSlim _structuralGate = new(1, 1);

    public int GetMemberCount(byte townId, byte areaId)
    {
        // 私密区域可能有多份实例名单（每成员一份），管理面看到的应是该区域的总人数。
        var count = 0;
        foreach (var (key, roster) in _areas)
        {
            if (key.TownId == townId && key.AreaId == areaId)
            {
                count += CountMembers(roster);
            }
        }

        return count;
    }

    public TownMemberSnapshot[] Snapshot(byte townId, byte areaId) =>
        [.. SnapshotMembers(townId, areaId).Select(ToSnapshot)];

    public bool TryGetSnapshot(Guid characterId, out TownMemberSnapshot snapshot)
    {
        if (!_membersByCharacter.TryGetValue(characterId, out var member))
        {
            snapshot = null!;
            return false;
        }

        snapshot = ToSnapshot(member);
        return true;
    }

    public bool IsPublicTownMember(Guid id) => _membersByCharacter.TryGetValue(id, out var m)
        && m.Instance == 0 && m.IsInTown?.Invoke() != false;
    public bool SharesPublicArea(Guid a, Guid b) => IsPublicTownMember(a) && IsPublicTownMember(b)
        && _membersByCharacter.TryGetValue(a, out var x) && _membersByCharacter.TryGetValue(b, out var y)
        && x.TownId == y.TownId && x.AreaId == y.AreaId;

    public async Task<TownMemberTicket?> EnterAsync(
        TownEntryRequest request,
        CancellationToken cancellationToken)
    {
        if (!options.TownSync.Enabled)
        {
            return null;
        }

        var member = new TownMember
        {
            AreaUserId = request.AreaUserId,
            CharacterId = request.CharacterId,
            CharacterNo = request.CharacterNo,
            CharacterNameBytes = request.CharacterNameBytes,
            DisplayName = request.DisplayName,
            SendAsync = request.SendAsync,
            IsInTown = request.IsInTown,
            Peer = request.Peer,
            X = request.X,
            Y = request.Y,
            Direction = request.Direction,
            TownId = request.TownId,
            AreaId = request.AreaId,
            Instance = ResolveInstance(request.TownId, request.AreaId, request.AreaUserId)
        };
        var areaKey = KeyOf(member);

        TownMember? migratedFrom = null;
        TownMember[] existing = [];
        TownMember[] departureTargets = [];
        GameServerPacket? departureNotice = null;

        await _structuralGate.WaitAsync(cancellationToken);
        try
        {
            // 同一角色再次进入（换区/重进）：先摘掉旧位置并准备好向旧区广播的离开信号，
            // 登记新位置之前旧位置一定已经不存在，保证任何时刻一个角色只属于一个区域。
            if (_membersByCharacter.TryGetValue(request.CharacterId, out var previous))
            {
                migratedFrom = previous;
                var remaining = RemoveMemberCore(previous);
                if (remaining.Length > 0)
                {
                    departureNotice = CreateDepartureNotice(previous, remaining);
                    departureTargets = remaining;
                }
            }

            while (true)
            {
                var roster = _areas.GetOrAdd(areaKey, _ => new AreaRoster());
                var registered = false;
                lock (roster.Gate)
                {
                    // 拿到区域 Gate 之前该区域可能刚被最后一个离开的人回收：此时必须换一份
                    // 名单重试，否则成员会被登记进一个已从 _areas 摘掉的 roster，谁也看不到他。
                    if (_areas.TryGetValue(areaKey, out var current)
                        && ReferenceEquals(current, roster))
                    {
                        existing = [.. roster.Members];
                        roster.Members.Add(member);
                        registered = true;
                    }
                }

                if (registered)
                {
                    break;
                }
            }

            _membersByCharacter[request.CharacterId] = member;
        }
        finally
        {
            _structuralGate.Release();
        }

        // 出锁后再发包：迁移先向旧区发离开信号，再走新区自己的进入流程。
        if (departureNotice is not null)
        {
            foreach (var target in departureTargets)
            {
                await SendAsync(target, departureNotice, cancellationToken);
            }

            logger.LogInformation(
                "Town member {Name} (uid {AreaUserId}) left {TownId}/{AreaId} (instance {Instance}); signal={Signal}.",
                migratedFrom!.DisplayName,
                migratedFrom.AreaUserId,
                migratedFrom.TownId,
                migratedFrom.AreaId,
                migratedFrom.Instance,
                options.TownSync.DepartureSignal);

            // 迁移同样是一次成员变化：开关打开时旧区剩下的人还要拿到收窄后的名单
            // （默认的离开信号销毁旧 actor，但客户端名单里可能仍写着迁移者）。
            await SendRosterRefreshAsync(departureTargets, cancellationToken);

            // 迁移也是一次成员变化：旧区剩下的人要把迁移者从对端表里删掉。
            await SendAreaPeerTableAsync(departureTargets, cancellationToken);
        }

        // 老人不在这里收新人的加入增量：客户端只为已有 USERINFO 记录的 uid 建立同镇角色，
        // 此刻新人的外观还没广播（它由本人会话在 SendTownAppearanceAsync 里通过
        // RefreshAppearanceAsync 发出），抢先发 USER_AREA 会被客户端静默丢掉。加入增量
        // 改由 RefreshAppearanceAsync 在同一个成员的外观之后补发一次（见 JoinAnnounced）。
        // 开关打开时老人先收一份含新人的完整名单：既有实现里老人的名单停在进入那一刻，
        // 不刷新的话它看不到新 uid。刷新名单按收件人现算、收件人自己排第一
        // （见 SendRosterRefreshAsync）。
        await SendRosterRefreshAsync(existing, cancellationToken);

        // 新人：同区已有成员的外观 → 名单（自己排在第一位，与单人时代的单元素名单一致）。
        var rosterPacket = GameProtocolEngine.CreateAreaUsers(
            request.TownId,
            request.AreaId,
            [member.ToTownUser(), .. existing.Select(other => other.ToTownUser())]);
        var foreignAppearances = existing.Where(other => other.Appearance is not null).ToArray();

        // 客户端可能只有在「名单里有该 uid」且「收到该 uid 的 USER_AREA」时才建立同镇角色：
        // 新人此前只拿到名单与外观，缺了这一半。开关打开时逐人补发 USER_AREA(active=1)。
        GameServerPacket[] foreignAreaPackets = options.TownSync.SendUserAreaForExistingMembers
            ? [.. existing.Select(other => GameProtocolEngine.CreateUserArea(
                request.TownId,
                request.AreaId,
                other.ToTownUser(),
                active: true))]
            : [];

        if (options.TownSync.ForeignAppearanceBeforeRoster)
        {
            // 默认顺序。客户端对 AREA_USERS/USER_AREA 里没有 USERINFO 记录的 uid 会静默
            // 跳过该条目，旧顺序（名单在前）下老人永远建不出同镇角色。
            foreach (var other in foreignAppearances)
            {
                await SendCachedAppearanceAsync(member, other, cancellationToken, includeDetail: false);
            }

            await SendAsync(member, rosterPacket, cancellationToken);

            // 该模式下外观本来就在名单之前，无法同时满足「名单之后、外观之前」；
            // 只能把新增的 USER_AREA 追加在名单之后，既有报文一个不动。
            foreach (var areaPacket in foreignAreaPackets)
            {
                await SendAsync(member, areaPacket, cancellationToken);
            }
        }
        else
        {
            // 对照实验用的旧顺序：名单 → 补发的 USER_AREA → 外观。
            await SendAsync(member, rosterPacket, cancellationToken);
            foreach (var areaPacket in foreignAreaPackets)
            {
                await SendAsync(member, areaPacket, cancellationToken);
            }

            foreach (var other in foreignAppearances)
            {
                await SendCachedAppearanceAsync(member, other, cancellationToken, includeDetail: false);
            }
        }

        // Keep mode 0 before the roster so native CNUser lookup works, and
        // deliver foreign detail after the server's roster/area writes. The
        // client may still be fading from dungeon; native pending-detail
        // handling waits for actual Town1 before initializing those actors.
        member.CombatDetailsReady = true;
        foreach (var other in SnapshotMembers(member.TownId, member.AreaId, member.Instance))
        {
            if (!ReferenceEquals(other, member))
            {
                await SendCachedAppearanceDetailAsync(member, other, cancellationToken);
            }
        }

        // 对端表在名单/外观之后：客户端只为表内的 uid 建立同镇角色，名单与外观本身
        // 不带 NAT 端点，缺了这张表第二个角色不会出现在地图上。每人拿到的表都把自己
        // 排在最前（与名单里新人排第一一致）。老人此时尚未收到新人的 USERINFO，
        // 不能抢先给他们发新人条目：客户端会静默丢弃，随后组队把 MTU=0 写进可靠
        // 缓冲容量而无限发送空帧。老人的刷新由首次外观广播在介绍之后发送一次。
        await SendAreaPeerTableAsync([member], cancellationToken);

        // 私密区域里「members」聚合数会让两个各自成区的成员看起来在同一区：实例号、
        // 本实例人数与区域实例数都打出来，运维一眼能分辨。
        logger.LogInformation(
            "Town member {Name} (uid {AreaUserId}) entered {TownId}/{AreaId} (instance {Instance}); instanceMembers={Members}, areaInstances={Instances}, foreignAppearances={Appearances}.",
            member.DisplayName,
            member.AreaUserId,
            request.TownId,
            request.AreaId,
            member.Instance,
            CountInstanceMembers(member),
            CountInstances(request.TownId, request.AreaId),
            foreignAppearances.Length);

        return new TownMemberTicket(member);
    }

    public Task LeaveAsync(TownMemberTicket? ticket, CancellationToken cancellationToken) =>
        ticket is null
            ? Task.CompletedTask
            : RemoveMemberAsync(ticket.Member, broadcastDeparture: true, cancellationToken);

    public async Task UpdatePositionAsync(
        TownMemberTicket? ticket,
        short x,
        short y,
        byte direction,
        ushort movementValue,
        CancellationToken cancellationToken)
    {
        // 迁移/离开后旧 ticket 立即失效：陈旧凭据既不能改状态，也不能把已离开的头像
        // 复活到旧区域。
        if (!TryGetActiveMember(ticket, out var member))
        {
            return;
        }

        member.X = x;
        member.Y = y;
        member.Direction = direction;

        if (!TryConsumePositionBudget(member))
        {
            return;
        }

        var packet = GameProtocolEngine.CreateUserPosition(member.ToTownUser(), movementValue);
        foreach (var other in SnapshotMembers(member.TownId, member.AreaId, member.Instance))
        {
            if (other.CharacterId == member.CharacterId)
            {
                // 本地客户端自己拥有自己的移动，回发会把它扯回原位。
                continue;
            }

            await SendAsync(other, packet, cancellationToken);
        }
    }

    public async Task RefreshAppearanceAsync(
        TownMemberTicket? ticket,
        GameServerPacket appearance,
        CancellationToken cancellationToken,
        GameServerPacket? detail = null)
    {
        // 与位置更新同理：陈旧 ticket 不能把自己的外观重播给旧区域。
        if (!TryGetActiveMember(ticket, out var member))
        {
            return;
        }

        await member.AppearanceDeliveryGate.WaitAsync(cancellationToken);
        try
        {
            // The ticket can expire while waiting for another delivery.
            if (!IsRegistered(member)) return;
            var snapshot = new TownAppearanceSnapshot(appearance, detail);
            member.Appearance = snapshot;

            // 收件人只算一次：加入增量必须发给刚拿到这份外观的同一批人，投递路上新进来的
            // 成员不能先收到「加入」却没收到外观（顺序反了客户端同样会丢掉这条增量）。
            var others = SnapshotMembers(member.TownId, member.AreaId, member.Instance)
                .Where(other => other.CharacterId != member.CharacterId)
                .ToArray();

            foreach (var other in others)
            {
                await SendAppearanceAsync(other, member, snapshot, cancellationToken);
            }

            // 新人进入时不同步发加入增量（见 EnterAsync）：客户端只为已有 USERINFO 记录的 uid
            // 建立同镇角色，所以这条 USER_AREA 必须紧跟本成员的外观之后，每个成员只介绍一次；
            // 后续换装/宠物变化重播外观时不再重复。当时没有同区观众（单人区域）不算介绍过：
            // 下一次广播仍是「外观 → 加入」，第一个到场的人也拿得到完整顺序。
            if (member.JoinAnnounced || others.Length == 0)
            {
                return;
            }

            var joinArea = GameProtocolEngine.CreateUserArea(
                member.TownId,
                member.AreaId,
                member.ToTownUser(),
                active: true);
            foreach (var other in others)
            {
                await SendAsync(other, joinArea, cancellationToken);
            }

            member.JoinAnnounced = true;

            if (member.Peer is not null)
            {
                await SendAreaPeerTableAsync(others, cancellationToken);
            }
        }
        finally
        {
            member.AppearanceDeliveryGate.Release();
        }
    }

    /// <summary>
    /// Refresh a town actor's cached combat detail after weakness recovery.
    /// NOTI 4/33 only update the owner; foreign actors need their own mode-1
    /// projection. Never initialize an unknown or already-in-dungeon actor here.
    /// </summary>
    public Task RefreshAppearanceDetailAsync(
        TownMemberTicket? ticket,
        GameServerPacket detail,
        CancellationToken cancellationToken) =>
        RefreshAppearanceDetailAsync(ticket, _ => detail, cancellationToken);

    public Task RefreshAppearanceRecoveryAsync(
        TownMemberTicket? ticket,
        byte recovery,
        CancellationToken cancellationToken) =>
        RefreshAppearanceDetailAsync(ticket,
            appearance => appearance.Detail is { } detail
                ? PartyMemberDetailProjection.WithStaminaRecovery(detail, recovery)
                : null,
            cancellationToken);

    private async Task RefreshAppearanceDetailAsync(
        TownMemberTicket? ticket,
        Func<TownAppearanceSnapshot, GameServerPacket?> createDetail,
        CancellationToken cancellationToken)
    {
        if (!TryGetActiveMember(ticket, out var member)) return;
        await member.AppearanceDeliveryGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsRegistered(member) || !IsInTown(member) || member.Appearance is not { } appearance)
            {
                return;
            }

            var detail = createDetail(appearance);
            if (detail is null) return;
            member.Appearance = appearance with { Detail = detail };
            foreach (var other in SnapshotMembers(member.TownId, member.AreaId, member.Instance))
            {
                await SendAppearanceDetailAsync(other, member, detail, cancellationToken);
            }
        }
        finally
        {
            member.AppearanceDeliveryGate.Release();
        }
    }

    public async Task<int> BroadcastToAreaAsync(
        TownMemberTicket? ticket,
        GameServerPacket packet,
        bool includeSelf,
        CancellationToken cancellationToken)
    {
        // 陈旧 ticket 不属于任何有效区域，既不投递也不计数。
        if (!TryGetActiveMember(ticket, out var member))
        {
            return 0;
        }

        var delivered = 0;
        foreach (var target in SnapshotMembers(member.TownId, member.AreaId, member.Instance))
        {
            if (!includeSelf && target.CharacterId == member.CharacterId)
            {
                continue;
            }

            if (await SendAsync(target, packet, cancellationToken))
            {
                delivered++;
            }
        }

        return delivered;
    }

    /// <summary>
    /// 组队名册的区域广播：把 <paramref name="packet"/> 投给该 (城镇, 区域) **全部实例**的成员
    /// （含私密实例），坏通道沿用 <see cref="SendAsync(TownMember, GameServerPacket, CancellationToken)"/>
    /// 的剔除逻辑。调用方（PartyService）负责按区域生成不同的 body，并保证
    /// **调用本方法时不持有自己的锁**（避免与目录的结构锁形成嵌套）。
    /// </summary>
    internal async Task<int> BroadcastPartyPacketAsync(
        byte townId,
        byte areaId,
        GameServerPacket packet,
        CancellationToken cancellationToken)
    {
        if (!options.TownSync.Enabled)
        {
            return 0;
        }

        var delivered = 0;
        foreach (var member in SnapshotMembers(townId, areaId))
        {
            if (await SendAsync(member, packet, cancellationToken))
            {
                delivered++;
            }
        }

        return delivered;
    }

    private async Task RemoveMemberAsync(
        TownMember member,
        bool broadcastDeparture,
        CancellationToken cancellationToken)
    {
        TownMember[] remaining;

        await _structuralGate.WaitAsync(cancellationToken);
        try
        {
            remaining = RemoveMemberCore(member);
        }
        finally
        {
            _structuralGate.Release();
        }

        // 区域已回收或成员本就不在：没有存活目标需要通知或刷新。
        if (remaining.Length == 0)
        {
            return;
        }

        if (broadcastDeparture)
        {
            var notice = CreateDepartureNotice(member, remaining);
            foreach (var target in remaining)
            {
                await SendAsync(target, notice, cancellationToken);
            }

            logger.LogInformation(
                "Town member {Name} (uid {AreaUserId}) left {TownId}/{AreaId} (instance {Instance}); instanceMembers={Count}, signal={Signal}.",
                member.DisplayName,
                member.AreaUserId,
                member.TownId,
                member.AreaId,
                member.Instance,
                remaining.Length,
                options.TownSync.DepartureSignal);
        }

        // 开关打开时离开也要刷新名单：剩下的人手里的名单仍然写着已离开的 uid。
        await SendRosterRefreshAsync(remaining, cancellationToken);

        // 任何离开都要刷新对端表（强制移除路径见 RemoveMemberLocallyAsync），
        // 否则剩下的人会继续把离开者当成镇内 UDP 对端。
        await SendAreaPeerTableAsync(remaining, cancellationToken);
    }

    /// <summary>
    /// 在结构锁内摘除成员并回收空区域，返回出锁后仍在区域里的成员（区域已空或成员本就不在时为空）。
    /// 调用方必须已持有 _structuralGate —— 摘除、登记与空区回收共用同一把结构锁，
    /// 否则并发的 EnterAsync 会在回收与登记之间插入，留下谁也看不到的孤儿成员。
    /// </summary>
    private TownMember[] RemoveMemberCore(TownMember member)
    {
        // 按「键 + 实例」移除：迁移后旧 ticket 的陈旧移除不能摘掉新登记的同角色成员。
        if (!_membersByCharacter.TryRemove(
                new KeyValuePair<Guid, TownMember>(member.CharacterId, member)))
        {
            return [];
        }

        var areaKey = KeyOf(member);
        if (!_areas.TryGetValue(areaKey, out var roster))
        {
            return [];
        }

        TownMember[] remaining;
        var released = false;
        lock (roster.Gate)
        {
            if (!roster.Members.Remove(member))
            {
                return [];
            }

            remaining = [.. roster.Members];
            member.KnownAppearances.Clear();
            member.KnownCombatDetails.Clear();
            foreach (var recipient in remaining)
            {
                if (recipient.KnownAppearances.TryRemove(
                        new KeyValuePair<ushort, TownMember>(member.AreaUserId, member)))
                {
                    recipient.KnownCombatDetails.TryRemove(member.AreaUserId, out _);
                }
            }
            if (remaining.Length == 0)
            {
                // 回收与「名单已空」的判断必须在同一把区域 Gate 内完成，
                // 否则新成员可能已经登记进这份名单，却随即被回收掉。
                _areas.TryRemove(new KeyValuePair<AreaKey, AreaRoster>(areaKey, roster));
                released = true;
            }
        }

        if (released)
        {
            logger.LogInformation(
                "Town area {TownId}/{AreaId} (instance {Instance}) is now empty and was released.",
                member.TownId,
                member.AreaId,
                member.Instance);
            return [];
        }

        return remaining;
    }

    private GameServerPacket CreateDepartureNotice(TownMember member, TownMember[] remaining) =>
        options.TownSync.DepartureSignal == TownDepartureSignal.RosterResend
            // 备选信号：重发不含该成员的名单。注意它**不能移除角色**——客户端的
            // AREA_USERS 逐条只做“加/更新”，对名单里缺席的 uid 不做任何处理，
            // 所以残留的 actor/显示特效依旧在，重登还可能显示旧位置；保留此选项仅为
            // 兼容旧配置，默认信号不用它。
            ? GameProtocolEngine.CreateAreaUsers(
                member.TownId,
                member.AreaId,
                [.. remaining.Select(other => other.ToTownUser())])
            // 真正的销毁信号：USER_AREA 带 0xFF/0xFF 哨兵，收包客户端发现 (town, area)
            // 与自己当前 scene 不一致，调 mapMgr->RemoveCreature（0x41EC1F）递归摘掉
            // 角色及其显示/特效子对象。此前的“同区域 + active=0”只写 creature+0x3E4
            //（隐藏），既不销毁也不清特效，正是“下线不消失/重登显示旧位置”的根因。
            : GameProtocolEngine.CreateUserAreaDeparture(member.ToTownUser());

    private async ValueTask RemoveMemberLocallyAsync(TownMember member)
    {
        TownMember[] remaining;

        await _structuralGate.WaitAsync(CancellationToken.None);
        try
        {
            remaining = RemoveMemberCore(member);
        }
        finally
        {
            _structuralGate.Release();
        }

        // 强制移除（通道已坏）不适用于普通离开信号，但同样是一次成员变化：剩下的人
        // 必须把坏通道的 uid 从对端表里删掉。刷新用 None —— 触发它的发送可能正处于
        // 取消状态，而这次投递没有可取消的会话语义。
        await SendAreaPeerTableAsync(remaining, CancellationToken.None);
    }

    /// <summary>
    /// 开关打开时向给定成员逐个重发区域完整名单（AREA_USERS）。既有实现只在进入时给新人
    /// 发名单，老人的名单永远停在进入那一刻；成员变化后刷新，客户端才能从名单里看到
    /// 新出现/已离开的 uid。与 BuildPeerTable 同款做法：每个收件人的名单都按发送当时的
    /// 存活成员现算，并把**收件人自己排第一**、其余按名单登记顺序——「名单第一条是本人」
    /// 是本文件与 AREA_USERS 的既有约定，共享一份「新人第一」的快照会让加入事件的收件人
    /// 把第 0 条误当成自己的角色并重建/错位。现算顺带保证：投递过程中收件人自己已不在
    /// 区域里（通道坏掉被摘除或刷新途中离开）时跳过，不对死通道重发，也不把刚离开的 uid
    /// 写回别人的名单。recipients 为空（单人区域）时不发任何包。
    /// </summary>
    private async Task SendRosterRefreshAsync(
        IReadOnlyList<TownMember> recipients,
        CancellationToken cancellationToken)
    {
        if (!options.TownSync.RefreshRosterOnMembershipChange || recipients.Count == 0)
        {
            return;
        }

        foreach (var recipient in recipients)
        {
            var members = SnapshotMembers(recipient.TownId, recipient.AreaId, recipient.Instance);
            var self = members.FirstOrDefault(candidate => ReferenceEquals(candidate, recipient));
            if (self is null)
            {
                continue;
            }

            var users = new GameTownUser[members.Length];
            users[0] = self.ToTownUser();
            var index = 1;
            foreach (var member in members)
            {
                if (!ReferenceEquals(member, recipient))
                {
                    users[index++] = member.ToTownUser();
                }
            }

            await SendAsync(
                recipient,
                GameProtocolEngine.CreateAreaUsers(recipient.TownId, recipient.AreaId, users),
                cancellationToken);
        }
    }

    /// <summary>
    /// 向给定成员逐个发镇内 UDP 对端表（NOTI 11）：客户端只为表内的 uid 建立同镇角色，
    /// 名单与外观本身不带 NAT 端点，单靠它们第二个角色不会出现在地图上。每个目标都按
    /// 发送当时的存活成员现算，并把自己排在最前（与 AREA_USERS 里自己排第一一致）——
    /// 投递过程中可能有成员因通道坏掉被摘除，冻结的名单会把刚离开的 uid 又写回别人的表里。
    /// 没有端点信息的成员（例如未上报 NAT 的大厅会话）被跳过；一个都不剩时整张表不发。
    /// </summary>
    private async Task SendAreaPeerTableAsync(
        IReadOnlyList<TownMember> recipients,
        CancellationToken cancellationToken)
    {
        if (!options.TownSync.SendAreaPeerTable)
        {
            return;
        }

        foreach (var recipient in recipients)
        {
            var peers = BuildPeerTable(recipient);
            if (peers.Length == 0)
            {
                continue;
            }

            // 表头条数是 u8：区域成员超过 255 时协议放不下（名单的条数是 u16，不受影响）。
            // 截断并告警，而不是让 CreateUdpPeerInfo 抛异常打断整个进出流程；
            // 每人自己的端点排在最前，截断不会把收件人自己丢掉。
            if (peers.Length > byte.MaxValue)
            {
                logger.LogWarning(
                    "Area {TownId}/{AreaId} has {Count} members with endpoints; the client peer table holds at most {Limit}.",
                    recipient.TownId,
                    recipient.AreaId,
                    peers.Length,
                    byte.MaxValue);
                peers = peers[..byte.MaxValue];
            }

            await SendAsync(
                recipient,
                GameProtocolEngine.CreateUdpPeerInfo(peers),
                cancellationToken);
        }
    }

    /// <summary>
    /// 现算一个成员应看到的对端表。目标已经不在区域里（自身通道坏掉被强制移除，或刷新
    /// 途中离开）时返回空表：既不对死通道重发，也不把自己的旧端点写进别人的表。
    /// </summary>
    private GamePeerInfo[] BuildPeerTable(TownMember recipient)
    {
        var members = SnapshotMembers(recipient.TownId, recipient.AreaId, recipient.Instance);
        var self = members.FirstOrDefault(member => ReferenceEquals(member, recipient));
        if (self is null)
        {
            return [];
        }

        var peers = new List<GamePeerInfo>(members.Length);
        if (self.Peer is not null)
        {
            peers.Add(self.Peer);
        }

        foreach (var member in members)
        {
            if (!ReferenceEquals(member, recipient) && member.Peer is not null
                && recipient.KnownAppearances.TryGetValue(member.AreaUserId, out var known)
                && ReferenceEquals(known, member))
            {
                // 别人的端点走"对端端点改写"（中继/黑洞实验）；自己的那条在上面已按原样加过。
                peers.Add(GameplayDatagramService.ApplyPeerEndpointOverride(
                    member.Peer,
                    isSelf: false,
                    _peerEndpointOverride));
            }
        }

        return [.. peers];
    }

    internal async Task ReplayPartyCombatDetailsAsync(
        IReadOnlyCollection<Guid> characterIds,
        CancellationToken cancellationToken,
        Func<Guid, bool>? isCurrentMember = null)
    {
        // A party-roster recipient is not necessarily a member (leavers also
        // receive cleanup rosters). The caller snapshots real member GUIDs.
        var members = characterIds.Distinct()
            .Select(characterId => _membersByCharacter.GetValueOrDefault(characterId))
            .Where(member => member is not null)
            .Cast<TownMember>()
            .ToArray();
        foreach (var subject in members)
        {
            await subject.AppearanceDeliveryGate.WaitAsync(cancellationToken);
            try
            {
                if (isCurrentMember?.Invoke(subject.CharacterId) == false) continue;
                if (subject.Appearance?.Detail is not { } detail) continue;
                foreach (var recipient in members)
                {
                    // Party membership can change while waiting for this
                    // subject's gate without changing either town ticket.
                    if (isCurrentMember?.Invoke(subject.CharacterId) == false
                        || isCurrentMember?.Invoke(recipient.CharacterId) == false) continue;
                    // Existing exact registration, known-basic, private-area
                    // and town-state guards apply to both ends. The client may
                    // have deleted its model, so packet identity cannot dedupe.
                    await SendAppearanceDetailAsync(recipient, subject, detail,
                        cancellationToken, force: true);
                }
            }
            finally
            {
                subject.AppearanceDeliveryGate.Release();
            }
        }
    }

    private async ValueTask SendCachedAppearanceAsync(
        TownMember recipient,
        TownMember subject,
        CancellationToken cancellationToken,
        bool includeDetail = true)
    {
        await subject.AppearanceDeliveryGate.WaitAsync(cancellationToken);
        try
        {
            if (subject.Appearance is { } appearance && IsRegistered(subject) && IsRegistered(recipient))
            {
                await SendAppearanceAsync(recipient, subject, appearance, cancellationToken, includeDetail);
            }
        }
        finally
        {
            subject.AppearanceDeliveryGate.Release();
        }
    }

    private async ValueTask SendCachedAppearanceDetailAsync(
        TownMember recipient,
        TownMember subject,
        CancellationToken cancellationToken)
    {
        await subject.AppearanceDeliveryGate.WaitAsync(cancellationToken);
        try
        {
            if (subject.Appearance?.Detail is { } detail)
            {
                await SendAppearanceDetailAsync(recipient, subject, detail, cancellationToken);
            }
        }
        finally
        {
            subject.AppearanceDeliveryGate.Release();
        }
    }

    private async ValueTask SendAppearanceAsync(
        TownMember recipient,
        TownMember subject,
        TownAppearanceSnapshot appearance,
        CancellationToken cancellationToken,
        bool includeDetail = true)
    {
        if (await SendAsync(recipient, appearance.Basic, cancellationToken))
        {
            // Pair registration with departure cleanup under the structural gate;
            // an in-flight appearance must not retain a member after it has left.
            await _structuralGate.WaitAsync(CancellationToken.None);
            try
            {
                if (_membersByCharacter.TryGetValue(subject.CharacterId, out var liveSubject)
                    && ReferenceEquals(liveSubject, subject)
                    && _membersByCharacter.TryGetValue(recipient.CharacterId, out var liveRecipient)
                    && ReferenceEquals(liveRecipient, recipient))
                {
                    recipient.KnownAppearances[subject.AreaUserId] = subject;
                }
            }
            finally
            {
                _structuralGate.Release();
            }

            if (includeDetail && appearance.Detail is { } detail)
            {
                await SendAppearanceDetailAsync(recipient, subject, detail, cancellationToken);
            }
        }
    }

    // All detail callers hold the subject's delivery gate. Track successful
    // packet identity so a racing recovery update and entry replay cannot send
    // the same initialization twice, or overwrite a newer cached projection.
    private async ValueTask SendAppearanceDetailAsync(
        TownMember recipient,
        TownMember subject,
        GameServerPacket detail,
        CancellationToken cancellationToken,
        bool force = false)
    {
        if (!CanSendAppearanceDetail(recipient, subject)
            || !force && recipient.KnownCombatDetails.TryGetValue(subject.AreaUserId, out var known)
                && ReferenceEquals(known, detail))
        {
            return;
        }
        if (!await SendAsync(recipient, detail, cancellationToken)) return;
        await _structuralGate.WaitAsync(CancellationToken.None);
        try
        {
            if (CanSendAppearanceDetail(recipient, subject))
            {
                recipient.KnownCombatDetails[subject.AreaUserId] = detail;
            }
        }
        finally
        {
            _structuralGate.Release();
        }
    }

    private bool CanSendAppearanceDetail(TownMember recipient, TownMember subject) =>
        !ReferenceEquals(recipient, subject)
        && recipient.CombatDetailsReady
        && KeyOf(recipient) == KeyOf(subject)
        && IsRegistered(subject) && IsRegistered(recipient)
        && IsInTown(subject) && IsInTown(recipient)
        && recipient.KnownAppearances.TryGetValue(subject.AreaUserId, out var known)
        && ReferenceEquals(known, subject);

    private static bool IsInTown(TownMember member) => member.IsInTown?.Invoke() ?? true;

    private bool IsRegistered(TownMember member) =>
        _membersByCharacter.TryGetValue(member.CharacterId, out var registered)
        && ReferenceEquals(registered, member);

    private async ValueTask<bool> SendAsync(
        TownMember member,
        GameServerPacket packet,
        CancellationToken cancellationToken)
    {
        try
        {
            await member.SendAsync(packet, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Town delivery to {Name} (uid {AreaUserId}) failed; removing the member.",
                member.DisplayName,
                member.AreaUserId);
            await RemoveMemberLocallyAsync(member);
            return false;
        }
    }

    /// <summary>
    /// ticket 只有在它指向的实例仍是 _membersByCharacter 里登记的那一个时才算有效：
    /// 迁移或离开之后，旧 ticket 不再属于任何区域，也不能再发包或改状态。
    /// </summary>
    private bool TryGetActiveMember(TownMemberTicket? ticket, out TownMember member)
    {
        if (ticket is null || !options.TownSync.Enabled)
        {
            member = null!;
            return false;
        }

        member = ticket.Member;
        return _membersByCharacter.TryGetValue(member.CharacterId, out var registered)
            && ReferenceEquals(registered, member);
    }

    private bool TryConsumePositionBudget(TownMember member)
    {
        var limit = options.TownSync.MaxPositionForwardsPerSecond;
        if (limit <= 0)
        {
            return true;
        }

        var nowMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (nowMs - member.PositionWindowStartMs >= 1000)
        {
            member.PositionWindowStartMs = nowMs;
            member.PositionForwardsInWindow = 0;
        }

        if (member.PositionForwardsInWindow >= limit)
        {
            return false;
        }

        member.PositionForwardsInWindow++;
        return true;
    }

    /// <summary>单个实例的成员快照：投递路径一律只在该实例内选收件人。</summary>
    private TownMember[] SnapshotMembers(byte townId, byte areaId, int instance)
    {
        if (!_areas.TryGetValue(new AreaKey(townId, areaId, instance), out var roster))
        {
            return [];
        }

        lock (roster.Gate)
        {
            return [.. roster.Members];
        }
    }

    /// <summary>按区域聚合全部实例（公开的 <see cref="Snapshot"/> 用）；共享区域只有一个实例。</summary>
    private TownMember[] SnapshotMembers(byte townId, byte areaId)
    {
        var members = new List<TownMember>();
        foreach (var (key, roster) in _areas)
        {
            if (key.TownId != townId || key.AreaId != areaId)
            {
                continue;
            }

            lock (roster.Gate)
            {
                members.AddRange(roster.Members);
            }
        }

        return [.. members];
    }

    /// <summary>
    /// 私密区域里每个成员各占一个实例（用其唯一的 AreaUserId），共享区域统一为 0。
    /// AreaUserIdPool 只分配 1..容量，0 不会与共享哨兵相撞（池的既有断言见 AreaUserIdPool）。
    /// </summary>
    private int ResolveInstance(byte townId, byte areaId, ushort areaUserId) =>
        _privateAreas.Contains((townId, areaId)) ? areaUserId : 0;

    private static AreaKey KeyOf(TownMember member) =>
        new(member.TownId, member.AreaId, member.Instance);

    /// <summary>日志用：某成员所在实例的当前人数（共享区域与旧实现的「members」同值）。</summary>
    private int CountInstanceMembers(TownMember member) =>
        _areas.TryGetValue(KeyOf(member), out var roster) ? CountMembers(roster) : 0;

    /// <summary>日志用：某区域当前的实例数（共享区域恒为 1，私密区域每个成员一份）。</summary>
    private int CountInstances(byte townId, byte areaId)
    {
        var count = 0;
        foreach (var key in _areas.Keys)
        {
            if (key.TownId == townId && key.AreaId == areaId)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 解析 "城镇:区域" 私密区域条目（两侧 1..255），非法条目告警跳过而不是抛异常：
    /// 一个笔误不该让服务端起不来，最多是该区域仍按共享处理。
    /// </summary>
    private static HashSet<(byte TownId, byte AreaId)> ParsePrivateAreas(
        string[] entries,
        ILogger<TownSessionDirectory> logger)
    {
        // ACT1 Script.pvf: town/*.twn marks these rooms as [gate]. They are
        // personal rooms, including StormPass 5/1; area 1 alone is not enough
        // (HendonMyre 2/1 and WestCoast 3/1 are shared streets).
        // PrivateAreas adds custom rooms; an omitted/empty config must not
        // expose another player's personal room.
        var areas = new HashSet<(byte TownId, byte AreaId)>
        {
            (1, 1), (2, 5), (3, 2), (4, 1), (5, 1)
        };
        foreach (var entry in entries ?? [])
        {
            var parts = (entry ?? string.Empty).Split(':');
            if (parts.Length == 2
                && byte.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var townId)
                && byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var areaId)
                && townId >= 1
                && areaId >= 1)
            {
                areas.Add((townId, areaId));
                continue;
            }

            logger.LogWarning(
                "TownSync.PrivateAreas entry {Entry} is not a valid \"town:area\" pair with 1..255 on both sides; skipping it.",
                entry);
        }

        return areas;
    }

    private static int CountMembers(AreaRoster roster)
    {
        lock (roster.Gate)
        {
            return roster.Members.Count;
        }
    }

    private static TownMemberSnapshot ToSnapshot(TownMember member) => new(
        member.AreaUserId,
        member.CharacterId,
        member.CharacterNo,
        member.DisplayName,
        member.TownId,
        member.AreaId,
        member.X,
        member.Y,
        member.Direction);
}
