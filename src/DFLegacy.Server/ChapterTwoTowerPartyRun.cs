using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed record TowerPartyApplicant(string Account, CharacterRecord Character);
internal sealed record TowerPartyTicketResult(CharacterRecord Character, GameInventoryEntry[] Updates);
internal sealed record TowerPartyResult(int Floor, uint Milliseconds, DateTimeOffset CompletedAt);
internal sealed record TowerPartyReward(uint Experience, GameChapterTwoTowerRewardItem[] Cards);

internal sealed class ChapterTwoTowerPartyRun
{
    private sealed class Member(PartySlotHandle roster)
    {
        internal PartySlotHandle Roster { get; } = roster;
        internal Queue<GameServerPacket> Packets { get; } = new();
        internal TowerPartyApplicant? Applicant { get; set; }
        internal TowerPartyReward? Reward { get; set; }
        internal bool RewardStarted { get; set; }
        internal bool Joined { get; set; }
        internal bool Loaded { get; set; }
        internal bool Alive { get; set; } = true;
        internal bool Available { get; set; } = true;
        internal bool ReselectStarted { get; set; }
        internal bool ReselectPrepared { get; set; }
    }

    private readonly Dictionary<Guid, Member> _members;
    private readonly DungeonTowerFloor _first;
    private IReadOnlyDictionary<Guid, TowerPartyTicketResult>? _admissions;
    private bool _saving;
    private bool _rejected;
    private bool _released;
    private byte _stage;
    private int _floor;
    private long _startedAt;
    private long _elapsed;
    private long? _wipeAt;
    private TowerPartyResult? _result;
    private bool _rewardsPublished;
    private byte? _settlementOption;
    private bool _reselect;

    internal ChapterTwoTowerPartyRun(DungeonInstance instance, DungeonTowerDefinition tower,
        DungeonTowerFloor first, Guid leader, PartySlotHandle[] roster, Func<bool> entryIsCurrent)
    {
        Instance = instance;
        Tower = tower;
        Leader = leader;
        Roster = roster;
        EntryIsCurrent = entryIsCurrent;
        _first = first;
        _members = roster.ToDictionary(slot => slot.CharacterId, slot => new Member(slot));
    }

    internal DungeonInstance Instance { get; }
    internal Guid AttemptId { get; } = Guid.NewGuid();
    internal DungeonTowerDefinition Tower { get; }
    internal Guid Leader { get; }
    internal PartySlotHandle[] Roster { get; }
    internal Func<bool> EntryIsCurrent { get; }

    internal TowerPartyApplicant[]? Apply(Guid id, Guid session, TowerPartyApplicant? applicant)
    {
        lock (Instance.Gate)
        {
            if (_rejected || _saving || _admissions is not null) return null;
            if (!_members.TryGetValue(id, out var member) || member.Roster.SessionId != session || applicant is null)
            {
                _rejected = true;
                return null;
            }

            member.Applicant = applicant;
            if (_members.Values.Any(m => m.Applicant is null)) return null;
            _saving = true;
            return _members.Values.Select(m => m.Applicant!).ToArray();
        }
    }

    internal bool IsAdmitting
    {
        get { lock (Instance.Gate) return _saving && !_rejected; }
    }

    internal bool AdmissionRejected
    {
        get { lock (Instance.Gate) return _rejected; }
    }

    internal void CompleteAdmission(IReadOnlyDictionary<Guid, TowerPartyTicketResult>? admissions)
    {
        lock (Instance.Gate)
        {
            _saving = false;
            _rejected = admissions is null;
            _admissions = admissions;
            if (admissions is not null) EnterFloorLocked(_first);
        }
    }

    internal ChapterTwoTowerRankingParticipant[] CaptureRankMembers()
    {
        lock (Instance.Gate)
            return _admissions is null ? [] : Roster.OrderBy(slot => slot.SlotIndex)
                .Select(slot => new ChapterTwoTowerRankingParticipant(slot.CharacterId,
                    ChapterTwoTowerRankMember.Capture(_admissions[slot.CharacterId].Character))).ToArray();
    }

    internal (bool IsReady, TowerPartyTicketResult? Ticket) AdmissionFor(Guid id)
    {
        lock (Instance.Gate)
        {
            if (_rejected) return (true, null);
            if (_admissions is null) return (false, null);
            return (true, _members[id].Available ? _admissions[id] : null);
        }
    }

    internal void Join(Guid id)
    {
        lock (Instance.Gate) _members[id].Joined = true;
    }

    internal GameServerPacket[] TakePackets(Guid id)
    {
        lock (Instance.Gate)
        {
            if (!_members.TryGetValue(id, out var member) || !member.Available || !member.Joined) return [];
            var packets = member.Packets.ToArray();
            member.Packets.Clear();
            return packets;
        }
    }

    private IEnumerable<Member> Available() => _members.Values.Where(m => m.Available);

    private void BroadcastLocked(GameServerPacket packet)
    {
        foreach (var member in Available()) member.Packets.Enqueue(packet);
    }

    private void EnterFloorLocked(DungeonTowerFloor floor)
    {
        var actors = floor.Apcs.Select(apc => new GameDungeonMonster(
                apc.MapListIndex, 0, apc.ApcId, apc.Level, Type: 5,
                X: (ushort)apc.Position.X, Y: (ushort)apc.Position.Y) { Team = apc.Team })
            .Concat(floor.Room.Monsters)
            .Select((actor, index) => actor with
            {
                UniqueId = checked((ushort)((floor.Definition.FloorNumber - 1) * 256 + index + 1))
            }).ToArray();
        _floor = floor.Definition.FloorNumber;
        _stage = 0;
        _released = false;
        foreach (var member in Available()) member.Loaded = false;
        Instance.EnterDetachedRoom(floor.Room with { Monsters = actors });
        Instance.ResetGroundItems();
        BroadcastLocked(GameProtocolEngine.CreateChapterTwoTowerFloor(checked((ushort)_floor),
            (uint)Random.Shared.NextInt64(1L << 32), floor.Definition.MapId, actors));
    }

    internal void MarkLoaded(Guid id, int floor)
    {
        lock (Instance.Gate)
        {
            if (floor != _floor || !_members.TryGetValue(id, out var member)
                || !member.Available || !member.Joined || member.Loaded) return;
            member.Loaded = true;
            ReleaseLoadingLocked();
        }
    }

    private void ReleaseLoadingLocked()
    {
        if (_released || !Available().Any() || !Available().All(m => m.Joined && m.Loaded)) return;
        _released = true;
        BroadcastLocked(GameProtocolEngine.CreateFinishLoading());
    }

    internal bool ProcessStage(Guid id, int floor, Guid leader, byte action, long tick)
    {
        lock (Instance.Gate)
        {
            if (id != leader || floor != _floor || !_released || _result is not null
                || !IsJoinedLocked(id) || !Available().Any(m => m.Alive)) return false;
            if (action == 1 && _stage == 0)
            {
                _stage = 1;
                _startedAt = tick;
                return true;
            }

            if (action == 2 && _stage == 1 && Instance.ClearEnabled)
            {
                _stage = 2;
                return true;
            }

            return false;
        }
    }

    internal bool TryAdvance(Guid id, int floor, Guid leader, DungeonCatalog catalog)
    {
        lock (Instance.Gate)
        {
            if (id != leader || floor != _floor || _stage != 2 || !Instance.ClearEnabled
                || _result is not null || !IsJoinedLocked(id) || !Available().Any(m => m.Alive) || _floor >= Tower.Floors.Count
                || !catalog.TryCreateTowerFloor(Tower.DungeonId, _floor + 1, out var next)) return false;
            EnterFloorLocked(next);
            return true;
        }
    }

    internal bool ReportMonsterDeath(Guid id, int floor, ushort uid, long tick)
    {
        lock (Instance.Gate)
        {
            if (floor != _floor || _stage != 1 || _result is not null || !IsAliveLocked(id)) return false;
            var generation = Instance.RoomGeneration;
            var room = Instance.Snapshot().Room!;
            if (!room.Monsters.Any(m => m.UniqueId == uid && m.IsHostile)
                || !Instance.TryBeginMonsterDeath(uid, generation, out _, out _)) return true;
            BroadcastLocked(GameProtocolEngine.CreateMonsterDie(uid));
            Instance.CompleteMonsterDeath(generation);
            if (!Instance.SnapshotAliveMonsters().Overlaps(room.Monsters.Where(m => m.IsHostile).Select(m => m.UniqueId))
                && Instance.TryEnableClear(generation))
            {
                _elapsed += Math.Max(0, tick - _startedAt);
                if (_floor == Tower.Floors.Count) FinishLocked(_floor);
            }

            return true;
        }
    }

    internal bool ReportDeath(Guid id, int floor, long tick)
    {
        lock (Instance.Gate)
        {
            if (floor != _floor || !_released || _result is not null || !IsAliveLocked(id)) return false;
            var member = _members[id];
            member.Alive = false;
            BroadcastLocked(GameProtocolEngine.CreateDieState(member.Roster.AreaUserId, isAlive: false));
            CheckWipeLocked(tick);
            return true;
        }
    }

    private bool IsAliveLocked(Guid id) => _members.TryGetValue(id, out var member)
        && member.Available && member.Joined && member.Alive;

    private bool IsJoinedLocked(Guid id) => _members.TryGetValue(id, out var member)
        && member.Available && member.Joined;

    private void CheckWipeLocked(long tick)
    {
        if (_result is null && Available().Any() && Available().All(m => !m.Alive))
            _wipeAt ??= tick + 5000;
    }

    internal void Depart(Guid id, long tick)
    {
        lock (Instance.Gate)
        {
            if (!_members.TryGetValue(id, out var member) || !member.Available) return;
            member.Available = false;
            member.Packets.Clear();
            if (_admissions is null)
            {
                _rejected = true;
                return;
            }
            ReleaseLoadingLocked();
            CheckWipeLocked(tick);
        }
    }

    private void FinishLocked(int floor)
    {
        _result = new TowerPartyResult(floor, (uint)Math.Min(_elapsed, uint.MaxValue), DateTimeOffset.UtcNow);
    }

    internal void QueueResult(Guid id, GameServerPacket packet)
    {
        lock (Instance.Gate)
            if (_members.TryGetValue(id, out var member) && member.Available)
                member.Packets.Enqueue(packet);
    }

    internal TowerPartyResult? BeginReward(Guid id, long tick)
    {
        lock (Instance.Gate)
        {
            if (_result is null && _wipeAt is { } wipe && tick >= wipe)
                FinishLocked(Math.Max(1, _floor - (Instance.ClearEnabled ? 0 : 1)));
            if (_result is null || !_members.TryGetValue(id, out var member) || !member.Available
                || !member.Joined || member.RewardStarted) return null;
            member.RewardStarted = true;
            return _result;
        }
    }

    internal void CompleteReward(Guid id, TowerPartyReward reward)
    {
        lock (Instance.Gate) _members[id].Reward = reward;
    }

    internal Guid[] CurrentRewardMembers()
    {
        lock (Instance.Gate) return Available().Select(m => m.Roster.CharacterId).ToArray();
    }

    internal bool CanPublishRewards
    {
        get
        {
            lock (Instance.Gate)
                return _result is not null && !_rewardsPublished && Available().All(m => m.Reward is not null);
        }
    }

    internal void PublishRewards(bool canRetry)
    {
        lock (Instance.Gate)
        {
            if (!CanPublishRewards) return;
            _rewardsPublished = true;
            var cards = new IReadOnlyList<GameChapterTwoTowerRewardItem>[4];
            foreach (var entry in _members.Values)
                cards[entry.Roster.SlotIndex] = entry.Reward?.Cards ?? [];
            foreach (var member in Available())
            {
                member.Packets.Enqueue(GameProtocolEngine.CreateChapterTwoTowerPartyReward(member.Reward!.Experience, cards));
                member.Packets.Enqueue(GameProtocolEngine.CreateChapterTwoTowerSettlementOptions(canRetry));
            }
        }
    }

    internal bool ChooseSettlement(Guid id, Guid leader, byte state, byte option, bool canRetry)
    {
        lock (Instance.Gate)
        {
            if (id != leader || !IsJoinedLocked(id) || !_rewardsPublished || _settlementOption is not null
                || state is not (1 or 2) || option > 2 || (option != 2 && !canRetry)) return false;
            if (state == 1) _settlementOption = option;
            BroadcastLocked(GameProtocolEngine.CreateEplpReply(state, option));
            return true;
        }
    }

    internal bool BeginReselect(Guid id, Guid leader)
    {
        lock (Instance.Gate)
        {
            if (_reselect || id != leader || !IsJoinedLocked(id) || _settlementOption is not (0 or 1)) return false;
            _reselect = true;
            return true;
        }
    }

    internal bool TryPrepareReselect(Guid id)
    {
        lock (Instance.Gate)
        {
            if (!_reselect || !_members.TryGetValue(id, out var member) || !member.Available
                || member.ReselectStarted) return false;
            member.ReselectStarted = true;
            return true;
        }
    }

    internal void MarkReselectPrepared(Guid id)
    {
        lock (Instance.Gate) _members[id].ReselectPrepared = true;
    }

    internal bool IsReselectPrepared(IReadOnlyCollection<Guid> live)
    {
        lock (Instance.Gate)
            return live.All(id => !_members.TryGetValue(id, out var member) || member.ReselectPrepared);
    }
}
