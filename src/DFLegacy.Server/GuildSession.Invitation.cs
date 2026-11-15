using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed partial class GuildSession
{
    private async Task<GuildSessionResult> HandleInvitationAsync(PacketFrame request, string accountName,
        CharacterRecord character, CancellationToken ct)
    {
        GuildSessionResult Error(byte failure) => new([GameProtocolEngine.CreateCommandError(request, failure)]);
        if (request.ProtocolId == GameProtocolEngine.GuildInviteCommand)
        {
            if (!GameProtocolEngine.TryReadGuildText(request.Body, out var target)) return Error(17);
            string targetName;
            try
            {
                targetName = MembershipEncoding.GetString(target);
            }
            catch (DecoderFallbackException)
            {
                return Error(17);
            }
            if (targetName.Any(char.IsControl)) return Error(17);
            var error = await SendInvitationAsync(accountName, character, targetName, store, sessions, ct);
            if (error != 0) return Error(error);
            return new([GameProtocolEngine.CreateCommandReply(request, true)]);
        }

        if (!GameProtocolEngine.TryReadGuildInvitationResponse(request.Body, out var accepted)) return Error(17);
        var resolution = await store.ResolveGuildInvitationAsync(accountName, character.Id, accepted,
            sessions.GetGuildSessionId, ct);
        if (resolution.Invitation is not { } pending)
            return accepted ? Error(resolution.Error) : new([]);
        var inviteeName = GuildRules.Encode(character.Name);
        if (resolution.Error is 39 or 104)
            sessions.NotifyGuildEvents(pending.Inviter, pending.GuildId,
                [GameProtocolEngine.CreateGuildInvitationResult(resolution.Error, inviteeName)]);
        if (resolution.Error != 0) return accepted ? Error(resolution.Error) : new([]);
        if (resolution.Member is not { } joined)
        {
            // The original CMD163 success receiver always reports that the character joined.
            sessions.NotifyGuildEvents(pending.Inviter, pending.GuildId,
                [GameProtocolEngine.CreateGuildInvitationResult(0, inviteeName)]);
            return new([]);
        }

        var joinedPacket = GameProtocolEngine.CreateGuildJoined(joined.GuildId, joined.CharacterNo,
            GuildRules.Encode(joined.GuildName!), inviteeName);
        sessions.NotifyGuildEvents(pending.Inviter, joined.GuildId,
            [GameProtocolEngine.CreateGuildInvitationResult(1, inviteeName), joinedPacket]);
        foreach (var member in resolution.OtherMembers ?? [])
            sessions.NotifyGuildEvents(member, joined.GuildId, [joinedPacket]);
        return new([GameProtocolEngine.CreateCommandReply(request, true), joinedPacket,
            GameProtocolEngine.CreateGuildSelfRank(joined.GuildRank),
            ..await InitialStateAsync(joined, ct)], joined);
    }

    internal static async Task<byte> SendInvitationAsync(string account, CharacterRecord inviter, string targetName,
        JsonGameStore store, CharacterSessionRegistry sessions, CancellationToken ct, uint? webGuildId = null)
    {
        byte[] inviterName;
        try { inviterName = GuildRules.Encode(inviter.Name); }
        catch (EncoderFallbackException) { return 17; }
        var offer = await store.OfferGuildInvitationAsync(account, inviter.Id, targetName,
            sessions.GetGuildSessionId, ct, webGuildId);
        if (offer.Invitation is { } invitation && offer.GuildName is { } guildName
            && !sessions.NotifyGuildEvents(invitation.Invitee, 0,
                [GameProtocolEngine.CreateGuildInvitation(GuildRules.Encode(guildName), inviterName)],
                expectedSessionId: invitation.InviteeSession))
        {
            await store.DiscardGuildInvitationAsync(invitation, ct);
            return 3;
        }
        return offer.Error;
    }
}
