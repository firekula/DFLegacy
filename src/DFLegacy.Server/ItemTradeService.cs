using System.Collections.Concurrent;
using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal enum ItemTradeSessionState
{
    Requested,
    Open
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
    public required byte TownId { get; init; }
    public required byte AreaId { get; init; }
    public required bool IsInitiator { get; init; }
    public List<ItemTradeEscrow> Escrows { get; } = [];
    public int OfferedGold { get; set; }
    public bool Locked { get; set; }
    public bool FinalConfirmed { get; set; }
}

internal sealed class ItemTradeSession
{
    public required Guid TradeId { get; init; }
    public required ItemTradeSide Initiator { get; init; }
    public required ItemTradeSide Partner { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public ItemTradeSessionState State { get; set; } = ItemTradeSessionState.Requested;
    public DateTimeOffset LastActivityAt { get; set; }

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
    /// <summary>客户端 CMD 10 自带时间差防抖；服务端独立限流，避免被单包装满。</summary>
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
        _channels[characterId] = new ItemTradeChannel
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
        _lastRequestAt.TryRemove(characterId, out _);
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
        if (_sessions.ContainsKey(characterId)
            || !_townDirectory.TryGetSnapshot(characterId, out var requesterTown))
        {
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

        // 被对方拉黑：完全静默（与悄悄话的既有策略一致，不向被拉黑方暴露）。
        if (await IsBlacklistedAsync(target.AccountName, requester.CharacterName, cancellationToken))
        {
            _logger.LogInformation(
                "Silently dropped a trade request from {Requester} to {Target}: the target's blacklist holds the requester.",
                requester.CharacterName,
                target.CharacterName);
            return;
        }

        var session = new ItemTradeSession
        {
            TradeId = Guid.NewGuid(),
            Initiator = new ItemTradeSide
            {
                Channel = requester,
                TownId = requesterTown.TownId,
                AreaId = requesterTown.AreaId,
                IsInitiator = true
            },
            Partner = new ItemTradeSide
            {
                Channel = target,
                TownId = targetTown.TownId,
                AreaId = targetTown.AreaId,
                IsInitiator = false
            },
            CreatedAt = now,
            LastActivityAt = now
        };
        _sessions[requester.CharacterId] = session;
        _sessions[target.CharacterId] = session;
        await SendAsync(
            session.Partner,
            GameProtocolEngine.CreateRequestPeerNotification(requester.AreaUserId, requester.AreaUserId),
            cancellationToken);
        _logger.LogInformation(
            "Trade {TradeId}: {Requester} requested {Target}.",
            session.TradeId,
            requester.CharacterName,
            target.CharacterName);
    }

    public async Task HandleResponseAsync(
        Guid characterId,
        ushort requesterAreaUserId,
        bool accept,
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

        session.State = ItemTradeSessionState.Open;
        session.LastActivityAt = _timeProvider.GetUtcNow();
        await SendAsync(
            session.Initiator,
            GameProtocolEngine.CreateResponsePeerNotification(
                session.Partner.Channel.AreaUserId,
                0,
                mode: 0),
            cancellationToken);
        await SendAsync(
            session.Partner,
            GameProtocolEngine.CreateResponsePeerReply(
                session.Initiator.Channel.AreaUserId,
                mode: 0),
            cancellationToken);
        _logger.LogInformation(
            "Trade {TradeId} opened between {Initiator} and {Partner}.",
            session.TradeId,
            session.Initiator.Channel.CharacterName,
            session.Partner.Channel.CharacterName);
    }

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepExpiredAsync(_timeProvider.GetUtcNow(), stoppingToken);
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
    private async Task CancelSessionAsync(
        ItemTradeSession session,
        Guid? absentCharacterId,
        CancellationToken cancellationToken)
    {
        if (!RemoveSession(session))
        {
            return;
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

            await SendCancelToSideAsync(side, cancellationToken);
        }
    }

    private async Task SendCancelToSideAsync(
        ItemTradeSide side,
        CancellationToken cancellationToken)
    {
        var entries = side.Escrows
            .Select(escrow => new GameItemTradeCancelEntry(
                escrow.PanelSlot,
                escrow.PanelSlot == ItemTradePlanner.GoldPanelSlot ? (byte)0 : escrow.SourceListType,
                escrow.PanelSlot == ItemTradePlanner.GoldPanelSlot ? (ushort)0 : escrow.SourceSlot))
            .ToList();
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

    private bool RemoveSession(ItemTradeSession session)
    {
        var removed = _sessions.TryRemove(
            new KeyValuePair<Guid, ItemTradeSession>(session.Initiator.Channel.CharacterId, session));
        var partnerRemoved = _sessions.TryRemove(
            new KeyValuePair<Guid, ItemTradeSession>(session.Partner.Channel.CharacterId, session));
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
