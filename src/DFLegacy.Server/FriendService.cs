using System.Collections.Concurrent;
using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// Turns account-level friend-list changes into the buddy notifications the 2008 client
/// understands and routes whispers between live game-channel sessions.
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
    /// Registers a live session for a character and announces it: pushes the friend-list
    /// snapshot to that session, pushes a USERINFO record for every online friend, marks
    /// its offline friends, and sends BUDDY_IN to every online account whose friend list
    /// holds the name.
    /// </summary>
    /// <remarks>
    /// The session id binds the channel to one specific session so a late cleanup from an
    /// older session can never detach a newer one (compare-and-remove in
    /// <see cref="DetachChannelAsync"/>). <paramref name="areaUserId"/> is the area user id
    /// allocated for this session at SELECT_CHARACTER: it is not written into the friend
    /// snapshot any more (R+0x21 must stay 0); it identifies this character in the USERINFO
    /// record pushed to friends and in the CMD 8 mode-3 reverse lookup.
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

        // 快照每个会话只推这一次：客户端收到 NOTI 56 是无条件追加条目（不去重），
        // 同名再推就会多出一行；之后的状态变化一律只走 BUDDY_IN/BUDDY_OUT。
        // 空快照在这里仍要推：客户端好友管理器随选角界面重建时是空的，同一进程
        // 换号登录时靠它把上一份列表的状态归位。
        await PushSnapshotAsync(accountName, characterName, cancellationToken);

        // 好友行/“查看信息”/社区菜单都先查本地 user 注册表（FindByUid）：没有 USERINFO
        // 记录时点“查看信息”连 CMD 8 都不会发。登录时把当前在线好友各推一条 mode-0
        // 记录（uid = 对方的区用户 id）。
        await PushOnlineFriendRecordsAsync(accountName, characterName, cancellationToken);

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
    public bool TryGetCharacterByAreaUserId(ushort areaUserId, out string characterName)
    {
        if (areaUserId != 0)
        {
            foreach (var (name, channel) in _channels)
            {
                if (channel.AreaUserId == areaUserId)
                {
                    characterName = name;
                    return true;
                }
            }
        }

        characterName = string.Empty;
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
        // id 63 的 handler 直接调 0x500B00 无条件追加新条目（其 body 里的记录字段
        // A..F 只填 A=0，B..F 读到流尾按 0），所以它自己就能建条目——**不能再补整表
        // 快照**：NOTI 56 同样是无条件追加，会把老好友全部再加一份（重复行的根因）。
        var addedName = friendName.Trim();
        var addedNameBytes = ClientEncoding.GetBytes(addedName);
        await SendAsync(
            characterName,
            GameProtocolEngine.CreateAddFriendResult(addedNameBytes),
            cancellationToken);

        // id 63 追加出来的条目停在构造默认状态，若好友此刻已在线，点开列表会看到一条
        // "刚加的在线好友显示离线"。紧接着补一条状态更新：BUDDY_IN/BUDDY_OUT 在客户端
        // 是按名字就地改状态（只写首个同名条目，0x4FFDC0）且**不会追加**，正好落在刚
        // 追加的那一行上；在线发 BUDDY_IN(0)，离线发 BUDDY_OUT(-1) 把默认值钉死。
        // 只发给执行添加的会话：这条新行只存在于他的列表里，别的会话收到也无处可改。
        await SendAsync(
            characterName,
            _channels.ContainsKey(addedName)
                ? GameProtocolEngine.CreateBuddyIn(state: 0, addedNameBytes)
                : GameProtocolEngine.CreateBuddyOut(addedNameBytes),
            cancellationToken);
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
    /// Routes a whisper (CMD 17 type 1/7) to the live session that owns the target name.
    /// </summary>
    /// <remarks>
    /// The target's main-chat line is the NOTI 12 the client resolves from the sender uid
    /// (that is why the target gets the sender's appearance first), and the target also
    /// gets the 1:1-window line as a NOTI 65 with type 0x12, flag 0 and the sender's name.
    /// The sender gets no NOTI 12 echo — the client's own-uid branch is a design-time no-op
    /// (it looks up a 1:1 window pointer that is permanently NULL) — and no 0x12 NOTI 65
    /// either: it gets exactly one NOTI 65 with type 1/7, flag 0 and the <em>target</em>
    /// name, which the client prints in the main chat as '%s : %s'; its own 1:1 window line
    /// is appended locally by the client after sending. The target's mode-0 record is
    /// pushed to the sender first so the local line and the friend row can resolve the peer.
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
            senderLineMessageType: messageType,
            selfNoticeText: "不能给自己发悄悄话",
            cancellationToken);

    /// <summary>
    /// Routes a 1:1 window message (CMD 17 type 18) to the live session that owns the peer
    /// name. Both sides get the other party's mode-0 user-info record and then a NOTI 65:
    /// there is no main-chat line, and the history stays in the client's local ring buffer.
    /// </summary>
    /// <remarks>
    /// The target receives the sender's appearance exactly like the whisper path; the
    /// sender's side gets a minimal record of the target built by
    /// <see cref="RoutePeerMessageAsync"/>. Both records feed the local user registry
    /// (friend rows and the community menu's “查看信息”), which is why the sender keeps
    /// its record even though a 1:1 line needs no server echo: the client appends its own
    /// line locally after sending. The wire line itself is flag 0 — a non-zero flag is a
    /// status code and makes the client drop the body.
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
            // 1:1 发送方自己的那一行由客户端发完包后本地追加（0x811D8A → 0x811A00），
            // 服务端不要再回显 NOTI 65。
            senderLineMessageType: null,
            selfNoticeText: "不能和自己进行1:1聊天",
            cancellationToken);

    private async Task RoutePeerMessageAsync(
        string characterName,
        byte[] peerNameBytes,
        byte[] textBytes,
        GameServerPacket? mainChatLine,
        GameServerPacket? receiverPreface,
        byte? senderLineMessageType,
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

        if (!_channels.TryGetValue(peerName, out var target))
        {
            await SendNoticeAsync(characterName, "对方不在线", cancellationToken);
            return;
        }

        // The client turns the sender uid into a name only after it has seen that
        // character's user-info record, so the appearance goes first, then the main-chat
        // line, then the 1:1 line. Every target-side send is guarded per recipient: a dead
        // target channel must still let the sender see its own 1:1 line instead of an
        // exception escaping the command handler.
        if (receiverPreface is not null)
        {
            await TrySendToChannelAsync(peerName, target, receiverPreface, cancellationToken);
        }

        if (mainChatLine is not null)
        {
            await TrySendToChannelAsync(peerName, target, mainChatLine, cancellationToken);
        }

        // NOTI 65 carries a name, not a uid: the target's 1:1 window keys on the sender's
        // name, while the sender's own window keys on the peer name it used.
        await TrySendToChannelAsync(
            peerName,
            target,
            GameProtocolEngine.CreateMessageOtherChannel(
                ClientEncoding.GetBytes(characterName),
                textBytes),
            cancellationToken);

        // 发送者侧先补一条对端的 mode-0 USERINFO：好友行/社区菜单按本地 user 注册表
        // 工作（没有记录时“查看信息”连 CMD 8 都不发），悄悄话的正名也依赖注册表。
        var peerRecord = await CreateMinimalPeerRecordAsync(peerName, target, cancellationToken);
        if (peerRecord is not null)
        {
            await SendAsync(characterName, peerRecord, cancellationToken);
        }

        // 悄悄话发送方自己那一行：客户端本地拼的“给[X] : 文本”会走自 uid 分支被丢弃，
        // 唯一能在主聊天栏显示的是服务端发 [type=1/7][flag=0][目标名][正文]。
        // 1:1（senderLineMessageType == null）不回显：客户端已在窗口本地追加。
        if (senderLineMessageType is { } lineType)
        {
            await SendAsync(
                characterName,
                GameProtocolEngine.CreateMessageOtherChannel(peerNameBytes, textBytes, lineType),
                cancellationToken);
        }
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
            if (_channels.ContainsKey(friend.Name))
            {
                // 在线与否只影响随后的 BUDDY_OUT 补发；记录里的 R+0x21 不再写 uid
                // （逆向要求恒 0），所以这里不取通道的区用户 id。
                entries.Add(new GameFriendEntry(nameBytes, Online: true));
                continue;
            }

            entries.Add(new GameFriendEntry(nameBytes, Online: false));
            offlineNames.Add(nameBytes);
        }

        await SendAsync(characterName, GameProtocolEngine.CreateFriendList(entries), cancellationToken);

        // The 42-byte snapshot record cannot express "offline": 0xFF keeps the client's
        // previous state and -1 only comes from BUDDY_OUT, so every friend without an
        // attached channel is marked offline right after the snapshot.
        foreach (var nameBytes in offlineNames)
        {
            await SendAsync(characterName, GameProtocolEngine.CreateBuddyOut(nameBytes), cancellationToken);
        }
    }

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

                // 状态变化只发 BUDDY_IN/BUDDY_OUT。客户端收到 NOTI 56 快照是无条件追加
                // 条目（不去重，按名字更新状态只更新首个命中），状态变化时重推快照会让
                // 每次好友重登都在观察者列表里多出一行同名好友；ec67261 的线上重推正是
                // 这样被真机验收否掉的。不要再往这里补快照。
                //
                // Use the name the recipient stored: the client matches buddy entries by
                // the exact name it sent when the friend was added.
                var packet = isOnline
                    ? GameProtocolEngine.CreateBuddyIn(state: 0, ClientEncoding.GetBytes(storedName))
                    : GameProtocolEngine.CreateBuddyOut(ClientEncoding.GetBytes(storedName));
                await TrySendToChannelAsync(ownerCharacterName, channel, packet, cancellationToken);

                // 好友上线时把他的 mode-0 USERINFO 记录补推给名单里含他的账号：
                // “查看信息”从本地 user 注册表取 uid（没有记录时连 CMD 8 都不发），
                // 悄悄话也要按 uid 反查发送者名字。记录按接收方的存储拼写之外的角色
                // 正名构建（CreateMinimalPeerRecordAsync 用存档角色记录）。
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
