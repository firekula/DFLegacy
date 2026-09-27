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
/// The session registry remains part of the constructor because Task 4 wires the service
/// with it; this channel-based implementation never needs to query it.
/// </remarks>
public sealed class FriendService
{
    private static readonly Encoding ClientEncoding = CreateClientEncoding();

    private readonly ConcurrentDictionary<string, Channel> _channels =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly JsonGameStore _store;
    private readonly ILogger<FriendService> _logger;

    public FriendService(
        JsonGameStore store,
        CharacterSessionRegistry sessions,
        ILogger<FriendService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sessions);
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

        // An empty snapshot is what tells the client the removed entry is gone.
        await PushSnapshotAsync(accountName, characterName, cancellationToken);
    }

    /// <summary>
    /// Routes a whisper (CMD 17 type 1/7) to the live session that owns the target name.
    /// </summary>
    /// <remarks>
    /// The account name belongs to the Task 4 wiring contract; routing only needs the
    /// sender's character name because the target is resolved through the channel table.
    /// </remarks>
    public async Task WhisperAsync(
        string accountName,
        string characterName,
        ushort senderAreaUserId,
        GameServerPacket? senderAppearance,
        byte[] targetNameBytes,
        byte messageType,
        byte[] textBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetNameBytes);
        ArgumentNullException.ThrowIfNull(textBytes);

        var targetName = ClientEncoding.GetString(targetNameBytes);
        if (string.Equals(targetName, characterName, StringComparison.OrdinalIgnoreCase))
        {
            await SendNoticeAsync(characterName, "不能给自己发悄悄话", cancellationToken);
            return;
        }

        if (!_channels.TryGetValue(targetName, out var target))
        {
            await SendNoticeAsync(characterName, "对方不在线", cancellationToken);
            return;
        }

        var message = GameProtocolEngine.CreateMessageNotification(
            (GameMessageType)messageType,
            senderAreaUserId,
            textBytes);

        // The client turns the sender uid into a name only after it has seen that
        // character's user-info record, so the appearance goes first. Both target-side
        // sends are guarded per recipient: a dead target channel must still let the sender
        // see its echo instead of an exception escaping the command handler.
        if (senderAppearance is not null)
        {
            await TrySendToChannelAsync(targetName, target, senderAppearance, cancellationToken);
        }

        await TrySendToChannelAsync(targetName, target, message, cancellationToken);
        await SendAsync(characterName, message, cancellationToken);
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
