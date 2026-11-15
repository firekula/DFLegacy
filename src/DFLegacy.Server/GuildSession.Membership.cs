using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed partial class GuildSession
{
    private static readonly Encoding MembershipEncoding = CodePagesEncodingProvider.Instance.GetEncoding(
        936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;

    private async Task<GuildSessionResult> HandleMembershipAsync(
        PacketFrame request, string accountName, CharacterRecord character, CancellationToken ct)
    {
        var command = request.ProtocolId;
        if (!GameProtocolEngine.TryReadGuildMembershipTarget(request.Body,
            allowSelf: command == GameProtocolEngine.GuildSecedeCommand,
            hasRank: command == GameProtocolEngine.GuildSetMemberRankCommand,
            out var nameBytes, out var rank))
            return new([GameProtocolEngine.CreateCommandError(request, 17)]);

        string? name;
        try { name = nameBytes.Length == 0 ? null : MembershipEncoding.GetString(nameBytes); }
        catch (DecoderFallbackException) { return new([GameProtocolEngine.CreateCommandError(request, 17)]); }

        var change = await store.ChangeGuildMembershipAsync(accountName, character.Id, character.GuildId,
            command, name, rank, sessions.IsOnline, ct);
        if (change.Error != 0) return new([GameProtocolEngine.CreateCommandError(request, change.Error)]);

        var guild = change.Guild!;
        var actor = change.Actor!;
        var target = change.Target!;
        var members = change.Members!;
        var guildName = GuildRules.Encode(guild.Name);
        var targetName = GuildRules.Encode(target.Name);
        if (command == GameProtocolEngine.GuildSecedeCommand)
        {
            var selfExit = target.Id == actor.Id;
            var mode = selfExit ? (byte)1 : (byte)2;
            var departure = GameProtocolEngine.CreateGuildMemberDeparture(guild.Id, target.CharacterNo, mode, guildName, targetName);
            foreach (var member in members.Where(member => member.Id != actor.Id))
                sessions.NotifyGuildEvents(member.Id, guild.Id, [departure]);
            var reply = GameProtocolEngine.CreateGuildSecedeReply(mode, guildName, targetName);
            var removed = GameProtocolEngine.CreateGuildDeparture(mode);
            var nonMember = GameProtocolEngine.CreateGuildSelfRank(5);
            if (selfExit)
            {
                Reset();
                return new([reply, removed, nonMember], actor);
            }

            sessions.NotifyGuildEvents(target.Id, 0, [removed, nonMember], refreshCharacter: true);
            return new([reply]);
        }

        if (command == GameProtocolEngine.GuildSetMemberRankCommand)
        {
            if (change.Changed)
            {
                var roster = CreateRosterPacket(GameProtocolEngine.GuildMembersCommand, guild, members);
                foreach (var member in members.Where(member => member.Id != actor.Id && member.Id != target.Id))
                    sessions.NotifyGuildEvents(member.Id, guild.Id, [roster]);
                sessions.NotifyGuildEvents(target.Id, guild.Id,
                    [GameProtocolEngine.CreateGuildSelfRank(target.GuildRank),
                        GameProtocolEngine.CreateGuildMemberRank(target.GuildRank), GuildInfoFor(target)], refreshCharacter: true);
            }
            return new([GameProtocolEngine.CreateGuildMemberRankReply(targetName, target.GuildRank)]);
        }

        var delegated = GameProtocolEngine.CreateGuildMasterDelegated(targetName);
        var updatedRoster = CreateRosterPacket(GameProtocolEngine.GuildMembersCommand, guild, members);
        foreach (var member in members.Where(member => member.Id != actor.Id && member.Id != target.Id))
            sessions.NotifyGuildEvents(member.Id, guild.Id, [delegated, updatedRoster]);
        sessions.NotifyGuildEvents(target.Id, guild.Id,
            [GameProtocolEngine.CreateGuildSelfRank(target.GuildRank), GuildInfoFor(target),
                GameProtocolEngine.CreateGuildMasterAppointed(), updatedRoster], refreshCharacter: true);
        return new([GameProtocolEngine.CreateCommandReply(request, true),
            GameProtocolEngine.CreateGuildSelfRank(actor.GuildRank),
            GameProtocolEngine.CreateGuildMemberRank(actor.GuildRank), GuildInfoFor(actor), updatedRoster], actor);

        GameServerPacket GuildInfoFor(CharacterRecord member) => CreateInfoPacket(guild, member.GuildRank, members.Length);
    }
}
