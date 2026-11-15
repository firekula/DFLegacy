using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed record GuildMembershipChange(
    byte Error, GuildRecord? Guild = null, CharacterRecord? Actor = null, CharacterRecord? Target = null,
    CharacterRecord[]? Members = null, bool Changed = false);

public sealed partial class JsonGameStore
{
    internal async Task<GuildMembershipChange> ChangeGuildMembershipAsync(
        string accountName, Guid actorId, uint expectedGuildId, byte command, string? targetName,
        byte requestedRank, Func<Guid, bool> isOnline, CancellationToken ct,
        bool allowOfflineRemoval = false, Guid? expectedTargetId = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined || IsEconomicWriteBlocked(actorId)) return new(17);
            if (!TryFindCharacterLocked(accountName, actorId, out _, out _, out var actor)
                || expectedGuildId == 0 || actor.GuildId != expectedGuildId)
                return new(34);
            var guildIndex = _guilds.FindIndex(candidate => candidate.Id == actor.GuildId);
            if (guildIndex < 0) return new(34);

            var guild = _guilds[guildIndex];
            var members = _accounts.SelectMany(account => account.Characters)
                .Where(member => member.GuildId == guild.Id).ToArray();
            var isMaster = guild.FounderCharacterId == actorId && actor.GuildRank == 1;
            var target = targetName is null ? actor : members.FirstOrDefault(member =>
                member.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
            if (expectedTargetId.HasValue && target?.Id != expectedTargetId.Value) return new(3);
            var permissionError = GetGuildMembershipError(
                actor, target, isMaster, command, requestedRank, targetName is null, members, isOnline,
                allowOfflineRemoval);
            if (permissionError != 0) return new(permissionError);
            if (target is null || IsEconomicWriteBlocked(target.Id)) return new(17);

            if (command == GameProtocolEngine.GuildSetMemberRankCommand && target.GuildRank == requestedRank)
                return new(0, guild, actor, target, members);

            var accounts = CopyStoreAccountsLocked();
            List<GuildRecord>? guilds = null;
            switch (command)
            {
                case GameProtocolEngine.GuildSecedeCommand:
                    target = target with { GuildId = 0, GuildName = null, GuildRank = 0, GuildDepartedAtUtc = DateTimeOffset.UtcNow };
                    if (target.Id == actor.Id) actor = target;
                    members = members.Where(member => member.Id != target.Id).ToArray();
                    break;
                case GameProtocolEngine.GuildDelegateMasterCommand:
                    actor = actor with { GuildRank = 2 };
                    target = target with { GuildRank = 1 };
                    guild = guild with { FounderCharacterId = target.Id };
                    guilds = new(_guilds) { [guildIndex] = guild };
                    SetStoreCharacter(accounts, actor);
                    break;
                case GameProtocolEngine.GuildSetMemberRankCommand:
                    target = target with { GuildRank = requestedRank };
                    break;
            }
            SetStoreCharacter(accounts, target);
            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, ct, guilds: guilds);
            }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                try { logger.LogError(error, "Guild membership snapshot outcome uncertain; writes quarantined."); } catch (Exception) { }
                return new(17);
            }

            _accounts = accounts;
            if (guilds is not null) _guilds = guilds;
            members = members.Select(member => member.Id == target.Id ? target : member.Id == actor.Id ? actor : member).ToArray();
            return new(0, guild, actor, target, members, Changed: true);
        }
        finally { _gate.Release(); }
    }

    private static byte GetGuildMembershipError(
        CharacterRecord actor, CharacterRecord? target, bool isMaster, byte command, byte requestedRank,
        bool selfExit, CharacterRecord[] members, Func<Guid, bool> isOnline, bool allowOfflineRemoval)
    {
        if (command == GameProtocolEngine.GuildSecedeCommand)
        {
            if (selfExit) return isMaster || actor.GuildRank == 1 ? (byte)87 : (byte)0;
            if (!(isMaster || actor.GuildRank is 2 or 3)) return 36;
            if (target is null || (!allowOfflineRemoval && !isOnline(target.Id))) return 3;
            return target.Id == actor.Id || MembershipPriority(target.GuildRank) <= MembershipPriority(actor.GuildRank)
                ? (byte)36 : (byte)0;
        }

        if (command == GameProtocolEngine.GuildDelegateMasterCommand)
        {
            if (!isMaster) return 36;
            if (target is null) return 34;
            return target.GuildRank == 2 && target.Id != actor.Id ? (byte)0 : (byte)86;
        }

        if (!(isMaster || actor.GuildRank == 2)) return 83;
        if (target is null) return 3;
        if (target.Id == actor.Id) return 85;
        if (target.GuildRank == 1) return 87;
        if (requestedRank == 1) return 88;
        if (requestedRank is not (0 or 2 or 3 or 4)) return 17;
        if (!isMaster && (target.GuildRank == 2 || requestedRank == 2)) return 86;
        if (requestedRank == 2 && target.GuildRank != 2 && members.Count(member => member.GuildRank == 2) >= 5) return 89;
        return 0;
    }

    private static int MembershipPriority(byte rank) => rank == 0 ? 5 : rank;
}
