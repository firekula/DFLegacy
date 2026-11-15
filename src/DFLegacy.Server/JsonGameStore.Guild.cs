namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    private List<GuildRecord> _guilds = [];
    private uint _nextGuildId = 1;

    private List<GuildRecord> GuildsWithMembers(IEnumerable<AccountRecord> accounts)
    {
        var memberships = accounts.SelectMany(account => account.Characters)
            .Select(character => character.GuildId).ToHashSet();
        return _guilds.Where(guild => memberships.Contains(guild.Id)).ToList();
    }

    public async Task<bool> IsGuildNameAvailableAsync(string name, CancellationToken ct = default)
    {
        if (!GuildRules.IsName(name)) return false;
        await _gate.WaitAsync(ct);
        try { return !_guilds.Any(guild => guild.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }
        finally { _gate.Release(); }
    }

    public async Task<bool> IsGuildAddressAvailableAsync(string address, CancellationToken ct = default)
    {
        if (!GuildRules.IsAddress(address)) return false;
        await _gate.WaitAsync(ct);
        try { return !_guilds.Any(guild => guild.Address.Equals(address, StringComparison.OrdinalIgnoreCase)); }
        finally { _gate.Release(); }
    }

    public async Task<(GuildRecord? Guild, CharacterRecord[] Members)> GetGuildAsync(uint id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return (_guilds.Find(guild => guild.Id == id),
                _accounts.SelectMany(account => account.Characters).Where(character => character.GuildId == id && id != 0).ToArray());
        }
        finally { _gate.Release(); }
    }

    internal async Task<GuildAnnouncementResult> SetGuildAnnouncementAsync(
        string accountName, Guid characterId, uint guildId, byte[] text, CancellationToken ct)
    {
        if (!GuildRules.TryReadAnnouncement(text, out var announcement)) return new(108);
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
            // The native announcement button allows a guild leader or deputy (ranks 1 and 2).
            var canEdit = character.GuildRank == 2
                || (character.GuildRank == 1 && guild.FounderCharacterId == characterId);
            if (!canEdit) return new(36);
            if (guild.Announcement == announcement) return new(0);

            var guilds = new List<GuildRecord>(_guilds);
            guilds[index] = guild with { Announcement = announcement };
            try
            {
                await WriteRewardStatePrivilegedAsync(_accounts, _dungeonRewardLedger, ct, guilds: guilds);
            }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                try { logger.LogError(error, "Guild announcement snapshot outcome uncertain; writes quarantined."); } catch (Exception) { }
                return new(17);
            }
            _guilds = guilds;
            return new(0, _accounts.SelectMany(account => account.Characters)
                .Where(member => member.GuildId == guild.Id)
                .Select(member => member.Id).ToArray());
        }
        finally { _gate.Release(); }
    }

    internal async Task<GuildDissolutionResult?> DissolveGuildAsync(
        string accountName, Guid founderId, uint guildId, CancellationToken ct)
    {
        if (guildId == 0) return null;
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined || IsEconomicWriteBlocked(founderId)
                || !TryFindCharacterLocked(accountName, founderId, out _, out _, out var founder)
                || founder.GuildId != guildId || founder.GuildRank != 1
                || !_guilds.Any(guild => guild.Id == guildId && guild.FounderCharacterId == founderId))
                return null;

            var accounts = CopyStoreAccountsLocked();
            var members = new List<Guid>();
            CharacterRecord? updatedFounder = null;
            foreach (var account in accounts)
            {
                for (var index = 0; index < account.Characters.Count; index++)
                {
                    var character = account.Characters[index];
                    if (character.GuildId != guildId) continue;
                    var updated = character with { GuildId = 0, GuildName = null, GuildRank = 0 };
                    account.Characters[index] = updated;
                    if (updated.Id == founderId) updatedFounder = updated;
                    else members.Add(updated.Id);
                }
            }
            if (updatedFounder is null) return null;
            var guilds = _guilds.Where(guild => guild.Id != guildId).ToList();
            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, ct, guilds: guilds);
            }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                try { logger.LogError(error, "Guild dissolution snapshot outcome uncertain; writes quarantined."); } catch (Exception) { }
                return null;
            }
            _accounts = accounts;
            _guilds = guilds;
            return new(updatedFounder, members.ToArray());
        }
        finally { _gate.Release(); }
    }

    internal async Task<byte> SetGuildIconAsync(string accountName, Guid characterId, uint guildId,
        byte[] pixels, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!TryFindCharacterLocked(accountName, characterId, out _, out _, out var character)
                || character.GuildId != guildId || guildId == 0)
                return 34;
            var index = _guilds.FindIndex(guild => guild.Id == guildId);
            if (index < 0) return 34;
            var guild = _guilds[index];
            if (character.GuildRank != 1 || guild.FounderCharacterId != characterId) return 36;
            if (guild.Level < GuildRules.IconMinimumGuildLevel) return 37;
            if (_rewardQuarantined || IsEconomicWriteBlocked(characterId)) return 17;
            if (guild.IconPixels is { } current && current.AsSpan().SequenceEqual(pixels)) return 0;

            var guilds = new List<GuildRecord>(_guilds);
            guilds[index] = guild with { IconPixels = pixels };
            try
            {
                await WriteRewardStatePrivilegedAsync(_accounts, _dungeonRewardLedger, ct, guilds: guilds);
            }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                try { logger.LogError(error, "Guild icon snapshot outcome uncertain; writes quarantined."); } catch (Exception) { }
                return 17;
            }
            _guilds = guilds;
            return 0;
        }
        finally { _gate.Release(); }
    }

    public async Task<GuildCreateResult> CreateGuildAsync(string accountName, Guid characterId,
        string name, string address, int cost, CancellationToken ct = default)
    {
        if (!GuildRules.IsName(name)) return new(108);
        if (!GuildRules.IsAddress(address)) return new(112);
        if (cost < 0) return new(17);
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return new(17, Unknown: true);
            if (IsEconomicWriteBlocked(characterId)) return new(17);
            if (!TryFindCharacterLocked(accountName, characterId, out _, out _, out var character)) return new(17);
            if (character.GuildId != 0)
            {
                var existing = _guilds.Find(guild => guild.Id == character.GuildId);
                return existing?.FounderCharacterId == characterId
                    && existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && existing.Address.Equals(address, StringComparison.OrdinalIgnoreCase)
                    ? new(0, character, existing, Replayed: true) : new(32);
            }
            if (_guilds.Any(guild => guild.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return new(107);
            if (_guilds.Any(guild => guild.Address.Equals(address, StringComparison.OrdinalIgnoreCase))) return new(111);
            if (character.Gold < cost) return new(31);
            if (_nextGuildId == uint.MaxValue) return new(17);
            var guild = new GuildRecord(_nextGuildId, name, address, characterId, DateTimeOffset.UtcNow);
            var updated = character with { Gold = character.Gold - cost, GuildId = guild.Id, GuildName = name, GuildRank = 1 };
            var accounts = CopyStoreAccountsLocked();
            SetStoreCharacter(accounts, updated);
            var guilds = new List<GuildRecord>(_guilds) { guild };
            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, ct,
                    guilds: guilds, nextGuildId: _nextGuildId + 1);
            }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                try { logger.LogError(error, "Guild creation snapshot outcome uncertain; writes quarantined."); } catch (Exception) { }
                return new(17, Unknown: true);
            }
            _accounts = accounts;
            _guilds = guilds;
            _nextGuildId++;
            return new(0, updated, guild);
        }
        finally { _gate.Release(); }
    }
}
