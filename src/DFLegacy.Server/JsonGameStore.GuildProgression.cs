namespace DFLegacy.Server;

internal sealed record GuildProgressionResult(
    byte Error, GuildRecord? Guild = null, CharacterRecord? Character = null, CharacterRecord[]? Members = null);

public sealed partial class JsonGameStore
{
    internal async Task<GuildProgressionResult> ChangeGuildProgressionAsync(
        string accountName, Guid characterId, uint guildId, byte? skill, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined || IsEconomicWriteBlocked(characterId)) return new(17);
            if (!TryFindCharacterLocked(accountName, characterId, out _, out _, out var character)
                || guildId == 0 || character.GuildId != guildId)
                return new(34);
            var index = _guilds.FindIndex(guild => guild.Id == guildId);
            if (index < 0) return new(34);
            var guild = _guilds[index];
            if (character.GuildRank != 1 || guild.FounderCharacterId != characterId)
                return new(skill.HasValue ? (byte)4 : (byte)36);

            int cost;
            GuildRecord updated;
            if (skill is { } skillId)
            {
                if (!GuildProgression.IsSkill(skillId)) return new(4);
                var status = skillId == GuildProgression.StatusSkill;
                var level = (byte)((status ? guild.StatusSkillLevel : guild.ExperienceSkillLevel) + 1);
                if (level > GuildProgression.MaximumSkillLevel || guild.Level < 2 * level) return new(92);
                cost = GuildProgression.SkillGoldCost;
                if (character.Gold < cost) return new(91);
                if (guild.SkillPoints == 0) return new(22);
                updated = guild with
                {
                    SkillPoints = (ushort)(guild.SkillPoints - 1),
                    StatusSkillLevel = status ? level : guild.StatusSkillLevel,
                    ExperienceSkillLevel = status ? guild.ExperienceSkillLevel : level
                };
            }
            else
            {
                var upgrade = GuildProgression.GetUpgrade(guild.Level);
                if (!upgrade.HasValue) return new(37);
                cost = upgrade.Value.Gold;
                if (guild.Experience < upgrade.Value.Experience) return new(84);
                if (character.Gold < cost) return new(35);
                updated = guild with { Level = (byte)(guild.Level + 1), SkillPoints = (ushort)(guild.SkillPoints + 1) };
            }

            var accounts = CopyStoreAccountsLocked();
            character = character with { Gold = character.Gold - cost };
            SetStoreCharacter(accounts, character);
            var guilds = new List<GuildRecord>(_guilds) { [index] = updated };
            if (!await WriteGuildProgressionLockedAsync(accounts, guilds, ct)) return new(17);

            return new(0, updated, character, accounts.SelectMany(account => account.Characters)
                .Where(member => member.GuildId == guildId).ToArray());
        }
        finally { _gate.Release(); }
    }

    internal async Task<GuildRecord?> CreditGuildDungeonClearAsync(
        string accountName, Guid characterId, uint enteredGuildId, uint fatigue, CancellationToken ct)
    {
        if (enteredGuildId == 0 || fatigue == 0) return null;
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined || IsEconomicWriteBlocked(characterId)
                || !TryFindCharacterLocked(accountName, characterId, out _, out _, out var character)
                || character.GuildId != enteredGuildId)
                return null;
            var index = _guilds.FindIndex(guild => guild.Id == enteredGuildId);
            if (index < 0) return null;
            var guild = _guilds[index];
            var experience = (uint)Math.Min((ulong)guild.Experience + fatigue, GuildProgression.MaximumExperience);
            if (experience <= guild.Experience) return null;
            var updated = guild with { Experience = experience };
            var guilds = new List<GuildRecord>(_guilds) { [index] = updated };
            return await WriteGuildProgressionLockedAsync(_accounts, guilds, ct) ? updated : null;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> WriteGuildProgressionLockedAsync(
        List<AccountRecord> accounts, List<GuildRecord> guilds, CancellationToken ct)
    {
        try
        {
            await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, ct, guilds: guilds);
        }
        catch (Exception error)
        {
            _rewardQuarantined = true;
            try { logger.LogError(error, "Guild progression snapshot outcome uncertain; writes quarantined."); }
            catch (Exception) { }
            return false;
        }

        _accounts = accounts;
        _guilds = guilds;
        return true;
    }
}
