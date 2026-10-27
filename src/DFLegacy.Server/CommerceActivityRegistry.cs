namespace DFLegacy.Server;

internal enum CommerceActivity { Trade, Store, Purchase }

/// <summary>Atomic admission across the two economic state machines; tokens protect displaced sessions.</summary>
internal sealed class CommerceActivityRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (CommerceActivity Kind, Guid Token)> _owners = [];
    internal bool TryAcquire(CommerceActivity kind, Guid token, params Guid[] ids)
    {
        if (token == Guid.Empty || ids.Length == 0 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length) return false;
        lock (_gate)
        {
            if (ids.Any(id => _owners.ContainsKey(id))) return false;
            foreach (var id in ids) _owners[id] = (kind, token);
            return true;
        }
    }
    internal void Release(Guid token, params Guid[] ids)
    {
        lock (_gate) foreach (var id in ids)
            if (_owners.TryGetValue(id, out var owner) && owner.Token == token) _owners.Remove(id);
    }
    internal bool Is(Guid id, CommerceActivity kind)
    { lock (_gate) return _owners.TryGetValue(id, out var owner) && owner.Kind == kind; }
    internal bool Owns(Guid id, CommerceActivity kind, Guid token)
    { lock (_gate) return _owners.TryGetValue(id, out var owner) && owner == (kind, token); }
}
