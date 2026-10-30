using System.Net;

namespace DFLegacy.Server;

internal sealed record RelayAccountAdmission(Guid Id, Guid SessionId, uint AccountUid, IPAddress RemoteAddress);
internal sealed record FallbackRelayRoute(RelayAccountAdmission Sender, RelayAccountAdmission Recipient,
    long SenderRevision, long RecipientRevision);

public sealed partial class GameplayDatagramService
{
    internal bool TryGetChannelPeerInfo(Guid sessionId, ushort userId, out DFLegacy.Protocol.GamePeerInfo peer)
    {
        peer = null!;
        if (!_sessions.TryGetValue(sessionId, out var session)) return false;
        lock (session.Gate)
            return session.AccountUid != AccountUidRules.InvalidUid
                && TryGetPeerInfo(session, userId, session.AccountUid, out peer);
    }

    // Identity used in NOTI11 is also needed for ordinary username-only UDP
    // compatibility. Binding it grants no authenticated TCP-relay admission.
    internal void BindChannelAccountIdentity(Guid sessionId, uint accountUid)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            lock (session.Gate) session.AccountUid = accountUid;
    }
    internal async Task RetireChannelSessionAsync(Guid sessionId, Func<Task> cleanup)
    {
        RevokeRelayAccount(sessionId);
        try { await cleanup(); }
        finally { UnregisterChannelSession(sessionId); }
    }

    internal bool HasFallbackRelayContext(Guid sessionId) => SnapshotFallbackRelayScope(sessionId) is { } scope
        && (scope.PartyId.HasValue || scope.Instance is not null || scope.Pvp is not null);
    private readonly object _relayAdmissionGate = new();
    private readonly Dictionary<uint, RelayAccountAdmission> _relayAdmissionsByAccount = [];
    private readonly Dictionary<Guid, RelayAccountAdmission> _relayAdmissionsBySession = [];

    // Only the password-verifying channel login boundary may call this grant.
    // The legacy native relay itself carries an account id, not a password/token.
    internal RelayAccountAdmission? AuthenticateRelayAccount(Guid sessionId, uint accountUid)
    {
        if (accountUid == AccountUidRules.InvalidUid) return null;
        lock (_relayAdmissionGate)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return null;
            lock (session.Gate) session.AccountUid = accountUid;
            RevokeRelayAccountUnderLock(sessionId);
            if (_relayAdmissionsByAccount.TryGetValue(accountUid, out var previous))
                _relayAdmissionsBySession.Remove(previous.SessionId);
            var admission = new RelayAccountAdmission(Guid.NewGuid(), sessionId, accountUid, session.TcpAddress);
            _relayAdmissionsByAccount[accountUid] = admission;
            _relayAdmissionsBySession[sessionId] = admission;
            return admission;
        }
    }

    internal void RevokeRelayAccount(Guid sessionId)
    {
        lock (_relayAdmissionGate) RevokeRelayAccountUnderLock(sessionId);
    }

    private void RevokeRelayAccountUnderLock(Guid sessionId)
    {
        if (_relayAdmissionsBySession.Remove(sessionId, out var previous)
            && _relayAdmissionsByAccount.TryGetValue(previous.AccountUid, out var current)
            && ReferenceEquals(previous, current))
            _relayAdmissionsByAccount.Remove(previous.AccountUid);
    }

    internal RelayAccountAdmission? FindRelayAdmission(uint accountUid, IPAddress source)
    {
        lock (_relayAdmissionGate)
            return _relayAdmissionsByAccount.TryGetValue(accountUid, out var admission)
                && IsCurrentRelayAdmissionUnderLock(admission)
                && admission.RemoteAddress.Equals(NormalizeAddress(source)) ? admission : null;
    }

    internal bool IsCurrentRelayAdmission(RelayAccountAdmission admission)
    {
        lock (_relayAdmissionGate) return IsCurrentRelayAdmissionUnderLock(admission);
    }

    private bool IsCurrentRelayAdmissionUnderLock(RelayAccountAdmission admission) =>
        admission is not null && _sessions.ContainsKey(admission.SessionId)
        && _relayAdmissionsByAccount.TryGetValue(admission.AccountUid, out var byAccount)
        && ReferenceEquals(admission, byAccount)
        && _relayAdmissionsBySession.TryGetValue(admission.SessionId, out var bySession)
        && ReferenceEquals(admission, bySession);

    internal bool TryCaptureRelayRoute(RelayAccountAdmission sender, uint recipient, out FallbackRelayRoute? route)
    {
        route = null;
        RelayAccountAdmission target;
        lock (_relayAdmissionGate)
        {
            if (!IsCurrentRelayAdmissionUnderLock(sender) || sender.AccountUid == recipient
                || !_relayAdmissionsByAccount.TryGetValue(recipient, out var candidate)
                || !IsCurrentRelayAdmissionUnderLock(candidate)) return false;
            target = candidate;
        }
        var sourceScope = SnapshotFallbackRelayScope(sender.SessionId);
        var targetScope = SnapshotFallbackRelayScope(target.SessionId);
        if (!CanRouteFallback(sourceScope, targetScope)) return false;
        var captured = new FallbackRelayRoute(sender, target, sourceScope!.Revision, targetScope!.Revision);
        if (!IsCurrentRelayRoute(captured)) return false;
        route = captured;
        return true;
    }

    internal bool IsCurrentRelayRoute(FallbackRelayRoute route)
    {
        if (route is null || !IsCurrentRelayAdmission(route.Sender)
            || !IsCurrentRelayAdmission(route.Recipient)) return false;
        var source = SnapshotFallbackRelayScope(route.Sender.SessionId);
        var target = SnapshotFallbackRelayScope(route.Recipient.SessionId);
        return source?.Revision == route.SenderRevision && target?.Revision == route.RecipientRevision
            && CanRouteFallback(source, target)
            && IsCurrentRelayAdmission(route.Sender) && IsCurrentRelayAdmission(route.Recipient);
    }

    private sealed record FallbackRelayScope(ushort? PartyId, object? Instance, PvpRelayContext? Pvp, long Revision);

    private FallbackRelayScope? SnapshotFallbackRelayScope(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return null;
        lock (session.Gate)
            return new(session.RelayPartyId, session.RelayDungeonInstance,
                session.PvpContext, session.RelayContextRevision);
    }

    private static bool CanRouteFallback(FallbackRelayScope? source, FallbackRelayScope? target)
    {
        if (source is null || target is null) return false;
        if (source.Pvp is { } pvp)
            return target.Pvp is { } other && pvp.Room != Guid.Empty
                && pvp.Room == other.Room && pvp.Match == other.Match;
        return target.Pvp is null && source.PartyId.HasValue && source.PartyId == target.PartyId
            && ReferenceEquals(source.Instance, target.Instance);
    }
}
