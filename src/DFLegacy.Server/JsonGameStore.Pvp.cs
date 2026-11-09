using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.Server;

internal sealed record PvpSettlementMember(Guid CharacterId, byte StartGrade, bool Forfeit, ushort[]? Stats, uint[]? Scores, byte Team = 0, byte Seat = byte.MaxValue);
internal sealed record PvpSettlementRequest(Guid MatchId, PvpSettlementMember[] Members, Guid? Winner, bool Draw, byte Mode = 0, byte? WinningTeam = null);
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
        if (request.MatchId == Guid.Empty || request.Members.Length is < 2 or > 8
            || request.Members.Select(m => m.CharacterId).Distinct().Count() != request.Members.Length
            || request.Members.Any(m => m.CharacterId == Guid.Empty || m.StartGrade > 30
                || (!m.Forfeit && (m.Stats is null || m.Scores is null))
                || (m.Stats is not null && m.Stats.Length != 11) || (m.Scores is not null && (m.Scores.Length != 8 || m.Scores.Sum(s => (long)s) > uint.MaxValue)))
            || (request.Winner is { } winner && !request.Members.Any(m => m.CharacterId == winner))
            || request.Mode > 2 || (request.Draw && (request.Winner is not null || request.WinningTeam is not null))
            || (request.Members.Any(m => m.Seat != 255) && (request.Members.Any(m => m.Seat > 7)
                || request.Members.Select(m => m.Seat).Distinct().Count() != request.Members.Length))
            || (request.Mode == 0 ? request.WinningTeam is not null || request.Members.Any(m => m.Team != 0)
                : request.Winner is not null || request.Members.Any(m => m.Team is not (1 or 2))
                    || request.Members.Count(m => m.Team == 1) != request.Members.Length / 2
                    || request.Members.Length % 2 != 0 || (!request.Draw && request.WinningTeam is not (1 or 2)))) return new(DungeonRewardLedgerStatus.Rejected, []);
        // Preserve fingerprints of pre-multiplayer two-member receipts across upgrades.
        var legacy = request.Mode == 0 && request.WinningTeam is null && request.Members.Length == 2
            && request.Members.All(m => m.Team == 0 && m.Seat == 255);
        var bytes = legacy ? JsonSerializer.SerializeToUtf8Bytes(new { request.MatchId,
            Members=request.Members.Select(m => new { m.CharacterId, m.StartGrade, m.Forfeit, m.Stats, m.Scores }).ToArray(),
            request.Winner, request.Draw }) : JsonSerializer.SerializeToUtf8Bytes(request);
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
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
                var win = !member.Forfeit && !request.Draw && (request.Mode == 0
                    ? request.Winner == member.CharacterId : request.WinningTeam == member.Team);
                var opponents = request.Members.Where(m => request.Mode == 0
                    ? m.CharacterId != member.CharacterId : m.Team != member.Team).ToArray();
                int delta;
                if (member.Forfeit)
                    delta = opponents.Sum(o => PvpRatingPolicy.ForfeitDelta(member.StartGrade,o.StartGrade)) / opponents.Length;
                else if (request.Draw || (request.Mode == 0 && request.Winner is null)) delta = 0;
                else if (request.Mode == 0)
                    delta = win ? opponents.Sum(o => PvpRatingPolicy.CalculateDeltas(member.StartGrade,o.StartGrade).WinnerDelta)
                        : PvpRatingPolicy.CalculateDeltas(request.Members.Single(m=>m.CharacterId==request.Winner).StartGrade,member.StartGrade).LoserDelta;
                else
                {
                    var winners=request.Members.Where(m=>m.Team==request.WinningTeam).Select(m=>m.StartGrade).ToArray();
                    var losers=request.Members.Where(m=>m.Team!=request.WinningTeam).Select(m=>m.StartGrade).ToArray();
                    var deltas=PvpRatingPolicy.CalculateTeamDeltas(winners,losers);
                    delta=win ? deltas.WinnerDelta : member.StartGrade<=9 ? 0 : deltas.LoserDelta;
                }
                var applied = PvpRatingPolicy.Apply(catalog, character.PvpPoints, member.StartGrade, delta);
                var vpAward = VictoryAward(request,member,win);
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

    private static uint VictoryAward(PvpSettlementRequest request, PvpSettlementMember member, bool win)
    {
        if (member.Forfeit) return 0;
        if (request.Draw || (request.Mode!=0 && !win)) return 1;
        var ranked=request.Members.Where(m=>!m.Forfeit && m.Scores is not null)
            .OrderByDescending(m=>m.Scores!.Sum(s=>(long)s)).ThenBy(m=>m.Seat).ThenBy(m=>Array.IndexOf(request.Members,m)).ToArray();
        if (request.Mode != 0)
        {
            var teamSize=request.Members.Count(m=>m.Team==member.Team);
            return (uint)(teamSize+(ranked.First(m=>m.Team==member.Team)==member ? 2 : 0));
        }
        if (request.Members.Length==2) return win ? 4u : 1u;
        if (win) return (uint)(request.Members.Length+1);
        var rank=Array.IndexOf(ranked,member);
        return rank<=0 ? 1u : (uint)((request.Members.Length+1)/(1<<rank));
    }

    // Absolute VP changes require their source balance. Omitted expectations remain compatible only
    // when no VP mutation is requested. This protects stale saves while allowing genuine spending.
    private static bool IsVictoryPointUpdateCurrent(CharacterRecord character, uint? requested, uint? expected) =>
        !requested.HasValue || (expected.HasValue ? expected == character.VictoryPoints : requested == character.VictoryPoints);
}
