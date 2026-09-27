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
    /// snapshot to that session, marks its offline friends, and sends BUDDY_IN to every
    /// online account whose friend list holds the name.
    /// </summary>
    /// <remarks>
    /// The session id binds the channel to one specific session so a late cleanup from an
    /// older session can never detach a newer one (compare-and-remove in
    /// <see cref="DetachChannelAsync"/>).
    /// </remarks>
    public async Task AttachChannelAsync(
        string accountName,
        string characterName,
        Guid sessionId,
        Func<GameServerPacket, ValueTask> send,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterName);
        ArgumentNullException.ThrowIfNull(send);
        _channels[characterName] = new Channel(accountName, sessionId, send);

        // The snapshot is what creates buddy entries client-side, and an empty snapshot is
        // the only way to clear entries left over from another login in the same client
        // process, so it is always pushed.
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
        // 结果先于快照：id 63 的条目写入会带出一个默认状态字节，让随后的快照与
        // BUDDY_OUT 成为条目状态的最后写入者，离线的新好友才不会被它翻成在线。
        await SendAsync(
            characterName,
            GameProtocolEngine.CreateAddFriendResult(
                ClientEncoding.GetBytes(friendName.Trim())),
            cancellationToken);

        // The client can only create buddy entries from a snapshot, so the new friend has
        // to be part of a fresh list; an offline friend is marked right afterwards.
        await PushSnapshotAsync(accountName, characterName, cancellationToken);
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

        // 快照只负责刷新其余条目：通知通道（NOTI 56/54/55）在客户端全是 InsertOrUpdate，
        // 永远删不掉条目；条目真正消失靠紧随其后的 type=1 REMOVE_FRIEND 结果包
        // （客户端 0x414BA7 → CNRDFriendManager::RemoveEntryByName → vector::erase）。
        await PushSnapshotAsync(accountName, characterName, cancellationToken);
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
    /// The target's main-chat line is the NOTI 12 the client resolves from the sender uid,
    /// and both sides additionally get a NOTI 65 for their 1:1 window. The sender gets no
    /// NOTI 12 echo: the client's own-uid branch is a design-time no-op (it looks up a 1:1
    /// window pointer that is permanently NULL), so the echo would never render and would
    /// only be recorded in the local whisper history.
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
            cancellationToken);

    /// <summary>
    /// Routes a 1:1 window message (CMD 17 type 18) to the live session that owns the peer
    /// name. Both sides get a NOTI 65 only: there is no main-chat line and no appearance
    /// push, because the client finds its 1:1 window by name and keeps its history locally.
    /// </summary>
    public Task SendMessengerMessageAsync(
        string characterName,
        byte[] peerNameBytes,
        byte[] textBytes,
        CancellationToken cancellationToken = default) =>
        RoutePeerMessageAsync(
            characterName,
            peerNameBytes,
            textBytes,
            mainChatLine: null,
            receiverPreface: null,
            cancellationToken);

    private async Task RoutePeerMessageAsync(
        string characterName,
        byte[] peerNameBytes,
        byte[] textBytes,
        GameServerPacket? mainChatLine,
        GameServerPacket? receiverPreface,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peerNameBytes);
        ArgumentNullException.ThrowIfNull(textBytes);

        var peerName = ClientEncoding.GetString(peerNameBytes);
        if (string.Equals(peerName, characterName, StringComparison.OrdinalIgnoreCase))
        {
            await SendNoticeAsync(characterName, "不能给自己发悄悄话", cancellationToken);
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

        await SendAsync(
            characterName,
            GameProtocolEngine.CreateMessageOtherChannel(peerNameBytes, textBytes),
            cancellationToken);
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
            var online = _channels.ContainsKey(friend.Name);
            entries.Add(new GameFriendEntry(nameBytes, online));
            if (!online)
            {
                offlineNames.Add(nameBytes);
            }
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

                // Use the name the recipient stored: the client matches buddy entries by
                // the exact name it sent when the friend was added.
                var packet = isOnline
                    ? GameProtocolEngine.CreateBuddyIn(state: 0, ClientEncoding.GetBytes(storedName))
                    : GameProtocolEngine.CreateBuddyOut(ClientEncoding.GetBytes(storedName));
                await TrySendToChannelAsync(ownerCharacterName, channel, packet, cancellationToken);
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
        Guid SessionId,
        Func<GameServerPacket, ValueTask> Send);
}
