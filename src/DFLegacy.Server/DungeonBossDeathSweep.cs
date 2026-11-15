namespace DFLegacy.Server;

// A session drains this queue on its normal read loop. No timer task writes to
// its stream or character, and a reused monster id in another room cannot win.
internal sealed class DungeonBossDeathSweep
{
    internal static readonly TimeSpan ReconcileDelay = TimeSpan.FromSeconds(5);
    private DungeonInstance? _instance;
    private long _generation;
    private readonly Dictionary<ushort, DateTimeOffset> _pending = [];

    internal bool HasPending => _pending.Count > 0;

    internal int Schedule(DungeonInstance instance, DateTimeOffset now)
    {
        ValidateRoom(instance);
        var targets = instance.SnapshotBossDeathCascadeTargets();
        foreach (var id in targets)
        {
            _pending.TryAdd(id, now + ReconcileDelay);
        }
        return targets.Length;
    }

    internal ushort[] TakeDue(DungeonInstance? instance, DateTimeOffset now)
    {
        if (instance is null)
        {
            _pending.Clear();
            _instance = null;
            return [];
        }
        ValidateRoom(instance);
        var alive = instance.SnapshotAliveMonsters();
        var due = _pending.Where(entry => entry.Value <= now && alive.Contains(entry.Key))
            .Select(entry => entry.Key).Order().ToArray();
        foreach (var id in _pending.Keys.Where(id => !alive.Contains(id) || due.Contains(id)).ToArray())
        {
            _pending.Remove(id);
        }
        return due;
    }

    private void ValidateRoom(DungeonInstance instance)
    {
        var generation = instance.RoomGeneration;
        if (!ReferenceEquals(instance, _instance) || generation != _generation)
        {
            _pending.Clear();
            _instance = instance;
            _generation = generation;
        }
    }
}
