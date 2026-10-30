using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.Server;

internal sealed record PvpSettlementMember(Guid CharacterId, byte StartGrade, bool Forfeit, ushort[]? Stats, uint[]? Scores);
internal sealed record PvpSettlementRequest(Guid MatchId, PvpSettlementMember[] Members, Guid? Winner, bool Draw);
internal sealed record PvpSettlementAward(Guid CharacterId, uint VictoryPoints, int Points, byte Grade,
    int Wins, int Losses, int PlayCount, int Count, int ExperienceDelta, byte GradeExtension = 0);
internal sealed record PvpSettlementReceipt(Guid MatchId, string Fingerprint, PvpSettlementAward[] Awards);
internal sealed record PvpSettlementResult(DungeonRewardLedgerStatus Status, PvpSettlementAward[] Awards);

public sealed partial class JsonGameStore
{
    private List<PvpSettlementReceipt> _pvpLedger = [];

    internal async Task<PvpSettlementResult> CommitPvpMatchAsync(PvpSettlementRequest request,
        PvpExperienceCatalog catalog, CancellationToken ct = default)
    {
        if (request.MatchId == Guid.Empty || request.Members.Length != 2
            || request.Members.Select(m => m.CharacterId).Distinct().Count() != 2
            || request.Members.Any(m => m.CharacterId == Guid.Empty || m.StartGrade > 30
                || (!m.Forfeit && (m.Stats is null || m.Scores is null))
                || (m.Stats is not null && m.Stats.Length != 11) || (m.Scores is not null && (m.Scores.Length != 8 || m.Scores.Sum(s => (long)s) > uint.MaxValue)))
            || (request.Winner is { } winner && !request.Members.Any(m => m.CharacterId == winner))
            || (request.Draw && request.Winner is not null)) return new(DungeonRewardLedgerStatus.Rejected, []);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        await _gate.WaitAsync(ct);
        try
        {
            if (_rewardQuarantined) return new(DungeonRewardLedgerStatus.Unknown, []);
            var old = _pvpLedger.Find(r => r.MatchId == request.MatchId);
            if (old is not null) return old.Fingerprint == fingerprint
                ? new(DungeonRewardLedgerStatus.Committed, old.Awards) : new(DungeonRewardLedgerStatus.Rejected, []);
            if (request.Members.Any(m => StoreCharacter(_accounts, m.CharacterId) is null))
                return new(DungeonRewardLedgerStatus.Rejected, []);
            var accounts = CopyStoreAccountsLocked();
            var awards = new List<PvpSettlementAward>();
            foreach (var member in request.Members)
            {
                var character = StoreCharacter(accounts, member.CharacterId)!;
                var other = request.Members.First(m => m.CharacterId != member.CharacterId);
                var win = !member.Forfeit && request.Winner == member.CharacterId;
                var delta = member.Forfeit ? PvpRatingPolicy.ForfeitDelta(member.StartGrade, other.StartGrade)
                    : request.Draw || request.Winner is null ? 0
                    : win ? PvpRatingPolicy.CalculateDeltas(member.StartGrade, other.StartGrade).WinnerDelta
                    : PvpRatingPolicy.CalculateDeltas(other.StartGrade, member.StartGrade).LoserDelta;
                var applied = PvpRatingPolicy.Apply(catalog, character.PvpPoints, member.StartGrade, delta);
                var vpAward = member.Forfeit ? 0u : win ? 4u : 1u;
                var updated = character with
                {
                    VictoryPoints = (uint)Math.Min(uint.MaxValue, (ulong)character.VictoryPoints + vpAward),
                    PvpPoints = applied.Points, PvpGrade = applied.Grade,
                    PvpWins = SaturatingAdd(character.PvpWins, win ? 1 : 0),
                    PvpLosses = SaturatingAdd(character.PvpLosses, win ? 0 : 1),
                    PvpPlayCount = SaturatingAdd(character.PvpPlayCount, 1), PvpCount = SaturatingAdd(character.PvpCount, 1)
                };
                SetStoreCharacter(accounts, updated);
                awards.Add(new(updated.Id, updated.VictoryPoints, updated.PvpPoints, updated.PvpGrade,
                    updated.PvpWins, updated.PvpLosses, updated.PvpPlayCount, updated.PvpCount,
                    updated.PvpPoints - character.PvpPoints, updated.PvpGradeExtension));
            }
            var receipt = new PvpSettlementReceipt(request.MatchId, fingerprint, awards.ToArray());
            var ledger = new List<PvpSettlementReceipt>(_pvpLedger) { receipt };
            try { await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, ct, pvpLedger: ledger); }
            catch (Exception error)
            {
                _rewardQuarantined = true;
                logger.LogError(error, "PvP match {MatchId} snapshot outcome uncertain; canonical writes quarantined.", request.MatchId);
                return new(DungeonRewardLedgerStatus.Unknown, []);
            }
            _accounts = accounts; _pvpLedger = ledger;
            return new(DungeonRewardLedgerStatus.Committed, receipt.Awards);
        }
        finally { _gate.Release(); }
    }

    // Absolute VP changes require their source balance. Omitted expectations remain compatible only
    // when no VP mutation is requested. This protects stale saves while allowing genuine spending.
    private static bool IsVictoryPointUpdateCurrent(CharacterRecord character, uint? requested, uint? expected) =>
        !requested.HasValue || (expected.HasValue ? expected == character.VictoryPoints : requested == character.VictoryPoints);
}
