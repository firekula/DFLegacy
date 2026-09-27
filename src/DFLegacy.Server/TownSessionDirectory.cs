using System.Collections.Concurrent;
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
    Func<GameServerPacket, CancellationToken, ValueTask> SendAsync);

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

internal sealed class TownMember
{
    public required ushort AreaUserId { get; init; }
    public required Guid CharacterId { get; init; }
    public required uint CharacterNo { get; init; }
    public required byte[] CharacterNameBytes { get; init; }
    public required string DisplayName { get; init; }
    public required Func<GameServerPacket, CancellationToken, ValueTask> SendAsync { get; init; }
    public GamePeerInfo? Peer { get; init; }
    public GameServerPacket? Appearance { get; set; }
    public short X { get; set; }
    public short Y { get; set; }
    public byte Direction { get; set; }
    public byte TownId { get; set; }
    public byte AreaId { get; set; }
    public long PositionWindowStartMs { get; set; }
    public int PositionForwardsInWindow { get; set; }

    public GameTownUser ToTownUser() => new(AreaUserId, X, Y, Direction);
}

/// <summary>
/// 城镇区域在线表：按 (城镇, 区域) 维护成员，负责名单、外观、位置与消息的区域投递。
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

    private readonly ConcurrentDictionary<(byte TownId, byte AreaId), AreaRoster> _areas = new();
    private readonly ConcurrentDictionary<Guid, TownMember> _membersByCharacter = new();

    // 结构锁把「读旧位置 → 摘除 → 登记 → 空区回收」串成一个原子段：同一角色的并发进入
    // 退化成先后两次迁移，任何时刻只属于一个区域；空区回收也不会与登记交错出孤儿成员。
    // 锁内只做记账与取快照，网络发送一律留到出锁之后。
    private readonly SemaphoreSlim _structuralGate = new(1, 1);

    public int GetMemberCount(byte townId, byte areaId) =>
        _areas.TryGetValue((townId, areaId), out var roster) ? CountMembers(roster) : 0;

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
            Peer = request.Peer,
            X = request.X,
            Y = request.Y,
            Direction = request.Direction,
            TownId = request.TownId,
            AreaId = request.AreaId
        };

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
                var roster = _areas.GetOrAdd((request.TownId, request.AreaId), _ => new AreaRoster());
                var registered = false;
                lock (roster.Gate)
                {
                    // 拿到区域 Gate 之前该区域可能刚被最后一个离开的人回收：此时必须换一份
                    // 名单重试，否则成员会被登记进一个已从 _areas 摘掉的 roster，谁也看不到他。
                    if (_areas.TryGetValue((request.TownId, request.AreaId), out var current)
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
                "Town member {Name} (uid {AreaUserId}) left {TownId}/{AreaId}; signal={Signal}.",
                migratedFrom!.DisplayName,
                migratedFrom.AreaUserId,
                migratedFrom.TownId,
                migratedFrom.AreaId,
                options.TownSync.DepartureSignal);

            // 迁移同样是一次成员变化：开关打开时旧区剩下的人还要拿到收窄后的名单
            // （默认的离开信号只让 uid 失效，名单里可能仍写着迁移者）。
            await SendRosterRefreshAsync(
                departureTargets,
                migratedFrom.TownId,
                migratedFrom.AreaId,
                [.. departureTargets.Select(other => other.ToTownUser())],
                cancellationToken);

            // 迁移也是一次成员变化：旧区剩下的人要把迁移者从对端表里删掉。
            await SendAreaPeerTableAsync(departureTargets, cancellationToken);
        }

        // 老人：只收增量 USER_AREA(active=1)。新人的外观由本人会话在
        // SendTownAppearanceAsync 里通过 RefreshAppearanceAsync 广播，
        // 避免同一次进入把外观发两遍。
        var joinArea = GameProtocolEngine.CreateUserArea(
            request.TownId,
            request.AreaId,
            member.ToTownUser());
        foreach (var other in existing)
        {
            await SendAsync(other, joinArea, cancellationToken);
        }

        // 名单快照：新人排第一（与单人时代的单元素名单一致；对端表也按同样约定把自己排最前）。
        // 新人名单与老人的刷新名单共用这一份，避免同一时刻出现两种成员顺序。
        var allMembers = new GameTownUser[existing.Length + 1];
        allMembers[0] = member.ToTownUser();
        for (var index = 0; index < existing.Length; index++)
        {
            allMembers[index + 1] = existing[index].ToTownUser();
        }

        // 开关打开时老人再收一份含新人的完整名单：既有实现里老人的名单停在进入那一刻，
        // 不刷新的话它看不到新 uid。追加在既有的增量 USER_AREA 之后，旧报文顺序不变。
        await SendRosterRefreshAsync(
            existing,
            request.TownId,
            request.AreaId,
            allMembers,
            cancellationToken);

        // 新人：名单（自己排在第一位）→ 每个老人的 USER_AREA（开关）→ 其他人的外观。
        var rosterPacket = GameProtocolEngine.CreateAreaUsers(request.TownId, request.AreaId, allMembers);
        var foreignAppearances = existing
            .Select(other => other.Appearance)
            .Where(appearance => appearance is not null)
            .Select(appearance => appearance!)
            .ToArray();

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
            foreach (var appearance in foreignAppearances)
            {
                await SendAsync(member, appearance, cancellationToken);
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
            await SendAsync(member, rosterPacket, cancellationToken);
            foreach (var areaPacket in foreignAreaPackets)
            {
                await SendAsync(member, areaPacket, cancellationToken);
            }

            foreach (var appearance in foreignAppearances)
            {
                await SendAsync(member, appearance, cancellationToken);
            }
        }

        // 对端表在名单/外观之后：客户端只为表内的 uid 建立同镇角色，名单与外观本身
        // 不带 NAT 端点，缺了这张表第二个角色不会出现在地图上。每人拿到的表都把自己
        // 排在最前（与名单里新人排第一一致），老人拿到的是含新人的刷新表。
        // 这是新增通知，上述既有报文的顺序与内容都不变。
        await SendAreaPeerTableAsync([member, .. existing], cancellationToken);

        logger.LogInformation(
            "Town member {Name} (uid {AreaUserId}) entered {TownId}/{AreaId}; members={Count}, foreignAppearances={Appearances}.",
            member.DisplayName,
            member.AreaUserId,
            request.TownId,
            request.AreaId,
            GetMemberCount(request.TownId, request.AreaId),
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
        foreach (var other in SnapshotMembers(member.TownId, member.AreaId))
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
        CancellationToken cancellationToken)
    {
        // 与位置更新同理：陈旧 ticket 不能把自己的外观重播给旧区域。
        if (!TryGetActiveMember(ticket, out var member))
        {
            return;
        }

        member.Appearance = appearance;

        foreach (var other in SnapshotMembers(member.TownId, member.AreaId))
        {
            if (other.CharacterId == member.CharacterId)
            {
                continue;
            }

            await SendAsync(other, appearance, cancellationToken);
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
        foreach (var target in SnapshotMembers(member.TownId, member.AreaId))
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
                "Town member {Name} (uid {AreaUserId}) left {TownId}/{AreaId}; members={Count}, signal={Signal}.",
                member.DisplayName,
                member.AreaUserId,
                member.TownId,
                member.AreaId,
                remaining.Length,
                options.TownSync.DepartureSignal);
        }

        // 开关打开时离开也要刷新名单：剩下的人手里的名单仍然写着已离开的 uid。
        await SendRosterRefreshAsync(
            remaining,
            member.TownId,
            member.AreaId,
            [.. remaining.Select(other => other.ToTownUser())],
            cancellationToken);

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

        if (!_areas.TryGetValue((member.TownId, member.AreaId), out var roster))
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
            if (remaining.Length == 0)
            {
                // 回收与「名单已空」的判断必须在同一把区域 Gate 内完成，
                // 否则新成员可能已经登记进这份名单，却随即被回收掉。
                _areas.TryRemove(
                    new KeyValuePair<(byte TownId, byte AreaId), AreaRoster>(
                        (member.TownId, member.AreaId),
                        roster));
                released = true;
            }
        }

        if (released)
        {
            logger.LogInformation(
                "Town area {TownId}/{AreaId} is now empty and was released.",
                member.TownId,
                member.AreaId);
            return [];
        }

        return remaining;
    }

    private GameServerPacket CreateDepartureNotice(TownMember member, TownMember[] remaining) =>
        options.TownSync.DepartureSignal == TownDepartureSignal.RosterResend
            // 备选信号：重发不含该成员的名单。已知副作用：客户端可能重建区域并扯回本地角色。
            ? GameProtocolEngine.CreateAreaUsers(
                member.TownId,
                member.AreaId,
                [.. remaining.Select(other => other.ToTownUser())])
            : GameProtocolEngine.CreateUserArea(
                member.TownId,
                member.AreaId,
                member.ToTownUser(),
                active: false);

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
    /// 开关打开时向给定成员重发区域完整名单（AREA_USERS）。既有实现只在进入时给新人
    /// 发名单，老人的名单永远停在进入那一刻；成员变化后刷新，客户端才能从名单里看到
    /// 新出现/已离开的 uid。名单内容由调用方按当时的快照传入（与离开信号同一来源），
    /// recipients 为空（单人区域）时不发任何包，保证单人流程一个新增报文都没有。
    /// </summary>
    private async Task SendRosterRefreshAsync(
        IReadOnlyList<TownMember> recipients,
        byte townId,
        byte areaId,
        IReadOnlyList<GameTownUser> users,
        CancellationToken cancellationToken)
    {
        if (!options.TownSync.RefreshRosterOnMembershipChange || recipients.Count == 0)
        {
            return;
        }

        var packet = GameProtocolEngine.CreateAreaUsers(townId, areaId, users);
        foreach (var recipient in recipients)
        {
            await SendAsync(recipient, packet, cancellationToken);
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
        var members = SnapshotMembers(recipient.TownId, recipient.AreaId);
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
            if (!ReferenceEquals(member, recipient) && member.Peer is not null)
            {
                peers.Add(member.Peer);
            }
        }

        return [.. peers];
    }

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

    private TownMember[] SnapshotMembers(byte townId, byte areaId)
    {
        if (!_areas.TryGetValue((townId, areaId), out var roster))
        {
            return [];
        }

        lock (roster.Gate)
        {
            return [.. roster.Members];
        }
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
