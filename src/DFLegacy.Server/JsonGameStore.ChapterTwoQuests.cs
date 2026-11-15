namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    public async Task<CharacterRecord?> ArchiveUnavailableChapterTwoQuestsAsync(
        string userName, Guid characterId, QuestCatalog catalog, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(a => string.Equals(a.UserName, userName, StringComparison.OrdinalIgnoreCase));
            var index = account?.Characters.FindIndex(c => c.Id == characterId) ?? -1;
            if (account is null || index < 0) return null;
            var character = account.Characters[index];
            var retired = (character.Quests ?? [])
                .Where(q => q.QuestId != ushort.MaxValue && catalog.IsUnavailableInChapterTwo(q.QuestId))
                .DistinctBy(q => q.QuestId).ToArray();
            if (retired.Length == 0) return character;
            var ids = retired.Select(q => q.QuestId).ToHashSet();
            var updated = character with
            {
                Quests = character.Quests!.Where(q => !ids.Contains(q.QuestId)).ToList(),
                ChapterTwoArchivedQuests = retired.Concat(character.ChapterTwoArchivedQuests ?? [])
                    .DistinctBy(q => q.QuestId).ToList()
            };
            account.Characters[index] = updated;
            var committed = false;
            try
            {
                await SaveLockedAsync(cancellationToken, () => committed = true, isolateCommittedDiagnostics: true);
            }
            catch
            {
                if (!committed) account.Characters[index] = character;
                throw;
            }
            try
            {
                logger.LogInformation("Archived {Count} unavailable Chapter Two quest(s) for character {CharacterId}, freeing active slots: {QuestIds}.",
                    retired.Length, characterId, string.Join(",", ids.Order()));
            }
            catch { /* A diagnostic sink cannot undo the committed migration. */ }
            return updated;
        }
        finally { _gate.Release(); }
    }
}
