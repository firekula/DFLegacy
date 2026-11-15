namespace DFLegacy.Server;

internal sealed record GuildInvitation(Guid Inviter, Guid? InviterSession, Guid Invitee, Guid InviteeSession,
    uint GuildId, long ExpiresAt);

internal sealed record GuildInvitationOffer(byte Error, GuildInvitation? Invitation = null, string? GuildName = null);

internal sealed record GuildInvitationResolution(byte Error, GuildInvitation? Invitation = null,
    CharacterRecord? Member = null, Guid[]? OtherMembers = null);

public sealed partial class JsonGameStore
{
    // GuildConfirmWindow returns a refusal after 15 seconds.
    private const long GuildInvitationLifetimeMilliseconds = 15_000;
    // The current roster response carries an 8-bit row count.
    private const int GuildInvitationMemberLimit = byte.MaxValue;
    private readonly Dictionary<Guid, GuildInvitation> _guildInvitations = [];

    internal async Task<GuildInvitationOffer> OfferGuildInvitationAsync(string accountName, Guid inviterId, string inviteeName,
        Func<Guid, Guid?> getSession, CancellationToken ct, uint? webGuildId = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!TryFindCharacterLocked(accountName, inviterId, out var inviterAccount, out _, out var inviter))
                return new(34);
            var guild = _guilds.Find(candidate => candidate.Id == inviter.GuildId);
            if (guild is null || (webGuildId is { } expectedGuildId && guild.Id != expectedGuildId)) return new(34);
            if (!CanInviteGuildMember(inviter, guild)) return new(36);
            var inviteeAccount = _accounts.FirstOrDefault(account => account.Characters.Any(target => target.Name == inviteeName));
            var invitee = inviteeAccount?.Characters.FirstOrDefault(target => target.Name == inviteeName);
            // The website authorizes the account separately from the sender's game session.
            var inviterSession = webGuildId is null ? getSession(inviterId) : null;
            if (invitee is null || getSession(invitee.Id) is not { } inviteeSession
                || (webGuildId is null && inviterSession is null))
                return new(3);
            if (invitee.GuildId != 0) return new(39);
            if (IsGuildInvitationBlocked(inviterAccount, inviter, inviteeAccount!, invitee)) return new(77);
            if (GuildMemberCount(guild.Id) >= GuildInvitationMemberLimit) return new(38);

            var now = Environment.TickCount64;
            foreach (var stale in _guildInvitations.Values.Where(invite => invite.ExpiresAt < now
                || getSession(invite.Invitee) != invite.InviteeSession
                || (invite.InviterSession is { } active && getSession(invite.Inviter) != active)).ToArray())
                _guildInvitations.Remove(stale.Invitee);
            if (_guildInvitations.TryGetValue(invitee.Id, out var previous))
                return previous.Inviter == inviterId && previous.GuildId == guild.Id ? new(0) : new(17);

            var invitation = new GuildInvitation(inviterId, inviterSession, invitee.Id, inviteeSession,
                guild.Id, now + GuildInvitationLifetimeMilliseconds);
            _guildInvitations.Add(invitee.Id, invitation);
            return new(0, invitation, guild.Name);
        }
        finally { _gate.Release(); }
    }

    internal async Task DiscardGuildInvitationAsync(GuildInvitation invitation, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_guildInvitations.TryGetValue(invitation.Invitee, out var pending) && ReferenceEquals(pending, invitation))
                _guildInvitations.Remove(invitation.Invitee);
        }
        finally { _gate.Release(); }
    }

    internal async Task<GuildInvitationResolution> ResolveGuildInvitationAsync(string accountName, Guid inviteeId, bool accept,
        Func<Guid, Guid?> getSession, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_guildInvitations.Remove(inviteeId, out var invitation)
                || getSession(inviteeId) != invitation.InviteeSession
                || Environment.TickCount64 > invitation.ExpiresAt)
                return new(103);
            if (invitation.InviterSession is { } active && getSession(invitation.Inviter) != active) return new(3);
            if (!TryFindCharacterLocked(accountName, inviteeId, out var inviteeAccount, out _, out var invitee))
                return new(3);
            var guild = _guilds.Find(candidate => candidate.Id == invitation.GuildId);
            var inviterAccount = _accounts.FirstOrDefault(account => account.Characters.Any(candidate => candidate.Id == invitation.Inviter));
            var inviter = inviterAccount?.Characters.FirstOrDefault(candidate => candidate.Id == invitation.Inviter);
            if (guild is null || inviter is null || inviter.GuildId != guild.Id || !CanInviteGuildMember(inviter, guild))
                return new(34);
            if (!accept) return new(0, invitation);
            if (invitee.GuildId != 0) return new(39, invitation);
            if (invitee.GuildDepartedAtUtc is { } departed && DateTimeOffset.UtcNow - departed < TimeSpan.FromDays(3))
                return new(104, invitation);
            if (IsGuildInvitationBlocked(inviterAccount!, inviter, inviteeAccount, invitee)) return new(17);
            if (_rewardQuarantined || IsEconomicWriteBlocked(inviteeId)) return new(17);

            var members = _accounts.SelectMany(account => account.Characters)
                .Where(member => member.GuildId == guild.Id).Select(member => member.Id).ToArray();
            if (members.Length >= GuildInvitationMemberLimit) return new(38, invitation);
            var joined = invitee with { GuildId = guild.Id, GuildName = guild.Name, GuildRank = 0 };
            var accounts = CopyStoreAccountsLocked();
            SetStoreCharacter(accounts, joined);
            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, ct);
            }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                try { logger.LogError(error, "Guild invitation snapshot outcome uncertain; writes quarantined."); } catch (Exception) { }
                return new(17);
            }
            _accounts = accounts;
            return new(0, invitation, joined, members.Where(member => member != invitation.Inviter).ToArray());
        }
        finally { _gate.Release(); }
    }

    private int GuildMemberCount(uint guildId) =>
        _accounts.SelectMany(account => account.Characters).Count(member => member.GuildId == guildId);

    internal static bool CanInviteGuildMember(CharacterRecord inviter, GuildRecord guild) =>
        inviter.GuildRank is 2 or 3 || (inviter.GuildRank == 1 && inviter.Id == guild.FounderCharacterId);

    private static bool IsGuildInvitationBlocked(AccountRecord inviterAccount, CharacterRecord inviter,
        AccountRecord inviteeAccount, CharacterRecord invitee) =>
        inviterAccount.Blacklist?.Any(entry => entry.Name == invitee.Name) == true
        || inviteeAccount.Blacklist?.Any(entry => entry.Name == inviter.Name) == true;
}
