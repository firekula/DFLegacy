using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>一条在线角色通道（沿用 ItemTradeChannel 的模式）：选角时登记，会话摘除时移除。</summary>
internal sealed class PartyChannel
{
    public required string AccountName { get; init; }
    public required string CharacterName { get; init; }
    public required Guid CharacterId { get; init; }
    public required ushort AreaUserId { get; set; }
    public required Guid SessionId { get; init; }
    public required Func<GameServerPacket, ValueTask> Send { get; init; }
}

/// <summary>
/// CMD 10 里客户端自报的三个显示值（应答窗正文，dnf.str 4150；A 卷 §3.2）。服务端把它们原样
/// 放进 NOTI 7 交给被邀请者，自己不做校验（设计稿 §5.1 的真机待验项之一）。
/// public 是必须的：public 的 <see cref="PartyService.HandleRequestAsync"/> 以它为参数类型
/// （C# 不允许 public 方法暴露更低的参数类型）。
/// </summary>
public sealed record PartyRequestInfo(uint ReviveCoins, ushort Fatigue, ushort WeaknessPercent);

/// <summary>
/// 一条待决请求：**邀请与"申请加入"是同一个包**（设计稿 §5.1），这里只记"谁向谁"，
/// 语义在接受时按双方此刻的队伍状态决定。键 = 目标角色 id（同一目标同时只有一条）。
/// </summary>
internal sealed class PartyPendingRequest
{
    public required Guid RequesterCharacterId { get; init; }
    public required ushort RequesterUid { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// 离队原因：决定 NOTI 10 发不发、kind 取几（设计稿 §5.3/§5.4/§5.5 三条路径的**唯一**差别）。
/// 用枚举而不是 bool：三态里的"被踢"与"掉线"文案不同（kind 0/1），bool 表达不了。
/// </summary>
internal enum PartyLeaveNotice
{
    /// <summary>主动退队（CMD 13）：不发 NOTI 10，只发新名册——客户端比较"处理前我在队里 / 处理后不在"自我清理（设计稿 §5.3）。</summary>
    Silent,

    /// <summary>被踢（CMD 14）：被踢者与其余成员都先收 `NOTI 10 [槽位, 0]`（"已被队长强行请出队伍"，设计稿 §5.4）。</summary>
    Kicked,

    /// <summary>掉线 / 换角色 / 换频道：其余成员收 `NOTI 10 [槽位, 1]`（"网络不稳，已返回城镇"，设计稿 §5.5）。</summary>
    Disconnected
}

/// <summary>
/// 城镇内组队（设计稿 docs/superpowers/specs/2026-09-28-party-system-design.md）。
/// 内存态、不落盘；队伍跨区域持续（只有切换角色/频道/断线才退队）。
/// 结构改动全部在 <see cref="_gate"/> 内完成，**锁内不做 await**；发包一律出锁之后。
/// </summary>
public sealed class PartyService : BackgroundService
{
    /// <summary>两次组队请求之间的最小间隔（与交易的 3 秒同款，防连点）。</summary>
    public const int MinimumRequestIntervalMilliseconds = 3_000;

    /// <summary>待决请求的存活窗口：客户端"拒绝"不发包，只能靠它收尾（设计稿 §5.2）。</summary>
    public static readonly TimeSpan RequestWindow = TimeSpan.FromSeconds(30);

    /// <summary>CMD 13 失败码 18：未组队（dnf.str 0x2CE）。本轮只发得出这一个 CMD 13 失败码。</summary>
    private const byte NotInPartyErrorCode = 18;
    /// <summary>CMD 14 失败码 4：不是队长（dnf.str 0x2D0）。</summary>
    private const byte NotLeaderErrorCode = 4;
    /// <summary>CMD 14 失败码 8：槽位里没有人（dnf.str 0x2D2）。</summary>
    private const byte MemberNotFoundErrorCode = 8;
    /// <summary>CMD 11 失败包的 peerType：100 且 code=0 → "对方不在线，或拒绝了您的组队请求"（0x7567）。</summary>
    private const byte RequestRefusedPeerType = 100;

    private readonly JsonGameStore _store;
    private readonly TownSessionDirectory _directory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PartyService> _logger;
    private readonly ConcurrentDictionary<Guid, PartyChannel> _channels = new();
    private readonly ConcurrentDictionary<ushort, PartyRoster> _parties = new();
    private readonly ConcurrentDictionary<Guid, ushort> _partyByCharacter = new();
    private readonly ConcurrentDictionary<Guid, PartyPendingRequest> _pending = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _blockedRequests = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastRequestAt = new();

    /// <summary>
    /// 每支队"曾经通知过哪些区域"（partyId → 区域累积集）。客户端没有列表查询包，「队伍信息」列表
    /// 完全由"客户端本地是否知道这支队伍"推导，所以名册变化与解散都必须通知**每一个曾经收到过名册的区域**，
    /// 而不只是"此刻还有队员站在"的区域——队伍撤出的那些区域里的旁观者手里还留着上一份名册的本地副本，
    /// 不刷新就会一直显示过时的成员/人数，点"加入"甚至会把申请发给已经退队的人（设计稿 §6.2.3 明文要求
    /// 解散时通知"广播过的各区域旁观者"）。只在 <see cref="_gate"/> 内读写；队伍解散时随队伍一起清除——
    /// partyId 会被复用，残留条目会把新队伍的名册广播到上一支队伍的旧区域。
    /// </summary>
    private readonly Dictionary<ushort, List<(byte TownId, byte AreaId)>> _notifiedAreas = new();
    private readonly object _gate = new();

    public PartyService(
        JsonGameStore store,
        TownSessionDirectory directory,
        TimeProvider timeProvider,
        ILogger<PartyService> logger)
    {
        _store = store;
        _directory = directory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>测试用：清掉 CMD 10 的服务端限流时间戳（真机路径不会调用）。</summary>
    internal void ResetRequestThrottleForTests(Guid characterId) => _lastRequestAt.TryRemove(characterId, out _);

    /// <summary>
    /// 测试用：把待决请求与"拉黑静默项"立刻推到过期，好让 <see cref="SweepExpiredAsync"/> 能覆盖到
    /// 30 秒窗口的收尾路径（真机路径不会调用；生产代码靠真实时钟）。
    /// </summary>
    internal void ExpirePendingForTests()
    {
        var expired = _timeProvider.GetUtcNow() - TimeSpan.FromSeconds(1);
        foreach (var (targetCharacterId, request) in _pending)
        {
            _pending[targetCharacterId] = new PartyPendingRequest
            {
                RequesterCharacterId = request.RequesterCharacterId,
                RequesterUid = request.RequesterUid,
                ExpiresAt = expired
            };
        }

        foreach (var (requesterCharacterId, _) in _blockedRequests)
        {
            _blockedRequests[requesterCharacterId] = expired;
        }
    }

    public Task AttachChannelAsync(
        string accountName,
        string characterName,
        Guid characterId,
        ushort areaUserId,
        Guid sessionId,
        Func<GameServerPacket, ValueTask> send,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterName);
        ArgumentNullException.ThrowIfNull(send);
        _channels[characterId] = new PartyChannel
        {
            AccountName = accountName,
            CharacterName = characterName,
            CharacterId = characterId,
            AreaUserId = areaUserId,
            SessionId = sessionId,
            Send = send
        };
        return Task.CompletedTask;
    }

    /// <summary>
    /// 角色会话结束（返回选人 / 换角色 / 换频道 / 断线）：摘通道、清待决请求，并把该角色从队伍里摘掉
    /// （设计稿 §5.5：这三条路径是唯一的"自动退队"来源）。会话 id 不匹配的陈旧清理完全不动作。
    /// </summary>
    public async Task DetachChannelAsync(
        Guid characterId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(characterId, out var channel)
            || channel.SessionId != sessionId
            || !_channels.TryRemove(new KeyValuePair<Guid, PartyChannel>(characterId, channel)))
        {
            return;
        }

        _lastRequestAt.TryRemove(characterId, out _);
        _blockedRequests.TryRemove(characterId, out _);
        _pending.TryRemove(characterId, out _);
        foreach (var (targetCharacterId, request) in _pending)
        {
            if (request.RequesterCharacterId == characterId)
            {
                _pending.TryRemove(new KeyValuePair<Guid, PartyPendingRequest>(targetCharacterId, request));
            }
        }

        await RemoveMemberFromPartyAsync(characterId, PartyLeaveNotice.Disconnected, cancellationToken);
    }

    /// <summary>
    /// CMD 10（peerType=0）：邀请 / 申请加入。校验顺序与失败形状见设计稿 §5.1：
    /// 目标解析不到 → `CMD 11 [0,100,0]`；双方都有队伍 / 目标不是队长 / 限流 → 静默；
    /// **目标已有待决请求 → 先到者优先**：占位用一次 `TryAdd` 原子完成，后来者立刻收同形的
    /// `CMD 11 [0,100,0]`（不用等 30 秒），目标正在看的那个窗口也不会被换掉；
    /// 被目标拉黑 → 只登记发起方（目标无感知，**不写** `_pending`），30 秒后照常给发起方失败提示。
    /// </summary>
    public async Task HandleRequestAsync(
        Guid requesterCharacterId,
        ushort targetUid,
        PartyRequestInfo info,
        CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(requesterCharacterId, out var requester))
        {
            return;
        }

        // uid **只取一次**：换区会改 PartyChannel.AreaUserId，而下面的拉黑判定要 await——
        // 两次读之间换区会让存下的 RequesterUid 与 NOTI 7 里发出去的 uid 不一致，
        // 接受方的 CMD 11 就永远对不上，请求方白等 30 秒。
        var requesterUid = requester.AreaUserId;

        var now = _timeProvider.GetUtcNow();
        if (_lastRequestAt.TryGetValue(requesterCharacterId, out var lastRequest)
            && now - lastRequest < TimeSpan.FromMilliseconds(MinimumRequestIntervalMilliseconds))
        {
            _logger.LogInformation(
                "Rate-limited a party request from {CharacterId}: the previous one is {Elapsed} ms old.",
                requesterCharacterId,
                (now - lastRequest).TotalMilliseconds);
            return;
        }

        _lastRequestAt[requesterCharacterId] = now;

        if (!TryResolveTarget(requesterCharacterId, targetUid, out var target))
        {
            await SendRequestFailureAsync(requesterCharacterId, cancellationToken);
            return;
        }

        // 队伍状态必须**在锁内**一次判完：名册是共享可变对象，锁外读 `Leader`（= `_members[0]`）
        // 可能正好撞上"队长与最后一名队员先后退队"而抛 ArgumentOutOfRangeException 逸出 handler。
        var bothInParties = false;
        var targetIsNotLeader = false;
        var partyIsFull = false;
        lock (_gate)
        {
            TryGetPartyOf(requesterCharacterId, out var requesterParty);
            TryGetPartyOf(target.CharacterId, out var targetParty);
            bothInParties = requesterParty is not null && targetParty is not null;
            targetIsNotLeader = targetParty is not null && targetParty.Leader.CharacterId != target.CharacterId;
            partyIsFull = (targetParty?.IsFull ?? false) || (requesterParty?.IsFull ?? false);
        }

        if (bothInParties)
        {
            _logger.LogInformation(
                "Ignored a party request from {Requester} to {Target}: both are already in parties.",
                requester.CharacterName,
                target.CharacterName);
            return;
        }

        if (targetIsNotLeader)
        {
            // 客户端只会把请求发给队长（列表详情窗取 leaderSlot 的 uid），到不了这里的是异常包。
            _logger.LogInformation(
                "Ignored a party request from {Requester} to {Target}: the target is not the party leader.",
                requester.CharacterName,
                target.CharacterName);
            return;
        }

        if (partyIsFull)
        {
            await SendRequestFailureAsync(requesterCharacterId, cancellationToken);
            return;
        }

        // 被拉黑：目标完全无感知；但登记一个只属于发起方的到期项，让 30 秒后照常收到
        // 与"对方没理"完全同形的失败提示（否则"立刻能重发"会成为拉黑的旁证）。
        // 这条分支**不写** _pending（只登记 _blockedRequests）。
        var blocked = await IsBlacklistedAsync(
            target.AccountName,
            requester.CharacterName,
            cancellationToken);
        if (blocked)
        {
            _blockedRequests[requesterCharacterId] = now + RequestWindow;
            _logger.LogInformation(
                "Silently dropped a party request from {Requester} to {Target}: the target's blacklist holds the requester.",
                requester.CharacterName,
                target.CharacterName);
            return;
        }

        var pending = new PartyPendingRequest
        {
            RequesterCharacterId = requesterCharacterId,
            RequesterUid = requesterUid,
            ExpiresAt = now + RequestWindow
        };

        // 原子占位："目标已有待决请求"的判定与写入必须是**一个操作**：先 ContainsKey 后赋值的写法在
        // 中间隔着一个 await（拉黑查询）时会放两个并发请求都通过判定。TryAdd 失败 = 目标已被更早的
        // 请求占住 → 后来者立刻收同形失败（先到者优先，设计稿 §5.1），目标正在看的窗口也不会被换掉。
        if (!_pending.TryAdd(target.CharacterId, pending))
        {
            await SendRequestFailureAsync(requesterCharacterId, cancellationToken);
            return;
        }

        await SendAsync(
            target.CharacterId,
            GameProtocolEngine.CreateRequestPartyNotification(
                requesterUid,
                info.ReviveCoins,
                info.Fatigue,
                info.WeaknessPercent),
            cancellationToken);
        _logger.LogInformation(
            "Party request from {Requester} to {Target} (uid {TargetUid}).",
            requester.CharacterName,
            target.CharacterName,
            targetUid);
    }

    /// <summary>
    /// CMD 11（peerType=0）：接受。四种情形按双方此刻的队伍状态决定（设计稿 §5.1）：
    /// 接受者在队伍里 → 请求者并入接受者的队伍；请求者在队伍里 → 接受者并入请求者的队伍；
    /// 都没有 → 新建队伍（请求者为队长，partyId = 请求者 uid）；接受者不是队长 / 队满 → 回失败。
    /// </summary>
    public async Task HandleResponseAsync(
        Guid accepterCharacterId,
        ushort requesterUid,
        CancellationToken cancellationToken = default)
    {
        if (!_pending.TryGetValue(accepterCharacterId, out var pending))
        {
            return;
        }

        if (pending.RequesterUid != requesterUid)
        {
            _logger.LogInformation(
                "Ignored a party response from {Accepter}: the pending requester uid is {Expected}, not {Actual}.",
                accepterCharacterId,
                pending.RequesterUid,
                requesterUid);
            return;
        }

        // 幂等：拿不到（并发/重复的第二次接受）就直接收场。丢掉返回值会让两个并发接受都往下跑，
        // 第二个要么重复广播名册，要么回一条本不该有的失败包。
        if (!_pending.TryRemove(new KeyValuePair<Guid, PartyPendingRequest>(accepterCharacterId, pending)))
        {
            return;
        }

        if (_timeProvider.GetUtcNow() > pending.ExpiresAt)
        {
            await SendRequestFailureAsync(pending.RequesterCharacterId, cancellationToken);
            return;
        }

        if (!_channels.TryGetValue(accepterCharacterId, out var accepter)
            || !_channels.TryGetValue(pending.RequesterCharacterId, out var requester))
        {
            // 一侧已离线：窗口不再有意义，静默收场（请求方由超时路径兜底）。
            return;
        }

        PartyRoster? roster = null;
        var created = false;
        lock (_gate)
        {
            TryGetPartyOf(pending.RequesterCharacterId, out var requesterParty);
            TryGetPartyOf(accepterCharacterId, out var accepterParty);
            if (requesterParty is not null && accepterParty is not null)
            {
                roster = null;
            }
            else if (accepterParty is not null)
            {
                if (accepterParty.Leader.CharacterId == accepterCharacterId
                    && !accepterParty.IsFull
                    && accepterParty.TryAdd(BuildMember(requester)))
                {
                    _partyByCharacter[pending.RequesterCharacterId] = accepterParty.PartyId;
                    roster = accepterParty;
                }
            }
            else if (requesterParty is not null)
            {
                if (!requesterParty.IsFull && requesterParty.TryAdd(BuildMember(accepter)))
                {
                    _partyByCharacter[accepterCharacterId] = requesterParty.PartyId;
                    roster = requesterParty;
                }
            }
            else if (PartyRoster.TryAllocatePartyId(
                         [.. _parties.Keys],
                         requester.AreaUserId,
                         out var partyId))
            {
                roster = new PartyRoster(
                    partyId,
                    BuildMember(requester),
                    GameProtocolEngine.PartyMemberSlotCount,
                    nameSelection: 0);
                roster.TryAdd(BuildMember(accepter));
                _parties[partyId] = roster;
                _partyByCharacter[pending.RequesterCharacterId] = partyId;
                _partyByCharacter[accepterCharacterId] = partyId;
                created = true;
            }
        }

        if (roster is null)
        {
            // 队伍在等待期间满了 / 双方都有队伍 / 接受者已不是队长：给接受方也回失败。
            // **形状必须与请求方完全一致**：`CMD 11 [0,100,0]`（peerType=100 → 文案 0x7567）。
            // CMD 11 的客户端分支按 body[1] 当 peerType 分流（100 → 提示，1 → 交易窗），
            // 填别的 errorCode（如 19）是未知 peerType → 接受方**什么都看不到**，静默失败（设计稿 §2.1）。
            await SendRequestFailureAsync(accepterCharacterId, cancellationToken);
            return;
        }

        await PublishRosterAsync(roster, cancellationToken);
        await SendAsync(
            pending.RequesterCharacterId,
            GameProtocolEngine.CreatePartyAcceptedNotification(accepter.AreaUserId),
            cancellationToken);
        _logger.LogInformation(
            "{Accepter} accepted the party request from {Requester}; party {PartyId} now has {Count} member(s){Created}.",
            accepter.CharacterName,
            requester.CharacterName,
            roster.PartyId,
            roster.Count,
            created ? " (created)" : string.Empty);
    }

    /// <summary>
    /// CMD 13：退出队伍（无载荷）。失败码只有 **18 = 未组队**（dnf.str 0x2CE）：本路径发不出别的码——
    /// "取到名册后又消失"的并发窗口由 <see cref="RemoveMemberFromPartyAsync"/> 静默收场，
    /// 客户端那边看到的仍然是"退队成功"（Task 3 评审删掉了永远发不出的 19 = 状态已变化）。
    /// </summary>
    public async Task HandleLeaveAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        PartyRoster? roster;
        lock (_gate)
        {
            TryGetPartyOf(characterId, out roster);
        }

        if (roster is null)
        {
            await SendCommandErrorAsync(
                characterId,
                GameProtocolEngine.LeavePartyCommand,
                NotInPartyErrorCode,
                cancellationToken);
            return;
        }

        await RemoveMemberFromPartyAsync(characterId, PartyLeaveNotice.Silent, cancellationToken);
    }

    /// <summary>
    /// CMD 14：踢人（载荷 = 槽位下标 0..3，不是 uid）。只有队长能踢，且必须指向队伍里的别人：
    /// 失败码只有 **4 = 不是队长、8 = 该槽位没人**（B 卷 §6.4；19 = 状态已变化在 Task 3 评审里
    /// 删掉了——本路径发不出来，判定与摘除都在同一个锁内一次做完）。
    /// </summary>
    public async Task HandleKickAsync(
        Guid leaderCharacterId,
        byte slot,
        CancellationToken cancellationToken = default)
    {
        Guid? victim = null;
        var isLeader = false;
        lock (_gate)
        {
            // 判定在锁内一次做完：锁外读 `Leader`（= 队长槽里的成员）会撞上名册被清空而抛异常。
            // 槽位下标按**稳定槽位**解释（成员不搬动）：`TryGetSlot(slot)` 就是客户端界面上那一格。
            if (TryGetPartyOf(leaderCharacterId, out var roster)
                && roster.Leader.CharacterId == leaderCharacterId)
            {
                isLeader = true;
                if (slot != roster.LeaderSlot && roster.TryGetSlot(slot, out var target))
                {
                    victim = target.CharacterId;
                }
            }
        }

        if (!isLeader)
        {
            await SendCommandErrorAsync(
                leaderCharacterId,
                GameProtocolEngine.WalkoutPartyMemberCommand,
                NotLeaderErrorCode,
                cancellationToken);
            return;
        }

        if (victim is null)
        {
            await SendCommandErrorAsync(
                leaderCharacterId,
                GameProtocolEngine.WalkoutPartyMemberCommand,
                MemberNotFoundErrorCode,
                cancellationToken);
            return;
        }

        await RemoveMemberFromPartyAsync(victim.Value, PartyLeaveNotice.Kicked, cancellationToken);
    }

    /// <summary>
    /// CMD 12：队伍名与人数上限。无队伍时 = "新建队伍"（1 人队，队长是自己，partyId = 自己的 uid）；
    /// 只有队长能改（非队长静默忽略——客户端没有这条失败路径）。自定义队名不传播（设计稿 §2.4）。
    /// </summary>
    public async Task HandleSetInfoAsync(
        Guid characterId,
        byte nameSelection,
        byte[] nameBytes,
        byte maxMembers,
        CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(characterId, out var channel))
        {
            return;
        }

        PartyRoster? roster = null;
        lock (_gate)
        {
            TryGetPartyOf(characterId, out var existing);
            if (existing is null)
            {
                if (!PartyRoster.TryAllocatePartyId([.. _parties.Keys], channel.AreaUserId, out var partyId))
                {
                    return;
                }

                roster = new PartyRoster(partyId, BuildMember(channel), maxMembers, nameSelection);
                _parties[partyId] = roster;
                _partyByCharacter[characterId] = partyId;
            }
            else if (existing.Leader.CharacterId == characterId)
            {
                existing.SetInfo(nameSelection, maxMembers);
                roster = existing;
            }
        }

        if (roster is null)
        {
            return;
        }

        if (nameBytes.Length > 0)
        {
            // 自定义名（sel==0）只存在服务端日志里：NOTI 9 只带预置名下标，别人看不到（设计稿 §2.4）。
            _logger.LogInformation(
                "Party {PartyId} received a custom name ({Length} bytes) that NOTI 9 cannot carry.",
                roster.PartyId,
                nameBytes.Length);
        }

        await PublishRosterAsync(roster, cancellationToken);
    }

    /// <summary>
    /// 进区/换区（`CMD 38` 与副本结束回城都会走到）：**不退队**（设计稿 §4.4 的用户决策），
    /// 只做三件事：①把本会话的 uid 更新成新区域的；②更新该成员在名册里的位置并重播名册
    /// （旧区域版本里他会变成 0、新区域版本里是真实 uid）；③**进区补发**——把本区域现有队伍
    /// 逐支发给刚进来的这位，否则他的「队伍信息」列表是空的、推荐伙伴也过滤不准（设计稿 §6.2）。
    /// 已知边界：私密区域（默认只有赛利亚房间 1/1）里每个成员各占一个实例，同 (城镇, 区域) 的
    /// "真实 uid"在对方的本地 user 表里解析不出来 → 那一行空白；私密区域里也无法组队
    /// （对方在他的实例里不可见，请求根本发不出）。
    /// </summary>
    public async Task OnAreaChangedAsync(
        Guid characterId,
        byte townId,
        byte areaId,
        ushort areaUserId,
        CancellationToken cancellationToken = default)
    {
        List<(Guid CharacterId, GameServerPacket Packet)> direct = [];
        List<(byte TownId, byte AreaId, GameServerPacket Packet)> areas = [];
        lock (_gate)
        {
            if (_channels.TryGetValue(characterId, out var channel))
            {
                channel.AreaUserId = areaUserId;
            }

            if (TryGetPartyOf(characterId, out var roster) && roster is not null)
            {
                roster.UpdateLocation(characterId, townId, areaId, areaUserId);
                foreach (var member in roster.Members)
                {
                    direct.Add((member.CharacterId, BuildRosterPacket(roster, member.CharacterId)));
                }

                // 新区域这一份 + "曾通知过的区域"各一份：旧区域的旁观者在这一份里看到他的格子变 0
                // （即使旧区域已经一个队员都不剩——它仍然"知道"这支队伍，设计稿 §5.6）。
                foreach (var (memberTownId, memberAreaId) in NotifiedAreas(roster))
                {
                    areas.Add((
                        memberTownId,
                        memberAreaId,
                        BuildRosterPacketForArea(roster, memberTownId, memberAreaId)));
                }
            }

            foreach (var visible in RostersVisibleIn(townId, areaId))
            {
                direct.Add((characterId, BuildRosterPacket(visible, characterId)));
            }
        }

        foreach (var (target, packet) in direct)
        {
            await SendAsync(target, packet, cancellationToken);
        }

        foreach (var (memberTownId, memberAreaId, packet) in areas)
        {
            await _directory.BroadcastPartyPacketAsync(memberTownId, memberAreaId, packet, cancellationToken);
        }
    }

    /// <summary>
    /// 离开城镇区域（进副本 / 回登录前的离场）：**不退队**（设计稿 §4.4），只重播名册——
    /// 目录里已经没有他，"曾通知过的区域"收到的那一份里他那一格因此变成 0
    /// （旁观者不会把 uid 解析到别人身上；设计稿 §5.6）。
    /// </summary>
    public async Task OnLeftAreaAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        PartyRoster? roster;
        lock (_gate)
        {
            TryGetPartyOf(characterId, out roster);
        }

        if (roster is null)
        {
            return;
        }

        await PublishRosterAsync(roster, cancellationToken);
    }

    /// <summary>**锁内调用**：此刻在 (townId, areaId) 有成员站着的队伍（进区补发用；目录是权威）。</summary>
    private List<PartyRoster> RostersVisibleIn(byte townId, byte areaId)
    {
        var visible = new List<PartyRoster>();
        foreach (var roster in _parties.Values)
        {
            foreach (var member in roster.Members)
            {
                if (_directory.TryGetSnapshot(member.CharacterId, out var snapshot)
                    && snapshot.TownId == townId
                    && snapshot.AreaId == areaId)
                {
                    visible.Add(roster);
                    break;
                }
            }
        }

        return visible;
    }

    /// <summary>
    /// 名册下发：每个成员自己那份（按他自己的区域替换槽位 uid）+ "曾通知过的区域"的旁观者那份。
    /// **不在任何城镇区域的成员**（在副本里等）只给"他自己可解析"的那一份（设计稿 §2.2.2）。
    /// </summary>
    internal Task PublishRosterAsync(PartyRoster roster, CancellationToken cancellationToken)
    {
        Guid[] recipients;
        lock (_gate)
        {
            recipients = [.. roster.Members.Select(member => member.CharacterId)];
        }

        return PublishRosterToAsync(roster, recipients, cancellationToken);
    }

    /// <summary>
    /// 名册下发：给 <paramref name="recipients"/> 各发一份按**他自己**区域替换的版本（内部薄包装
    /// <see cref="PublishRosterAsync"/> 传的就是当前成员），并顺带刷新"曾通知过的区域"里的旁观者。
    /// 收件人**不等于**名册成员（离队/被踢的人也要收一份"没有他"的新名册，
    /// 客户端靠它自我清理并提示"已退出队伍"，设计稿 §5.3/§5.4）。
    /// </summary>
    private async Task PublishRosterToAsync(
        PartyRoster roster,
        IReadOnlyList<Guid> recipients,
        CancellationToken cancellationToken)
    {
        List<(Guid CharacterId, GameServerPacket Packet)> direct = [];
        List<(byte TownId, byte AreaId, GameServerPacket Packet)> areas = [];
        lock (_gate)
        {
            foreach (var characterId in recipients)
            {
                direct.Add((characterId, BuildRosterPacket(roster, characterId)));
            }

            foreach (var (townId, areaId) in NotifiedAreas(roster))
            {
                areas.Add((townId, areaId, BuildRosterPacketForArea(roster, townId, areaId)));
            }
        }

        foreach (var (characterId, packet) in direct)
        {
            await SendAsync(characterId, packet, cancellationToken);
        }

        foreach (var (townId, areaId, packet) in areas)
        {
            await _directory.BroadcastPartyPacketAsync(townId, areaId, packet, cancellationToken);
        }
    }

    /// <summary>
    /// **锁内调用**：本队"曾经通知过的区域" ∪ 此刻有成员站着的区域，并把新出现的区域记进累积集。
    /// 目录是位置的权威：副本里/离开城镇的成员不在任何区域，只有此刻站在城镇区域的他才贡献区域。
    /// </summary>
    private List<(byte TownId, byte AreaId)> NotifiedAreas(PartyRoster roster)
    {
        _notifiedAreas.TryGetValue(roster.PartyId, out var registered);
        var areas = registered is null ? new List<(byte, byte)>() : [.. registered];
        foreach (var member in roster.Members)
        {
            if (_directory.TryGetSnapshot(member.CharacterId, out var snapshot)
                && !areas.Contains((snapshot.TownId, snapshot.AreaId)))
            {
                areas.Add((snapshot.TownId, snapshot.AreaId));
            }
        }

        // 只有"仍在册"的队伍才登记累积集：并发路径可能拿着已解散的名册出锁后才来发包，
        // 那时 partyId 可能已经归新队伍所有，残留条目会把新队伍的名册广播给上一支队伍的旧区域。
        if (_parties.TryGetValue(roster.PartyId, out var live) && ReferenceEquals(live, roster))
        {
            _notifiedAreas[roster.PartyId] = areas;
        }

        return [.. areas];
    }

    /// <summary>同一区域内所有收包者共用的那份名册（能解析到的成员填真实 uid，其余 0）。</summary>
    private GameServerPacket BuildRosterPacketForArea(PartyRoster roster, byte townId, byte areaId) =>
        GameProtocolEngine.CreatePartyInfoNotification(
            roster.PartyId,
            roster.NameSelection,
            roster.MaxMembers,
            roster.SlotUids(member =>
                _directory.TryGetSnapshot(member.CharacterId, out var snapshot)
                && snapshot.TownId == townId
                && snapshot.AreaId == areaId
                    ? snapshot.AreaUserId
                    : null),
            roster.LeaderSlot);

    /// <summary>
    /// 为某个收包者生成名册包（设计稿 §2.2.2）：同区域成员填真实 uid；收包者自己永远填自己的 uid
    /// （本地 user 表里必定有自己）；其余填 0（缺席哨兵）。**槽位下标全局一致（成员从不搬动）**。
    /// </summary>
    private GameServerPacket BuildRosterPacket(PartyRoster roster, Guid receiverCharacterId)
    {
        var receiverUid = _channels.TryGetValue(receiverCharacterId, out var receiver)
            ? receiver.AreaUserId
            : (ushort)0;
        var receiverInTown = _directory.TryGetSnapshot(receiverCharacterId, out var receiverTown);
        var slots = roster.SlotUids(member =>
        {
            if (member.CharacterId == receiverCharacterId)
            {
                return receiverUid;
            }

            if (!receiverInTown)
            {
                return null;
            }

            return _directory.TryGetSnapshot(member.CharacterId, out var memberTown)
                && memberTown.TownId == receiverTown.TownId
                && memberTown.AreaId == receiverTown.AreaId
                    ? memberTown.AreaUserId
                    : null;
        });
        return GameProtocolEngine.CreatePartyInfoNotification(
            roster.PartyId,
            roster.NameSelection,
            roster.MaxMembers,
            slots,
            roster.LeaderSlot);
    }

    /// <summary>
    /// 把角色从队伍里摘掉（退队 / 被踢 / 掉线共用）：
    /// 1) <paramref name="notice"/> 不是 <see cref="PartyLeaveNotice.Silent"/> 时**先**发 `NOTI 10`
    ///    （按**离开者当时的槽位**）——它的分支按槽位查成员对象取名字，`NOTI 9` 会把那个槽清成 0xFFFF
    ///    （B 卷 §4.1）；收件人是**离开者本人 + 其余成员**：被踢者自己也要看到"已被队长强行请出队伍"
    ///    （设计稿 §5.4），掉线路径里离开者已离线、给他的那份自然落空（设计稿 §5.5）；
    /// 2) 同一批人各收一份新名册（离开者那份的槽数组里没有他 → 客户端自我清理，设计稿 §5.3），
    ///    同时刷新"曾通知过的区域"的旁观者；
    /// 3) 队伍空了 → 销毁，并给"知道它的人"（成员 + 曾通知过的每个区域）发 kind=3。
    /// </summary>
    private async Task RemoveMemberFromPartyAsync(
        Guid characterId,
        PartyLeaveNotice notice,
        CancellationToken cancellationToken)
    {
        PartyRoster? roster;
        Guid[] recipients = [];
        List<(byte TownId, byte AreaId)> knownAreas = [];
        byte slot = 0;
        var dissolved = false;
        lock (_gate)
        {
            if (!TryGetPartyOf(characterId, out roster))
            {
                return;
            }

            // 收件人在 Remove **之前**取：离队者本人也要收 NOTI 10 和新名册（设计稿 §5.3/§5.4）。
            recipients = [.. roster.Members.Select(member => member.CharacterId)];
            // 解散时通知的区域也在 Remove **之前**取：离场者此刻站的区域也要清（否则那里的列表留脏行）。
            knownAreas = NotifiedAreas(roster);
            // NOTI 10 用的是**离队者的槽位下标**（稳定槽位：成员从不搬动，所以他此刻的槽下标就是
            // 名册里那一格）。取不到（名册与映射不一致的异常态）按 0 处理，与旧实现同形。
            slot = roster.TryGetSlot(characterId, out var memberSlot) ? memberSlot : (byte)0;
            if (roster.Remove(characterId))
            {
                _partyByCharacter.TryRemove(characterId, out _);
            }

            if (roster.Count == 0)
            {
                dissolved = true;
                _parties.TryRemove(roster.PartyId, out _);
                // 队伍没了，累积的区域集也要跟着走：partyId 会被复用，残留会把新队伍的名册发到旧区域。
                _notifiedAreas.Remove(roster.PartyId);
            }
        }

        if (roster is null)
        {
            return;
        }

        if (notice != PartyLeaveNotice.Silent && !dissolved)
        {
            var walkout = GameProtocolEngine.CreateWalkoutNotification(
                slot,
                disconnected: notice == PartyLeaveNotice.Disconnected);
            foreach (var recipient in recipients)
            {
                await SendAsync(recipient, walkout, cancellationToken);
            }
        }

        if (dissolved)
        {
            await BroadcastDisbandAsync(roster.PartyId, [], characterId, knownAreas, cancellationToken);
            _logger.LogInformation("Party {PartyId} dissolved.", roster.PartyId);
            return;
        }

        await PublishRosterToAsync(roster, recipients, cancellationToken);
        _logger.LogInformation(
            "Character {CharacterId} left party {PartyId}; {Count} member(s) remain.",
            characterId,
            roster.PartyId,
            roster.Count);
    }

    /// <summary>
    /// 解散：给离开者、剩余成员、以及**曾通知过的每个区域**的旁观者各发一条 kind=3
    /// （设计稿 §6.2.3）——旁观者收到后会把该队伍从本地队伍表摘除，否则列表里会留一支点不开的队伍。
    /// </summary>
    private async Task BroadcastDisbandAsync(
        ushort partyId,
        IReadOnlyList<Guid> remaining,
        Guid leavingCharacterId,
        IReadOnlyList<(byte TownId, byte AreaId)> knownAreas,
        CancellationToken cancellationToken)
    {
        var packet = GameProtocolEngine.CreatePartyDisbandNotification(partyId);
        await SendAsync(leavingCharacterId, packet, cancellationToken);
        foreach (var member in remaining)
        {
            await SendAsync(member, packet, cancellationToken);
        }

        foreach (var (townId, areaId) in knownAreas)
        {
            await _directory.BroadcastPartyPacketAsync(townId, areaId, packet, cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await SweepExpiredAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The party request sweep failed.");
            }
        }
    }

    /// <summary>
    /// 超时收尾：待决请求到期 → 给请求方 `CMD 11 [0,100,0]`。客户端的"拒绝"不发包（`0x821EA8`），
    /// 所以这是唯一能告诉请求方"对方没答应"的途径（与交易同构，设计稿 §5.2）。
    /// </summary>
    internal async Task SweepExpiredAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (targetCharacterId, request) in _pending)
        {
            if (now < request.ExpiresAt)
            {
                continue;
            }

            if (_pending.TryRemove(new KeyValuePair<Guid, PartyPendingRequest>(targetCharacterId, request)))
            {
                await SendRequestFailureAsync(request.RequesterCharacterId, cancellationToken);
            }
        }

        foreach (var (requesterCharacterId, expiresAt) in _blockedRequests)
        {
            if (now >= expiresAt
                && _blockedRequests.TryRemove(
                    new KeyValuePair<Guid, DateTimeOffset>(requesterCharacterId, expiresAt)))
            {
                await SendRequestFailureAsync(requesterCharacterId, cancellationToken);
            }
        }
    }

    /// <summary>取角色所在队伍。返回 true 时 <paramref name="roster"/> 必非 null（供锁内直接解引用）。</summary>
    private bool TryGetPartyOf(Guid characterId, [NotNullWhen(true)] out PartyRoster? roster)
    {
        roster = null;
        return _partyByCharacter.TryGetValue(characterId, out var partyId)
            && _parties.TryGetValue(partyId, out roster);
    }

    /// <summary>
    /// 目标解析：uid 只在**请求者所在区域**内有意义（客户端就是这么取的），
    /// 所以要求目标此刻与请求者同城镇同区域（设计稿 §5.1）。
    /// </summary>
    private bool TryResolveTarget(Guid requesterCharacterId, ushort targetUid, out PartyChannel target)
    {
        target = null!;
        if (!_directory.TryGetSnapshot(requesterCharacterId, out var requesterTown))
        {
            return false;
        }

        foreach (var candidate in _channels.Values)
        {
            if (candidate.CharacterId == requesterCharacterId || candidate.AreaUserId != targetUid)
            {
                continue;
            }

            if (!_directory.TryGetSnapshot(candidate.CharacterId, out var candidateTown)
                || candidateTown.TownId != requesterTown.TownId
                || candidateTown.AreaId != requesterTown.AreaId)
            {
                continue;
            }

            target = candidate;
            return true;
        }

        return false;
    }

    private PartyMember BuildMember(PartyChannel channel)
    {
        var townId = (byte)0;
        var areaId = (byte)0;
        if (_directory.TryGetSnapshot(channel.CharacterId, out var snapshot))
        {
            townId = snapshot.TownId;
            areaId = snapshot.AreaId;
        }

        return new PartyMember
        {
            CharacterId = channel.CharacterId,
            CharacterName = channel.CharacterName,
            AreaUserId = channel.AreaUserId,
            TownId = townId,
            AreaId = areaId
        };
    }

    private async Task<bool> IsBlacklistedAsync(
        string accountName,
        string characterName,
        CancellationToken cancellationToken)
    {
        var blacklist = await _store.GetBlacklistAsync(accountName, cancellationToken);
        // 客户端黑名单逐字节大小写敏感（沿用 ItemTradeService 的判据）。
        return blacklist.Any(entry => string.Equals(entry.Name, characterName, StringComparison.Ordinal));
    }

    private Task SendRequestFailureAsync(Guid characterId, CancellationToken cancellationToken) =>
        SendAsync(
            characterId,
            GameProtocolEngine.CreateResponsePeerError(RequestRefusedPeerType, 0),
            cancellationToken);

    private Task SendCommandErrorAsync(
        Guid characterId,
        byte commandId,
        byte errorCode,
        CancellationToken cancellationToken) =>
        SendAsync(characterId, GameProtocolEngine.CreatePartyCommandError(commandId, errorCode), cancellationToken);

    private async Task SendAsync(
        Guid characterId,
        GameServerPacket packet,
        CancellationToken cancellationToken)
    {
        if (!_channels.TryGetValue(characterId, out var channel))
        {
            return;
        }

        try
        {
            await channel.Send(packet);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException)
        {
            _logger.LogWarning(
                exception,
                "Could not deliver party packet {ProtocolId} to {CharacterId}.",
                packet.ProtocolId,
                characterId);
        }
    }
}
