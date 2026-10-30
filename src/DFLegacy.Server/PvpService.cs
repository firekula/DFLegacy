using System.Collections.Concurrent;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed record PvpModelSnapshot(GameServerPacket Appearance, GameServerPacket Detail);

internal sealed record PvpAttachment(Guid Id, Guid SessionId, string Account, Guid CharacterId, ushort Uid,
    uint AccountUid, Func<ValueTask<PvpModelSnapshot?>> CaptureModel, Func<GameServerPacket, ValueTask> Send,
    Func<bool> CanEnter, Func<Task> LeaveTownAndParty, Action Wake, Action? Disconnect = null, Func<Task>? RestoreTown = null);

/// <summary>Runtime-only ACT1 individual rooms. Every mutation and enqueue is ordered under one gate;
/// socket writes are drained afterwards per attachment. Immutable matches outlive emptied rooms.</summary>
public sealed class PvpService(JsonGameStore store, GameplayDatagramService udp, PvpExperienceCatalog rating,
    PvpMapCatalog maps, TimeProvider clock, ILogger<PvpService> logger) : BackgroundService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, Channel> _channels = [];
    private readonly ConcurrentDictionary<Guid, Channel> _ingress = [];
    private readonly Dictionary<ushort, Room> _rooms = [];
    private readonly Dictionary<Guid, Match> _matches = [];
    private readonly ConcurrentDictionary<Guid, long> _revisions = [];
    private ushort _nextRoom = 1;

    private sealed class Channel(PvpAttachment attachment)
    {
        internal readonly PvpAttachment A = attachment;
        internal PvpModelSnapshot? Model;
        internal readonly PvpCommandSequence Sequence = new();
        internal long Generation;
        internal readonly Queue<GameServerPacket> Outbox = [];
        internal bool Draining;
        internal Room? Room;
        internal bool Entering, Retired;
        internal Guid Activity;
    }
    private sealed class Room(ushort id, byte selection, byte[] name, ushort map)
    {
        internal readonly Guid Identity = Guid.NewGuid();
        internal readonly ushort Id = id;
        internal readonly byte Selection = selection;
        internal readonly byte[] Name = name;
        internal ushort RequestedMap = map;
        internal byte Host;
        internal readonly Channel?[] Seats = new Channel?[2];
        internal readonly bool[] Closed = new bool[2];
        internal readonly bool[] Ready = new bool[2];
        internal Match? Match;
    }
    private sealed class Participant(Channel channel, byte seat, byte grade)
    {
        internal readonly Channel Channel = channel;
        internal readonly byte Seat = seat, Grade = grade;
        internal bool Alive = true, Forfeit, Ack;
        internal PvpRankReport? Report;
    }
    private enum Phase { Playing, Ranking, Results, Unknown }
    private sealed class Match(Room room, Participant[] players, long started)
    {
        internal readonly Guid Id = Guid.NewGuid();
        internal readonly Room Room = room;
        internal readonly Participant[] Players = players;
        internal Phase Phase;
        internal long Started = started, PhaseStarted = started;
        internal long? FirstResponse;
        internal byte? Winner;
        internal bool Draw, Removing;
    }

    internal bool IsBusy(Guid characterId) => store.Commerce.Is(characterId, CommerceActivity.Pvp);
    internal long Revision(Guid character) => _revisions.GetValueOrDefault(character);
    internal static bool AllowedWhileBusy(byte command) => command is 0 or 2 or 3 or 4 or 7 or 8 or 17 or 53 or 54 or 55 or 56 or 57 or 58 or 59 or 60 or 61 or 62 or 100 or 101 or 120 or 141 or 160 or 161 or 162;

    internal async Task AttachAsync(PvpAttachment attachment, CancellationToken ct = default)
    {
        await RunAsync(async () =>
        {
        if (_channels.TryGetValue(attachment.CharacterId, out var previous))
        {
            await LeaveCoreAsync(previous, detach: true);
            previous.Retired = true; previous.Outbox.Clear();
        }
        var channel = new Channel(attachment);
        _channels[attachment.CharacterId] = channel;
        _ingress[attachment.CharacterId] = channel;
        foreach (var room in _rooms.Values) PublishModels(channel, room);
        Enqueue(channel, GameProtocolEngine.CreatePvpRoomList(_rooms.Values.Select(RoomInfo).ToArray()));
        }, ct);
    }

    internal Task DetachAsync(Guid characterId, Guid attachment, CancellationToken ct = default) => RunAsync(async () =>
    {
        var channel = _channels.TryGetValue(characterId, out var candidate) && candidate.A.Id == attachment ? candidate : null;
        if (channel is null) return;
        await LeaveCoreAsync(channel, detach: true);
        channel.Retired = true; channel.Outbox.Clear();
        _channels.Remove(characterId);
        _ingress.TryRemove(new KeyValuePair<Guid, Channel>(characterId, channel));
    }, ct);

    internal Task RefreshPeersAsync(Guid character, Guid attachment, CancellationToken ct = default) => RunAsync(() =>
    {
        if (Current(character, attachment)?.Room is { } room) PublishPeers(room);
        return Task.CompletedTask;
    }, ct);

    private Channel? Current(Guid character, Guid attachment) =>
        _channels.TryGetValue(character, out var c) && c.A.Id == attachment && !c.Retired ? c : null;

    internal static bool IsPvpCommand(byte command) => command is >= 53 and <= 62 or 100 or 101 or 120;

    internal bool ObserveCommand(Guid character, Guid attachment, byte command, byte[] body) =>
        _ingress.TryGetValue(character, out var channel) && channel.A.Id == attachment
        && !channel.Retired && !IsPvpCommand(command) && channel.Sequence.TryConsume(command, body);

    internal async Task HandleAsync(Guid character, Guid attachment, byte command, byte[] body, CancellationToken ct = default)
    {
        // Admission precedes awaiting the global mutation gate. Queued commands retain the
        // room/phase generation they observed, rather than rebinding to a later match.
        if (!_ingress.TryGetValue(character, out var admitted) || admitted.A.Id != attachment || admitted.Retired) return;
        var generation = Interlocked.Read(ref admitted.Generation);
        if (!admitted.Sequence.TryConsume(command, body)) return;
        if (command is 53 or 54)
        {
            PvpCreateRequest? create = null; ushort target = 0;
            var valid = command == 53 ? GameProtocolEngine.TryReadPvpCreate(body, out create)
                : GameProtocolEngine.TryReadPvpEnter(body, out target);
            if (valid) { await EnterAsync(character, attachment, create, target, generation, ct); return; }
        }
        await RunAsync(async () =>
        {
            var c = Current(character, attachment);
            if (c is null || c != admitted || c.Generation != generation) return;
            var r = c.Room;
            var seat = r is null ? -1 : Array.IndexOf(r.Seats, c);
            switch (command)
            {
                case 55 when r is not null && GameProtocolEngine.TryReadPvpSeat(body, out var requestedSeat, out var state):
                    if (requestedSeat >= 2) break;
                    if (requestedSeat == seat && state == 254) { await LeaveCoreAsync(c, false); return; }
                    if (r.Match is not null || seat != r.Host || r.Seats[requestedSeat] is not null || state is not (254 or 255)) break;
                    r.Closed[requestedSeat] = state == 254; PublishSeats(r); return;
                case 56 when r is not null && r.Match is null && GameProtocolEngine.TryReadPvpReady(body, out var ready):
                    if (seat != r.Host) { r.Ready[seat] = ready; BroadcastMembers(r, GameProtocolEngine.CreatePvpReadyState((byte)seat, ready)); return; }
                    if (!ready) { r.Ready[seat] = false; BroadcastMembers(r, GameProtocolEngine.CreatePvpReadyState((byte)seat, false)); return; }
                    if (r.Seats.Any(s => s is null) || !r.Ready[1 - seat] || !r.Seats.All(s => TryPeer(s!, out _))) break;
                    var players = new List<Participant>();
                    for (byte i = 0; i < 2; i++)
                    {
                        var actor = r.Seats[i]!;
                        var canonical = await store.GetCharacterAsync(actor.A.Account, actor.A.CharacterId, ct);
                        if (canonical is null) return;
                        players.Add(new(actor, i, canonical.PvpGrade));
                    }
                    var match = new Match(r, players.ToArray(), clock.GetTimestamp());
                    foreach (var p in players)
                        if (!udp.SetPvpContext(p.Channel.A.SessionId, p.Channel.A.Id, r.Identity, match.Id))
                        {
                            foreach (var rollback in players) udp.SetPvpContext(rollback.Channel.A.SessionId, rollback.Channel.A.Id, r.Identity, null);
                            Error(c, command); return;
                        }
                    r.Match = match; _matches.Add(match.Id, match);
                    AdvanceGeneration(r);
                    r.Ready[seat] = true;
                    BroadcastMembers(r, GameProtocolEngine.CreatePvpReadyState((byte)seat, true));
                    PublishPeers(r); BroadcastState(r);
                    BroadcastMembers(r, GameProtocolEngine.CreatePvpStart(maps.Select(r.RequestedMap)));
                    return;
                case 57 when r is not null && r.Match is null && seat == r.Host && GameProtocolEngine.TryReadPvpMode(body, out var mode) && mode == 0:
                    BroadcastState(r); return;
                case 62 when r is not null && r.Match is null && seat == r.Host && GameProtocolEngine.TryReadPvpMap(body, out var map) && maps.IsValidRequested(map):
                    r.RequestedMap = map; BroadcastState(r); return;
                case 58 when r?.Match is { Phase: Phase.Playing } m && GameProtocolEngine.TryReadPvpDeath(body, out var killerUid):
                    var victim = m.Players[seat];
                    if (!victim.Alive || victim.Forfeit) break;
                    var killer = m.Players.FirstOrDefault(p => p.Channel.A.Uid == killerUid && !p.Forfeit);
                    if (killerUid != ushort.MaxValue && killer is null) break;
                    victim.Alive = false;
                    BroadcastMembers(r, GameProtocolEngine.CreatePvpDeath((byte)seat, killer?.Seat ?? byte.MaxValue));
                    FinishDecision(m); return;
                case 59 when r?.Match is { Phase: Phase.Playing } m && seat == r.Host && GameProtocolEngine.TryReadPvpTimeout(body, out var hp)
                    && Elapsed(m.Started) >= TimeSpan.FromSeconds(180):
                    var draw = hp.Distinct().Count() == 1;
                    m.Winner = draw ? null : m.Players.Where(p => !p.Forfeit).OrderBy(p => hp[p.Seat]).ThenBy(p => p.Seat).FirstOrDefault()?.Seat;
                    m.Draw = draw;
                    foreach (var p in m.Players) p.Alive = p.Seat == m.Winner;
                    BeginRanking(m); return;
                case 61 when r?.Match is { Phase: Phase.Ranking } m && GameProtocolEngine.TryReadPvpRank(body, out var report):
                    var participant = m.Players[seat];
                    if (participant.Forfeit || participant.Report is not null) return;
                    participant.Report = report; m.FirstResponse ??= clock.GetTimestamp();
                    if (m.Players.All(p => p.Forfeit || p.Report is not null)) await SettleAsync(m);
                    return;
                case 60 when r?.Match is { Phase: Phase.Results } m && GameProtocolEngine.TryReadPvpAck(body):
                    m.Players[seat].Ack = true; m.FirstResponse ??= clock.GetTimestamp();
                    if (Present(m).All(p => p.Ack)) Reset(m);
                    return;
                case 120 when GameProtocolEngine.TryReadPvpHeartbeat(body): return;
                case 100: return; // Native peer result is not fabricated into a readiness signal.
            }
            Error(c, command);
        }, ct);
    }

    private async Task EnterAsync(Guid character, Guid attachment, PvpCreateRequest? create, ushort target, long generation, CancellationToken ct)
    {
        Channel? c = null; Room? r = null; Guid activity = Guid.NewGuid();
        (Channel Channel, long Generation)[] modelOwners = [];
        var models = new Dictionary<Channel, PvpModelSnapshot>();
        await RunAsync(() =>
        {
            c = Current(character, attachment);
            if (c is null || c.Generation != generation) { c = null; return Task.CompletedTask; }
            if (c.Room is not null || c.Entering || !c.A.CanEnter() || !TryPeer(c, out _) || store.IsTradeDeliveryPending(character)
                || (create is not null && !maps.IsValidRequested(create.RequestedMap))
                || (create is null && (!_rooms.TryGetValue(target, out r) || r.Match is not null || !r.Seats.OfType<Channel>().All(c => TryPeer(c, out _)) || Enumerable.Range(0,2).All(i => r.Seats[i] is not null || r.Closed[i])))
                || !store.Commerce.TryAcquire(CommerceActivity.Pvp, activity, character))
            { Error(c, create is null ? (byte)54 : (byte)53); c = null; return Task.CompletedTask; }
            c.Entering = true; c.Activity = activity;
            modelOwners = (r?.Seats.OfType<Channel>() ?? []).Append(c).Select(owner => (owner, owner.Generation)).ToArray();
            return Task.CompletedTask;
        }, ct);
        if (c is null) return;
        var transitioned = false; var entered = false;
        try
        {
            // Actor/economy work is outside the room gate. Every snapshot is revalidated at
            // publication, including an existing host whose timed equipment may have expired.
            foreach (var (owner, _) in modelOwners)
            {
                var model = await owner.A.CaptureModel();
                if (model is null) throw new InvalidOperationException("PvP model attachment is no longer current.");
                models.Add(owner, model);
            }
            await c.A.LeaveTownAndParty(); transitioned = true;
        }
        catch (Exception e) { logger.LogWarning(e, "PvP entry transition failed for {Character}.", character); }
        await RunAsync(() =>
        {
            c.Entering = false;
            if (!transitioned || Current(character, attachment) != c || c.Generation != generation || c.Activity != activity
                || modelOwners.Any(owner => Current(owner.Channel.A.CharacterId, owner.Channel.A.Id) != owner.Channel
                    || owner.Channel.Generation != owner.Generation)
                || (create is null && (r is null || !_rooms.TryGetValue(r.Id, out var current) || current != r || r.Match is not null)))
            { store.Commerce.Release(activity, character); Error(c, create is null ? (byte)54 : (byte)53); return Task.CompletedTask; }
            if (create is not null)
            {
                var id = AllocateRoom();
                if (id == 0) { store.Commerce.Release(activity, character); Error(c, 53); return Task.CompletedTask; }
                r = new(id, create.NameSelection, create.Name, create.RequestedMap); _rooms.Add(id, r);
            }
            var open = Enumerable.Range(0, 2).FirstOrDefault(i => r!.Seats[i] is null && !r.Closed[i], -1);
            if (open < 0 || !udp.SetPvpContext(c.A.SessionId, c.A.Id, r!.Identity, null))
            { store.Commerce.Release(activity, character); Error(c, 54); if (create is not null) _rooms.Remove(r!.Id); return Task.CompletedTask; }
            foreach (var (owner, model) in models) owner.Model = model;
            c.Room = r; r.Seats[open] = c; entered = true;
            Interlocked.Increment(ref c.Generation);
            foreach (var recipient in _channels.Values) PublishModels(recipient, r);
            PublishPeers(r);
            if (create is not null) Broadcast(GameProtocolEngine.CreatePvpRoomList([RoomInfo(r)]));
            else { PublishSeats(r); Enqueue(c, GameProtocolEngine.CreatePvpEnterReply([.. r.Ready, false,false,false,false,false,false])); }
            return Task.CompletedTask;
        }, CancellationToken.None);
        if (!entered)
        {
            if (c.A.RestoreTown is { } restore && CurrentAttachment(c))
                try { await restore(); } catch (Exception e) { logger.LogWarning(e, "PvP town rollback failed."); c.A.Disconnect?.Invoke(); }
            c.A.Wake();
        }
    }

    private bool CurrentAttachment(Channel c) => !c.Retired;

    private async Task LeaveCoreAsync(Channel c, bool detach)
    {
        var r = c.Room;
        if (r is null)
        {
            if (c.Entering) { c.Entering = false; store.Commerce.Release(c.Activity, c.A.CharacterId); }
            return;
        }
        var seat = Array.IndexOf(r.Seats, c);
        var m = r.Match;
        if (m is { Phase: Phase.Playing or Phase.Ranking })
        {
            m.Players[seat].Forfeit = true; m.Players[seat].Alive = false;
        }
        r.Seats[seat] = null; r.Ready[seat] = false; c.Room = null;
        Interlocked.Increment(ref c.Generation);
        udp.ClearPvpContext(c.A.SessionId, c.A.Id, r.Identity);
        PublishSeats(r);
        if (r.Host == seat) r.Host = (byte)Math.Max(0, Array.FindIndex(r.Seats, s => s is not null));
        if (r.Seats.All(s => s is null))
        { _rooms.Remove(r.Id); Broadcast(GameProtocolEngine.CreatePvpRoomState(r.Id, 0, r.Host, r.RequestedMap)); }
        else BroadcastState(r);
        if (m is null || m.Phase == Phase.Results) store.Commerce.Release(c.Activity, c.A.CharacterId);
        if (m is { Phase: Phase.Playing }) FinishDecision(m);
        if (m is { Phase: Phase.Ranking, Removing: false } && m.Players.All(p => p.Forfeit || p.Report is not null)) await SettleAsync(m);
        c.A.Wake();
    }

    private void FinishDecision(Match match)
    {
        if (match.Phase != Phase.Playing) return;
        var alive = match.Players.Where(p => p.Alive && !p.Forfeit).ToArray();
        if (alive.Length > 1) return;
        match.Winner = alive.FirstOrDefault()?.Seat; match.Draw = alive.Length == 0;
        BeginRanking(match);
    }
    private void BeginRanking(Match m)
    {
        AdvanceGeneration(m.Room);
        m.Phase = Phase.Ranking; m.PhaseStarted = clock.GetTimestamp(); m.FirstResponse = null;
        BroadcastMembers(m.Room, GameProtocolEngine.CreatePvpRankRequest());
    }
    private static IEnumerable<Participant> Present(Match m) => m.Players.Where(p => p.Channel.Room == m.Room && !p.Channel.Retired);
    private TimeSpan Elapsed(long stamp) => clock.GetElapsedTime(stamp, clock.GetTimestamp());
    private bool ResponseExpired(Match m) => Elapsed(m.PhaseStarted) >= TimeSpan.FromSeconds(20)
        || m.FirstResponse is { } first && Elapsed(first) >= TimeSpan.FromSeconds(10);

    internal Task SweepAsync(CancellationToken ct = default) => RunAsync(async () =>
    {
        foreach (var m in _matches.Values.ToArray())
        {
            if (m.Phase == Phase.Playing && Elapsed(m.Started) >= TimeSpan.FromSeconds(240))
            {
                // Liveness hardening: no synthetic HP or winner when the host never reports.
                m.Winner = null; m.Draw = true; BeginRanking(m);
                m.Removing = true;
                foreach (var p in m.Players) { p.Forfeit = true; p.Alive = false; }
                foreach (var p in Present(m).ToArray()) await LeaveCoreAsync(p.Channel, false);
                m.Removing = false;
                if (_matches.ContainsKey(m.Id) && m.Phase == Phase.Ranking) await SettleAsync(m);
            }
            else if (m.Phase == Phase.Ranking && ResponseExpired(m))
            {
                foreach (var p in Present(m).Where(p => p.Report is null).ToArray()) await LeaveCoreAsync(p.Channel, false);
                if (_matches.ContainsKey(m.Id) && m.Phase == Phase.Ranking) await SettleAsync(m);
            }
            else if (m.Phase == Phase.Results && ResponseExpired(m)) Reset(m);
        }
    }, ct);

    private async Task SettleAsync(Match m)
    {
        if (m.Phase != Phase.Ranking) return;
        var result = await store.CommitPvpMatchAsync(new(m.Id, m.Players.Select(p => new PvpSettlementMember(
            p.Channel.A.CharacterId, p.Grade, p.Forfeit, p.Report?.Stats, p.Report?.Scores)).ToArray(),
            m.Winner is { } seat ? m.Players[seat].Channel.A.CharacterId : null, m.Draw), rating);
        if (result.Status != DungeonRewardLedgerStatus.Committed)
        { m.Phase = Phase.Unknown; logger.LogError("PvP {Match} settlement {Status}; rewards withheld.", m.Id, result.Status); return; }
        AdvanceGeneration(m.Room);
        m.Phase = Phase.Results; m.PhaseStarted = clock.GetTimestamp(); m.FirstResponse = null;
        foreach (var p in m.Players)
        {
            var award = result.Awards.Single(a => a.CharacterId == p.Channel.A.CharacterId);
            // A displaced attachment must never install an old actor over the replacement.
            _revisions.AddOrUpdate(p.Channel.A.CharacterId, 1, (_, value) => value + 1);
            if (_channels.TryGetValue(p.Channel.A.CharacterId, out var current)) current.A.Wake();
            p.Channel.A.Wake();
            if (p.Channel.Room != m.Room) store.Commerce.Release(p.Channel.Activity, p.Channel.A.CharacterId);
        }
        var present = Present(m).ToArray();
        var ranked = present.Where(p => p.Report is not null).OrderByDescending(p => p.Report!.TotalScore).ThenBy(p => p.Seat).ToArray();
        var ranks = ranked.Select(p =>
        {
            var a = result.Awards.Single(a => a.CharacterId == p.Channel.A.CharacterId);
            return new PvpResultRankEntry(p.Channel.A.Uid, p.Report!.TotalScore, a.Grade, (uint)a.Points,
                (uint)rating.GetCurrentRankPoint(a.Grade), (uint)rating.GetNextRankPoint(a.Grade));
        }).ToArray();
        var ace = ranked.OrderByDescending(p => p.Report!.Stats[0]).ThenByDescending(p => p.Report!.TotalScore).ThenBy(p => p.Seat).FirstOrDefault();
        foreach (var p in present)
        {
            var a = result.Awards.Single(a => a.CharacterId == p.Channel.A.CharacterId);
            Enqueue(p.Channel, GameProtocolEngine.CreatePvpRecord(a.Wins, a.Losses, a.Points,
                rating.GetCurrentRankPoint(a.Grade), rating.GetNextRankPoint(a.Grade), a.Grade, a.GradeExtension));
            var aliveWinner = present.FirstOrDefault(candidate => candidate.Alive && !candidate.Forfeit);
            Enqueue(p.Channel, GameProtocolEngine.CreatePvpResult(aliveWinner?.Seat ?? present.Min(candidate => candidate.Seat), a.VictoryPoints,
                a.Grade, a.ExperienceDelta, ranks, ace?.Channel.A.Uid ?? 0, aliveWinner is null));
        }
        if (present.Length == 0) Reset(m);
    }

    private void Reset(Match m)
    {
        if (!_matches.Remove(m.Id)) return;
        var r = m.Room;
        if (r.Match != m) return;
        r.Match = null; Array.Clear(r.Ready); AdvanceGeneration(r);
        if (m.Winner is { } winner && r.Seats[winner] is not null) r.Host = winner;
        foreach (var c in r.Seats.OfType<Channel>()) udp.SetPvpContext(c.A.SessionId, c.A.Id, r.Identity, null);
        if (_rooms.TryGetValue(r.Id, out var actual) && actual == r) { PublishPeers(r); BroadcastState(r); }
    }

    private static void AdvanceGeneration(Room room)
    { foreach (var c in room.Seats.OfType<Channel>()) Interlocked.Increment(ref c.Generation); }

    private ushort AllocateRoom()
    {
        for (var i = 0; i < ushort.MaxValue - 1; i++)
        { var candidate = _nextRoom++; if (_nextRoom == 0) _nextRoom = 1; if (candidate != 0 && !_rooms.ContainsKey(candidate)) return candidate; }
        return 0;
    }
    private bool TryPeer(Channel c, out GamePeerInfo peer) => udp.TryGetPeerInfo(c.A.SessionId, c.A.Uid, c.A.AccountUid, out peer)
        && peer.PublicPort != 0 && !peer.PublicAddress.Equals(System.Net.IPAddress.Any) && udp.HasUniqueEndpoint(c.A.SessionId);
    private static PvpSeatInfo[] Seats(Room r) => Enumerable.Range(0,8).Select(i =>
        new PvpSeatInfo((byte)i, i >= 2 ? (byte)254 : r.Seats[i] is not null ? (byte)0 : r.Closed[i] ? (byte)254 : (byte)255,
            i < 2 ? r.Seats[i]?.A.Uid ?? ushort.MaxValue : ushort.MaxValue)).ToArray();
    private static PvpRoomInfo RoomInfo(Room r) => new(r.Id, r.Selection, r.Name, r.Match is null ? (byte)1 : (byte)2,
        r.Host, r.RequestedMap, 0, Seats(r));
    private void PublishModels(Channel recipient, Room room)
    { foreach (var c in room.Seats.OfType<Channel>()) { if (c.Model is { } model) { Enqueue(recipient, model.Appearance); Enqueue(recipient, model.Detail); } } }
    private void PublishPeers(Room r)
    {
        foreach (var recipient in r.Seats.OfType<Channel>())
        {
            var peers = r.Seats.OfType<Channel>().OrderByDescending(c => c == recipient).Where(c => TryPeer(c, out _)).Select(c =>
            { TryPeer(c, out var peer); return GameplayDatagramService.ApplyPeerEndpointOverride(peer, c == recipient, udp.PeerEndpointOverride); }).ToArray();
            if (peers.Length > 0) Enqueue(recipient, GameProtocolEngine.CreateUdpPeerInfo(peers));
        }
    }
    private void PublishSeats(Room r) => Broadcast(GameProtocolEngine.CreatePvpSeatState(r.Id, Seats(r)));
    private void BroadcastState(Room r) => Broadcast(GameProtocolEngine.CreatePvpRoomState(r.Id, r.Match is null ? (byte)1 : (byte)2, r.Host, r.RequestedMap));
    private void Broadcast(GameServerPacket packet) { foreach (var c in _channels.Values) Enqueue(c, packet); }
    private static void BroadcastMembers(Room r, GameServerPacket packet) { foreach (var c in r.Seats.OfType<Channel>()) Enqueue(c, packet); }
    private static void Enqueue(Channel c, GameServerPacket packet) { if (!c.Retired) c.Outbox.Enqueue(packet); }
    private static void Error(Channel c, byte command) => Enqueue(c, new(GameProtocolEngine.CommandPacketType, command, [0, 2]));

    private async Task RunAsync(Func<Task> mutate, CancellationToken ct)
    {
        Channel[] channels;
        await _gate.WaitAsync(ct);
        try
        {
            await mutate();
            channels = _channels.Values.Where(c => !c.Draining && !c.Retired && c.Outbox.Count > 0).ToArray();
            foreach (var c in channels) c.Draining = true;
        }
        finally { _gate.Release(); }
        await Task.WhenAll(channels.Select(DrainAsync));
    }
    private async Task DrainAsync(Channel c)
    {
        while (true)
        {
            GameServerPacket? packet;
            await _gate.WaitAsync();
            try
            {
                packet = !c.Retired && c.Outbox.Count > 0 ? c.Outbox.Dequeue() : null;
                if (packet is null) c.Draining = false;
            }
            finally { _gate.Release(); }
            if (packet is null) return;
            try { await c.A.Send(packet).AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception e)
            {
                logger.LogWarning(e, "PvP attachment transport retired {Attachment}.", c.A.Id);
                await _gate.WaitAsync();
                try { c.Retired = true; c.Outbox.Clear(); c.Draining = false; }
                finally { _gate.Release(); }
                c.A.Disconnect?.Invoke();
                await DetachAsync(c.A.CharacterId, c.A.Id);
                return;
            }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), clock);
            while (await timer.WaitForNextTickAsync(stoppingToken)) await SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
