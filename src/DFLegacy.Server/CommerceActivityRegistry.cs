namespace DFLegacy.Server;

internal enum CommerceActivity { Trade, Store, StoreRevoked, Purchase, Pvp }

/// <summary>Atomic commerce/PvP ownership and party admission; tokens protect displaced sessions.</summary>
internal sealed class CommerceActivityRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (CommerceActivity Kind, Guid Token)> _owners = [];
    // Party admission includes its asynchronous roster/accepted publication. A PvP reservation
    // cannot overtake packets from an admission that has already consumed its invitation.
    private readonly Dictionary<Guid, int> _partyAdmissions = [];
    internal bool TryAcquire(CommerceActivity kind, Guid token, params Guid[] ids)
    {
        if (token == Guid.Empty || ids.Length == 0 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length) return false;
        lock (_gate)
        {
            if (ids.Any(id => _owners.ContainsKey(id)
                || (kind == CommerceActivity.Pvp && _partyAdmissions.ContainsKey(id)))) return false;
            foreach (var id in ids) _owners[id] = (kind, token);
            return true;
        }
    }
    internal IDisposable? TryBeginPartyAdmission(params Guid[] ids)
    {
        if (ids.Length == 0 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length) return null;
        lock (_gate)
        {
            if (ids.Any(id => _owners.TryGetValue(id, out var owner) && owner.Kind == CommerceActivity.Pvp)) return null;
            var participants = ids.ToArray();
            foreach (var id in participants) _partyAdmissions[id] = _partyAdmissions.GetValueOrDefault(id) + 1;
            return new PartyAdmission(this, participants);
        }
    }
    private void EndPartyAdmission(Guid[] ids)
    {
        lock (_gate) foreach (var id in ids)
            if (_partyAdmissions[id] == 1) _partyAdmissions.Remove(id);
            else _partyAdmissions[id]--;
    }
    private sealed class PartyAdmission(CommerceActivityRegistry registry, Guid[] ids) : IDisposable
    {
        private CommerceActivityRegistry? _registry = registry;
        public void Dispose() => Interlocked.Exchange(ref _registry, null)?.EndPartyAdmission(ids);
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
    internal void RevokeStores()
    {
        lock (_gate) foreach (var (id, owner) in _owners.ToArray())
            if (owner.Kind == CommerceActivity.Store) _owners[id] = (CommerceActivity.StoreRevoked, owner.Token);
    }
}
