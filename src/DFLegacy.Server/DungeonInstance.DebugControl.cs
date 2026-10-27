namespace DFLegacy.Server;

internal sealed partial class DungeonInstance
{
    private readonly Dictionary<ushort, DungeonDebugDropState> _debugDropReplies = [];
    private readonly Dictionary<ushort, long> _debugDropApplied = [];

    private Dictionary<ushort, DungeonDebugDropState> CaptureDropTestStatesLocked() => _members.ToDictionary(
        member => member.AreaUserId,
        member => _debugDropReplies.GetValueOrDefault(member.AreaUserId, new(false, DungeonDebugDropResult.None, 0))
            with { Allowed = string.Equals(member.AccountName, DebugChatCommands.AllowedAccount, StringComparison.OrdinalIgnoreCase) });

    internal DungeonDebugDropResult ApplyDebugDropRequest(DungeonDebugDropRequest request, long now)
    {
        lock (Gate)
        {
            var member = _members.FirstOrDefault(value => value.AreaUserId == request.UserId);
            if (!_run.IsActive || member is null) return DungeonDebugDropResult.Stale;
            var result = request.RunId != _debugRunId || request.RequestId > now + 2_500_000
                || request.RequestId < now - 50_000_000 ? DungeonDebugDropResult.Stale
                : !string.Equals(member.AccountName, DebugChatCommands.AllowedAccount, StringComparison.OrdinalIgnoreCase)
                    ? DungeonDebugDropResult.Denied : DungeonDebugDropResult.Applied;
            // Rejected timestamps may be arbitrarily far in the future. Replies
            // must never become the ordering watermark for accepted commands.
            if (_debugDropApplied.TryGetValue(request.UserId, out var applied) && request.RequestId <= applied)
            {
                if (request.RequestId == applied && result == DungeonDebugDropResult.Applied)
                    _debugDropReplies[request.UserId] = new(false, result, request.RequestId);
                return result;
            }
            if (result == DungeonDebugDropResult.Applied)
            {
                _dropDebugAll = request.Enabled;
                _debugDropApplied[request.UserId] = request.RequestId;
            }
            _debugDropReplies[request.UserId] = new(false, result, request.RequestId);
            return result;
        }
    }
}
