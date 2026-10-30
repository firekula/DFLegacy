using System.Collections.Concurrent;
using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal enum ItemTradeSessionState
{
    Requested,
    Open,
    Settling
}

/// <summary>一条在线角色通道：会话建立时登记，拆除时摘除（沿用 FriendService 的模式）。</summary>
internal sealed class ItemTradeChannel
{
    public required string AccountName { get; init; }
    public required string CharacterName { get; init; }
    public required Guid CharacterId { get; init; }
    public required ushort AreaUserId { get; init; }
    public required Guid SessionId { get; init; }
    public required Func<GameServerPacket, ValueTask> Send { get; init; }
}

internal sealed class ItemTradeSide
{
    public required ItemTradeChannel Channel { get; init; }
    public required bool IsInitiator { get; init; }
    public List<ItemTradeEscrow> Escrows { get; } = [];
    public int OfferedGold { get; set; }
    public bool Locked { get; set; }
    public bool FinalConfirmed { get; set; }

    /// <summary>
    /// 本侧客户端在 CMD 10 / CMD 11 里带来的 u32＝「当前负重 − 负重上限」（克，正常为负，二补码）。
    /// 它必须原样回传给另一方（NOTI 7 给被请求方、NOTI 8 给请求方）：客户端把它存进
    /// <c>CNUser+0xF4</c>，锁定时的负重自检（0x4623A0）就是 <c>该值 + 对方面板重 − 我方面板重 &gt;= 0</c>。
    /// 服务端不自行换算——客户端用自己的重量模型算，换算尺度与 wire 单位不一定一致
    /// （trade-weight-check-recon §5.1）。
    /// </summary>
    public uint WeightDelta { get; set; }
}

internal sealed class ItemTradeSession
{
    public required Guid TradeId { get; init; }
    public required ItemTradeSide Initiator { get; init; }
    public required ItemTradeSide Partner { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public ItemTradeSessionState State { get; set; } = ItemTradeSessionState.Requested;
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>
    /// 会话状态迁移的互斥对象：锁定广播、最终确认的「双方都确认 → 抢占 Settling」与取消的
    /// 「结算在途不得插手」判定都必须在同一把锁里完成（锁内全同步、不含 await）。
    /// </summary>
    public object Sync { get; } = new();

    public ItemTradeSide SideOf(Guid characterId) =>
        Initiator.Channel.CharacterId == characterId ? Initiator : Partner;

    public ItemTradeSide OtherOf(Guid characterId) =>
        Initiator.Channel.CharacterId == characterId ? Partner : Initiator;
}

public sealed record ItemTradePendingInstall(Guid TradeId, CharacterRecord State);

public sealed record ItemTradeStateOutcome(bool SettlementApplied, CharacterRecord? ReplacementState);

/// <summary>
/// 交易会话的注册与编排。所有服务端→客户端的交易包都由本服务发出（操作者回显 + 对端通知
/// 成对发出，避免两条路径各写一半）。会话身份依赖连接（命令里没有 uid），托管一律以服务端
/// 自己的 (栏,格) 记录为准。
/// </summary>
public sealed class ItemTradeService : BackgroundService
{
    /// <summary>服务端独立限流（客户端 CMD 10 的 u32 是负重差、不承担防抖）；避免被单包装满。</summary>
    public const int MinimumRequestIntervalMilliseconds = 3_000;

    public static readonly TimeSpan RequestWindow = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan IdleWindow = TimeSpan.FromMinutes(5);

    private readonly JsonGameStore _store;
    private readonly TownSessionDirectory _townDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ItemTradeService> _logger;

    // 这两本目录 Task 4 的路径还用不到：放入/取回（Task 5）与结算（Task 6）才读它们。
    // 构造签名先按最终形态定下来（Task 5/6 依赖），字段在此一并留好。
    private readonly ItemCatalog _itemCatalog;
    private readonly CharacterStatCatalog _statCatalog;

    private readonly ConcurrentDictionary<Guid, ItemTradeChannel> _channels = new();
    private readonly ConcurrentDictionary<Guid, ItemTradeSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastRequestAt = new();
    private readonly ConcurrentDictionary<Guid, ItemTradePendingInstall> _pendingInstalls = new();

    public ItemTradeService(
        JsonGameStore store,
        ItemCatalog itemCatalog,
        CharacterStatCatalog statCatalog,
        TownSessionDirectory townDirectory,
        TimeProvider timeProvider,
        ILogger<ItemTradeService> logger)
    {
        _store = store;
        _itemCatalog = itemCatalog;
        _statCatalog = statCatalog;
        _townDirectory = townDirectory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsOnline(Guid characterId) => _channels.ContainsKey(characterId);

    /// <summary>测试用：清掉 CMD 10 的服务端限流时间戳（真机路径不会调用）。</summary>
    internal void ResetRequestThrottleForTests(Guid characterId) =>
        _lastRequestAt.TryRemove(characterId, out _);

    public async Task AttachChannelAsync(
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
        // 顶号/重登：旧连接可能还挂着一笔会话（旧通道已消失、它自己的拆除步骤未必跑到）。
        // 不在这里取消的话，该角色会一直"忙"到 5 分钟扫描，旧槽位也一直冻结。
        // 旧通道被当作已离场（不给他发包），存活方照常拿 NOTI 16。
        if (_sessions.ContainsKey(characterId)
            && (!_channels.TryGetValue(characterId, out var previous)
                || previous.SessionId != sessionId))
        {
            await CancelSessionForCharacterAsync(characterId, characterId, cancellationToken);
        }

        _channels[characterId] = new ItemTradeChannel
        {
            AccountName = accountName,
            CharacterName = characterName,
            CharacterId = characterId,
            AreaUserId = areaUserId,
            SessionId = sessionId,
            Send = send
        };
    }

    /// <summary>
    /// 角色会话结束（返回选人/换角色/背包修复失败/断线）：结束在途交易并通知对手，
    /// 然后摘除通道。会话 id 不匹配的陈旧清理完全不动作。
    /// </summary>
    public async Task DetachChannelAsync(
        Guid characterId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(characterId, out var channel)
            || channel.SessionId != sessionId
            || !_channels.TryRemove(new KeyValuePair<Guid, ItemTradeChannel>(characterId, channel)))
        {
            return;
        }

        _pendingInstalls.TryRemove(characterId, out _);
        // 带着「未安装交付」掉线的角色如果不清掉 store 的守门标记，之后所有盲写保存都会拒绝他。
        _store.ClearTradeDeliveryPending(characterId);
        _lastRequestAt.TryRemove(characterId, out _);
        if (_sessions.TryGetValue(characterId, out var session)
            && session.State == ItemTradeSessionState.Settling
            && !IsOnline(session.Initiator.Channel.CharacterId)
            && !IsOnline(session.Partner.Channel.CharacterId))
        {
            // 兜底：结算在途时双方通道都已消失，正常守卫不会让任何取消路径插手，会话会永远留在
            // _sessions 里（按 characterId 键控，重登也带着它）。两侧都不在线时强制收尾，绝不在
            // 还有一侧在线时插手（那一侧的结算/拆除路径会自己处理）。拆除路径的 token 可能已取消，
            // 通知一律尽力而为——会话摘除在锁内同步完成，不受影响。
            await BestEffortAsync(() => AbortSettlementAsync(session, cancellationToken));
            return;
        }

        await CancelSessionForCharacterAsync(characterId, characterId, cancellationToken);
    }

    /// <summary>
    /// 进副本 / 换区 / 离开城镇：客户端此时仍在线，双方都要收到取消与退回，
    /// 所以这里不把发起者当作「已离场」——<see cref="DetachChannelAsync"/> 才是那样。
    /// </summary>
    public Task CancelForAreaChangeAsync(Guid characterId, CancellationToken cancellationToken = default) =>
        CancelSessionForCharacterAsync(characterId, absentCharacterId: null, cancellationToken);

    public async Task HandleRequestAsync(
        Guid characterId,
        ushort targetAreaUserId,
        uint weightDelta,
        CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(characterId, out var requester))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (_lastRequestAt.TryGetValue(characterId, out var lastRequest)
            && now - lastRequest < TimeSpan.FromMilliseconds(MinimumRequestIntervalMilliseconds))
        {
            _logger.LogInformation(
                "Rate-limited a trade request from {CharacterId}: the previous one is {Elapsed} ms old.",
                characterId,
                (now - lastRequest).TotalMilliseconds);
            return;
        }

        _lastRequestAt[characterId] = now;

        // 客户端只能右键到同区可见的人，服务端仍独立判定：双方都在同一城镇同一区域，
        // 且都不在交易中（含等待应答的 Requested）。
        if (_sessions.ContainsKey(characterId))
        {
            // 已在会话中（含 Requested）：静默丢弃，既不给自己回包也不打扰对手。
            return;
        }

        if (!_townDirectory.TryGetSnapshot(characterId, out var requesterTown))
        {
            // 设计 §2.4：请求方不在城镇区域（刚进副本 / 已被移出区域目录）→ CMD 11 [0,0x64,1]。
            await SendRequestFailureAsync(requester, cancellationToken);
            return;
        }

        var target = _channels.Values.FirstOrDefault(candidate =>
            candidate.AreaUserId == targetAreaUserId && candidate.CharacterId != characterId);
        if (target is null)
        {
            await SendRequestFailureAsync(requester, cancellationToken);
            return;
        }

        if (_sessions.ContainsKey(target.CharacterId)
            || !_townDirectory.TryGetSnapshot(target.CharacterId, out var targetTown)
            || targetTown.TownId != requesterTown.TownId
            || targetTown.AreaId != requesterTown.AreaId)
        {
            await SendRequestFailureAsync(requester, cancellationToken);
            return;
        }

        // 被对方拉黑：对目标完全静默（与悄悄话的既有策略一致），但"静默"只是其中一半——
        // 设计 §2.6 的另一半是「请求方 30 秒后收到失败提示」，而且必须与普通「对方没理」
        // 完全同形，否则"立刻就能重发"会成为拉黑的旁证。于是：
        //   ① 不发 NOTI 7：目标无感知；
        //   ② 只登记发起方的键（不登记目标）：不占用目标的交易名额，"同时只能一笔"不会
        //      把目标自己发起的请求挡掉；
        //   ③ 会话照建：30 秒扫描照常触发 Requested 分支，只给发起方回 CMD 11 [0,100,1]。
        var blocked = await IsBlacklistedAsync(
            target.AccountName,
            requester.CharacterName,
            cancellationToken);

        var session = new ItemTradeSession
        {
            TradeId = Guid.NewGuid(),
            Initiator = new ItemTradeSide
            {
                Channel = requester,
                IsInitiator = true,
                WeightDelta = weightDelta
            },
            Partner = new ItemTradeSide
            {
                Channel = target,
                IsInitiator = false
            },
            CreatedAt = now,
            LastActivityAt = now
        };
        var activityIds = blocked ? new[] { requester.CharacterId } : new[] { requester.CharacterId, target.CharacterId };
        if (!_store.Commerce.TryAcquire(CommerceActivity.Trade, session.TradeId, activityIds))
        {
            await SendRequestFailureAsync(requester, cancellationToken);
            return;
        }
        _sessions[requester.CharacterId] = session;
        if (blocked)
        {
            _logger.LogInformation(
                "Silently dropped a trade request from {Requester} to {Target}: the target's blacklist holds the requester.",
                requester.CharacterName,
                target.CharacterName);
            return;
        }

        _sessions[target.CharacterId] = session;
        await SendAsync(
            session.Partner,
            // 被请求方要拿请求方的「当前负重 − 负重上限」来判断锁定后请求方会不会超重：
            // 原样透传 CMD 10 的 u32（旧实现回传 requester.AreaUserId 正是真机 1 克假报错的根因）。
            GameProtocolEngine.CreateRequestPeerNotification(
                session.Initiator.Channel.AreaUserId,
                session.Initiator.WeightDelta),
            cancellationToken);
        _logger.LogInformation(
            "Trade {TradeId}: {Requester} requested {Target} (weight delta 0x{WeightDelta:X8}).",
            session.TradeId,
            requester.CharacterName,
            target.CharacterName,
            session.Initiator.WeightDelta);
    }

    public async Task HandleResponseAsync(
        Guid characterId,
        ushort requesterAreaUserId,
        bool accept,
        uint weightDelta,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(characterId, out var session)
            || session.State != ItemTradeSessionState.Requested
            || session.Partner.Channel.CharacterId != characterId
            || session.Initiator.Channel.AreaUserId != requesterAreaUserId)
        {
            return;
        }

        if (!accept || !IsOnline(session.Initiator.Channel.CharacterId))
        {
            await CancelSessionAsync(session, absentCharacterId: null, cancellationToken);
            return;
        }

        session.Partner.WeightDelta = weightDelta;
        session.State = ItemTradeSessionState.Open;
        session.LastActivityAt = _timeProvider.GetUtcNow();
        await SendAsync(
            session.Initiator,
            // 请求方同样要用接受方的「当前负重 − 负重上限」做锁定自检：原样透传 CMD 11 的 u32
            // （旧实现填 0 会让请求方永远算出 1 克假缺额，锁不上）。
            GameProtocolEngine.CreateResponsePeerNotification(
                session.Partner.Channel.AreaUserId,
                session.Partner.WeightDelta,
                mode: 0),
            cancellationToken);
        await SendAsync(
            session.Partner,
            GameProtocolEngine.CreateResponsePeerReply(
                session.Initiator.Channel.AreaUserId,
                mode: 0),
            cancellationToken);
        _logger.LogInformation(
            "Trade {TradeId} opened between {Initiator} and {Partner} (accepter weight delta 0x{WeightDelta:X8}).",
            session.TradeId,
            session.Initiator.Channel.CharacterName,
            session.Partner.Channel.CharacterName,
            session.Partner.WeightDelta);
    }

    /// <summary>
    /// CMD 26：0/1 = 取消/设置锁定并给双方广播 NOTI 17，2 = 取消交易，3 = 最终确认
    /// （只有双方都锁定才接受，双方都确认才由后确认者结算）。
    /// </summary>
    public async Task<ItemTradeStateOutcome> HandleStateAsync(
        Guid characterId,
        byte state,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(characterId, out var session))
        {
            return new ItemTradeStateOutcome(false, null);
        }

        if (state == 3)
        {
            return await ConfirmAsync(session, characterId, cancellationToken);
        }

        if (session.State != ItemTradeSessionState.Open)
        {
            return new ItemTradeStateOutcome(false, null);
        }

        var mine = session.SideOf(characterId);
        session.LastActivityAt = _timeProvider.GetUtcNow();
        switch (state)
        {
            case 0:
            case 1:
            {
                var locked = state == 1;
                lock (session.Sync)
                {
                    // 取消/超时/换区/掉线会在同一把锁内把会话从 _sessions 摘掉：持旧引用的
                    // 锁定/解锁不得再给一个已经关闭的窗口广播 NOTI 17。
                    if (session.State != ItemTradeSessionState.Open || !IsStillRegistered(session))
                    {
                        return new ItemTradeStateOutcome(false, null);
                    }

                    mine.Locked = locked;
                    mine.FinalConfirmed = false;
                }

                foreach (var target in new[] { session.Initiator, session.Partner })
                {
                    await SendAsync(
                        target.Channel.CharacterId,
                        GameProtocolEngine.CreateStateItemTrade(mine.Channel.AreaUserId, locked),
                        cancellationToken);
                }

                return new ItemTradeStateOutcome(false, null);
            }

            case 2:
                await CancelSessionAsync(session, absentCharacterId: null, cancellationToken);
                return new ItemTradeStateOutcome(false, null);

            default:
                // CMD 26 的 state 4（0x7C9C90）语义未定（勘察 §7.1 / 设计稿 T2）：忽略并记日志。
                _logger.LogInformation(
                    "Ignored CMD 26 state {State} in trade {TradeId}.",
                    state,
                    session.TradeId);
                return new ItemTradeStateOutcome(false, null);
        }
    }

    /// <summary>
    /// CMD 26 = 3。整段状态判定与迁移在 <see cref="ItemTradeSession.Sync"/> 内原子完成：
    /// 双方都锁定、本人尚未确认过，最后一名确认者把会话抢占为 <c>Settling</c> 并在锁外发起结算。
    /// 并发确认因此只有一个调用者能进入 <see cref="SettleAsync"/>（不会重复交付/重复转账），
    /// 落败方只会看到结算已开始。
    /// </summary>
    private async Task<ItemTradeStateOutcome> ConfirmAsync(
        ItemTradeSession session,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var mine = session.SideOf(characterId);
        var other = session.OtherOf(characterId);
        var settle = false;
        var rejected = false;
        lock (session.Sync)
        {
            if (session.State != ItemTradeSessionState.Open)
            {
                return new ItemTradeStateOutcome(false, null);
            }

            // 取消/跑单会在同一把锁内把会话从 _sessions 摘掉：持旧引用的确认必须在锁内重新确认
            // 自己仍是在册会话，否则会出现「客户端已被通知取消、服务端却仍然结算」。
            if (!IsStillRegistered(session))
            {
                return new ItemTradeStateOutcome(false, null);
            }

            session.LastActivityAt = _timeProvider.GetUtcNow();
            // 设计 §2.4：只有双方都已锁定才接受确认；同一侧重复确认不得重设标志或重复结算。
            if (!mine.Locked || !other.Locked || mine.FinalConfirmed)
            {
                rejected = true;
            }
            else
            {
                mine.FinalConfirmed = true;
                if (other.FinalConfirmed)
                {
                    session.State = ItemTradeSessionState.Settling;
                    settle = true;
                }
            }
        }

        if (settle)
        {
            return await SettleAsync(session, mine, cancellationToken);
        }

        if (rejected)
        {
            await SendAsync(
                characterId,
                GameProtocolEngine.CreateSetItemTradeStateResult(success: false, errorCode: 1),
                cancellationToken);
        }

        return new ItemTradeStateOutcome(false, null);
    }

    private async Task<ItemTradeStateOutcome> SettleAsync(
        ItemTradeSession session,
        ItemTradeSide actor,
        CancellationToken cancellationToken)
    {
        // 会话已在 ConfirmAsync 的锁内抢占为 Settling：结算期间不再接受托管变动或取消；
        // 确认瞬间对方掉线就按取消处理，绝不把货发给已离开的会话。
        // 这里必须走结算旁路：会话此刻是 Settling，被守卫的 CancelSessionAsync 会直接返回，
        // 会话既不摘除也不通知（SweepExpired 也清不掉），两个角色会永久卡在同一笔交易里。
        if (!IsOnline(session.Initiator.Channel.CharacterId)
            || !IsOnline(session.Partner.Channel.CharacterId))
        {
            await AbortSettlementAsync(session, cancellationToken);
            return new ItemTradeStateOutcome(false, null);
        }

        // 提交 + 双方发包 + 登记待安装/ack 整段兜底：磁盘 IO 失败、确认瞬间被顶号/停服导致
        // token 已取消、提交后的发包被连接 token 取消——任何一种逃出去都会把会话永久留在
        // Settling（所有退出路径都被守卫拦着），对手收不到 NOTI 16/18、两个角色永久"已在交易中"、
        // 托管物品在所有冻结路径上永久冻结。这里无论成败都先收尾，再决定是否重新抛出。
        var committed = false;
        ItemTradeCommitResult? commit = null;
        try
        {
            commit = await _store.CommitItemTradeAsync(
                new ItemTradeCommitRequest(
                    session.Initiator.Channel.CharacterId,
                    session.Initiator.Channel.AccountName,
                    session.Partner.Channel.CharacterId,
                    session.Partner.Channel.AccountName,
                    [.. session.Initiator.Escrows],
                    [.. session.Partner.Escrows],
                    session.Initiator.OfferedGold,
                    session.Partner.OfferedGold),
                cancellationToken);
            if (!commit.Committed
                || commit.Initiator is null
                || commit.Partner is null
                || commit.Plan is null)
            {
                _logger.LogWarning(
                    "Trade {TradeId} settlement failed: {Failure}; the whole trade is aborted.",
                    session.TradeId,
                    commit.Failure);
                await SendAsync(
                    actor.Channel.CharacterId,
                    GameProtocolEngine.CreateSetItemTradeStateResult(
                        success: false,
                        errorCode: commit.Failure is ItemTradeCommitFailure.NoSpace
                            or ItemTradeCommitFailure.WeightExceeded
                                ? (byte)4
                                : (byte)1),
                    cancellationToken);
                await AbortSettlementAsync(session, cancellationToken);
                return new ItemTradeStateOutcome(false, null);
            }

            committed = true;
            var actorIsInitiator = session.Initiator.Channel.CharacterId == actor.Channel.CharacterId;
            var passiveSide = actorIsInitiator ? session.Partner : session.Initiator;
            var passiveRecord = actorIsInitiator ? commit.Partner : commit.Initiator;
            // 待安装登记必须在 NOTI 18 之前：被动方收到 NOTI 18 后的第一条命令就要能先安装
            // 新状态，而不是先被守门拒绝一次再自愈。
            _pendingInstalls[passiveSide.Channel.CharacterId] =
                new ItemTradePendingInstall(commit.TradeId, passiveRecord);
            // 上面「双方在线」复核到这次登记之间，被动方可能刚好掉线：它的拆除清理
            // 已经跑完，既不会消费这条登记，也不会清掉 store 里刚打上的交付守门标记。再查一次
            // 在线状态，离线就撤销登记并清标记——否则这个角色的下一次盲写（含选角时的背包修复）
            // 会被守门永久拒绝，锁死到重启。结算本身仍然有效：存档已提交，他重登读到的就是结果。
            if (!IsOnline(passiveSide.Channel.CharacterId))
            {
                _pendingInstalls.TryRemove(passiveSide.Channel.CharacterId, out _);
                _store.ClearTradeDeliveryPending(passiveSide.Channel.CharacterId);
                _logger.LogInformation(
                    "Trade {TradeId}: {Passive} disconnected before the delivery could be installed; the settlement stands and the blind-write gate was released.",
                    commit.TradeId,
                    passiveSide.Channel.CharacterName);
            }

            await SendFinishAsync(session.Initiator, commit.Plan.Initiator, commit.Initiator, cancellationToken);
            await SendFinishAsync(session.Partner, commit.Plan.Partner, commit.Partner, cancellationToken);
            // 设计 §2.3.5/§2.5.5：结算后**双方**都要拿到权威全量背包刷新。被动方由它的会话在
            // 顶部安装待安装状态时刷新；确认方（主动方）的会话只是静默换掉内存，必须在这里就地
            // 补一条（与那条刷新共用 EntranceService.CreateMainInventoryRefresh，避免两份投影漂移）。
            var actorRecord = actorIsInitiator ? commit.Initiator : commit.Partner;
            await SendAsync(
                actor.Channel.CharacterId,
                EntranceService.CreateMainInventoryRefresh(
                    actorRecord.Gold,
                    actorRecord.VictoryPoints,
                    actorRecord.Inventory ?? [],
                    _itemCatalog),
                cancellationToken);
            _store.AcknowledgeTradeDelivery(actor.Channel.CharacterId, commit.TradeId);
            RemoveSession(session);
            _logger.LogInformation(
                "Trade {TradeId} completed between {Initiator} and {Partner}.",
                session.TradeId,
                session.Initiator.Channel.CharacterName,
                session.Partner.Channel.CharacterName);
            return new ItemTradeStateOutcome(true, actorRecord);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Trade {TradeId} settlement between {Initiator} and {Partner} threw (commit={Committed}); the session is finished before the error is handled.",
                session.TradeId,
                session.Initiator.Channel.CharacterName,
                session.Partner.Channel.CharacterName,
                committed);

            if (!committed)
            {
                // 提交没有生效（异常来自 store 或 store 之前的路径）：整笔中止，按失败路径的
                // 通用错误码 1 给确认方回包，并走结算旁路摘会话 + 通知存活方。
                await BestEffortAsync(() => SendAsync(
                    actor.Channel.CharacterId,
                    GameProtocolEngine.CreateSetItemTradeStateResult(success: false, errorCode: 1),
                    cancellationToken));
            }
            else
            {
                // 提交已生效：交易已经达成，绝不能再回失败包（那会让客户端回滚一笔已完成的
                // 交易）；只需保证主动方的守门标记释放、会话摘除，并把自己的新记录交还给
                // 会话安装。被动方的待安装登记在发包之前就已写下，会照常被消费。
                _store.AcknowledgeTradeDelivery(actor.Channel.CharacterId, commit!.TradeId);
            }

            await BestEffortAsync(() => AbortSettlementAsync(session, cancellationToken));

            if (exception is OperationCanceledException)
            {
                // 取消语义照旧上抛（会话循环按"服务端在关停/连接已断"处理），但收尾必须已经完成。
                throw;
            }

            return new ItemTradeStateOutcome(
                committed,
                committed
                    ? (session.Initiator.Channel.CharacterId == actor.Channel.CharacterId
                        ? commit!.Initiator
                        : commit!.Partner)
                    : null);
        }
    }

    /// <summary>
    /// 兜底收尾期间的尽力而为：这里的任何异常都不能再逃出去——异常本身可能就是取消
    /// （连接 token 已废），此时发包会二次抛出；就算换成别的 IO/连接异常，收尾也必须走完
    /// （重新抛出取消语义、或把会话的新记录交还给它），不能让它顶掉原始错误或漏掉摘除。
    /// 会话摘除由 <see cref="CancelSessionAsyncCore"/> 在锁内同步完成，不依赖通知是否送达
    /// （连接已死时本来也没有收件人）。
    /// </summary>
    private async Task BestEffortAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "A best-effort trade session cleanup step failed; the session removal itself is not affected.");
        }
    }

    private async Task SendFinishAsync(
        ItemTradeSide side,
        ItemTradeSidePlan plan,
        CharacterRecord updated,
        CancellationToken cancellationToken)
    {
        var entries = plan.Deliveries
            .Select(delivery => new GameItemTradeDeliveryEntry(
                delivery.PanelSlot,
                delivery.DestinationSlot))
            .ToList();
        await SendAsync(
            side.Channel.CharacterId,
            GameProtocolEngine.CreateFinishItemTrade(entries),
            cancellationToken);
        await SendAsync(
            side.Channel.CharacterId,
            GameProtocolEngine.CreateUpdateItemList(
                0,
                [new GameInventoryEntry(0, 0, checked((uint)Math.Max(0, updated.Gold)))]),
            cancellationToken);
    }

    public bool TryTakePendingInstall(Guid characterId, out ItemTradePendingInstall install) =>
        _pendingInstalls.TryRemove(characterId, out install!);

    /// <summary>30 秒请求窗口 / 5 分钟空闲窗口；由 <see cref="ExecuteAsync"/> 每秒调用。</summary>
    public async Task<int> SweepExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var expired = 0;
        foreach (var session in _sessions.Values.Distinct())
        {
            var window = session.State == ItemTradeSessionState.Requested
                ? RequestWindow
                : IdleWindow;
            var reference = session.State == ItemTradeSessionState.Requested
                ? session.CreatedAt
                : session.LastActivityAt;
            if (now - reference < window)
            {
                continue;
            }

            expired++;
            await CancelSessionAsync(session, absentCharacterId: null, cancellationToken);
        }

        return expired;
    }

    /// <summary>
    /// 托管查询：EntranceService 的核心写入路径用它冻结"已放进交易栏"的物品。
    /// </summary>
    public bool IsEscrowed(Guid characterId, byte listType, ushort slot) =>
        _sessions.TryGetValue(characterId, out var session)
        && session.SideOf(characterId).Escrows.Any(escrow =>
            escrow.SourceListType == listType
            && escrow.PanelSlot != ItemTradePlanner.GoldPanelSlot
            && escrow.SourceSlot == slot);

    public bool IsEscrowedInstance(Guid characterId, Guid instanceId) =>
        instanceId != Guid.Empty
        && _sessions.TryGetValue(characterId, out var session)
        && session.SideOf(characterId).Escrows.Any(escrow => escrow.InstanceId == instanceId);

    public async Task<bool> HandleMoveAsync(
        Guid characterId,
        byte sourceListType,
        ushort sourceSlot,
        ushort itemId,
        uint count,
        byte destinationListType,
        ushort destinationSlot,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(characterId, out var session)
            || session.State != ItemTradeSessionState.Open)
        {
            await SendMoveFailureAsync(
                characterId, sourceListType, destinationListType, ItemTradeFailure.NotOpen,
                cancellationToken);
            return false;
        }

        var mine = session.SideOf(characterId);
        var other = session.OtherOf(characterId);
        var character = await _store.GetCharacterAsync(
            mine.Channel.AccountName,
            characterId,
            cancellationToken);
        if (character is null)
        {
            await SendMoveFailureAsync(
                characterId, sourceListType, destinationListType, ItemTradeFailure.NotOpen,
                cancellationToken);
            return false;
        }

        session.LastActivityAt = _timeProvider.GetUtcNow();
        if (destinationListType == GameProtocolEngine.ItemTradeListType)
        {
            return await PlaceAsync(
                session, mine, other, character,
                sourceListType, sourceSlot, itemId, count, destinationSlot,
                // 真机放入：客户端 CMD 19 的 destinationSlot 恒为 0（0x4627A3 的 xor esi,esi，
                // recon-2 §3.2），面板槽必须由服务端分配。面板→面板（srcType==4）不是放入，
                // 保持原样交给规划器拒绝。
                allocatePanelSlot: sourceListType != GameProtocolEngine.ItemTradeListType,
                cancellationToken);
        }

        if (sourceListType == GameProtocolEngine.ItemTradeListType)
        {
            return await ReleaseAsync(
                session, mine, other, panelSlot: sourceSlot, count, destinationSlot,
                cancellationToken);
        }

        await SendMoveFailureAsync(
            characterId, sourceListType, destinationListType, ItemTradeFailure.InvalidSourceSpace,
            cancellationToken);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepExpiredAsync(_timeProvider.GetUtcNow(), stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // 单次扫描的任何异常都不能杀掉全局扫描器：否则 30 秒/5 分钟窗口永远不再过期，
                    // 每个卡住的会话都会活到进程重启。取消（关停）照旧交给外层 catch 结束循环。
                    _logger.LogError(
                        exception,
                        "A trade session sweep failed; the scanner keeps running.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> IsBlacklistedAsync(
        string accountName,
        string characterName,
        CancellationToken cancellationToken)
    {
        var blacklist = await _store.GetBlacklistAsync(accountName, cancellationToken);
        // 客户端黑名单是逐字节大小写敏感的（CNRDBlackListdManager::FindByName），这里照抄。
        return blacklist.Any(entry =>
            string.Equals(entry.Name, characterName, StringComparison.Ordinal));
    }

    private async Task SendRequestFailureAsync(
        ItemTradeChannel requester,
        CancellationToken cancellationToken) =>
        await SendAsync(
            requester.CharacterId,
            GameProtocolEngine.CreateResponsePeerError(GameProtocolEngine.ItemTradePeerType, 1),
            cancellationToken);

    private Task CancelSessionForCharacterAsync(
        Guid characterId,
        Guid? absentCharacterId,
        CancellationToken cancellationToken) =>
        _sessions.TryGetValue(characterId, out var session)
            ? CancelSessionAsync(session, absentCharacterId, cancellationToken)
            : Task.CompletedTask;

    /// <summary>
    /// 结束一笔会话。<paramref name="absentCharacterId"/> 为已离场的角色（掉线/返回选人）：
    /// 不给他发包；为 null 时双方都要收到通知（取消、超时、换区）。
    /// </summary>
    private Task CancelSessionAsync(
        ItemTradeSession session,
        Guid? absentCharacterId,
        CancellationToken cancellationToken) =>
        CancelSessionAsyncCore(session, absentCharacterId, ignoreSettling: false, cancellationToken);

    /// <summary>
    /// 结算失败中止：这一次「整笔退回」由结算路径自己发起（会话已被它抢占为 Settling），
    /// 因此绕过取消路径的 Settling 保护——那条保护只针对外部的取消/超时/换区/掉线。
    /// </summary>
    private Task AbortSettlementAsync(ItemTradeSession session, CancellationToken cancellationToken) =>
        CancelSessionAsyncCore(session, absentCharacterId: null, ignoreSettling: true, cancellationToken);

    private async Task CancelSessionAsyncCore(
        ItemTradeSession session,
        Guid? absentCharacterId,
        bool ignoreSettling,
        CancellationToken cancellationToken)
    {
        lock (session.Sync)
        {
            // 结算在途：取消、超时、换区、掉线都不得插手，成功或失败由结算路径自己收尾。
            if (!ignoreSettling && session.State == ItemTradeSessionState.Settling)
            {
                return;
            }

            if (!RemoveSession(session))
            {
                return;
            }
        }

        if (session.State == ItemTradeSessionState.Requested)
        {
            if (absentCharacterId == session.Initiator.Channel.CharacterId)
            {
                return;
            }

            await SendAsync(
                session.Initiator.Channel.CharacterId,
                GameProtocolEngine.CreateResponsePeerError(GameProtocolEngine.ItemTradePeerType, 1),
                cancellationToken);
            return;
        }

        _logger.LogInformation(
            "Trade {TradeId} cancelled (absent={Absent}).",
            session.TradeId,
            absentCharacterId);

        foreach (var side in new[] { session.Initiator, session.Partner })
        {
            if (absentCharacterId == side.Channel.CharacterId)
            {
                continue;
            }

            await SendCancelToSideAsync(session, side, cancellationToken);
        }
    }

    private async Task SendCancelToSideAsync(
        ItemTradeSession session,
        ItemTradeSide side,
        CancellationToken cancellationToken)
    {
        // 快照必须在 session.Sync 内取：托管的唯一写入方（PlaceAsync/ReleaseAsync）在同一把
        // 锁里改 Escrows，锁外枚举会在它 Add 的瞬间抛 InvalidOperationException（杀掉扫描器）。
        List<GameItemTradeCancelEntry> entries;
        lock (session.Sync)
        {
            entries = side.Escrows
                .Select(escrow => new GameItemTradeCancelEntry(
                    escrow.PanelSlot,
                    escrow.PanelSlot == ItemTradePlanner.GoldPanelSlot ? (byte)0 : escrow.SourceListType,
                    escrow.PanelSlot == ItemTradePlanner.GoldPanelSlot ? (ushort)0 : escrow.SourceSlot))
                .ToList();
        }

        await SendAsync(
            side.Channel.CharacterId,
            GameProtocolEngine.CreateCancelItemTrade(entries),
            cancellationToken);
        await SendGoldRefreshAsync(side.Channel, cancellationToken);
    }

    /// <summary>权威金币刷新：NOTI 14 带 slot 0/itemId 0（与邮件领取同一条通道）。</summary>
    private async Task SendGoldRefreshAsync(
        ItemTradeChannel channel,
        CancellationToken cancellationToken)
    {
        var character = await _store.GetCharacterAsync(
            channel.AccountName,
            channel.CharacterId,
            cancellationToken);
        if (character is null)
        {
            return;
        }

        var update = GameProtocolEngine.CreateUpdateItemList(
            0,
            [new GameInventoryEntry(0, 0, checked((uint)Math.Max(0, character.Gold)))]);
        await SendAsync(channel.CharacterId, update, cancellationToken);
    }

    private async Task<bool> PlaceAsync(
        ItemTradeSession session,
        ItemTradeSide mine,
        ItemTradeSide other,
        CharacterRecord character,
        byte sourceListType,
        ushort sourceSlot,
        ushort itemId,
        uint count,
        ushort panelSlot,
        bool allocatePanelSlot,
        CancellationToken cancellationToken)
    {
        // 金币：srcType 0 / srcSlot 0 / itemId 0，面板固定 0 号格。
        // 这是客户端唯一会自带真实目的槽的放入动作（金币伪物品常驻面板列表下标 0），保持不变。
        if (sourceListType == 0 && sourceSlot == 0 && itemId == 0
            && panelSlot == ItemTradePlanner.GoldPanelSlot)
        {
            if (!ItemTradePlanner.TryPlaceGoldEscrow(
                    mine.Escrows,
                    mine.Locked,
                    count,
                    character.Gold,
                    out var goldEscrow,
                    out var goldFailure))
            {
                _logger.LogInformation(
                    "Trade {TradeId}: {Character} gold offer {Count} refused: {Failure}.",
                    session.TradeId,
                    mine.Channel.CharacterName,
                    count,
                    goldFailure);
                await SendMoveFailureAsync(
                    mine.Channel.CharacterId, sourceListType,
                    GameProtocolEngine.ItemTradeListType, goldFailure, cancellationToken);
                return false;
            }

            // HandleMoveAsync 的状态判定在 await store 之前，对手的连接线程可以在任意时刻取消
            // 同一会话（关窗 = CMD 26=2）。复核必须和内存改动在同一把锁里完成，否则会出现
            // 「服务端已取消、客户端却收到 CMD 19 成功 + NOTI 15」——面板里留下一个任何取消/
            // 完成清单都不会归还的物品，而服务端从未移动它。
            if (!TryMutateEscrows(session, () =>
                {
                    ReplaceEscrow(mine, goldEscrow);
                    mine.OfferedGold = checked((int)count);
                }))
            {
                _logger.LogInformation(
                    "Trade {TradeId}: dropped a gold offer from {Character}: the session is no longer open.",
                    session.TradeId,
                    mine.Channel.CharacterName);
                await SendMoveFailureAsync(
                    mine.Channel.CharacterId, sourceListType,
                    GameProtocolEngine.ItemTradeListType, ItemTradeFailure.NotOpen, cancellationToken);
                return false;
            }

            await SendAsync(
                mine.Channel.CharacterId,
                GameProtocolEngine.CreateMoveItemSpaceReply(
                    sourceListType, sourceSlot, count,
                    GameProtocolEngine.ItemTradeListType, panelSlot),
                cancellationToken);
            await SendAsync(
                other,
                GameProtocolEngine.CreateChangeItemTradeItem(
                    ItemTradePlanner.ProjectPanelEntry(goldEscrow, _itemCatalog)),
                cancellationToken);
            await ClearLocksAsync(session, cancellationToken);
            return true;
        }

        if (allocatePanelSlot)
        {
            // 真机放入：客户端 CMD 19 的 destinationSlot 恒为 0（它把分配权留给服务端，recon-2 §3.2），
            // 不能用它当面板槽。服务端按自己的托管表分配（recon-2 §4.2-1）：客户端给的槽在 [3,26]
            // 且本侧空着就采用，否则取最小空槽 3..26；24 格全占则失败。
            // 分配结果统一用于托管登记、CMD 19 成功回包与对端 NOTI 15——后两者都从 escrow.PanelSlot
            // 读，NOTI 16/18 也一样，不需要各自改写。
            bool allocated;
            lock (session.Sync)
            {
                allocated = ItemTradePlanner.TryAllocatePanelSlot(mine.Escrows, panelSlot, out panelSlot);
            }

            if (!allocated)
            {
                _logger.LogInformation(
                    "Trade {TradeId}: {Character} could not escrow slot {Slot} ({ItemId} x{Count}): all 24 panel slots are occupied.",
                    session.TradeId,
                    mine.Channel.CharacterName,
                    sourceSlot,
                    itemId,
                    count);
                await SendMoveFailureAsync(
                    mine.Channel.CharacterId, sourceListType,
                    GameProtocolEngine.ItemTradeListType, ItemTradeFailure.PanelSlotOccupied,
                    cancellationToken);
                return false;
            }
        }

        var inventory = (character.Inventory ?? []).ToDictionary(item => item.Slot);
        if (!ItemTradePlanner.TryPlaceEscrow(
                inventory,
                mine.Escrows,
                mine.Locked,
                sourceListType,
                sourceSlot,
                panelSlot,
                count,
                _itemCatalog,
                out var escrow,
                out var failure))
        {
            _logger.LogInformation(
                "Trade {TradeId}: {Character} could not escrow slot {Slot} ({ItemId} x{Count}): {Failure}.",
                session.TradeId,
                mine.Channel.CharacterName,
                sourceSlot,
                itemId,
                count,
                failure);
            await SendMoveFailureAsync(
                mine.Channel.CharacterId, sourceListType,
                GameProtocolEngine.ItemTradeListType, failure, cancellationToken);
            return false;
        }

        if (!TryMutateEscrows(session, () => mine.Escrows.Add(escrow)))
        {
            _logger.LogInformation(
                "Trade {TradeId}: dropped an escrow from {Character}: the session is no longer open.",
                session.TradeId,
                mine.Channel.CharacterName);
            await SendMoveFailureAsync(
                mine.Channel.CharacterId, sourceListType,
                GameProtocolEngine.ItemTradeListType, ItemTradeFailure.NotOpen, cancellationToken);
            return false;
        }

        await SendAsync(
            mine.Channel.CharacterId,
            GameProtocolEngine.CreateMoveItemSpaceReply(
                sourceListType, sourceSlot, count,
                GameProtocolEngine.ItemTradeListType, panelSlot),
            cancellationToken);
        await SendAsync(
            other,
            GameProtocolEngine.CreateChangeItemTradeItem(
                ItemTradePlanner.ProjectPanelEntry(escrow, _itemCatalog)),
            cancellationToken);
        await ClearLocksAsync(session, cancellationToken);
        _logger.LogInformation(
            "Trade {TradeId}: {Character} escrowed item {ItemId} x{Count} from slot {Slot} into panel slot {PanelSlot}.",
            session.TradeId,
            mine.Channel.CharacterName,
            escrow.Snapshot.ItemId,
            escrow.Count,
            sourceSlot,
            panelSlot);
        return true;
    }

    private async Task<bool> ReleaseAsync(
        ItemTradeSession session,
        ItemTradeSide mine,
        ItemTradeSide other,
        ushort panelSlot,
        uint count,
        ushort destinationSlot,
        CancellationToken cancellationToken)
    {
        if (!ItemTradePlanner.TryReleaseEscrow(
                mine.Escrows,
                mine.Locked,
                panelSlot,
                out var released,
                out var failure))
        {
            _logger.LogInformation(
                "Trade {TradeId}: {Character} could not take back panel slot {PanelSlot}: {Failure}.",
                session.TradeId,
                mine.Channel.CharacterName,
                panelSlot,
                failure);
            await SendMoveFailureAsync(
                mine.Channel.CharacterId,
                GameProtocolEngine.ItemTradeListType,
                0,
                failure,
                cancellationToken);
            return false;
        }

        if (!TryMutateEscrows(session, () =>
            {
                mine.Escrows.Remove(released);
                if (panelSlot == ItemTradePlanner.GoldPanelSlot)
                {
                    mine.OfferedGold = 0;
                }
            }))
        {
            _logger.LogInformation(
                "Trade {TradeId}: dropped a release from {Character}: the session is no longer open.",
                session.TradeId,
                mine.Channel.CharacterName);
            await SendMoveFailureAsync(
                mine.Channel.CharacterId,
                GameProtocolEngine.ItemTradeListType,
                0,
                ItemTradeFailure.NotOpen,
                cancellationToken);
            return false;
        }

        await SendAsync(
            mine.Channel.CharacterId,
            GameProtocolEngine.CreateMoveItemSpaceReply(
                GameProtocolEngine.ItemTradeListType,
                panelSlot,
                released.Count,
                0,
                destinationSlot),
            cancellationToken);
        await SendAsync(
            other,
            GameProtocolEngine.CreateChangeItemTradeItem(
                new GameItemTradeEntry(
                    panelSlot,
                    GameProtocolEngine.ClearItemTradeSlot,
                    0,
                    0,
                    0)),
            cancellationToken);
        await ClearLocksAsync(session, cancellationToken);
        return true;
    }

    private static void ReplaceEscrow(ItemTradeSide side, ItemTradeEscrow escrow)
    {
        side.Escrows.RemoveAll(candidate => candidate.PanelSlot == escrow.PanelSlot);
        side.Escrows.Add(escrow);
    }

    /// <summary>
    /// 托管表改动的第二道闸：放入/取回在 <c>await _store.GetCharacterAsync</c> 之前判过会话
    /// 状态，但对手的连接线程可以在那个 await 期间取消同一会话。这里在同一把锁里复核
    /// <see cref="ItemTradeSessionState.Open"/> 并仍在册，通过后才执行内存改动（锁内不含 await，
    /// 发包一律留到锁外）。返回 false 表示会话已经没了：调用者只回自己的 CMD 19 失败包，
    /// 绝不再通知对端。
    /// </summary>
    private bool TryMutateEscrows(ItemTradeSession session, Action mutate)
    {
        lock (session.Sync)
        {
            if (session.State != ItemTradeSessionState.Open || !IsStillRegistered(session))
            {
                return false;
            }

            mutate();
            return true;
        }
    }

    /// <summary>
    /// 任何托管变动都把双方锁定清零（客户端 0x411040 自己也这么做），
    /// 并给双方各发两条 NOTI 17：接收端按 uid 决定写 myLock 还是 otherLock。
    /// </summary>
    private async Task ClearLocksAsync(ItemTradeSession session, CancellationToken cancellationToken)
    {
        session.Initiator.Locked = false;
        session.Partner.Locked = false;
        session.Initiator.FinalConfirmed = false;
        session.Partner.FinalConfirmed = false;
        foreach (var side in new[] { session.Initiator, session.Partner })
        {
            foreach (var target in new[] { session.Initiator, session.Partner })
            {
                await SendAsync(
                    target.Channel.CharacterId,
                    GameProtocolEngine.CreateStateItemTrade(side.Channel.AreaUserId, locked: false),
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// CMD 19 失败：客户端只读前 4 字节 [0, errorCode, srcType, destType]（recon-2 §4.1），
    /// 所以要按 <see cref="ItemTradeFailure"/> 映射到它 switch 里的错误码，**不能**回显 srcSlot/count/destSlot。
    /// </summary>
    private async Task SendMoveFailureAsync(
        Guid characterId,
        byte sourceListType,
        byte destinationListType,
        ItemTradeFailure failure,
        CancellationToken cancellationToken) =>
        await SendAsync(
            characterId,
            GameProtocolEngine.CreateMoveItemSpaceFailure(
                ItemTradePlanner.ToMoveErrorCode(failure),
                sourceListType,
                destinationListType),
            cancellationToken);

    /// <summary>
    /// 会话是否仍是在册会话（取消/超时/换区/掉线/结算收尾都会把它从 <see cref="_sessions"/> 摘掉）。
    /// 在摘除之前就取到会话引用的调用者必须在 <see cref="ItemTradeSession.Sync"/> 内用它复核，
    /// 否则会出现「客户端已被通知取消，服务端却仍然广播锁定或结算」。
    /// </summary>
    private bool IsStillRegistered(ItemTradeSession session) =>
        _sessions.TryGetValue(session.Initiator.Channel.CharacterId, out var initiator)
        && ReferenceEquals(initiator, session)
        && _sessions.TryGetValue(session.Partner.Channel.CharacterId, out var partner)
        && ReferenceEquals(partner, session);

    private bool RemoveSession(ItemTradeSession session)
    {
        var removed = _sessions.TryRemove(
            new KeyValuePair<Guid, ItemTradeSession>(session.Initiator.Channel.CharacterId, session));
        var partnerRemoved = _sessions.TryRemove(
            new KeyValuePair<Guid, ItemTradeSession>(session.Partner.Channel.CharacterId, session));
        _store.Commerce.Release(session.TradeId, session.Initiator.Channel.CharacterId, session.Partner.Channel.CharacterId);
        return removed || partnerRemoved;
    }

    private async Task SendAsync(
        ItemTradeSide side,
        GameServerPacket packet,
        CancellationToken cancellationToken) =>
        await SendAsync(side.Channel.CharacterId, packet, cancellationToken);

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
                "Could not deliver trade packet {ProtocolId} to {CharacterId}.",
                packet.ProtocolId,
                characterId);
        }
    }
}
