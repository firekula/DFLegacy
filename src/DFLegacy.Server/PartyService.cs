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
/// 队伍槽位快照：槽位下标 + 该成员的通道发送句柄 + 队伍 id。
/// **只读**，不参与任何既有语义——组队副本的编排（<c>DungeonPartyService</c>）靠它把
/// 选图/入场请求投给队员，队员会话靠它取自己的槽位与队伍实例键。
/// </summary>
internal sealed record PartySlotHandle(
    int SlotIndex,
    Guid CharacterId,
    Guid SessionId,
    ushort AreaUserId,
    string CharacterName,
    Func<GameServerPacket, ValueTask> Send,
    ushort PartyId);

/// <summary>
/// NOTI 7 应答窗正文（dnf.str 4150：`Lv.%d%s [%s](复活币%d个、疲劳值%d点、虚弱%d%%)请求加入[%s]队伍`）
/// 里的三个显示值，由 <c>EntranceService</c> 在收到 CMD 10 时算好传入。三个值的口径：
/// <list type="bullet">
/// <item><c>ReviveCoins</c> = 请求者客户端自报的复活币个数（CMD 10 尾部的 u32，**原样透传**；
/// 真机上它显示的 0 是客户端自己算的）。</item>
/// <item><c>Fatigue</c> = **剩余疲劳** = 本会话口径的疲劳上限（含黑钻加成）− 已耗疲劳，下限 0 ——
/// 与客户端 HUD 疲劳条/`疲劳值%d点`同一口径（真机回归：旧实现填的是 `UsedFatigue` 已耗值）。</item>
/// <item><c>WeaknessPercent</c> = **虚弱%** = `FullStamina − 当前耐力值`（钳 0..100），耐力值取服务端
/// 发给该客户端的投影（`GetProjectedStaminaRecovery`，见 EntranceService）。</item>
/// </list>
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

    /// <summary>
    /// 待决请求的存活窗口：**等于客户端应答窗自己的存活时长（10 秒）**，不是设计稿原来写的 30 秒。
    /// 客户端点【否】**会**发包（11 字节带 `64 00`，G 卷 §2.2），走 <see cref="HandleResponseAsync"/> 的
    /// 拒绝分支；超时收尾针对的是"客户端什么都不发"的情形：决斗邀请（peerType 2/3）点否只关窗、
    /// 发起者下线/守卫失败静默关窗、**窗口自身计时器到点静默关窗**。
    /// <para>
    /// **为什么从 30 s 改成 10 s（真机实测，2026-10-23）**：那扇「是否接受组队」窗**不会**活到 30 秒。
    /// 实测（`measure-invite-window/`，两个独立方法结论一致）：
    /// ① 功能探针——邀请后 **+8 s** 点【是】服务端立刻广播 `Party roster … 2 member(s)`（窗口还活着）；
    /// **+12 s** 点【是】零名册、零 `protocol=11`（客户端窗口已消失，点了什么包都不发）。
    /// ② 1 Hz 桌面序列——第 8 帧（≈+10.0 s）窗还在、第 9 帧（≈+11.0 s）窗没了。
    /// 窗口比客户端活得久的那 20 秒里，待决项会**替一扇已经不存在的窗占位**：真机 20:01 那轮
    /// （`work/server-demo.log` 522/529/530/536）业主在 +15 s 重邀，就被 `_pending` 直接回绝
    /// （`TX protocol=11 payload=006400`），而他屏幕上的窗口早没了。
    /// 取值 10 s 而不是更小：窗口还开着时服务端不能先忘（否则目标点【是】会被静默丢掉）。
    /// </para>
    /// </summary>
    public static readonly TimeSpan RequestWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 名册补播的第一次延迟（相对发布时刻），第二次见 <see cref="SecondResettleDelay"/>。
    /// **为什么要补播**：客户端对**同一份**名册不重试——收到 NOTI 9 后它做两件事，任一件在"那一刻"
    /// 不成立就无声丢帧，且不会自己再来一次：①收尾两道门（`DNF.exe 0x41BDA8`：`[myUser+0xFC] != party` →
    /// 跳过整个 UI 收尾，HUD 逐槽、列表刷新、成员数组提交全在跳过范围内；`0x41BEA5`：`0x4FE850` 解析到的
    /// 成员数 < 2 → 跳过 HUD 组激活）；②逐槽填充用 `0x4FDDB0(userMgr, slotUid)` 解析，取不到 user 就
    /// **静默跳过该行**，永久丢到下一份名册为止（勘察见 party-protocol-recon-e-window-open.md §7、
    /// -f-partyui.md §「门控」）。
    /// 真机证据（2026-09-28）：A 邀请 B、B 接受那一步发出的 NOTI 9 双方都看不见（HUD 不激活、队伍信息
    /// 列表为空、推荐伙伴仍把队友当无队伍的人）；1 分多钟后由 CMD 12 触发的一份内容几乎相同（只有
    /// nameSel 0→1）的 NOTI 9 一到，队伍立刻出现 ⇒ 包本身没错，只是客户端在那一刻没渲染、也不会自己重试。
    /// 所以名册下发之后一律排两次补播：给客户端两个"重来一次"的机会（这是对客户端不重试的兜底）。
    /// 重复发同一份名册是安全的：客户端本来就收两份（定向 + 区域广播）。
    /// **覆盖范围**：①所有走 <see cref="PublishRosterToAsync"/> 的发布（接受、CMD 12 改名、退队/被踢、
    /// 离区）与 ②<see cref="OnAreaChangedAsync"/> 的换区/进区重播（它自己按区域 Build 包、不走上面那条
    /// 发布路径，在 <see cref="_gate"/> 内单独排期）。
    /// **边界**：补播只是"把同一份名册再发一遍"，治不了"客户端此刻根本解析不到某个成员 user"这类
    /// 持续性的缺失（例如对方迟迟没出现在他的本地 user 表里）——那属于时序上游的问题，本兜底不保证能救。
    /// </summary>
    private static readonly TimeSpan FirstResettleDelay = TimeSpan.FromSeconds(2);

    /// <summary>名册补播的第二次延迟（见 <see cref="FirstResettleDelay"/>）。</summary>
    private static readonly TimeSpan SecondResettleDelay = TimeSpan.FromSeconds(6);

    /// <summary>CMD 13 失败码 18：未组队（dnf.str 0x2CE）。本轮只发得出这一个 CMD 13 失败码。</summary>
    private const byte NotInPartyErrorCode = 18;
    /// <summary>CMD 14 失败码 4：不是队长（dnf.str 0x2D0）。</summary>
    private const byte NotLeaderErrorCode = 4;
    /// <summary>CMD 14 失败码 8：槽位里没有人（dnf.str 0x2D2）。</summary>
    private const byte MemberNotFoundErrorCode = 8;
    /// <summary>CMD 11 失败包的 peerType：100 且 code=0 → "对方不在线，或拒绝了您的组队请求"（0x7567）。</summary>
    private const byte RequestRefusedPeerType = 100;

    /// <summary>
    /// 出站名册的 nameSel 永远不会是 0：客户端只在 nameSel==0 时走"取文本 0x272（'无队伍名'）并写队伍名"
    /// 这条路径（0x41B933-0x41B95A），而真机证据表明 nameSel=0 的名册在被接受/补播时**完全无效**
    /// （零链接、无聊天行、推荐伙伴照旧），nameSel=1 的同样名册立刻生效。
    /// 所以自定义队名（客户端 sel=0）在出站时降级为预置名第 1 项——与"自定义队名不传播"的既有结论一致。
    /// </summary>
    private const byte DefaultPartyNameSelection = 1;

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
    /// 待补播表：partyId → 该队尚未到期的两个补播时刻（见 <see cref="FirstResettleDelay"/>）。
    /// **只在 <see cref="_gate"/> 内登记，且只有两处**：真正的发布点 <see cref="PublishRosterToAsync"/>
    /// （真发布）与换区/进区重播 <see cref="OnAreaChangedAsync"/>，都走 <see cref="ScheduleResettle"/>；
    /// 登记是覆盖式的（同一支队永远只留"最近一次下发"排的两个时刻，表不会随次数无界增长）；
    /// 补播自己走 `registerResettle: false` 的那条路，**绝不登记新项**——否则就成了无限循环。
    /// 队伍解散（<see cref="_parties.TryRemove"/> 那条路径）时随队伍一起清掉：partyId 会被复用，
    /// 残留项只会让新队伍白白多发一份名册。
    /// </summary>
    private readonly ConcurrentDictionary<ushort, ResettleSchedule> _resettleSchedule = new();

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
    /// 待决窗口的收尾路径（真机路径不会调用；生产代码靠真实时钟）。
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

    /// <summary>
    /// 测试用：把待补播项统统推到过期，好让 <see cref="SweepExpiredAsync"/> 覆盖到"补播时刻到了"的路径
    /// （真机路径不会调用；生产代码靠真实时钟等满 2 秒 / 6 秒）。
    /// </summary>
    internal void ExpireResettleForTests()
    {
        var expired = _timeProvider.GetUtcNow() - TimeSpan.FromSeconds(1);
        foreach (var (partyId, _) in _resettleSchedule)
        {
            _resettleSchedule[partyId] = new ResettleSchedule(expired, expired);
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
    /// **目标已有待决请求 → 先到者优先**：占位用一次比较交换原子完成，后来者立刻收同形的
    /// `CMD 11 [0,100,0]`（不用等窗口过期），目标正在看的那个窗口也不会被换掉；
    /// **但只有"还活着"的待决项有权拒绝**——已过期的（目标客户端的应答窗早已消失，见
    /// <see cref="RequestWindow"/>）必须被新请求顶掉，否则真机上会表现成"再次邀请直接被拒绝"；
    /// 被目标拉黑 → 只登记发起方（目标无感知，**不写** `_pending`），窗口过期后照常给发起方失败提示。
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
        // 接受方的 CMD 11 就永远对不上，请求方白等一整个窗口。
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

        // 队伍状态必须**在锁内**一次判完：名册是共享可变对象（成员会随并发退队/掉线被摘掉），
        // 判定结果在锁内落成 bool 快照（队长是谁、上限多少）再带出锁，出锁后只读这些 bool，
        // 不再碰名册本身——名册可以在两次读之间变空，出锁读到的就是撕裂的中间状态。
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

        // 被拉黑：目标完全无感知；但登记一个只属于发起方的到期项，让窗口过期后照常收到
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

        // Blacklist lookup above yields to other sessions. Reserve both participants only after
        // it completes, and retain the lease through the actual invitation send. PvP acquisition
        // uses the same registry gate, so an entrant cannot miss a concurrently created invite.
        using var admission = _store.Commerce.TryBeginPartyAdmission(requesterCharacterId, target.CharacterId);
        if (admission is null
            || !_channels.TryGetValue(requesterCharacterId, out var currentRequester) || currentRequester != requester
            || !_channels.TryGetValue(target.CharacterId, out var currentTarget) || currentTarget != target
            || !_directory.TryGetSnapshot(requesterCharacterId, out _)
            || !_directory.TryGetSnapshot(target.CharacterId, out _))
        {
            await SendRequestFailureAsync(requesterCharacterId, cancellationToken);
            return;
        }

        var pending = new PartyPendingRequest
        {
            RequesterCharacterId = requesterCharacterId,
            RequesterUid = requesterUid,
            ExpiresAt = now + RequestWindow
        };

        // 原子占位（比较交换）：**只有还活着的待决项才有权拒绝后来者**。
        // 先 TryGetValue 后 TryAdd 的写法在中间隔着 await（拉黑查询）时会放两个并发请求都通过判定；
        // 而"过期但还没被 1 Hz 扫描摘掉"的项必须被顶掉——真机 2026-10-23 20:01
        // （work/server-demo.log 522/529/530/536）就是这条缺口：被邀请者客户端的应答窗早就消失了
        // （它超时**不发包**，那轮零条 RX protocol=11），服务端待决项还在，第二次邀请撞上 TryAdd 失败
        // → 请求方立刻收 CMD 11 [0,100,0]、被邀请者一包都收不到（业主动报告："再尝试就直接返回拒绝了"）。
        if (!TryReservePending(target.CharacterId, pending, now, out var superseded, out var holder))
        {
            _logger.LogInformation(
                "Refused a party request from {Requester} to {Target}: {Holder} already has a live request open for another {Remaining} ms.",
                requester.CharacterName,
                target.CharacterName,
                DescribeCharacter(holder!.RequesterCharacterId),
                (holder.ExpiresAt - now).TotalMilliseconds);
            await SendRequestFailureAsync(requesterCharacterId, cancellationToken);
            return;
        }

        if (superseded is not null)
        {
            _logger.LogInformation(
                "Took over the invite slot of {Target}: the request from {StaleRequester} expired {Elapsed} ms ago, so the new request from {Requester} is pending.",
                target.CharacterName,
                DescribeCharacter(superseded.RequesterCharacterId),
                (now - superseded.ExpiresAt).TotalMilliseconds,
                requester.CharacterName);
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
    /// CMD 11（peerType=0）：应答。`accepted == false` 是小窗【否】——真机（G 卷 §2.2）里它**会发包**：
    /// 与【是】同前缀、尾部多一个 u16 0x0064（9 字节 vs 11 字节），由协议层判出。拒绝的收尾与超时 /
    /// 拉黑**完全同形**：待决项先被原子摘除、给请求方一条 `CMD 11 [0,100,0]`（"对方不在线，或拒绝了
    /// 您的组队请求"）、接受方零包，**不建队、不加人**（不得走到下面的建队逻辑）。
    /// 接受时四种情形按双方此刻的队伍状态决定（设计稿 §5.1）：接受者在队伍里 → 请求者并入接受者的队伍；
    /// 请求者在队伍里 → 接受者并入请求者的队伍；都没有 → 新建队伍（请求者为队长，partyId = 请求者 uid）；
    /// 接受者不是队长 / 队满 → 回失败。
    /// </summary>
    public async Task HandleResponseAsync(
        Guid accepterCharacterId,
        ushort requesterUid,
        bool accepted,
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

        // 小窗【否】：待决项已经在上面被原子摘除（超时收尾不会再补一条失败）。只给请求方一条与超时
        // 完全同形的 `CMD 11 [0,100,0]`，接受方零包，**绝不**往下走建队/并队。
        if (!accepted)
        {
            _logger.LogInformation(
                "{Accepter} refused the party request from {Requester}.",
                accepterCharacterId,
                pending.RequesterCharacterId);
            await SendRequestFailureAsync(pending.RequesterCharacterId, cancellationToken);
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

        // The pending request has already been consumed. Cancellation alone cannot protect this
        // interval: admission of BOTH players and the complete accepted/roster publication must
        // exclude the PvP ownership transition atomically, including asynchronous socket writes.
        using var admission = _store.Commerce.TryBeginPartyAdmission(pending.RequesterCharacterId, accepterCharacterId);
        if (admission is null
            || !_directory.TryGetSnapshot(pending.RequesterCharacterId, out _)
            || !_directory.TryGetSnapshot(accepterCharacterId, out _))
        {
            await SendRequestFailureAsync(accepterCharacterId, cancellationToken);
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
        if (!await TryLeavePartySilentlyAsync(characterId, cancellationToken))
        {
            await SendCommandErrorAsync(
                characterId,
                GameProtocolEngine.LeavePartyCommand,
                NotInPartyErrorCode,
                cancellationToken);
        }
    }

    /// <summary>
    /// "主动退队"（= CMD 13 成功分支）的最小入口：**不在队伍里返回 false 且一个包都不发**。
    /// 与 <see cref="HandleLeaveAsync"/> 的差别只有"没有请求包要回、所以不发失败码"这一条——
    /// 官服 CMD 45（副本内擅自退出，第 13 条）走的就是同一次 `CParty::leave_user(party, user, 2)`，
    /// 那条路径没有 CMD 13 的请求包，回失败码 18 只会给客户端一个它没问过的包。
    /// </summary>
    internal async Task<bool> TryLeavePartySilentlyAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        PartyRoster? roster;
        lock (_gate)
        {
            TryGetPartyOf(characterId, out roster);
        }

        if (roster is null)
        {
            return false;
        }

        await RemoveMemberFromPartyAsync(characterId, PartyLeaveNotice.Silent, cancellationToken);
        return true;
    }

    /// <summary>Called after the entrant owns Commerce.Pvp and before publishing PvP membership.
    /// Removes both invitation directions, then drains the ordinary party departure. The shared
    /// admission lease prevents an already-consumed response from crossing this transition.</summary>
    internal async Task PrepareForPvpAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        if (!_store.Commerce.Is(characterId, CommerceActivity.Pvp))
            throw new InvalidOperationException("PvP must reserve the entrant before cancelling party admission.");
        lock (_gate)
        {
            _pending.TryRemove(characterId, out _);
            _blockedRequests.TryRemove(characterId, out _);
            foreach (var (target, request) in _pending)
                if (request.RequesterCharacterId == characterId)
                    _pending.TryRemove(new KeyValuePair<Guid, PartyPendingRequest>(target, request));
        }
        await TryLeavePartySilentlyAsync(characterId, cancellationToken);
    }

    /// <summary>
    /// "擅自退出"专用的静默退队：**只在队里还有别人时才真的把他移出队伍**（用户 2026-09-30 真机更正）。
    /// 官方语义：退队后名册为空**不**解散——他仍是这支 1 人队的队长（`CParty::leave_user` 只有
    /// `IsSinglePlay` 那支才 `CParty::destroy`，玩家自建的 1 人队不走那条）；真机 2026-09-30 party26
    /// 的对照组就是"队长先擅自退出 → 队员成为 1 人队队长 → 队员再擅自退出时队伍不该消失"。
    /// 不在队里 / 只有自己 → 返回 false 且**一个包都不发**，队伍原样保留。
    /// （显式的 CMD 13 退队不走这里：那条按玩家意图退队，最后一人退队仍然解散。）
    /// </summary>
    internal async Task<bool> TryLeavePartySilentlyIfOthersRemainAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!TryGetPartyOf(characterId, out var roster) || roster is null || roster.Count <= 1)
            {
                return false;
            }
        }

        await RemoveMemberFromPartyAsync(characterId, PartyLeaveNotice.Silent, cancellationToken);
        return true;
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
    /// CMD 131：转交队长（载荷 = 槽位下标 0..3，B 卷 §4.3）。只有队长能转交、目标槽位必须有人且
    /// 不是自己；失败**静默**——客户端收不到 CMD 131 的任何回包（分发到默认空分支），设计上不给失败码。
    /// 成功后广播新名册（`leaderSlot` 变了），客户端用它驱动队长标记与"我是否队长"的 UI 切换。
    /// </summary>
    public async Task HandleChangeHostAsync(
        Guid leaderCharacterId,
        byte slot,
        CancellationToken cancellationToken = default)
    {
        PartyRoster? roster = null;
        string newLeaderName = string.Empty;
        string ignoredReason = string.Empty;
        lock (_gate)
        {
            // 校验与提升在**同一个锁内**一次做完（与 CMD 14 同款）：锁外读 `Leader`（= 队长槽里的成员）
            // 会撞上名册被清空而抛异常；槽位下标按稳定槽位解释，`TryPromoteLeader` 只改 LeaderSlot、
            // 不搬动任何成员（搬动会让客户端误判退队——"幽灵队伍"根因，见 PartyRoster 的 remarks）。
            if (!TryGetPartyOf(leaderCharacterId, out var party))
            {
                ignoredReason = "the requester is not in a party";
            }
            else if (party.Leader.CharacterId != leaderCharacterId)
            {
                ignoredReason = "the requester is not the party leader";
            }
            else if (slot == party.LeaderSlot)
            {
                ignoredReason = "the target slot is already the leader";
            }
            else if (!party.TryPromoteLeader(slot))
            {
                ignoredReason = "the target slot has no member";
            }
            else
            {
                roster = party;
                // 队长名在锁内取：出锁后并发 `Remove` 可能清空这个槽，锁外再读 `Leader` 会 NRE。
                newLeaderName = party.Leader.CharacterName;
            }
        }

        if (roster is null)
        {
            _logger.LogInformation(
                "CMD 131 change-host from {CharacterId} to slot {Slot} was ignored: {Reason}.",
                leaderCharacterId,
                slot,
                ignoredReason);
            return;
        }

        _logger.LogInformation(
            "Party {PartyId}: {CharacterId} handed the leadership to slot {Slot} ({NewLeader}) via CMD 131.",
            roster.PartyId,
            leaderCharacterId,
            slot,
            newLeaderName);
        await PublishRosterAsync(roster, cancellationToken);
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

        // CMD12 can create a one-person party even without an invitation.
        using var admission = _store.Commerce.TryBeginPartyAdmission(characterId);
        if (admission is null) return;

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
    /// （新区域版本里他是真实 uid；旧区域版本里他回落到**他自己的 uid**——**绝不填 0**，
    /// 真机证明 0 会被客户端当成"该成员已离开"→ 跨区域散队，见 `PartyRoster.SlotUids`）；③**进区补发**——把本区域现有队伍
    /// 逐支发给刚进来的这位，否则他的「队伍信息」列表是空的、推荐伙伴也过滤不准（设计稿 §6.2）。
    /// 已知边界：私密区域（包含五处赛丽亚房间）里每个成员各占一个实例，同 (城镇, 区域) 的
    /// "真实 uid"在对方的本地 user 表里解析不出来 → 那一行空白；私密区域里也无法组队
    /// （对方在他的实例里不可见，请求根本发不出）。
    /// 本路径自己 Build 包（按区域替换槽位 uid），不走 <see cref="PublishRosterToAsync"/>，
    /// 所以**补播要在这里单独排**（<see cref="ScheduleResettle"/>）：客户端在同一个门控上静默丢帧
    /// 且不重试，换区/进区重播的故障面与发布完全相同（真机 L7：回到同区域后队友行才恢复显示）。
    /// </summary>
    public async Task OnAreaChangedAsync(
        Guid characterId,
        byte townId,
        byte areaId,
        ushort areaUserId,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
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

                // 新区域这一份 + "曾通知过的区域"各一份：旧区域版本里他那一格回落到他自己的 uid
                // （**不是 0**：0 会被客户端当成"该成员已离开"→ 跨区域散队，见 `PartyRoster.SlotUids`；
                // 即使旧区域已经一个队员都不剩——它仍然"知道"这支队伍，设计稿 §5.6）。
                foreach (var (memberTownId, memberAreaId) in NotifiedAreas(roster))
                {
                    areas.Add((
                        memberTownId,
                        memberAreaId,
                        BuildRosterPacketForArea(roster, memberTownId, memberAreaId)));
                }

                ScheduleResettle(roster, now);
            }

            foreach (var visible in RostersVisibleIn(townId, areaId))
            {
                // 进区补发也一样：这份名册被客户端静默丢掉时，他的列表就一直空着（不会自己重试），
                // 所以涉及的每支队伍都排一次补播（幂等，代价 = 每次换区多两次名册）。
                ScheduleResettle(visible, now);
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
    /// 目录里已经没有他，"曾通知过的区域"收到的那一份里他那一格回落到**他自己的 uid**
    /// （**绝不填 0**：真机证明 0 会被客户端当成"该成员已离开"→ 散队，见 `PartyRoster.SlotUids`；设计稿 §5.6）。
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
    internal Task PublishRosterAsync(PartyRoster roster, CancellationToken cancellationToken) =>
        // recipients 交给 PublishRosterToAsync 在**锁内**现取（= 此刻的成员），避免锁外读名册快照。
        PublishRosterToAsync(roster, recipients: null, cancellationToken, registerResettle: true);

    /// <summary>
    /// 只读：该角色所在队伍的槽位快照（槽位下标 + 通道发送句柄）。不在队伍 → null。
    /// 槽位下标一律走 <see cref="PartyRoster.TryGetSlot"/>（成员从不搬动，下标就是权威值）。
    /// </summary>
    internal PartySlotHandle[]? GetPartySlots(Guid characterId)
    {
        if (!_partyByCharacter.TryGetValue(characterId, out var partyId)
            || !_parties.TryGetValue(partyId, out var roster))
        {
            return null;
        }

        List<PartySlotHandle> slots = [];
        lock (_gate)
        {
            foreach (var member in roster.Members)
            {
                // 槽位下标一律走 TryGetSlot（成员从不搬动，下标就是权威值）。
                if (!roster.TryGetSlot(member.CharacterId, out var slot)
                    || !_channels.TryGetValue(member.CharacterId, out var channel))
                {
                    continue;
                }

                slots.Add(new PartySlotHandle(
                    slot,
                    channel.CharacterId,
                    channel.SessionId,
                    channel.AreaUserId,
                    channel.CharacterName,
                    channel.Send,
                    roster.PartyId));
            }
        }

        return [.. slots];
    }

    /// <summary>
    /// 该角色所在队伍的**队长槽位**（0..3），不在队伍返回 null。
    /// 用途：NOTI 26 UDP_HOST 的载荷。官服实证（df_game_r `CParty::send_host_info` 反汇编）：
    /// 载荷 = 找到 `member[i] == party->host` 的那个**槽位号 i**，且**全队收到同一个值**——
    /// 即"主机（队长）的槽位"，不是收件人自己的槽位；客户端把它当 0..7 的槽位索引使用
    /// （DNF.exe 0x78D2B1 段：`mov esi,[eax+28h]; cmp esi,8; jge`）。
    /// 派发器只在 `CParty::game_start` 与 `change_manager*`（队长变更）里发这个包。
    /// </summary>
    internal byte? TryGetLeaderSlot(Guid characterId)
    {
        if (!_partyByCharacter.TryGetValue(characterId, out var partyId)
            || !_parties.TryGetValue(partyId, out var roster))
        {
            return null;
        }

        lock (_gate)
        {
            return roster.LeaderSlot;
        }
    }

    /// <summary>
    /// 按收包者生成组队名册包（收件人自己那份，槽位按他的区域解析）；不在队伍 → null。
    /// 用于「外观（USERINFO）→ 名册 → 副本报文」的补播顺序（客户端槽位是在收名册那一刻按 uid
    /// 查本地 user 表建链接的，记录晚到就建不出链接，与城镇同屏的"外观先于名单"同类）。
    /// </summary>
    internal GameServerPacket? GetRosterPacketFor(Guid characterId)
    {
        if (!_partyByCharacter.TryGetValue(characterId, out var partyId)
            || !_parties.TryGetValue(partyId, out var roster))
        {
            return null;
        }

        lock (_gate)
        {
            return BuildRosterPacket(roster, characterId);
        }
    }

    /// <summary>
    /// ACT1 队伍聊天发给当前全部队员（含自己），不按城镇区域或副本筛选。
    /// </summary>
    internal async Task<int> SendPartyChatAsync(
        Guid characterId,
        byte[] messageBytes,
        CancellationToken cancellationToken)
    {
        var slots = GetPartySlots(characterId);
        var sender = slots?.FirstOrDefault(slot => slot.CharacterId == characterId);
        if (sender is null) return 0;
        var packet = GameProtocolEngine.CreateMessageNotification(
            GameMessageType.Type02LightCyan, sender.AreaUserId, messageBytes);
        var sent = 0;
        foreach (var slot in slots!)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await slot.Send(packet);
                sent++;
            }
            catch (Exception exception) when (CrossSessionDelivery.IsBrokenChannel(exception))
            {
                _logger.LogWarning(exception, "Could not deliver party chat from {Sender} to {Recipient}.",
                    characterId, slot.CharacterId);
            }
        }
        return sent;
    }

    /// <summary>
    /// 把包发给队伍其他成员；单人时返回 0。坏通道不影响其他收件人。
    /// </summary>
    internal async Task<int> SendToOtherMembersAsync(
        Guid omitCharacterId,
        GameServerPacket packet,
        CancellationToken cancellationToken)
    {
        var slots = GetPartySlots(omitCharacterId);
        if (slots is null)
        {
            return 0;
        }

        var sent = 0;
        foreach (var slot in slots)
        {
            if (slot.CharacterId == omitCharacterId)
            {
                continue;
            }

            try
            {
                await slot.Send(packet);
                sent++;
            }
            catch (Exception exception) when (CrossSessionDelivery.IsBrokenChannel(exception))
            {
                _logger.LogWarning(
                    exception,
                    "Could not deliver party packet {ProtocolId} to {Recipient}.",
                    packet.ProtocolId,
                    CrossSessionDelivery.Describe(
                        slot.CharacterId, slot.CharacterName, slot.AreaUserId));
            }
        }

        return sent;
    }

    /// <summary>
    /// 名册下发：给 <paramref name="recipients"/> 各发一份按**他自己**区域替换的版本
    /// （<paramref name="recipients"/> = null 时在**锁内**取当前成员），并顺带刷新"曾通知过的区域"里的
    /// 旁观者。收件人**不等于**名册成员（离队/被踢的人也要收一份"没有他"的新名册，
    /// 客户端靠它自我清理并提示"已退出队伍"，设计稿 §5.3/§5.4）。
    /// 真发布（<paramref name="registerResettle"/> = true）在锁内排两次补播（见
    /// <see cref="FirstResettleDelay"/>）；补播自己（false）不再排期，否则无限循环。
    /// 名册成员/收件人**一律在锁内**取：`PartyRoster.Members` 先读 `Count` 再遍历槽数组，
    /// 锁外读会撞上并发的 `TryAdd`（越界写）或 `Remove`（数组尾部留 null → 枚举时 NRE）。
    /// </summary>
    private async Task PublishRosterToAsync(
        PartyRoster roster,
        IReadOnlyList<Guid>? recipients,
        CancellationToken cancellationToken,
        bool registerResettle)
    {
        var now = _timeProvider.GetUtcNow();
        List<(Guid CharacterId, GameServerPacket Packet)> direct = [];
        List<(byte TownId, byte AreaId, GameServerPacket Packet)> areas = [];
        Guid[] combatMembers;
        int memberCount;
        byte storedNameSelection;
        byte outboundNameSelection;
        lock (_gate)
        {
            // "名册此刻仍在册"（而不是 partyId 被同名的新队伍占用）：两种用途都要它——
            // 真发布只给在册的队伍排补播（不给陈旧引用留垃圾项）；补播则靠它拒绝把已解散队伍的名册
            // 再发一遍（客户端收到会把队伍重新加回本地列表）。
            var live = _parties.TryGetValue(roster.PartyId, out var current) && ReferenceEquals(current, roster);
            if (registerResettle)
            {
                if (live)
                {
                    ScheduleResettle(roster, now);
                }
            }
            else if (!live)
            {
                return;
            }

            // 人数与出站 nameSel 都在锁内取（与 members 同一把锁下的同一个名册）：并发改名时，
            // 锁外读 `NameSelection` 再归一化会打印**并非真正发出去的值**，而这行日志正是真机核对
            // nameSel 用的（`NormalizeNameSelection` 与 BuildRosterPacket 里的出站取值同源）。
            memberCount = roster.Count;
            // Recipients can include a departing player. Combat rebuilding is
            // restricted to this publication's actual, still-live members.
            combatMembers = live ? [.. roster.Members.Select(member => member.CharacterId)] : [];
            storedNameSelection = roster.NameSelection;
            outboundNameSelection = NormalizeNameSelection(roster.NameSelection);

            if (recipients is null)
            {
                recipients = [.. roster.Members.Select(member => member.CharacterId)];
            }

            foreach (var characterId in recipients)
            {
                direct.Add((characterId, BuildRosterPacket(roster, characterId)));
            }

            foreach (var (townId, areaId) in NotifiedAreas(roster))
            {
                areas.Add((townId, areaId, BuildRosterPacketForArea(roster, townId, areaId)));
            }
        }

        // 真机核对用：出站 nameSel 是这次修复的判据（0 从不出现，自定义名显示为 1）。与既有日志同风格；
        // 两个值都取自锁内局部变量（见上），保证与真正发出去的那份名册同值。
        _logger.LogInformation(
            "Party roster {PartyId} published: nameSel {StoredNameSelection}->{OutboundNameSelection}, {MemberCount} member(s).",
            roster.PartyId,
            storedNameSelection,
            outboundNameSelection,
            memberCount);

        foreach (var (characterId, packet) in direct)
        {
            await SendAsync(characterId, packet, cancellationToken);
        }

        foreach (var (townId, areaId, packet) in areas)
        {
            await _directory.BroadcastPartyPacketAsync(townId, areaId, packet, cancellationToken);
        }

        // NOTI 9 can delete foreign combat actors even while their town user
        // records survive. Rebuild after all direct/area rosters, using only
        // immutable cached details and current members, never observers.
        lock (_gate)
        {
            combatMembers = _parties.TryGetValue(roster.PartyId, out var current)
                && ReferenceEquals(current, roster)
                ? [.. combatMembers.Where(characterId =>
                    _partyByCharacter.TryGetValue(characterId, out var partyId)
                    && partyId == roster.PartyId)]
                : [];
        }
        bool IsCurrentMember(Guid characterId)
        {
            lock (_gate)
            {
                return _parties.TryGetValue(roster.PartyId, out var current)
                    && ReferenceEquals(current, roster)
                    && _partyByCharacter.TryGetValue(characterId, out var partyId)
                    && partyId == roster.PartyId;
            }
        }
        await _directory.ReplayPartyCombatDetailsAsync(combatMembers, cancellationToken, IsCurrentMember);
    }

    /// <summary>
    /// **锁内调用**：给这支队伍排补播——覆盖成"最近一次下发之后 2 秒 / 6 秒各一次"
    /// （为什么要补播见 <see cref="FirstResettleDelay"/>）。覆盖式 ⇒ 同一支队最多留两项，
    /// 表不随下发次数（发布 + 换区重播）无界增长；补播路径本身**不会**走到这里。
    /// </summary>
    private void ScheduleResettle(PartyRoster roster, DateTimeOffset now) =>
        _resettleSchedule[roster.PartyId] = new ResettleSchedule(
            now + FirstResettleDelay,
            now + SecondResettleDelay);

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

    /// <summary>
    /// **出站边界**把 nameSel 规范化（见 <see cref="DefaultPartyNameSelection"/>）：0（自定义名，也是
    /// 服务端建队时的初值）降级为预置名第 1 项。**存储不动**：<see cref="PartyRoster.NameSelection"/>
    /// 保留客户端给的原值，这样"自定义名不传播"只是出站策略，将来要改也不用动名册。
    /// </summary>
    private static byte NormalizeNameSelection(byte nameSelection) =>
        nameSelection == 0 ? DefaultPartyNameSelection : nameSelection;

    /// <summary>
    /// 同一区域内所有收包者共用的那份名册：能解析（与收包者同区域且在目录里）的成员填真实 uid；
    /// 解析不到 → 返回 null，由 <see cref="PartyRoster.SlotUids"/> 回落成**该成员自己的 uid**
    /// （**绝不填 0**——真机证明 0 会被客户端当成"该成员已离开"→ 跨区域散队，见 `PartyRoster.SlotUids`）。
    /// </summary>
    private GameServerPacket BuildRosterPacketForArea(PartyRoster roster, byte townId, byte areaId) =>
        GameProtocolEngine.CreatePartyInfoNotification(
            roster.PartyId,
            NormalizeNameSelection(roster.NameSelection),
            roster.MaxMembers,
            roster.SlotUids(member =>
                _directory.TryGetSnapshot(member.CharacterId, out var snapshot)
                && snapshot.TownId == townId
                && snapshot.AreaId == areaId
                    ? snapshot.AreaUserId
                    : null),
            roster.LeaderSlot);

    /// <summary>
    /// 为某个收包者生成名册包（设计稿 §2.2.2）：能解析（与收包者同区域且在目录里）的成员填真实 uid；
    /// 收包者自己永远填自己的 uid（本地 user 表里必定有自己）；解析不到 → 返回 null，由
    /// <see cref="PartyRoster.SlotUids"/> 回落成**该成员自己的 uid**（**绝不填 0**——真机证明 0 会被
    /// 客户端当成"该成员已离开"→ 跨区域散队，见 `PartyRoster.SlotUids`）。
    /// **槽位下标全局一致（成员从不搬动）**。
    /// </summary>
    private GameServerPacket BuildRosterPacket(PartyRoster roster, Guid receiverCharacterId)
    {
        // 收包者自己走通道里的当前 uid；通道不在（理论上的异常态：名册成员在 AttachChannelAsync 时必挂通道、
        // DetachChannelAsync 会同时退队）→ null，由 SlotUids 回落成名册里他自己的 uid，**绝不填 0**。
        var receiverUid = _channels.TryGetValue(receiverCharacterId, out var receiver)
            ? receiver.AreaUserId
            : (ushort?)null;
        var receiverInTown = _directory.TryGetSnapshot(receiverCharacterId, out var receiverTown);
        var slots = roster.SlotUids(member =>
        {
            if (member.CharacterId == receiverCharacterId)
            {
                return receiverUid;
            }

            // 解析不到（收包者不在城镇 / 成员与收包者不同区域 / 目录里查不到）→ null（槽位回落见上）。
            return receiverInTown
                && _directory.TryGetSnapshot(member.CharacterId, out var memberTown)
                && memberTown.TownId == receiverTown.TownId
                && memberTown.AreaId == receiverTown.AreaId
                    ? memberTown.AreaUserId
                    : null;
        });
        return GameProtocolEngine.CreatePartyInfoNotification(
            roster.PartyId,
            NormalizeNameSelection(roster.NameSelection),
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
        // 出锁后要用的名册数据（剩余人数）也在锁内取：出锁后一律不碰名册的可变成员
        // （`Members`/`Count` 都是"先读一个值、再遍历/推导"的复合读，锁外读会撞上并发增删）。
        var remainingCount = 0;
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
                // 队伍没了，累积的区域集与补播排期也要跟着走：partyId 会被复用，
                // 区域集残留会把新队伍的名册发到旧区域，补播残留只是让新队伍多发一份（同样不该留）。
                _notifiedAreas.Remove(roster.PartyId);
                _resettleSchedule.TryRemove(roster.PartyId, out _);
            }

            remainingCount = roster.Count;
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

        await PublishRosterToAsync(roster, recipients, cancellationToken, registerResettle: true);
        _logger.LogInformation(
            "Character {CharacterId} left party {PartyId}; {Count} member(s) remain.",
            characterId,
            roster.PartyId,
            remainingCount);
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
    /// 每秒扫描一次：①超时收尾——待决请求到期 → 给请求方 `CMD 11 [0,100,0]`。小窗【否】会发包
    /// （11 字节带尾 `64 00`，G 卷 §2.2），由 <see cref="HandleResponseAsync"/> 立刻回同样的失败包；
    /// 超时只兜底三种**静默**情形：决斗（peerType 2/3）点【否】不发包、请求方在窗口内离线/守卫失败、
    /// 窗口计时器到点静默关窗（设计稿 §5.2）；
    /// ②名册补播——发布时排的两个时刻到点就重发一份名册（对客户端"对同一份名册不重试"的兜底，
    /// 见 <see cref="FirstResettleDelay"/>）。**补播走 `registerResettle: false`**：它自己不再排期，
    /// 否则每轮扫描都会把补播续下去，永远停不下来。
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
                // 这条日志是**必须**的：真机 2026-10-23 20:01 那轮里，超时收尾只留下一条
                // `TX protocol=11 payload=006400`，没有任何业务行，业主因此以为"服务端没有处理"。
                _logger.LogInformation(
                    "The party request from {Requester} to {Target} timed out with no answer after {Seconds:F0} s; the requester was told it failed.",
                    DescribeCharacter(request.RequesterCharacterId),
                    DescribeCharacter(targetCharacterId),
                    RequestWindow.TotalSeconds);
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

        // 补播：到点的 partyId 重发一份**当前**名册（完整发布：成员定向 + 区域广播）。
        // 先摘/推进待补播项再发包：一轮扫描里同一支队最多补播一次（名册幂等，客户端本来就收两份），
        // 而且摘项失败（期间有新的一次发布换掉了它）就跳过本轮——那一份自己会重新排期。
        List<PartyRoster> resettle = [];
        foreach (var (partyId, schedule) in _resettleSchedule)
        {
            if (now < schedule.First)
            {
                continue;
            }

            PartyRoster? roster;
            lock (_gate)
            {
                _parties.TryGetValue(partyId, out roster);
            }

            // 已解散 → 整个待补播项清掉（第二次也不补了，避免无界增长）；第二次也到点 → 摘掉整项；
            // 否则推进到第二次。
            if (roster is null || now >= schedule.Second)
            {
                if (!_resettleSchedule.TryRemove(new KeyValuePair<ushort, ResettleSchedule>(partyId, schedule)))
                {
                    continue;
                }
            }
            else if (!_resettleSchedule.TryUpdate(partyId, schedule with { First = schedule.Second }, schedule))
            {
                continue;
            }

            if (roster is not null)
            {
                resettle.Add(roster);
            }
        }

        foreach (var roster in resettle)
        {
            // recipients 传 null：收件人（此刻的成员）由 PublishRosterToAsync **在锁内**取。
            // 别在这里先取快照再传进去——那是锁外读 `Members`：它先读 Count 再遍历槽数组，
            // 并发 `TryAdd` 会让写入越界（IndexOutOfRangeException）、并发 `Remove` 会在数组尾部
            // 留 null（枚举时 NRE），而异常被 ExecuteAsync 吞掉记日志后，这一轮已经摘掉/推进的补播项
            // 不会重来——丢的恰好是这条兜底本身。
            await PublishRosterToAsync(roster, recipients: null, cancellationToken, registerResettle: false);
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
    /// 占住目标的待决槽位（一个原子操作）。**只有还活着的待决项有权拒绝后来者**：已过期但还没被
    /// 1 Hz 扫描摘掉的项必须被新请求顶掉（见 <see cref="HandleRequestAsync"/> 的注释与
    /// <see cref="RequestWindow"/> 的真机依据）。用比较交换（<c>TryUpdate</c>）而不是"先删后加"：
    /// 两个并发请求不会双双通过，也不会互相删掉对方刚写进去的项。
    /// </summary>
    /// <param name="superseded">被顶掉的那条**已过期**请求（没有则为 null），只用于日志。</param>
    /// <param name="holder">占着槽位的那条**还活着**的请求（拒绝时用于日志）。</param>
    /// <returns>true = 本次请求已占位（可能顶掉了一条过期项）；false = 有活着的前者，调用方回失败。</returns>
    private bool TryReservePending(
        Guid targetCharacterId,
        PartyPendingRequest pending,
        DateTimeOffset now,
        out PartyPendingRequest? superseded,
        out PartyPendingRequest? holder)
    {
        superseded = null;
        holder = null;
        while (true)
        {
            if (!_pending.TryGetValue(targetCharacterId, out var existing))
            {
                if (_pending.TryAdd(targetCharacterId, pending))
                {
                    return true;
                }

                // 竞态：另一条请求刚好插进来 → 回到开头重新判定（它可能也已过期）。
                continue;
            }

            if (now < existing.ExpiresAt)
            {
                holder = existing;
                return false;
            }

            if (_pending.TryUpdate(targetCharacterId, pending, existing))
            {
                superseded = existing;
                return true;
            }
        }
    }

    /// <summary>日志用：优先给角色名，通道已摘除时退回角色 id（掉线/换角色后日志仍可追）。</summary>
    private string DescribeCharacter(Guid characterId) =>
        _channels.TryGetValue(characterId, out var channel) ? channel.CharacterName : characterId.ToString();

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

    /// <summary>
    /// 一支队伍的补播排期：两个尚未到期的补播时刻（第一次、第二次）。值类型 + 整体替换（而不是原地改）
    /// 是为了让 <see cref="SweepExpiredAsync"/> 的"摘掉/推进"能用 <c>TryRemove/TryUpdate</c> 的
    /// 比较交换语义与"期间新发布的一次登记"互不踩踏。
    /// </summary>
    private readonly record struct ResettleSchedule(DateTimeOffset First, DateTimeOffset Second);

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
