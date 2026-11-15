using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.Server;

internal sealed record ChapterTwoTowerRankingParticipant(Guid CharacterId, ChapterTwoTowerRankMember Member);

internal sealed record ChapterTwoTowerRankingResult(
    ChapterTwoTowerPersonalBest? PersonalBest,
    bool Improved,
    uint? PersonalRank,
    uint? VirtualRank,
    ChapterTwoTowerPersonalBest[] TopFive);

internal sealed record ChapterTwoTowerRankingState(
    DateTimeOffset ObservedAt,
    ChapterTwoTowerRankingEntry[] Entries,
    Dictionary<Guid, ChapterTwoTowerRankingReceipt> Receipts);

internal sealed record ChapterTwoTowerRankingEntry(
    Guid AttemptId,
    DateTimeOffset CompletedAt,
    ChapterTwoTowerRankingParticipant[] Participants,
    ushort ClearedFloor,
    uint TotalMilliseconds)
{
    internal ChapterTwoTowerPersonalBest ToBest() =>
        new(Participants.Select(participant => participant.Member).ToArray(), ClearedFloor, TotalMilliseconds);
}

internal sealed record ChapterTwoTowerRankingReceipt(string Fingerprint, Guid[] ImprovedCharacterIds);

public sealed partial class JsonGameStore
{
    private const ushort MinimumTowerRankingFloor = 10;
    private static readonly TimeSpan TowerRankingChinaOffset = TimeSpan.FromHours(8);
    private ChapterTwoTowerRankingState _chapterTwoTowerRanking = new(default, [], []);

    internal async Task<ChapterTwoTowerRankingResult> SettleChapterTwoTowerRankingAsync(
        Guid attemptId, ushort dungeonId, IReadOnlyList<ChapterTwoTowerRankingParticipant> participants,
        ushort clearedFloor, uint totalMilliseconds, DateTimeOffset completedAt, Guid requestingCharacterId,
        CancellationToken cancellationToken)
    {
        ValidateTowerRankingCategory(dungeonId, participants.Count);
        var roster = participants.ToArray();
        var memberIds = roster.Select(participant => participant.CharacterId).ToHashSet();
        if (attemptId == Guid.Empty || clearedFloor == 0 || completedAt == default
            || memberIds.Count != roster.Length || memberIds.Contains(Guid.Empty)
            || !memberIds.Contains(requestingCharacterId))
            throw new ArgumentException("Invalid tower ranking attempt.");

        // Enlistment order is significant for the display. Hash the exact captured roster.
        var attempt = new ChapterTwoTowerRankingEntry(attemptId, completedAt.ToUniversalTime(),
            roster, clearedFloor, totalMilliseconds);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(attempt)));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = _chapterTwoTowerRanking;
            var character = FindTowerRankingCharacterLocked(requestingCharacterId);
            if (current.Receipts.TryGetValue(attemptId, out var receipt))
            {
                if (receipt.Fingerprint != fingerprint)
                    throw new ArgumentException("Tower ranking attempt was already completed with other data.",
                        nameof(attemptId));
                return ProjectTowerRanking(current, roster.Length, requestingCharacterId, completedAt,
                    GetTowerRankingBest(character, roster.Length), receipt.ImprovedCharacterIds.Contains(requestingCharacterId),
                    attempt);
            }

            var members = _accounts.SelectMany(account => account.Characters)
                .Where(candidate => memberIds.Contains(candidate.Id)).ToArray();
            if (_rewardQuarantined || members.Length != roster.Length)
                return ProjectTowerRanking(current, roster.Length, requestingCharacterId, completedAt,
                    GetTowerRankingBest(character, roster.Length), improved: false, attempt);

            var best = attempt.ToBest();
            var improvements = new List<Guid>();
            var accounts = CopyStoreAccountsLocked();
            foreach (var member in members)
            {
                var previous = GetTowerRankingBest(member, roster.Length);
                if (previous is not null && CompareTowerScore(best, previous) >= 0)
                    continue;

                if (previous is not null) improvements.Add(member.Id);
                var records = member.ChapterTwoTowerPersonalBests ?? [];
                SetStoreCharacter(accounts, member with
                {
                    ChapterTwoTowerPersonalBests =
                        [.. records.Where(record => record.Members.Length != roster.Length), best]
                });
            }

            var observedAt = current.ObservedAt > attempt.CompletedAt ? current.ObservedAt : attempt.CompletedAt;
            var entries = clearedFloor >= MinimumTowerRankingFloor ? current.Entries.Append(attempt) : current.Entries;
            var next = new ChapterTwoTowerRankingState(observedAt, CompactTowerRankingEntries(entries, observedAt),
                new Dictionary<Guid, ChapterTwoTowerRankingReceipt>(current.Receipts)
                {
                    [attemptId] = new(fingerprint, improvements.ToArray())
                });
            try
            {
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, cancellationToken,
                    chapterTwoTowerRanking: next);
            }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                logger.LogError(error, "Tower ranking snapshot outcome uncertain; writes quarantined.");
                return ProjectTowerRanking(current, roster.Length, requestingCharacterId, completedAt,
                    GetTowerRankingBest(character, roster.Length), improved: false, attempt);
            }

            _accounts = accounts;
            _chapterTwoTowerRanking = next;
            return ProjectTowerRanking(next, roster.Length, requestingCharacterId, completedAt,
                GetTowerRankingBest(FindTowerRankingCharacterLocked(requestingCharacterId), roster.Length),
                improvements.Contains(requestingCharacterId), attempt);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<ChapterTwoTowerRankingResult> GetChapterTwoTowerRankingAsync(
        ushort dungeonId, int teamSize, Guid characterId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ValidateTowerRankingCategory(dungeonId, teamSize);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var best = GetTowerRankingBest(FindTowerRankingCharacterLocked(characterId), teamSize);
            return ProjectTowerRanking(_chapterTwoTowerRanking, teamSize, characterId, now, best, improved: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // The original Chapter Two catalog contains one rank-enabled tower. Its legacy private
    // record did not store a dungeon id. An additional tower would need its own private records.
    private static void ValidateTowerRankingCategory(ushort dungeonId, int teamSize)
    {
        if (dungeonId != DungeonCatalog.TowerOfIllusionId)
            throw new ArgumentOutOfRangeException(nameof(dungeonId));
        if (teamSize is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(teamSize));
    }

    private CharacterRecord? FindTowerRankingCharacterLocked(Guid characterId) =>
        _accounts.SelectMany(account => account.Characters).FirstOrDefault(character => character.Id == characterId);

    private static ChapterTwoTowerPersonalBest? GetTowerRankingBest(CharacterRecord? character, int teamSize) =>
        character?.ChapterTwoTowerPersonalBests?.FirstOrDefault(record => record.Members.Length == teamSize);

    private static int CompareTowerScore(ChapterTwoTowerPersonalBest left, ChapterTwoTowerPersonalBest right) =>
        CompareTowerScore(left.ClearedFloor, left.TotalMilliseconds, right.ClearedFloor, right.TotalMilliseconds);

    private static int CompareTowerScore(ushort leftFloor, uint leftTime, ushort rightFloor, uint rightTime) =>
        leftFloor == rightFloor ? leftTime.CompareTo(rightTime) : rightFloor.CompareTo(leftFloor);

    private static (DateTimeOffset SeasonStart, DateTimeOffset Refresh) GetTowerRankingPublication(DateTimeOffset now)
    {
        var rankingDay = now.ToOffset(TowerRankingChinaOffset).AddHours(-4);
        var firstMonth = (rankingDay.Month - 1) / 3 * 3 + 1;
        return (new(rankingDay.Year, firstMonth, 1, 4, 0, 0, TowerRankingChinaOffset),
            new(rankingDay.Year, rankingDay.Month, rankingDay.Day, 4, 0, 0, TowerRankingChinaOffset));
    }

    private static string TowerRankingTeam(ChapterTwoTowerRankingEntry entry) =>
        string.Join(',', entry.Participants.Select(participant => participant.CharacterId).Order());

    private static IOrderedEnumerable<ChapterTwoTowerRankingEntry> OrderTowerRanking(
        IEnumerable<ChapterTwoTowerRankingEntry> entries) =>
        entries.OrderByDescending(entry => entry.ClearedFloor).ThenBy(entry => entry.TotalMilliseconds)
            .ThenBy(entry => entry.CompletedAt).ThenBy(entry => entry.AttemptId);

    // One published best and, only if better, one pending score per team are enough to
    // serve the current daily publication. Receipts remain available even after scores expire.
    private static ChapterTwoTowerRankingEntry[] CompactTowerRankingEntries(
        IEnumerable<ChapterTwoTowerRankingEntry> entries, DateTimeOffset now)
    {
        var (seasonStart, refresh) = GetTowerRankingPublication(now);
        var result = new List<ChapterTwoTowerRankingEntry>();
        foreach (var team in entries.Where(entry => entry.CompletedAt >= seasonStart).GroupBy(TowerRankingTeam))
        {
            var published = OrderTowerRanking(team.Where(entry => entry.CompletedAt < refresh)).FirstOrDefault();
            var pending = OrderTowerRanking(team.Where(entry => entry.CompletedAt >= refresh)).FirstOrDefault();
            if (published is not null) result.Add(published);
            if (pending is not null && (published is null || CompareTowerScore(pending.ClearedFloor,
                    pending.TotalMilliseconds, published.ClearedFloor, published.TotalMilliseconds) < 0))
                result.Add(pending);
        }
        return result.ToArray();
    }

    private static ChapterTwoTowerRankingResult ProjectTowerRanking(ChapterTwoTowerRankingState state,
        int teamSize, Guid characterId, DateTimeOffset now, ChapterTwoTowerPersonalBest? personalBest, bool improved,
        ChapterTwoTowerRankingEntry? attempt = null)
    {
        var (seasonStart, refresh) = GetTowerRankingPublication(now);
        var eligible = state.Entries.Where(entry => entry.Participants.Length == teamSize
            && entry.CompletedAt >= seasonStart && entry.CompletedAt < refresh);
        var ordered = OrderTowerRanking(eligible.GroupBy(TowerRankingTeam)
            .Select(team => OrderTowerRanking(team).First()));
        var topFive = new List<ChapterTwoTowerPersonalBest>(5);
        uint? personalRank = null;
        uint? virtualRank = attempt is { ClearedFloor: >= MinimumTowerRankingFloor } ? 1u : null;
        uint position = 0;
        uint rank = 0;
        ChapterTwoTowerRankingEntry? previous = null;
        foreach (var entry in ordered)
        {
            position++;
            if (previous is null || CompareTowerScore(entry.ClearedFloor,
                    entry.TotalMilliseconds, previous.ClearedFloor, previous.TotalMilliseconds) != 0)
                rank = position;
            previous = entry;

            if (position <= 5)
                topFive.Add(entry.ToBest());
            if (personalRank is null && entry.Participants.Any(participant => participant.CharacterId == characterId))
                personalRank = rank;
            if (virtualRank is not null && CompareTowerScore(entry.ClearedFloor,
                    entry.TotalMilliseconds, attempt!.ClearedFloor, attempt.TotalMilliseconds) < 0)
                virtualRank++;
        }

        return new(personalBest is null ? null : personalBest with { Members = personalBest.Members.ToArray() },
            improved, personalRank, virtualRank, topFive.ToArray());
    }
}
