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
/// delegate and the online state in one table means they can never disagree. Task 4
/// attaches a channel when a game session starts and detaches it (or calls
/// <see cref="UnregisterOnlineAsync"/>) when the session ends.
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

    /// <summary>Injects the packet channel of a live session (Task 4 calls this from EntranceService).</summary>
    public void AttachChannel(
        string accountName,
        string characterName,
        Func<GameServerPacket, ValueTask> send)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterName);
        ArgumentNullException.ThrowIfNull(send);
        _channels[characterName] = new Channel(accountName, send);
    }

    /// <summary>Drops a session's channel; friends that are still online are told it went offline.</summary>
    public void DetachChannel(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)
            || !_channels.TryRemove(characterName, out _))
        {
            return;
        }

        // Detaching alone takes the character offline, so friends that are still
        // connected hear about it even when the caller skips UnregisterOnlineAsync.
        _ = NotifyFriendOwnersAsync(characterName, isOnline: false, CancellationToken.None);
    }

    public async Task RegisterOnlineAsync(
        string accountName,
        string characterName,
        CancellationToken cancellationToken = default)
    {
        if (!_channels.ContainsKey(characterName))
        {
            // Online state is the channel table: without a channel there is nobody to
            // snapshot and nobody who could answer a whisper, so stay silent.
            return;
        }

        await PushSnapshotAsync(accountName, characterName, skipWhenEmpty: true, cancellationToken);
        await NotifyFriendOwnersAsync(characterName, isOnline: true, cancellationToken);
    }

    public async Task UnregisterOnlineAsync(
        string accountName,
        string characterName,
        CancellationToken cancellationToken = default)
    {
        // Remove only the channel that belongs to this account: a reconnect may already
        // have attached a fresh session under the same character name.
        var removed = _channels.TryGetValue(characterName, out var channel)
            && string.Equals(channel.AccountName, accountName, StringComparison.OrdinalIgnoreCase)
            && _channels.TryRemove(new KeyValuePair<string, Channel>(characterName, channel));
        if (!removed)
        {
            // Somebody else is online under this name (or nobody is): friends already
            // have the right state, so this stale unregister stays silent.
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
        await PushSnapshotAsync(accountName, characterName, skipWhenEmpty: false, cancellationToken);
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
        await PushSnapshotAsync(accountName, characterName, skipWhenEmpty: false, cancellationToken);
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

        if (!_channels.TryGetValue(ClientEncoding.GetString(targetNameBytes), out var target))
        {
            await SendNoticeAsync(characterName, "对方不在线", cancellationToken);
            return;
        }

        if (senderAppearance is not null)
        {
            // The client turns the sender uid into a name only after it has seen that
            // character's user-info record, so the appearance goes first.
            await target.Send(senderAppearance);
        }

        var message = GameProtocolEngine.CreateMessageNotification(
            (GameMessageType)messageType,
            senderAreaUserId,
            textBytes);
        await target.Send(message);
        await SendAsync(characterName, message, cancellationToken);
    }

    private async Task PushSnapshotAsync(
        string accountName,
        string characterName,
        bool skipWhenEmpty,
        CancellationToken cancellationToken)
    {
        var friends = await _store.GetFriendsAsync(accountName, cancellationToken);
        if (skipWhenEmpty && friends.Count == 0)
        {
            // Nothing to create client-side: the buddy window starts empty, so an
            // account with no friends gets no snapshot when it logs in.
            return;
        }

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
        foreach (var (ownerName, channel) in _channels)
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
                await channel.Send(packet);
            }
            catch (Exception exception)
            {
                // One unreachable friend must not stop the remaining notifications.
                _logger.LogWarning(
                    exception,
                    "Failed to tell {Owner} that {Character} went {State}.",
                    ownerName,
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
        Func<GameServerPacket, ValueTask> Send);
}
