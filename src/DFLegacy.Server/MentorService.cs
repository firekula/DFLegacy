using System.Collections.Concurrent;
using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>ACT1 mentorship, canonical edge growth, and attachment-owned online reward caches.</summary>
public sealed class MentorService(JsonGameStore store, TimeProvider clock, ILogger<MentorService> logger,
    Func<int>? mentorGoldRand31 = null) : BackgroundService
{
    private sealed record Channel(string Account, Guid CharacterId, string Name, Guid SessionId,
        Func<GameServerPacket, ValueTask> Send)
    {
        internal readonly Queue<GameServerPacket> Outbox = new();
        internal readonly SemaphoreSlim DeliveryGate = new(1, 1);
        internal bool Retired;
        internal readonly Guid Incarnation = Guid.NewGuid();
        internal long LastGrowthTimestamp;
        internal long LastGoldTimestamp;
        internal int GoldDelay;
        internal Action? GoldReady;
        internal Guid? RewardMaster;
        internal byte RewardLevel;
    }
    private sealed record Pending(Guid Requester, Guid RequesterSession, Guid TargetSession);
    private sealed record Delivery(Channel Channel, GameServerPacket Packet);
    private readonly Dictionary<Guid, Channel> _channels = [];
    private readonly Dictionary<Guid, Pending> _pending = [];
    private readonly ConcurrentDictionary<Guid, byte> _refresh = new();
    private readonly ConcurrentDictionary<Guid, Guid> _goldRefresh = new();
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
    public bool HasPendingGold(Guid id, Guid incarnation) => _goldRefresh.GetValueOrDefault(id) == incarnation && incarnation != Guid.Empty;
    public bool TryTakeGoldNotice(Guid id, Guid incarnation) =>
        ((ICollection<KeyValuePair<Guid, Guid>>)_goldRefresh).Remove(new(id, incarnation));
    internal void DeferGoldNotice(Guid id, Guid incarnation) => _goldRefresh.TryAdd(id, incarnation);
    private int GoldDelay() => MentorGoldTimerPolicy.DelaySeconds(MentorGoldTimerPolicy.NextJitter(mentorGoldRand31
        ?? (() => (GameRandomSource.Shared.Next(65536) << 15) | (GameRandomSource.Shared.Next(65536) & 0x7FFF))));
    private void NotifyGold(MentorGoldFlushResult result)
    {
        if (!result.NewlyCommitted || result.Receipt is not { CreditedGold: > 0 }
            || !_channels.TryGetValue(result.MasterId, out var master) || master.Incarnation != result.MasterIncarnation) return;
        _goldRefresh[master.CharacterId] = master.Incarnation;
        master.GoldReady?.Invoke();
    }

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
        MentorListEntry Entry(MentorListMember member, uint experience)
        {
            var growth = MentorGrowthPolicy.Project(experience);
            return new(_channels.ContainsKey(member.Character.Id) ? (byte)1 : (byte)255,
                Level(member.Character), Name(member.Character.Name), member.BlocksViewer ? (byte)1 : (byte)0,
                growth.Level, growth.Experience, growth.NextExperience);
        }
        deliveries.Add(new(channel, GameProtocolEngine.CreateMentorListReply(
            roster?.Master is { } master ? Entry(master, roster!.Owner.MentorRelationshipExperience) : null,
            roster?.Apprentices.Select(c => Entry(c, c.Character.MentorRelationshipExperience)).ToArray() ?? [])));
    }

    private void CancelRequests(Guid id)
    {
        foreach (var pair in _pending.Where(p => p.Key == id || p.Value.Requester == id).ToArray())
            _pending.Remove(pair.Key);
    }

    public async Task<Guid?> AttachChannelAsync(string account, Guid characterId, ushort areaUserId, Guid sessionId,
        Func<GameServerPacket, ValueTask> send, CancellationToken ct = default, Action? goldReady = null)
    {
        Guid? attached = null;
        await LifecycleAsync(characterId, async () =>
    {
        var character = await store.GetCharacterAsync(account, characterId, ct);
        if (character is null) return;
        Channel? retired = null;
        await RunAsync(async unused =>
        {
            if (_channels.Remove(characterId, out retired))
            {
                retired.Retired = true; retired.Outbox.Clear(); ClearDependentCaches(characterId);
                await store.SetMentorGoldEndpointAsync(characterId, retired.Incarnation, false, ct);
                _goldRefresh.TryRemove(characterId, out _);
            }
            CancelRequests(characterId);
        }, ct);
        if (retired is not null) await DrainAsync(retired);
        await RunAsync(async deliveries =>
        {
            var channel = new Channel(account, characterId, character.Name, sessionId, send)
            { LastGrowthTimestamp = clock.GetTimestamp(), LastGoldTimestamp = clock.GetTimestamp(),
                GoldDelay = GoldDelay(), GoldReady = goldReady };
            _channels[characterId] = channel;
            await store.SetMentorGoldEndpointAsync(characterId, channel.Incarnation, true, ct);
            attached = channel.Incarnation;
            await ReconcileCachesAsync(ct);
            await AddListAsync(deliveries, channel, ct);
            await AddPresenceAsync(deliveries, characterId, online: true, ct);
        }, ct);
        }, ct);
        return attached;
    }

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

    public Task DetachChannelAsync(Guid id, Guid sessionId, CancellationToken ct = default) =>
        DetachChannelCoreAsync(id, sessionId, null, () => MentorGoldRetirementReason.NormalLeave, ct);

    public Task DetachChannelAsync(Guid id, Guid sessionId, Guid incarnation,
        Func<MentorGoldRetirementReason> retirementReason, CancellationToken ct = default) =>
        DetachChannelCoreAsync(id, sessionId, incarnation, retirementReason, ct);

    private Task DetachChannelCoreAsync(Guid id, Guid sessionId, Guid? incarnation,
        Func<MentorGoldRetirementReason> reason, CancellationToken ct) => LifecycleAsync(id, async () =>
    {
        Channel? retired = null;
        await RunAsync(async deliveries =>
        {
            retired = Current(id, sessionId);
            if (retired is null || incarnation is { } token && retired.Incarnation != token) return;
            NotifyGold(await store.RetireMentorGoldEndpointAsync(id, retired.Incarnation, reason, ct));
            retired.Retired = true; retired.Outbox.Clear();
            _channels.Remove(id);
            ClearDependentCaches(id);
            await ReconcileCachesAsync(ct);
            CancelRequests(id);
            _refresh.TryRemove(id, out _);
            _goldRefresh.TryRemove(id, out _);
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
        await ReconcileCachesAsync(ct);
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
        await ReconcileCachesAsync(ct);
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
        await ReconcileCachesAsync(ct);
        await GrowDueEndpointsAsync(deliveries, ct);
        await FlushDueGoldAsync(ct);
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

    private void ClearDependentCaches(Guid master)
    {
        foreach (var channel in _channels.Values.Where(c => c.RewardMaster == master))
        { channel.RewardMaster = null; channel.RewardLevel = 0; }
    }

    private void ReconcileCache(Channel channel, MentorRoster? roster)
    {
        var master = roster?.Master is { } parent && _channels.ContainsKey(parent.Character.Id)
            ? parent.Character.Id : (Guid?)null;
        if (channel.RewardMaster == master) return;
        channel.RewardMaster = master;
        channel.RewardLevel = master is null ? (byte)0 : MentorGrowthPolicy.Project(roster!.Owner.MentorRelationshipExperience).Level;
    }

    private async Task ReconcileCachesAsync(CancellationToken ct)
    {
        foreach (var channel in _channels.Values)
            ReconcileCache(channel, await store.GetMentorRosterAsync(channel.CharacterId, ct));
    }

    /// <summary>The canonical read under service→store locks is reward eligibility's linearization point.</summary>
    public async Task<double> GetExperienceBonusRateAsync(Guid characterId, Guid sessionId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var channel = Current(characterId, sessionId);
            if (channel is null) return 0;
            ReconcileCache(channel, await store.GetMentorRosterAsync(characterId, ct));
            return MentorGrowthPolicy.Rate(channel.RewardLevel);
        }
        finally { _gate.Release(); }
    }

    private async Task GrowDueEndpointsAsync(List<Delivery> deliveries, CancellationToken ct)
    {
        var now = clock.GetTimestamp();
        foreach (var channel in _channels.Values.ToArray())
        {
            if (clock.GetElapsedTime(channel.LastGrowthTimestamp, now) < TimeSpan.FromSeconds(600)) continue;
            // Claim this incarnation's event before awaiting persistence; late or uncertain periods are never retried.
            channel.LastGrowthTimestamp = now;
            var result = await store.GrowMentorEndpointAsync(channel.CharacterId, _channels.Keys.ToHashSet(), ct);
            logger.LogInformation("Mentor 600-second endpoint {Character} attachment {Attachment} status {Status}: {Edges}",
                channel.CharacterId, channel.Incarnation, result.Status,
                string.Join(",", result.Edges.Select(e => $"{e.ChildId}->{e.MasterId}={e.Experience}")));
            if (result.Status != DungeonRewardLedgerStatus.Committed || result.Edges.Count == 0) continue;
            foreach (var edge in result.Edges.Where(e => MentorGrowthPolicy.IsLevelUp(e.Experience)))
            {
                var master = await store.GetMentorRosterAsync(edge.MasterId, ct);
                var level = MentorGrowthPolicy.Project(edge.Experience).Level;
                foreach (var child in master?.Apprentices ?? [])
                    if (_channels.TryGetValue(child.Character.Id, out var recipient))
                        recipient.RewardLevel = Math.Max(recipient.RewardLevel, level);
            }
            foreach (var id in result.Edges.SelectMany(e => new[] { e.ChildId, e.MasterId }).Distinct())
            {
                _refresh[id] = 1;
                if (_channels.TryGetValue(id, out var recipient)) await AddListAsync(deliveries, recipient, ct);
            }
        }
    }

    private async Task FlushDueGoldAsync(CancellationToken ct)
    {
        var now = clock.GetTimestamp();
        foreach (var channel in _channels.Values)
        {
            if (clock.GetElapsedTime(channel.LastGoldTimestamp, now) < TimeSpan.FromSeconds(channel.GoldDelay)) continue;
            channel.LastGoldTimestamp = now;
            channel.GoldDelay = GoldDelay();
            NotifyGold(await store.FlushMentorGoldAsync(channel.CharacterId, channel.Incarnation, Guid.NewGuid(), ct));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await SweepChangesAsync(stoppingToken);
    }
}
