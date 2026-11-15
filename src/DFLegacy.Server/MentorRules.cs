namespace DFLegacy.Server;

public sealed record MentorRegistrationState(long RegisteredAtUnix = 0, long DeletedAtUnix = 0);

internal static class MentorRules
{
    internal static int Capacity(int level) => level switch
    {
        < 18 => 0,
        < 20 => 5,
        < 30 => 8,
        <= 99 => 10,
        _ => 0
    };

    internal static bool CanRegister(MentorRegistrationState? state, DateTimeOffset now)
    {
        if (state is null) return true;
        var reset = new DateTimeOffset(now.Date.AddHours(6), now.Offset);
        if (now < reset) reset = reset.AddDays(-1);
        return state.RegisteredAtUnix < reset.ToUnixTimeSeconds()
            && state.DeletedAtUnix < reset.AddDays(-2).ToUnixTimeSeconds();
    }
}

internal sealed record MentorListMember(CharacterRecord Character, string AccountName, bool BlocksViewer);
internal sealed record MentorRoster(CharacterRecord Owner, MentorListMember? Master,
    IReadOnlyList<MentorListMember> Apprentices);
internal sealed record MentorMutationResult(DungeonRewardLedgerStatus Status, byte Error = 0,
    CharacterRecord? Requester = null, CharacterRecord? Target = null,
    bool RequesterEmpty = false, bool TargetEmpty = false, string? Reason = null);
internal sealed record MentorRemovalNotice(Guid CharacterId, string PeerName);
