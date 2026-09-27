using System.Collections.Concurrent;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 目录成员注册请求。外观包由成员所在会话构建（它手里才有装备/宠物状态），
/// 目录只保存与重放，不读存档、不反向访问别的会话状态。
/// </summary>
public sealed record TownEntryRequest(
    byte TownId,
    byte AreaId,
    ushort AreaUserId,
    Guid CharacterId,
    uint CharacterNo,
    byte[] CharacterNameBytes,
    string DisplayName,
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

        // 同一角色再次进入（换区/重进）：先摘掉旧位置并向旧区广播离开，
        // 保证任何时刻一个角色只属于一个区域。
        if (_membersByCharacter.TryGetValue(request.CharacterId, out var previous))
        {
            await RemoveMemberAsync(previous, broadcastDeparture: true, cancellationToken);
        }

        var member = new TownMember
        {
            AreaUserId = request.AreaUserId,
            CharacterId = request.CharacterId,
            CharacterNo = request.CharacterNo,
            CharacterNameBytes = request.CharacterNameBytes,
            DisplayName = request.DisplayName,
            SendAsync = request.SendAsync,
            X = request.X,
            Y = request.Y,
            Direction = request.Direction,
            TownId = request.TownId,
            AreaId = request.AreaId
        };

        var roster = _areas.GetOrAdd((request.TownId, request.AreaId), _ => new AreaRoster());
        TownMember[] existing;
        lock (roster.Gate)
        {
            existing = [.. roster.Members];
            roster.Members.Add(member);
        }

        _membersByCharacter[request.CharacterId] = member;

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

        // 新人：名单（自己排在第一位，与单人时代的单元素名单一致）→ 其他人的外观。
        var rosterPacket = GameProtocolEngine.CreateAreaUsers(
            request.TownId,
            request.AreaId,
            [member.ToTownUser(), .. existing.Select(other => other.ToTownUser())]);
        var foreignAppearances = existing
            .Select(other => other.Appearance)
            .Where(appearance => appearance is not null)
            .Select(appearance => appearance!)
            .ToArray();

        if (options.TownSync.ForeignAppearanceBeforeRoster)
        {
            foreach (var appearance in foreignAppearances)
            {
                await SendAsync(member, appearance, cancellationToken);
            }

            await SendAsync(member, rosterPacket, cancellationToken);
        }
        else
        {
            await SendAsync(member, rosterPacket, cancellationToken);
            foreach (var appearance in foreignAppearances)
            {
                await SendAsync(member, appearance, cancellationToken);
            }
        }

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
        if (ticket is null || !options.TownSync.Enabled)
        {
            return;
        }

        var member = ticket.Member;
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
        if (ticket is null || !options.TownSync.Enabled)
        {
            return;
        }

        var member = ticket.Member;
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
        if (ticket is null || !options.TownSync.Enabled)
        {
            return 0;
        }

        var member = ticket.Member;
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
        _membersByCharacter.TryRemove(
            new KeyValuePair<Guid, TownMember>(member.CharacterId, member));

        if (!_areas.TryGetValue((member.TownId, member.AreaId), out var roster))
        {
            return;
        }

        TownMember[] remaining;
        lock (roster.Gate)
        {
            if (!roster.Members.Remove(member))
            {
                return;
            }

            remaining = [.. roster.Members];
        }

        if (remaining.Length == 0)
        {
            _areas.TryRemove(
                new KeyValuePair<(byte TownId, byte AreaId), AreaRoster>(
                    (member.TownId, member.AreaId),
                    roster));
            logger.LogInformation(
                "Town area {TownId}/{AreaId} is now empty and was released.",
                member.TownId,
                member.AreaId);
            return;
        }

        if (!broadcastDeparture)
        {
            return;
        }

        if (options.TownSync.DepartureSignal == TownDepartureSignal.RosterResend)
        {
            // 备选信号：重发不含该成员的名单。已知副作用：客户端可能重建区域并扯回本地角色。
            var rosterPacket = GameProtocolEngine.CreateAreaUsers(
                member.TownId,
                member.AreaId,
                [.. remaining.Select(other => other.ToTownUser())]);
            foreach (var other in remaining)
            {
                await SendAsync(other, rosterPacket, cancellationToken);
            }
        }
        else
        {
            var departure = GameProtocolEngine.CreateUserArea(
                member.TownId,
                member.AreaId,
                member.ToTownUser(),
                active: false);
            foreach (var other in remaining)
            {
                await SendAsync(other, departure, cancellationToken);
            }
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

    private void RemoveMemberLocally(TownMember member)
    {
        _membersByCharacter.TryRemove(
            new KeyValuePair<Guid, TownMember>(member.CharacterId, member));
        if (!_areas.TryGetValue((member.TownId, member.AreaId), out var roster))
        {
            return;
        }

        lock (roster.Gate)
        {
            roster.Members.Remove(member);
            if (roster.Members.Count == 0)
            {
                _areas.TryRemove(
                    new KeyValuePair<(byte TownId, byte AreaId), AreaRoster>(
                        (member.TownId, member.AreaId),
                        roster));
            }
        }
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
            RemoveMemberLocally(member);
            return false;
        }
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
