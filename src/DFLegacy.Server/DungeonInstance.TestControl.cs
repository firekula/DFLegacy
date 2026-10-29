namespace DFLegacy.Server;

internal sealed record DungeonTestMonster(ushort Uid, ushort MonsterId, bool Alive);
internal sealed record DungeonTestGround(ushort GroundId, ushort ItemId, uint Count, bool IsGold,
    short X, short Y, bool Pending, Guid InstanceId, ushort OwnerUid, byte RoutingSource,
    byte? State, ushort? Durability, byte? SealState, uint? EquipmentQualitySeed);
internal sealed record DungeonTestState(long RunId, long Generation, ushort MapId, ushort DungeonId, byte RoomX,
    byte RoomY, DungeonInstanceMember[] Members, ushort[] DeadMemberUids, DungeonTestMonster[] Monsters, DungeonTestGround[] GroundItems);

internal sealed partial class DungeonInstance
{
    internal long TestRunId { get { lock (Gate) return _run.IsActive ? _debugRunId : 0; } }
    private static long _nextTestLootEvent;
    private readonly Queue<DungeonTestLootResult> _testLootResults = [];
    internal DungeonTestState? CaptureTestControlState()
    {
        lock (Gate)
        {
            if (!_run.IsActive || _run.CurrentRoom is not { } room) return null;
            return new(_debugRunId, _roomGeneration, room.MapId, _run.DungeonId, _run.RoomX, _run.RoomY,
                _members.ToArray(), _members.Where(member => _deadMembers.Contains(member.CharacterId)).Select(member => member.AreaUserId).ToArray(),
                room.Monsters.Select(value => new DungeonTestMonster(value.UniqueId, value.MonsterIndex, AliveMonsters.Contains(value.UniqueId))).ToArray(),
                GroundItems.GetRoomItems(_run.RoomX, _run.RoomY).Where(value => value.Activated).Select(value =>
                    new DungeonTestGround(value.GroundId, value.ItemId, value.CountOrValue, value.IsGold, value.X, value.Y,
                        _pendingLoot.ContainsKey(value.GroundId), value.PreservedItem?.InstanceId ?? Guid.Empty,
                        value.OwnerUserId, value.RoutingSource, value.PreservedItem?.State, value.PreservedItem?.Durability,
                        value.PreservedItem?.SealState, value.PreservedItem?.EquipmentQualitySeed)).ToArray());
        }
    }
    internal string RelocateTestGround(DungeonTestCommand command)
    {
        lock (Gate)
        {
            if (!_run.IsActive || command.RunId != _debugRunId) return "run";
            if (command.RoomGeneration != _roomGeneration) return "room-generation";
            if (!_members.Any(value => value.AreaUserId == command.UserId)) return "member";
            if (command.Placements is not { Length: > 0 and <= 256 } placements) return "placements";
            if (placements.Any(value => value is null)) return "placements";
            if (placements.Select(value => value.GroundId).Distinct().Count() != placements.Length) return "duplicate-ground";
            var originals = new List<(DungeonGroundItem Item, DungeonTestPlacement Target)>();
            foreach (var target in placements)
            {
                if (target.X < 0 || target.X > short.MaxValue || target.Y < 0 || target.Y > short.MaxValue) return "coordinates";
                if (!GroundItems.TryGet(target.GroundId, _run.RoomX, _run.RoomY, out var item)) return "ground";
                if (_pendingLoot.ContainsKey(target.GroundId)) return "pickup-pending";
                originals.Add((item, target));
            }
            // Validation and replacement share Gate with every claim and reservation. No delete/regrant or new ID.
            foreach (var (item, target) in originals)
                GroundItems.Relocate(item, (short)target.X, (short)target.Y);
            Version++;
            return "";
        }
    }
    internal DungeonTestLootContext CaptureTestLootContext(DungeonGroundItem ground, Guid pickupId, ushort picker,
        ushort winner, IReadOnlyList<byte>? scores)
    {
        lock (Gate)
        {
            var member = _members.FirstOrDefault(value => value.AreaUserId == winner);
            return new(_debugRunId, _roomGeneration, _run.CurrentRoom?.MapId ?? 0, ground.RoomX, ground.RoomY,
                ground.GroundId, ground.ItemId, ground.CountOrValue, pickupId, picker, winner,
                member?.CharacterId ?? Guid.Empty, member?.PartySlot ?? byte.MaxValue, scores?.ToArray() ?? new byte[4], ground.IsGold);
        }
    }
    internal void RecordTestLootResult(DungeonTestLootContext context, bool committed, string reason,
        ushort? destinationSlot = null, Guid instanceId = default)
    {
        lock (Gate)
        {
            var result = new DungeonTestLootResult(context, committed ? "committed" : "rejected", reason,
                destinationSlot, instanceId, DateTime.UtcNow.ToFileTimeUtc(), Interlocked.Increment(ref _nextTestLootEvent));
            _testLootResults.Enqueue(result);
            DungeonTestLootJournal.Add(result);
            while (_testLootResults.Count > 256) _testLootResults.Dequeue();
        }
    }
    internal DungeonTestLootResult[] CaptureTestLootResults() { lock (Gate) return _testLootResults.ToArray(); }
}
