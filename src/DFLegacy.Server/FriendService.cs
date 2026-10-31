using System.Collections.Concurrent;
using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// Turns account-level friend-list changes into the buddy notifications the 2008 client
/// understands, serves the account blacklist commands (CMD 128/129/130) on the type-1
/// command channel, and routes whispers/1:1 messages between live game-channel sessions
/// with the server-side blacklist filter applied.
/// </summary>
/// <remarks>
/// A character is online exactly when it has an attached channel: keeping the send
/// delegate and the online state in one table means they can never disagree, and
/// <see cref="AttachChannelAsync"/> is the single entry point that both registers a
/// session and announces it, so a live character can never sit online without having
/// been announced.
/// </remarks>
public sealed class FriendService
{
    private static readonly Encoding ClientEncoding = CreateClientEncoding();

    private readonly ConcurrentDictionary<string, Channel> _channels =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly JsonGameStore _store;
    private readonly ILogger<FriendService> _logger;

    public FriendService(JsonGameStore store, ILogger<FriendService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Registers a live session for a character and announces it: pushes a USERINFO record
    /// for every online friend, then the friend-list snapshot (which marks offline friends),
    /// and sends BUDDY_IN to every online account whose friend list holds the name.
    /// </summary>
    /// <remarks>
    /// The session id binds the channel to one specific session so a late cleanup from an
    /// older session can never detach a newer one (compare-and-remove in
    /// <see cref="DetachChannelAsync"/>). <paramref name="areaUserId"/> is the area user id
    /// allocated for this session at SELECT_CHARACTER: it identifies this character in the
    /// USERINFO record pushed to friends and in the CMD 8 mode-3 reverse lookup. It is not
    /// written into the friend snapshot: R+0x21 carries the friend's level instead (the old
    /// “R+0x21 must stay 0” reading mistook the SecureInt decoder 0x402030 for a string getter).
    /// </remarks>
    /// <remarks>
    /// 顺序必须「在线好友记录 → 名单快照」：客户端追加条目的 0x500B00 会按名字查本地
    /// user 注册表并把 user* 存进 entry+0x38，记录先到才拿得到（“查看信息”的菜单门正
    /// 依赖它）。已知限制：登录时**不在线**的好友，快照追加条目时没有 user 对象，要等
    /// 他到下一次上线（<see cref="NotifyFriendOwnersAsync"/> 会先推记录再播报 BUDDY_IN）
    /// 或本会话下次登录才可“查看信息”；不能用重发快照补救（名单只增不减，会出重复行）。
    /// </remarks>
    public async Task AttachChannelAsync(
        string accountName,
        string characterName,
        ushort areaUserId,
        Guid sessionId,
        Func<GameServerPacket, ValueTask> send,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterName);
        ArgumentNullException.ThrowIfNull(send);
        _channels[characterName] = new Channel(accountName, areaUserId, sessionId, send);

        // 好友行/“查看信息”/社区菜单都先查本地 user 注册表（FindByUid）：没有 USERINFO
        // 记录时点“查看信息”连 CMD 8 都不会发。登录时把当前在线好友各推一条 mode-0
        // 记录（uid = 对方的区用户 id），**必须先于快照**——快照追加条目时按名字查注册
        // 表填 entry+0x38，记录迟到就只能等 USERINFO 尾部的 RefreshAll 补。
        await PushOnlineFriendRecordsAsync(accountName, characterName, cancellationToken);

        // 快照每个会话只推这一次：客户端收到 NOTI 56 是无条件追加条目（不去重），
        // 同名再推就会多出一行；之后的状态变化一律只走 BUDDY_IN/BUDDY_OUT。
        // 空快照在这里仍要推：客户端好友管理器随选角界面重建时是空的，同一进程
        // 换号登录时靠它把上一份列表的状态归位。
        await PushSnapshotAsync(accountName, characterName, cancellationToken);

        await NotifyFriendOwnersAsync(characterName, isOnline: true, cancellationToken);
    }

    /// <summary>
    /// Ends a character session: removes the channel and tells online friends it went
    /// offline. A detach whose session id does not match the attached channel is ignored
    /// in full — no removal and no notification.
    /// </summary>
    public async Task DetachChannelAsync(
        string characterName,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterName)
            || !_channels.TryGetValue(characterName, out var existing)
            || existing.SessionId != sessionId
            || !_channels.TryRemove(new KeyValuePair<string, Channel>(characterName, existing)))
        {
            return;
        }

        await NotifyFriendOwnersAsync(characterName, isOnline: false, cancellationToken);
    }

    /// <summary>
    /// 按区用户 id 反查当前持有它的在线角色名（CMD 8 mode 3“查看信息”的 uid → 角色）。
    /// </summary>
    /// <remarks>
    /// 数据源是**所有已挂通道的在线角色**，不只是好友：城镇里右键任何路人玩家走的也是
    /// CMD 8 mode 3，所以这里按整张通道表线性查找，绝不能收窄成"查好友名单"。区用户 id
    /// 由 <c>AreaUserIdPool</c> 全局分配，同一时刻只有一个会话持有；uid 为 0 或无人持有
    /// 时返回 false。客户端侧"能右键到的人"都有本地 user 记录：好友由本服务的 USERINFO
    /// 推送提供，同区路人由城镇同屏的外观推送（目录重放 mode-0 记录）提供。
    /// </remarks>
    public bool TryGetCharacterByAreaUserId(ushort areaUserId, out string characterName) =>
        TryGetOnlineCharacter(areaUserId, out characterName, out _);

    /// <summary>
    /// 按区用户 id 反查当前持有它的在线角色名**及其账号名**。账号名用于 CMD 8 mode 3
    /// 从存档里取该角色的装备（资料卡渲染目标 actor，必须带装备）以及将来的跨账号查询。
    /// </summary>
    public bool TryGetOnlineCharacter(
        ushort areaUserId,
        out string characterName,
        out string accountName)
    {
        if (areaUserId != 0)
        {
            foreach (var (name, channel) in _channels)
            {
                if (channel.AreaUserId == areaUserId)
                {
                    characterName = name;
                    accountName = channel.AccountName;
                    return true;
                }
            }
        }

        characterName = string.Empty;
        accountName = string.Empty;
        return false;
    }

    public async Task AddFriendAsync(
        string accountName,
        string characterName,
        string friendName,
        CancellationToken cancellationToken = default)
    {
        var failure = await _store.AddFriendAsync(accountName, friendName, cancellationToken);
        if (failure != FriendUpdateFailure.None)
        {
            await SendNoticeAsync(characterName, DescribeFailure(failure), cancellationToken);
            return;
        }

        // 命令结果必须走 type=1 命令通道：客户端接收循环把包头 type 1 的 body 首字节当
        // 结果标志走另一张分发表，只有这条包会打印“[名字]已被添加到好友名单”。
        // 用 store 归一化后的名字（去首尾空格）：客户端按这份拼写建条目。
        // id 63 的 handler 会无条件追加条目，但**必须把尾部 9 字节发全**：读不到的部分
        // 被接收缓冲填成 0x30，其中 B=0x30 撞上 0x500B00 的 `B < 5` 守卫，条目会被静默
        // 拒绝（聊天栏提示照打）——这就是“加好友不进列表”的根因。尾部 D 填好友的区用户
        // id（客户端顺带用它给 user 对象打好友标记），离线填 0。**不能再补整表快照**：
        // NOTI 56 同样是无条件追加，会把老好友全部再加一份（重复行的根因）。
        var addedName = friendName.Trim();
        var addedNameBytes = ClientEncoding.GetBytes(addedName);
        var isOnline = _channels.TryGetValue(addedName, out var addedChannel);
        // 尾部 A/B/C = 等级/职业系/打包成长（与 NOTI 56 记录同源同序）：取存档投影，
        // 新加的离线好友也立刻显示真实 Lv./职业，不再停在 Lv.0 鬼剑士。
        var addedProfile = await GetFriendProfileAsync(addedName, cancellationToken);
        await SendAsync(
            characterName,
            GameProtocolEngine.CreateAddFriendResult(
                addedNameBytes,
                isOnline ? addedChannel!.AreaUserId : (ushort)0,
                addedProfile.Level,
                addedProfile.Job,
                addedProfile.GrowType,
                addedProfile.AwakeningType),
            cancellationToken);

        // id 63 追加出来的条目停在构造默认状态，若好友此刻已在线，点开列表会看到一条
        // "刚加的在线好友显示离线"。紧接着补一条状态更新：BUDDY_IN/BUDDY_OUT 在客户端
        // 是按名字就地改状态（只写首个同名条目，0x4FFDC0）且**不会追加**，正好落在刚
        // 追加的那一行上；在线发 BUDDY_IN(0)，离线发 BUDDY_OUT(-1) 把默认值钉死。
        // 只发给执行添加的会话：这条新行只存在于他的列表里，别的会话收到也无处可改。
        await SendAsync(
            characterName,
            isOnline
                ? GameProtocolEngine.CreateBuddyIn(state: 0, addedNameBytes)
                : GameProtocolEngine.CreateBuddyOut(addedNameBytes),
            cancellationToken);

        // 最后给添加者补一条在线好友的 mode-0 USERINFO 记录：条目与状态都有了，但添加者
        // 客户端的本地 user 注册表里还没有这个 uid，“查看信息”的 FindByUid 门会静默
        // 落空。登录时的记录推送覆盖不到“添加这一刻”，所以在这里补；离线好友没有可用
        // uid（也还没有需要它的场景），不发——对方的记录会在下次上线播报（记录先于
        // BUDDY_IN）时补上。
        if (isOnline)
        {
            var friendRecord = await CreateMinimalPeerRecordAsync(
                addedName,
                addedChannel!,
                cancellationToken);
            if (friendRecord is not null)
            {
                await SendAsync(characterName, friendRecord, cancellationToken);
            }
        }
    }

    public async Task RemoveFriendAsync(
        string accountName,
        string characterName,
        string friendName,
        CancellationToken cancellationToken = default)
    {
        var failure = await _store.RemoveFriendAsync(accountName, friendName, cancellationToken);
        if (failure != FriendUpdateFailure.None)
        {
            await SendNoticeAsync(characterName, DescribeFailure(failure), cancellationToken);
            return;
        }

        // 删除只能靠 type=1 id 64 结果包：客户端 0x414BA7 → RemoveEntryByName →
        // vector::erase。通知通道（NOTI 56/54/55）既不能删条目，NOTI 56 还会把整份名单
        // 再追加一遍（重复行的另一个根因），所以这里**不推任何快照**。
        await SendAsync(
            characterName,
            GameProtocolEngine.CreateRemoveFriendResult(
                ClientEncoding.GetBytes(friendName.Trim())),
            cancellationToken);
    }

    /// <summary>
    /// CMD 128 REGISTER_TO_BLACKLIST：把名字写进账号黑名单并回 type-1 id 128 结果。
    /// 成功回 [非零][u32 名字长度 + 名字]（客户端按本机日期追加条目并提示“已被添加到
    /// 黑名单”）；失败回 [0][u8 失败码]（74 已在该名单 / 76 找不到该角色 / 78 名单已满）。
    /// </summary>
    /// <remarks>
    /// 黑名单**没有通知包**（勘察：通知名表里没有 BLACKLIST 条目），列表窗口完全靠
    /// 这三条 type-1 回包驱动，所以失败也必须是应答包而不是白字提示。名字比较大小写
    /// 敏感的说明见 <see cref="JsonGameStore.AddBlacklistAsync"/>。
    /// </remarks>
    public async Task AddBlacklistAsync(
        string accountName,
        string characterName,
        byte[] blacklistNameBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blacklistNameBytes);
        var name = ClientEncoding.GetString(blacklistNameBytes).Trim();
        var failure = await _store.AddBlacklistAsync(accountName, name, cancellationToken);
        var packet = failure switch
        {
            BlacklistUpdateFailure.None =>
                GameProtocolEngine.CreateBlacklistAddResult(ClientEncoding.GetBytes(name)),
            BlacklistUpdateFailure.AlreadyBlacklisted => GameProtocolEngine.CreateBlacklistError(
                GameProtocolEngine.RegisterToBlacklistCommand,
                GameProtocolEngine.BlacklistAlreadyListedError),
            BlacklistUpdateFailure.CharacterNotFound => GameProtocolEngine.CreateBlacklistError(
                GameProtocolEngine.RegisterToBlacklistCommand,
                GameProtocolEngine.BlacklistCharacterNotFoundError),
            BlacklistUpdateFailure.BlacklistFull => GameProtocolEngine.CreateBlacklistError(
                GameProtocolEngine.RegisterToBlacklistCommand,
                GameProtocolEngine.BlacklistFullError),
            // 会话的账号在存档里消失（内部不一致）没有专属失败码，按“找不到该角色”回，
            // 客户端至少有“无法找到的名字…”文案，不会静默。
            _ => GameProtocolEngine.CreateBlacklistError(
                GameProtocolEngine.RegisterToBlacklistCommand,
                GameProtocolEngine.BlacklistCharacterNotFoundError)
        };
        await SendAsync(characterName, packet, cancellationToken);
    }

    /// <summary>
    /// CMD 129 DELETE_TO_BLACKLIST：按名字从账号黑名单里删掉一条并回 type-1 id 129 结果。
    /// 成功回 [非零][u32 名字长度 + 名字]；失败一律回 [0][0x4B]（客户端文案“对方不在
    /// 黑名单中”）。
    /// </summary>
    public async Task RemoveBlacklistAsync(
        string accountName,
        string characterName,
        byte[] blacklistNameBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blacklistNameBytes);
        var name = ClientEncoding.GetString(blacklistNameBytes).Trim();
        var failure = await _store.RemoveBlacklistAsync(accountName, name, cancellationToken);
        var packet = failure == BlacklistUpdateFailure.None
            ? GameProtocolEngine.CreateBlacklistRemoveResult(ClientEncoding.GetBytes(name))
            : GameProtocolEngine.CreateBlacklistError(
                GameProtocolEngine.DeleteToBlacklistCommand,
                GameProtocolEngine.BlacklistNotListedError);
        await SendAsync(characterName, packet, cancellationToken);
    }

    /// <summary>
    /// CMD 130 REQUEST_BLACKLIST：回 type-1 id 130 的黑名单**全量快照**
    /// （[非零][u8 条数] + 每条(u32 名字长度 + 名字 + u8 年-1900 + u8 月-1 + u8 日)）。
    /// </summary>
    /// <remarks>
    /// 客户端收到后先 clear 再逐条追加，所以这是“清空 + 全量追加”，**没有每会话一次的
    /// 约束**：社区窗口每次打开/刷新黑名单页都会重发本命令（客户端侧只有 3s/10s 节流），
    /// 这里每次都按存档回完整名单，不做任何会话标记——与好友名单的 NOTI 56 快照相反
    /// （那个只能推一次，重复推送会追加出重复行）。
    /// </remarks>
    public async Task SendBlacklistSnapshotAsync(
        string accountName,
        string characterName,
        CancellationToken cancellationToken = default)
    {
        var blacklist = await _store.GetBlacklistAsync(accountName, cancellationToken);
        var entries = new GameBlacklistEntry[blacklist.Count];
        for (var index = 0; index < blacklist.Count; index++)
        {
            var entry = blacklist[index];
            entries[index] = new GameBlacklistEntry(
                ClientEncoding.GetBytes(entry.Name),
                entry.Year,
                entry.Month,
                entry.Day);
        }

        await SendAsync(
            characterName,
            GameProtocolEngine.CreateBlacklistSnapshot(entries),
            cancellationToken);
    }

    /// <summary>
    /// Routes a whisper (CMD 17 type 1/7) to the live session that owns the target name.
    /// </summary>
    /// <remarks>
    /// The target's main-chat line is the NOTI 12 the client resolves from the sender uid
    /// (that is why the target gets the sender's appearance first), and that is the whole
    /// delivery: a whisper must not open or write a 1:1 window, so the target gets no
    /// NOTI 65. The sender gets nothing at all — the client's own line is assembled
    /// locally as '给[X] : 文本' and dropped by its own-uid branch (the 1:1 window pointer
    /// on its record is permanently NULL), and there is no server packet that can render
    /// that line: the main chat always prints the packet name as the speaker, and a NOTI 65
    /// named after the target would mirror the text into the sender's 1:1 window.
    /// </remarks>
    public Task WhisperAsync(
        string characterName,
        ushort senderAreaUserId,
        GameServerPacket? senderAppearance,
        byte[] targetNameBytes,
        byte messageType,
        byte[] textBytes,
        CancellationToken cancellationToken = default) =>
        RoutePeerMessageAsync(
            characterName,
            targetNameBytes,
            textBytes,
            GameProtocolEngine.CreateMessageNotification(
                (GameMessageType)messageType,
                senderAreaUserId,
                textBytes),
            senderAppearance,
            selfNoticeText: "不能给自己发悄悄话",
            cancellationToken);

    /// <summary>
    /// Routes a 1:1 window message (CMD 17 type 18) to the live session that owns the peer
    /// name. The target gets the sender's mode-0 record and then a NOTI 65 (type 0x12,
    /// flag 0); the sender gets the target's record only, because the client appends its
    /// own line locally after sending. There is no main-chat line, and the history stays
    /// in the client's local ring buffer.
    /// </summary>
    /// <remarks>
    /// NOTI 65 is the 1:1-channel carrier and is used only here — a whisper (types 1/7)
    /// must not create a 1:1 window. Both records feed the local user registry (friend rows
    /// and the community menu's “查看信息”): the target's record lets it resolve the sender
    /// name, the sender's copy of the target record keeps the newly appended row usable.
    /// </remarks>
    public Task SendMessengerMessageAsync(
        string characterName,
        GameServerPacket? senderAppearance,
        byte[] peerNameBytes,
        byte[] textBytes,
        CancellationToken cancellationToken = default) =>
        RoutePeerMessageAsync(
            characterName,
            peerNameBytes,
            textBytes,
            mainChatLine: null,
            receiverPreface: senderAppearance,
            selfNoticeText: "不能和自己进行1:1聊天",
            cancellationToken);

    private async Task RoutePeerMessageAsync(
        string characterName,
        byte[] peerNameBytes,
        byte[] textBytes,
        GameServerPacket? mainChatLine,
        GameServerPacket? receiverPreface,
        string selfNoticeText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peerNameBytes);
        ArgumentNullException.ThrowIfNull(textBytes);

        var peerName = ClientEncoding.GetString(peerNameBytes);
        if (string.Equals(peerName, characterName, StringComparison.OrdinalIgnoreCase))
        {
            // 悄悄话与 1:1 窗口是两套界面，自投的提示文案各自贴合自己的上下文。
            await SendNoticeAsync(characterName, selfNoticeText, cancellationToken);
            return;
        }

        // 黑名单是**双向的服务器侧过滤**：客户端在发送路径（0x8464F0/0x8495B0/0x811D21）
        // 和接收路径（NOTI 12 handler 0x41C994）都没有任何 CNRDBlackListdManager 访问，
        // 过滤只能由服务器做。
        // 两个方向都拒绝投递，但提示是不对称的：
        //   命中来自**发起者自己的名单** → 发起者知道自己在拉黑谁，回一条中性白字提示；
        //   命中来自**对端的名单**（= 自己被对方拉黑）→ **静默丢弃，一个包都不发**，
        //   原版行为就是“被拉黑的人看不到任何提示”（勘察 §2.4），拒绝本身也不能泄露名单
        //   状态。**原版提示文案未知**（“对方当前无法接收消息”是自定义的）。
        // 注意“发起者完全无感知”服务端保证不了：悄悄话发起者的客户端在发包后会本地渲染
        // 自己那一行（“给[X] : 文本”，见 WhisperAsync 的 remarks），我们唯一能保证的是
        // 一个包都不发；这行是否仍然出现属真机观察项。
        // 名字比较按客户端语义**逐字节、大小写敏感**（CNRDBlackListdManager::FindByName
        // 的手写 strcmp），与好友名单的 OrdinalIgnoreCase 相反；客户端只会按本地 user
        // 注册表里的精确拼写发目标名，所以线上正常路径永远命中。
        // TODO(交易/组队邀请)：原版客户端对这些路径同样不做黑名单检查（0xB5DCD0 的
        // 引用点里没有它们），落地时必须复用这里的两向检查；“对端名单命中”那一路同样
        // 要保持静默。
        if (_channels.TryGetValue(characterName, out var senderChannel)
            && await IsBlacklistedAsync(senderChannel.AccountName, peerName, cancellationToken))
        {
            await SendNoticeAsync(characterName, "对方当前无法接收消息", cancellationToken);
            return;
        }

        if (_channels.TryGetValue(peerName, out var peerChannel)
            && await IsBlacklistedAsync(peerChannel.AccountName, characterName, cancellationToken))
        {
            return;
        }

        if (!_channels.TryGetValue(peerName, out var target))
        {
            await SendNoticeAsync(characterName, "对方不在线", cancellationToken);
            return;
        }

        // 目标侧的记录必须最先到：客户端要靠它把包里的 uid（NOTI 12）或名字解析出来。
        // 每个接收者单独 try/catch：目标通道坏掉也不能让异常逃出命令处理。
        if (receiverPreface is not null)
        {
            await TrySendToChannelAsync(peerName, target, receiverPreface, cancellationToken);
        }

        // 悄悄话：主聊天窗那一行（NOTI 12）是唯一的投递，发送方无包可发（见 WhisperAsync
        // 的 remarks）；也**不**发 NOTI 65——那会让客户端为一条悄悄话开/写 1:1 窗口。
        if (mainChatLine is not null)
        {
            await TrySendToChannelAsync(peerName, target, mainChatLine, cancellationToken);
            return;
        }

        // 1:1（CMD 17 type 18）：按名字把正文投递进对端的 1:1 窗口。
        // NOTI 65 带的是名字不是 uid：目标窗口按发送者名建/找。
        await TrySendToChannelAsync(
            peerName,
            target,
            GameProtocolEngine.CreateMessageOtherChannel(
                ClientEncoding.GetBytes(characterName),
                textBytes),
            cancellationToken);

        // 发送者侧补一条目标的 mode-0 USERINFO：1:1 发送方自己的行由客户端本地追加，
        // 但好友行/“查看信息”的本地 user 注册表仍需要目标记录（没有记录时点“查看信息”
        // 连 CMD 8 都不发）。
        var peerRecord = await CreateMinimalPeerRecordAsync(peerName, target, cancellationToken);
        if (peerRecord is not null)
        {
            await SendAsync(characterName, peerRecord, cancellationToken);
        }
    }

    /// <summary>
    /// 查询某个账号的黑名单里是否有这个名字。名字比较是 Ordinal（大小写敏感），
    /// 与客户端 CNRDBlackListdManager::FindByName 的逐字节比较一致。
    /// </summary>
    /// <remarks>
    /// 对端离线时查不到它的账号，只能查发起者自己的名单——离线对端本来也收不到消息
    /// （调用方随后会回“对方不在线”），不存在旁路。两向检查与提示的不对称规则见
    /// <see cref="RoutePeerMessageAsync"/> 的说明。
    /// </remarks>
    private async Task<bool> IsBlacklistedAsync(
        string accountName,
        string characterName,
        CancellationToken cancellationToken)
    {
        var blacklist = await _store.GetBlacklistAsync(accountName, cancellationToken);
        foreach (var entry in blacklist)
        {
            if (string.Equals(entry.Name, characterName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 为消息发送者拼一条对端的 mode-0 USERINFO 记录，让发送者的客户端在本地渲染
    /// 之前先认识对方（推送点见 <see cref="RoutePeerMessageAsync"/>）。
    /// </summary>
    /// <remarks>
    /// 发送者的会话拿不到对端的装备栏，这里按存储角色拼最小记录：名字 / 职业 / 成长 /
    /// 等级，无装备、无宠物，与调试幽灵 <c>TownGhostService.CreateGhostAppearance</c> 的
    /// 外观同形。它的用途是往客户端的本地 user 注册表里放进"uid → 记录"，
    /// 好友行、“查看信息”的菜单项与悄悄话按 uid 反查名字都依赖它；1:1 正文能否渲染
    /// 不取决于这条记录（取决于 NOTI 65 的 flag）。字段够不够属"最小记录，待真机确认"。
    /// 账号里查不到这个角色名、或通道没有可用的区用户 id（uid 0 无法构建记录）时返回
    /// null，调用方跳过推送但不影响消息本身。
    /// </remarks>
    private async Task<GameServerPacket?> CreateMinimalPeerRecordAsync(
        string peerName,
        Channel channel,
        CancellationToken cancellationToken)
    {
        if (channel.AreaUserId == 0)
        {
            return null;
        }

        var characters = await _store.GetCharactersAsync(channel.AccountName, cancellationToken);
        var character = characters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, peerName, StringComparison.OrdinalIgnoreCase));
        if (character is null)
        {
            return null;
        }

        return GameProtocolEngine.CreateCurrentCharacterInfo(
            new GameCharacterSummary(
                0,
                ClientEncoding.GetBytes(character.Name),
                (byte)Math.Clamp(character.Job, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp(character.GrowType, 0, 15),
                (byte)Math.Clamp(character.Level, 1, byte.MaxValue)),
            channel.AreaUserId,
            equippedItems: []);
    }

    private async Task PushSnapshotAsync(
        string accountName,
        string characterName,
        CancellationToken cancellationToken)
    {
        var friends = await _store.GetFriendsAsync(accountName, cancellationToken);
        var entries = new List<GameFriendEntry>(friends.Count);
        var offlineNames = new List<byte[]>();
        foreach (var friend in friends)
        {
            var nameBytes = ClientEncoding.GetBytes(friend.Name);
            // 等级/职业/成长取存档投影：好友行在没有本地 user 记录时（离线好友的常见
            // 情况）就用 R+0x21/+0x23/+0x24 画 `Lv.%d 职业`，不填就会显示 Lv.0 鬼剑士。
            // 区用户 id **不**进快照（R+0x21 现在是等级）。
            var profile = await GetFriendProfileAsync(friend.Name, cancellationToken);
            if (_channels.ContainsKey(friend.Name))
            {
                entries.Add(new GameFriendEntry(
                    nameBytes,
                    Online: true,
                    profile.Level,
                    profile.Job,
                    profile.GrowType,
                    profile.AwakeningType));
                continue;
            }

            entries.Add(new GameFriendEntry(
                nameBytes,
                Online: false,
                profile.Level,
                profile.Job,
                profile.GrowType,
                profile.AwakeningType));
            offlineNames.Add(nameBytes);
        }

        await SendAsync(characterName, GameProtocolEngine.CreateFriendList(entries), cancellationToken);

        // The 42-byte snapshot record cannot express "offline": 0xFF keeps the client's
        // previous state and the -1 only comes from BUDDY_OUT, so every friend without an
        // attached channel is marked offline right after the snapshot. BUDDY_OUT now carries
        // flag=1: with a user record present (every online friend got one) only flag!=0 makes
        // the row switch to the grey "not connected" style.
        foreach (var nameBytes in offlineNames)
        {
            await SendAsync(characterName, GameProtocolEngine.CreateBuddyOut(nameBytes), cancellationToken);
        }
    }

    /// <summary>
    /// 好友行 / id-63 尾部需要的存档投影：等级、职业系、成长阶、觉醒阶。
    /// </summary>
    /// <remarks>
    /// 数据源是存档里的 <c>CharacterRecord</c>（<see cref="JsonGameStore.GetCharacterByNameAsync"/>），
    /// 不依赖对方是否在线、也不依赖任何会话数据——离线好友因此也能显示真实 Lv./职业。
    /// 职业系钳到 0..4：客户端条目插入 0x500B00 的 `cmp arg3,5; jge` 会拒绝越界条目。
    /// 存档里查不到（角色已删/竞态）时返回全 0，行至少还在。
    /// </remarks>
    private async Task<FriendProfile> GetFriendProfileAsync(
        string friendName,
        CancellationToken cancellationToken)
    {
        var character = await _store.GetCharacterByNameAsync(friendName, cancellationToken);
        if (character is null)
        {
            return default;
        }

        return new FriendProfile(
            (ushort)Math.Clamp(character.Level, 1, ushort.MaxValue),
            (byte)Math.Clamp(character.Job, 0, 4),
            (byte)Math.Clamp(character.GrowType, 0, 15),
            (byte)Math.Clamp(character.AwakeningType, 0, 7));
    }

    private readonly record struct FriendProfile(
        ushort Level,
        byte Job,
        byte GrowType,
        byte AwakeningType);

    /// <summary>
    /// 给刚登录的会话补推"当前在线好友"的 mode-0 USERINFO 记录（uid = 好友的区用户 id）。
    /// </summary>
    /// <remarks>
    /// 客户端的好友行、社区/角色菜单的“查看信息”都先查本地 user 注册表（FindByUid）：
    /// 表里没有这个 uid 的记录时点“查看信息”连 CMD 8 都不会发（服务器看到 0 个包），
    /// 悄悄话按 uid 反查名字也会落空。所以登录时对每个在线好友补一条最小记录；
    /// 好友之后上线时由 <see cref="NotifyFriendOwnersAsync"/> 再补。同区路人的记录不是
    /// 这条路的职责：他们由城镇同屏的外观推送（<c>TownSessionDirectory.RefreshAppearanceAsync</c>
    /// 的重放）提供，两边共同保证本地注册表覆盖"能右键到的人"。
    /// </remarks>
    private async Task PushOnlineFriendRecordsAsync(
        string accountName,
        string characterName,
        CancellationToken cancellationToken)
    {
        var friends = await _store.GetFriendsAsync(accountName, cancellationToken);
        foreach (var friend in friends)
        {
            if (!_channels.TryGetValue(friend.Name, out var friendChannel))
            {
                continue;
            }

            var record = await CreateMinimalPeerRecordAsync(friend.Name, friendChannel, cancellationToken);
            if (record is not null)
            {
                await SendAsync(characterName, record, cancellationToken);
            }
        }
    }

    private async Task NotifyFriendOwnersAsync(
        string characterName,
        bool isOnline,
        CancellationToken cancellationToken)
    {
        foreach (var (ownerCharacterName, channel) in _channels)
        {
            try
            {
                var friends = await _store.GetFriendsAsync(channel.AccountName, cancellationToken);
                var storedName = FindTrackedName(friends, characterName);
                if (storedName is null)
                {
                    continue;
                }

                // 好友上线时先推他的 mode-0 USERINFO 记录，再播报 BUDDY_IN：记录先到，
                // 客户端后续按名字刷新 entry+0x38（“查看信息”的菜单门要用）与 uid 反查
                // 表。“查看信息”从本地 user 注册表取 uid（没有记录时连 CMD 8 都不发），
                // 悄悄话也要按 uid 反查发送者名字。记录用存档角色的正名构建
                // （CreateMinimalPeerRecordAsync）。
                if (isOnline && _channels.TryGetValue(characterName, out var onlineChannel))
                {
                    var friendRecord = await CreateMinimalPeerRecordAsync(
                        characterName,
                        onlineChannel,
                        cancellationToken);
                    if (friendRecord is not null)
                    {
                        await TrySendToChannelAsync(ownerCharacterName, channel, friendRecord, cancellationToken);
                    }
                }

                // 状态变化只发 BUDDY_IN/BUDDY_OUT。客户端收到 NOTI 56 快照是无条件追加
                // 条目（不去重，按名字更新状态只更新首个命中），状态变化时重推快照会让
                // 每次好友重登都在观察者列表里多出一行同名好友；ec67261 的线上重推正是
                // 这样被真机验收否掉的。不要再往这里补快照。
                //
                // Use the name the recipient stored: the client matches buddy entries by
                // the exact name it sent when the friend was added.
                var storedNameBytes = ClientEncoding.GetBytes(storedName);
                if (isOnline)
                {
                    // 上线只有一条 BUDDY_IN（flag=0，正常显示）。
                    await TrySendToChannelAsync(
                        ownerCharacterName,
                        channel,
                        GameProtocolEngine.CreateBuddyIn(state: 0, storedNameBytes),
                        cancellationToken);
                    continue;
                }

                // 下线要**两条** BUDDY_OUT，才能同时保留退出提示与灰色“未连接”样式：
                //   第 1 条 flag=0 → 触发客户端聊天栏的“[%s]已退出游戏”提示（0x501140），
                //                    行样式不动；
                //   第 2 条 flag=1 → 行转灰/排末组（entry+0xBC=1），同时抑制它自己的
                //                    重复提示（0x501140 的 flag 门），所以不会打两遍。
                // 顺序必须是 0 → 1：反过来第二条会把第一条的提示抑制掉。两条的名字与
                // 首字节完全一致，只有末尾 flag 不同。
                await TrySendToChannelAsync(
                    ownerCharacterName,
                    channel,
                    GameProtocolEngine.CreateBuddyOut(storedNameBytes, flag: 0),
                    cancellationToken);
                await TrySendToChannelAsync(
                    ownerCharacterName,
                    channel,
                    GameProtocolEngine.CreateBuddyOut(storedNameBytes, flag: 1),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                // A store failure for one owner must not stop the remaining notifications.
                _logger.LogWarning(
                    exception,
                    "Failed to tell {Owner} that {Character} went {State}.",
                    channel.AccountName,
                    characterName,
                    isOnline ? "online" : "offline");
            }
        }
    }

    private static string? FindTrackedName(
        IReadOnlyList<AccountFriendRecord> friends,
        string characterName)
    {
        foreach (var friend in friends)
        {
            if (string.Equals(friend.Name, characterName, StringComparison.OrdinalIgnoreCase))
            {
                return friend.Name;
            }
        }

        return null;
    }

    private async ValueTask SendAsync(
        string characterName,
        GameServerPacket packet,
        CancellationToken cancellationToken)
    {
        if (!_channels.TryGetValue(characterName, out var channel))
        {
            // The recipient logged out between the command and the push: drop it silently.
            return;
        }

        await channel.Send(packet);
    }

    private async Task TrySendToChannelAsync(
        string characterName,
        Channel channel,
        GameServerPacket packet,
        CancellationToken cancellationToken)
    {
        try
        {
            await channel.Send(packet);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to push protocol {Protocol} to {Character}.",
                packet.ProtocolId,
                characterName);
        }
    }

    private ValueTask SendNoticeAsync(
        string characterName,
        string text,
        CancellationToken cancellationToken) =>
        SendAsync(
            characterName,
            GameProtocolEngine.CreateMessageNotification(
                GameMessageType.Type03White,
                0,
                ClientEncoding.GetBytes(text)),
            cancellationToken);

    private static string DescribeFailure(FriendUpdateFailure failure) => failure switch
    {
        FriendUpdateFailure.CharacterNotFound => "找不到该角色",
        FriendUpdateFailure.AlreadyFriend => "已经是好友",
        FriendUpdateFailure.Self => "不能加自己",
        FriendUpdateFailure.FriendListFull => "好友已满",
        FriendUpdateFailure.NotFriend => "不是好友",
        _ => "操作失败"
    };

    private static Encoding CreateClientEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            936,
            EncoderFallback.ReplacementFallback,
            DecoderFallback.ReplacementFallback);
    }

    private readonly record struct Channel(
        string AccountName,
        ushort AreaUserId,
        Guid SessionId,
        Func<GameServerPacket, ValueTask> Send);
}
