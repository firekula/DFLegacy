using System.Collections.Concurrent;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed record PvpModelSnapshot(GameServerPacket Appearance, GameServerPacket Detail);

internal sealed record PvpAttachment(Guid Id, Guid SessionId, string Account, Guid CharacterId, ushort Uid,
    uint AccountUid, Func<ValueTask<PvpModelSnapshot?>> CaptureModel, Func<GameServerPacket, ValueTask> Send,
    Func<bool> CanEnter, Func<Task> LeaveTownAndParty, Action Wake, Action? Disconnect = null, Func<Task>? RestoreTown = null,
    int ChannelNumber = 51);

/// <summary>Runtime-only ACT1 arena rooms. Every mutation and enqueue is ordered under one gate;
/// socket writes are drained afterwards per attachment. Immutable matches outlive emptied rooms.</summary>
public sealed partial class PvpService(JsonGameStore store, GameplayDatagramService udp, PvpExperienceCatalog rating,
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
    private sealed class Room(ushort id, byte selection, byte[] name, ushort map, int channelNumber)
    {
        internal readonly int ChannelNumber = channelNumber;
        internal readonly Guid Identity = Guid.NewGuid();
        internal readonly ushort Id = id;
        internal readonly byte Selection = selection;
        internal readonly byte[] Name = name;
        internal ushort RequestedMap = map;
        internal byte Host;
        internal readonly Channel?[] Seats = new Channel?[8];
        internal readonly bool[] Closed = new bool[8];
        internal readonly bool[] Ready = new bool[8];
        internal byte Mode;
        internal readonly byte[] Teams = new byte[8];
        internal readonly bool[] Requested = new bool[8];
        internal Match? Match;
    }
    private sealed class Participant(Channel channel, byte seat, byte grade, byte team)
    {
        internal readonly Channel Channel = channel;
        internal readonly byte Seat = seat, Grade = grade, Team = team;
        internal int Kills;
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
        internal readonly byte Mode = room.Mode;
        internal byte? WinningTeam;
        internal readonly Participant?[] Fighters = new Participant?[2];
        internal long? TurnScheduled;
        internal bool InitialTurn = true;
    }

    internal bool IsBusy(Guid characterId) => store.Commerce.Is(characterId, CommerceActivity.Pvp);
    internal long Revision(Guid character) => _revisions.GetValueOrDefault(character);
    internal static bool AllowedWhileBusy(byte command) => command is 0 or 2 or 3 or 4 or 7 or 8 or 17 or 53 or 54 or 55 or 56 or 57 or 58 or 59 or 60 or 61 or 62 or 100 or 101 or 120 or 122 or 141 or 160 or 161 or 162;

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
        var rooms = _rooms.Values.Where(r => r.ChannelNumber == attachment.ChannelNumber).ToArray();
        foreach (var room in rooms) PublishModels(channel, room);
        Enqueue(channel, GameProtocolEngine.CreatePvpRoomList(rooms.Select(RoomInfo).ToArray()));
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

    internal static bool IsPvpCommand(byte command) => command is >= 53 and <= 62 or 100 or 101 or 120 or 122;

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
                    if (requestedSeat == seat && state == 254) { await LeaveCoreAsync(c, false); return; }
                    if (r.Match is not null) break;
                    if (requestedSeat == seat && r.Mode != 0 && state is 1 or 2)
                    {
                        if (r.Teams[seat] == state) return;
                        if (Enumerable.Range(0,8).Count(i=>r.Seats[i] is not null && r.Teams[i]==state) >= 4) break;
                        r.Teams[seat] = state; InvalidateReady(r); PublishSeats(r); return;
                    }
                    if (seat != r.Host || r.Seats[requestedSeat] is not null || state is not (254 or 255)) break;
                    r.Closed[requestedSeat] = state == 254; PublishSeats(r); return;
                case 56 when r is not null && r.Match is null && GameProtocolEngine.TryReadPvpReady(body, out var ready):
                    if (seat != r.Host || !ready)
                    { r.Ready[seat] = ready; BroadcastMembers(r, GameProtocolEngine.CreatePvpReadyState((byte)seat, ready)); return; }
                    var occupied = Enumerable.Range(0,8).Where(i=>r.Seats[i] is not null).ToArray();
                    if (occupied.Length < 2 || occupied.Any(i=>i!=seat && !r.Ready[i])
                        || occupied.Any(i=>!TryPeer(r.Seats[i]!,out _))
                        || !maps.IsValidRequested(r.RequestedMap,occupied.Length)
                        || (r.Mode != 0 && (occupied.Length%2!=0 || occupied.Any(i=>r.Teams[i] is not (1 or 2))
                            || occupied.Count(i=>r.Teams[i]==1)!=occupied.Length/2))) break;
                    var players = new List<Participant>();
                    foreach (var i in occupied)
                    {
                        var actor = r.Seats[i]!;
                        var canonical = await store.GetCharacterAsync(actor.A.Account, actor.A.CharacterId, ct);
                        if (canonical is null) return;
                        players.Add(new(actor, (byte)i, canonical.PvpGrade, r.Teams[i]));
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
                    BroadcastMembers(r, GameProtocolEngine.CreatePvpStart(maps.Select(r.RequestedMap,players.Count),r.Mode));
                    if (r.Mode == 2) match.TurnScheduled = clock.GetTimestamp();
                    logger.LogInformation("PvP match {Match} started: channel={Channel} room={Room} mode={Mode} players={Players}.",
                        match.Id,r.ChannelNumber,r.Id,r.Mode,players.Count);
                    return;
                case 57 when r is not null && r.Match is null && seat == r.Host && GameProtocolEngine.TryReadPvpMode(body, out var mode):
                    if (r.Mode == mode) { BroadcastState(r); return; }
                    if (mode == 0) Array.Clear(r.Teams);
                    else if (r.Mode == 0)
                    {
                        byte team=1;
                        for(int i=0;i<8;i++) if(r.Seats[i] is not null) { r.Teams[i]=team;team=(byte)(3-team); }
                    }
                    r.Mode=mode; Array.Clear(r.Requested); InvalidateReady(r); PublishSeats(r); BroadcastState(r); return;
                case 62 when r is not null && r.Match is null && seat == r.Host && GameProtocolEngine.TryReadPvpMap(body, out var map) && maps.IsValidRequested(map,Math.Max(2,r.Seats.Count(s=>s is not null))):
                    r.RequestedMap = map; BroadcastState(r); return;
                case 58 when r?.Match is { Phase: Phase.Playing } m && GameProtocolEngine.TryReadPvpDeath(body, out var killerUid):
                    var victim = m.Players.Single(p=>p.Seat==seat);
                    if (!victim.Alive || victim.Forfeit || (m.Mode==2 && !m.Fighters.Contains(victim))) break;
                    var killer = m.Players.FirstOrDefault(p => p.Channel.A.Uid == killerUid && !p.Forfeit && p.Alive
                        && (m.Mode==0 || p.Team!=victim.Team) && (m.Mode!=2 || m.Fighters.Contains(p)));
                    if (killerUid != ushort.MaxValue && killer is null) break;
                    victim.Alive = false; if(killer is not null) killer.Kills++;
                    BroadcastMembers(r, GameProtocolEngine.CreatePvpDeath((byte)seat, killer?.Seat ?? byte.MaxValue));
                    FinishDecision(m); return;
                case 59 when r?.Match is { Phase: Phase.Playing, Mode: not 2 } m && seat == r.Host && GameProtocolEngine.TryReadPvpTimeout(body, out var hp)
                    && Elapsed(m.Started) >= TimeSpan.FromSeconds(180):
                    DecideTimeout(m,hp); BeginRanking(m); return;
                case 122 when r is { Mode: 2 } && GameProtocolEngine.TryReadPvpAck(body)
                    && (r.Match is null || r.Match is {Phase: Phase.Playing} live
                        && live.Players.Any(p=>p.Seat==seat && p.Alive && !p.Forfeit && !live.Fighters.Contains(p))):
                    r.Requested[seat]=!r.Requested[seat]; BroadcastMembers(r,GameProtocolEngine.CreatePvpFightRequest((byte)seat)); return;
                case 61 when r?.Match is { Phase: Phase.Ranking } m && GameProtocolEngine.TryReadPvpRank(body, out var report):
                    var participant = m.Players.Single(p=>p.Seat==seat);
                    if (participant.Forfeit || participant.Report is not null) return;
                    participant.Report = report; m.FirstResponse ??= clock.GetTimestamp();
                    if (m.Players.All(p => p.Forfeit || p.Report is not null)) await SettleAsync(m);
                    return;
                case 60 when r?.Match is { Phase: Phase.Results } m && GameProtocolEngine.TryReadPvpAck(body):
                    m.Players.Single(p=>p.Seat==seat).Ack = true; m.FirstResponse ??= clock.GetTimestamp();
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
                || (create is null && (!_rooms.TryGetValue(target, out r) || r.ChannelNumber != c.A.ChannelNumber || r.Match is not null || !r.Seats.OfType<Channel>().All(c => TryPeer(c, out _)) || Enumerable.Range(0,8).All(i => r.Seats[i] is not null || r.Closed[i])))
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
                r = new(id, create.NameSelection, create.Name, create.RequestedMap, c.A.ChannelNumber); _rooms.Add(id, r);
            }
            var open = Enumerable.Range(0, 8).FirstOrDefault(i => r!.Seats[i] is null && !r.Closed[i], -1);
            if (open < 0 || !udp.SetPvpContext(c.A.SessionId, c.A.Id, r!.Identity, null))
            { store.Commerce.Release(activity, character); Error(c, 54); if (create is not null) _rooms.Remove(r!.Id); return Task.CompletedTask; }
            foreach (var (owner, model) in models) owner.Model = model;
            c.Room = r; r.Seats[open] = c; entered = true;
            r.Teams[open]=r.Mode==0 ? (byte)0 : (byte)(Enumerable.Range(0,8).Count(i=>i!=open && r.Seats[i] is not null && r.Teams[i]==1)
                <= Enumerable.Range(0,8).Count(i=>i!=open && r.Seats[i] is not null && r.Teams[i]==2) ? 1 : 2);
            InvalidateReady(r);
            Interlocked.Increment(ref c.Generation);
            foreach (var recipient in _channels.Values.Where(x => x.A.ChannelNumber == r.ChannelNumber)) PublishModels(recipient, r);
            PublishPeers(r);
            if (create is not null) Broadcast(r, GameProtocolEngine.CreatePvpRoomList([RoomInfo(r)]));
            else { PublishSeats(r); Enqueue(c, GameProtocolEngine.CreatePvpEnterReply(r.Ready)); }
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
            var leaving=m.Players.Single(p=>p.Seat==seat);
            if(m.Phase==Phase.Playing && leaving.Alive) BroadcastMembers(r,GameProtocolEngine.CreatePvpDeath((byte)seat,255));
            leaving.Forfeit = true; leaving.Alive = false;
        }
        r.Seats[seat] = null; r.Ready[seat] = false; r.Teams[seat]=0;r.Requested[seat]=false;c.Room = null;
        Interlocked.Increment(ref c.Generation);
        udp.ClearPvpContext(c.A.SessionId, c.A.Id, r.Identity);
        PublishSeats(r);
        if (r.Host == seat) r.Host = (byte)Math.Max(0, Array.FindIndex(r.Seats, s => s is not null));
        if (r.Seats.All(s => s is null))
        { _rooms.Remove(r.Id); Broadcast(r, GameProtocolEngine.CreatePvpRoomState(r.Id, 0, r.Host, r.RequestedMap,r.Mode)); }
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
        if (match.Mode==0)
        {
            if (alive.Length > 1) return;
            match.Winner = alive.FirstOrDefault()?.Seat; match.Draw = alive.Length == 0;
        }
        else
        {
            var teams=alive.Select(p=>p.Team).Distinct().ToArray();
            if(teams.Length>1)
            {
                if(match.Mode==2 && match.Fighters.Any(p=>p is not null && (!p.Alive || p.Forfeit)))
                    match.TurnScheduled ??= clock.GetTimestamp();
                return;
            }
            match.WinningTeam=teams.Length==1 ? teams[0] : null;match.Draw=teams.Length==0;
            match.Winner=alive.FirstOrDefault()?.Seat;
        }
        BeginRanking(match);
    }
    private void BeginRanking(Match m)
    {
        AdvanceGeneration(m.Room);
        m.TurnScheduled=null; m.Phase = Phase.Ranking; m.PhaseStarted = clock.GetTimestamp(); m.FirstResponse = null;
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
            if (m.Phase == Phase.Playing && m.Mode==2 && m.TurnScheduled is { } due
                && Elapsed(due)>=TimeSpan.FromSeconds(m.InitialTurn?7:3)) RelayTurn(m);
            if (m.Phase == Phase.Playing && Elapsed(m.Started) >= TimeSpan.FromSeconds(m.Mode==2 ? 70+123*(m.Players.Length-1) : 240))
            {
                // Liveness hardening: no synthetic HP or winner when the host never reports.
                m.Winner = null; m.WinningTeam=null; m.Draw = true; BeginRanking(m);
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
            p.Channel.A.CharacterId, p.Grade, p.Forfeit, p.Report?.Stats, p.Report?.Scores,p.Team,p.Seat)).ToArray(),
            m.Mode==0 && m.Winner is { } seat ? m.Players.Single(p=>p.Seat==seat).Channel.A.CharacterId : null, m.Draw,m.Mode,m.WinningTeam), rating);
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
            var aliveWinner = present.FirstOrDefault(candidate => !candidate.Forfeit && (m.Mode==0 ? candidate.Alive : candidate.Team==m.WinningTeam));
            var allKill=m.Mode==2 && !m.Draw ? present.FirstOrDefault(p=>p.Alive && !p.Forfeit && p.Team==m.WinningTeam
                && p.Kills>1 && p.Kills>=m.Players.Count(q=>q.Team!=p.Team)) : null;
            Enqueue(p.Channel, GameProtocolEngine.CreatePvpResult(aliveWinner?.Seat ?? present.Min(candidate => candidate.Seat), a.VictoryPoints,
                a.Grade, a.ExperienceDelta, ranks, ace?.Channel.A.Uid ?? 0, aliveWinner is null,allKill?.Channel.A.Uid ?? ushort.MaxValue));
        }
        logger.LogInformation("PvP match {Match} committed: mode={Mode} winner={Winner} team={Team} draw={Draw} participants={Count}.",
            m.Id,m.Mode,m.Winner,m.WinningTeam,m.Draw,m.Players.Length);
        if (present.Length == 0) Reset(m);
    }

    private void Reset(Match m)
    {
        if (!_matches.Remove(m.Id)) return;
        var r = m.Room;
        if (r.Match != m) return;
        r.Match = null; Array.Clear(r.Ready); AdvanceGeneration(r);
        var winner=m.Mode==0 ? m.Winner : m.Players.FirstOrDefault(p=>p.Team==m.WinningTeam && r.Seats[p.Seat] is not null)?.Seat;
        if (winner is { } host && r.Seats[host] is not null) r.Host=host;
        Array.Clear(r.Requested);
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
        new PvpSeatInfo((byte)i, r.Seats[i] is not null ? r.Teams[i] : r.Closed[i] ? (byte)254 : (byte)255,
            r.Seats[i]?.A.Uid ?? ushort.MaxValue)).ToArray();
    private static PvpRoomInfo RoomInfo(Room r) => new(r.Id, r.Selection, r.Name, r.Match is null ? (byte)1 : (byte)2,
        r.Host, r.RequestedMap, r.Mode, Seats(r));
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
    private void PublishSeats(Room r) => Broadcast(r, GameProtocolEngine.CreatePvpSeatState(r.Id, Seats(r),r.Mode));
    private void BroadcastState(Room r) => Broadcast(r, GameProtocolEngine.CreatePvpRoomState(r.Id, r.Match is null ? (byte)1 : (byte)2, r.Host, r.RequestedMap,r.Mode));
    private void Broadcast(Room r, GameServerPacket packet)
    { foreach (var c in _channels.Values.Where(c => c.A.ChannelNumber == r.ChannelNumber)) Enqueue(c, packet); }
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
