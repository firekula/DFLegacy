using System.Collections.Concurrent;
using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>ACT1 师徒基础关系；会话请求与持久化规范关系分别管理。</summary>
public sealed class MentorService(JsonGameStore store, TimeProvider clock, ILogger<MentorService> logger) : BackgroundService
{
    private sealed record Channel(string Account, Guid CharacterId, string Name, Guid SessionId,
        Func<GameServerPacket, ValueTask> Send)
    {
        internal readonly Queue<GameServerPacket> Outbox = new();
        internal readonly SemaphoreSlim DeliveryGate = new(1, 1);
        internal bool Retired;
    }
    private sealed record Pending(Guid Requester, Guid RequesterSession, Guid TargetSession);
    private sealed record Delivery(Channel Channel, GameServerPacket Packet);
    private readonly Dictionary<Guid, Channel> _channels = [];
    private readonly Dictionary<Guid, Pending> _pending = [];
    private readonly ConcurrentDictionary<Guid, byte> _refresh = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _lifecycles = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly Encoding TextEncoding = CreateEncoding();
    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }
    private static byte[] Name(string value) => TextEncoding.GetBytes(value);
    private static byte Level(CharacterRecord c) => checked((byte)c.Level);
    private Channel? Current(Guid id, Guid session) =>
        _channels.TryGetValue(id, out var channel) && channel.SessionId == session ? channel : null;
    public bool TryTakeRefresh(Guid characterId) => _refresh.TryRemove(characterId, out _);

    private async Task RunAsync(Func<List<Delivery>, Task> action, CancellationToken ct)
    {
        var deliveries = new List<Delivery>();
        Channel[] drains;
        await _gate.WaitAsync(ct);
        try
        {
            await action(deliveries);
            var queued = new HashSet<Channel>(ReferenceEqualityComparer.Instance);
            foreach (var delivery in deliveries)
            {
                if (delivery.Channel.Retired) continue;
                delivery.Channel.Outbox.Enqueue(delivery.Packet);
                queued.Add(delivery.Channel);
            }
            drains = queued.ToArray();
        }
        finally { _gate.Release(); }
        await Task.WhenAll(drains.Select(DrainAsync));
    }

    private async Task DrainAsync(Channel channel)
    {
        await channel.DeliveryGate.WaitAsync();
        try
        {
            while (true)
            {
                GameServerPacket? packet = null;
                await _gate.WaitAsync();
                try
                {
                    if (channel.Retired) channel.Outbox.Clear();
                    else if (channel.Outbox.Count > 0) packet = channel.Outbox.Dequeue();
                }
                finally { _gate.Release(); }
                if (packet is null) break;
                if (Volatile.Read(ref channel.Retired)) continue;
                try { await channel.Send(packet); }
                catch (Exception e) when (CrossSessionDelivery.IsBrokenChannel(e))
                { logger.LogWarning(e, "Mentor packet {Protocol} could not reach {Character}.", packet.ProtocolId, channel.CharacterId); }
            }
        }
        finally { channel.DeliveryGate.Release(); }
    }

    private async Task LifecycleAsync(Guid id, Func<Task> action, CancellationToken ct)
    {
        var gate = _lifecycles.GetOrAdd(id, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try { await action(); }
        finally { gate.Release(); }
    }

    private async Task AddListAsync(List<Delivery> deliveries, Channel channel, CancellationToken ct)
    {
        var roster = await store.GetMentorRosterAsync(channel.CharacterId, ct);
        MentorListEntry Entry(MentorListMember member) => new(
            _channels.ContainsKey(member.Character.Id) ? (byte)1 : (byte)255,
            Level(member.Character), Name(member.Character.Name), member.BlocksViewer ? (byte)1 : (byte)0);
        deliveries.Add(new(channel, GameProtocolEngine.CreateMentorListReply(
            roster?.Master is { } master ? Entry(master) : null,
            roster?.Apprentices.Select(Entry).ToArray() ?? [])));
    }

    private void CancelRequests(Guid id)
    {
        foreach (var pair in _pending.Where(p => p.Key == id || p.Value.Requester == id).ToArray())
            _pending.Remove(pair.Key);
    }

    public Task AttachChannelAsync(string account, Guid characterId, ushort areaUserId, Guid sessionId,
        Func<GameServerPacket, ValueTask> send, CancellationToken ct = default) => LifecycleAsync(characterId, async () =>
    {
        var character = await store.GetCharacterAsync(account, characterId, ct);
        if (character is null) return;
        Channel? retired = null;
        await RunAsync(_ =>
        {
            if (_channels.Remove(characterId, out retired))
            { retired.Retired = true; retired.Outbox.Clear(); }
            CancelRequests(characterId);
            return Task.CompletedTask;
        }, ct);
        if (retired is not null) await DrainAsync(retired);
        await RunAsync(async deliveries =>
        {
            var channel = new Channel(account, characterId, character.Name, sessionId, send);
            _channels[characterId] = channel;
            await AddListAsync(deliveries, channel, ct);
            await AddPresenceAsync(deliveries, characterId, online: true, ct);
        }, ct);
    }, ct);

    private async Task AddPresenceAsync(List<Delivery> deliveries, Guid id, bool online, CancellationToken ct)
    {
        var roster = await store.GetMentorRosterAsync(id, ct);
        if (roster is null) return;
        var peers = roster.Apprentices.Concat(roster.Master is { } master ? [master] : []);
        foreach (var peer in peers)
            if (!peer.BlocksViewer && _channels.TryGetValue(peer.Character.Id, out var channel))
                deliveries.Add(new(channel, GameProtocolEngine.CreateMentorPresence(online,
                    online ? (byte)1 : (byte)255, Level(roster.Owner), Name(roster.Owner.Name))));
    }

    public Task DetachChannelAsync(Guid id, Guid sessionId, CancellationToken ct = default) => LifecycleAsync(id, async () =>
    {
        Channel? retired = null;
        await RunAsync(async deliveries =>
        {
            retired = Current(id, sessionId);
            if (retired is null) return;
            retired.Retired = true; retired.Outbox.Clear();
            _channels.Remove(id);
            CancelRequests(id);
            _refresh.TryRemove(id, out _);
            await AddPresenceAsync(deliveries, id, online: false, ct);
        }, ct);
        if (retired is not null) await DrainAsync(retired);
    }, ct);

    public Task RequestAsync(Guid id, Guid sessionId, string targetName, CancellationToken ct = default) => RunAsync(async deliveries =>
    {
        var requester = Current(id, sessionId);
        if (requester is null) return;
        var target = _channels.Values.FirstOrDefault(c => string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase));
        var error = target is null ? (byte)49 : await store.CheckMentorPairAsync(id, target.CharacterId, clock.GetLocalNow(), ct);
        if (error == 0 && (_pending.ContainsKey(target!.CharacterId)
            || (_pending.TryGetValue(id, out var incoming) && incoming.Requester == target.CharacterId))) error = 41;
        var character = await store.GetCharacterAsync(requester.Account, id, ct);
        if (character is null) error = 49;
        deliveries.Add(new(requester, GameProtocolEngine.CreateMentorRequestReply(Name(target?.Name ?? targetName), error)));
        if (error != 0) return;
        _pending[target!.CharacterId] = new(id, sessionId, target.SessionId);
        deliveries.Add(new(target, GameProtocolEngine.CreateMentorInvitation((ushort)character!.Level, Name(character.Name))));
        logger.LogInformation("Mentor request from {Requester} to {Target}.", requester.Name, target.Name);
    }, ct);

    public Task ReplyAsync(Guid id, Guid sessionId, byte reply, CancellationToken ct = default) => RunAsync(async deliveries =>
    {
        var responder = Current(id, sessionId);
        if (responder is null || reply is not (1 or 2)
            || !_pending.TryGetValue(id, out var pending) || pending.TargetSession != sessionId) return;
        _pending.Remove(id);
        var requester = Current(pending.Requester, pending.RequesterSession);
        if (requester is null) return;
        if (reply == 2)
        {
            deliveries.Add(new(requester, GameProtocolEngine.CreateMentorEstablished(2, 0, 0, Name(responder.Name))));
            return;
        }
        var result = await store.EstablishMentorAsync(requester.CharacterId, id, clock.GetLocalNow(), ct);
        if (result.Status != DungeonRewardLedgerStatus.Committed)
        {
            deliveries.Add(new(responder, result.Status == DungeonRewardLedgerStatus.Rejected
                ? GameProtocolEngine.CreateMentorRequestReply(Name(requester.Name), result.Error)
                : new GameServerPacket(GameProtocolEngine.CommandPacketType, 80, [0])));
            return;
        }
        var requesterCharacter = result.Requester!; var responderCharacter = result.Target!;
        var requesterRole = requesterCharacter.Level < responderCharacter.Level ? (byte)1 : (byte)2;
        deliveries.Add(new(requester, GameProtocolEngine.CreateMentorEstablished(1, requesterRole,
            Level(responderCharacter), Name(responderCharacter.Name))));
        deliveries.Add(new(responder, GameProtocolEngine.CreateMentorReply(requesterRole == 1 ? (byte)2 : (byte)1,
            Level(requesterCharacter), Name(requesterCharacter.Name))));
        _refresh[requester.CharacterId] = 1; _refresh[id] = 1;
        logger.LogInformation("Mentor relation committed between {Requester} and {Responder}.", requester.Name, responder.Name);
    }, ct);

    public Task SendListAsync(Guid id, Guid sessionId, CancellationToken ct = default) => RunAsync(async deliveries =>
    {
        if (Current(id, sessionId) is { } channel) await AddListAsync(deliveries, channel, ct);
    }, ct);

    public Task RemoveAsync(Guid id, Guid sessionId, string targetName, CancellationToken ct = default) => RunAsync(async deliveries =>
    {
        var requester = Current(id, sessionId);
        if (requester is null) return;
        var result = await store.RemoveMentorAsync(id, targetName, clock.GetLocalNow(), ct);
        if (result.Status != DungeonRewardLedgerStatus.Committed)
        { deliveries.Add(new(requester, GameProtocolEngine.CreateMentorRemoved([], false))); return; }
        if (result.RequesterEmpty) deliveries.Add(new(requester, GameProtocolEngine.CreateMentorReset()));
        deliveries.Add(new(requester, GameProtocolEngine.CreateMentorRemoved(Name(result.Target!.Name))));
        if (_channels.TryGetValue(result.Target.Id, out var peer))
        {
            if (result.TargetEmpty) deliveries.Add(new(peer, GameProtocolEngine.CreateMentorReset()));
            deliveries.Add(new(peer, GameProtocolEngine.CreateMentorRemovalNotice(Name(result.Requester!.Name))));
        }
        _refresh[id] = 1; _refresh[result.Target.Id] = 1;
        logger.LogInformation("Mentor relation removed by {Requester} with {Target}.", requester.Name, result.Target.Name);
    }, ct);

    internal Task SweepChangesAsync(CancellationToken ct = default) => RunAsync(async deliveries =>
    {
        while (store.TryTakeMentorRemovalNotice(out var notice))
        {
            _refresh[notice!.CharacterId] = 1;
            if (!_channels.TryGetValue(notice.CharacterId, out var channel)) continue;
            var roster = await store.GetMentorRosterAsync(notice.CharacterId, ct);
            if (roster is null || (roster.Master is null && roster.Apprentices.Count == 0))
                deliveries.Add(new(channel, GameProtocolEngine.CreateMentorReset()));
            deliveries.Add(new(channel, GameProtocolEngine.CreateMentorRemovalNotice(Name(notice.PeerName))));
            await AddListAsync(deliveries, channel, ct);
        }
    }, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await SweepChangesAsync(stoppingToken);
    }
}
