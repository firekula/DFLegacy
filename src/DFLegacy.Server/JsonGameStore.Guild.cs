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
