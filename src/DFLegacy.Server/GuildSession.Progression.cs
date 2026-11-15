using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed partial class GuildSession
{
    private async Task<GuildSessionResult> HandleProgressionAsync(
        PacketFrame request, string accountName, CharacterRecord character, int skillPoints, CancellationToken ct)
    {
        byte? skill = request.ProtocolId == GameProtocolEngine.BuySkillCommand ? request.Body[2] : null;
        if (!GameProtocolEngine.HasCommandBodyLength(request.Body, skill.HasValue ? 3 : 2, true))
            return new([GameProtocolEngine.CreateCommandError(request, 17)]);

        var result = await store.ChangeGuildProgressionAsync(accountName, character.Id, character.GuildId, skill, ct);
        if (result.Error != 0) return new([GameProtocolEngine.CreateCommandError(request, result.Error)]);

        var guild = result.Guild!;
        var members = result.Members!;
        foreach (var member in members)
        {
            if (member.Id != character.Id)
                sessions.NotifyGuildEvents(member.Id, guild.Id, [CreateInfoPacket(guild, member.GuildRank, members.Length)]);
        }

        var reply = skill is { } id
            ? GameProtocolEngine.CreateBuySkillReply((ushort)Math.Clamp(skillPoints, 0, ushort.MaxValue),
                GuildProgression.Slot(id), id, id == GuildProgression.StatusSkill ? guild.StatusSkillLevel : guild.ExperienceSkillLevel)
            : GameProtocolEngine.CreateCommandReply(request, true);
        return new([reply, CreateInfoPacket(guild, character.GuildRank, members.Length)], result.Character, ProgressionChanged: true);
    }
}
