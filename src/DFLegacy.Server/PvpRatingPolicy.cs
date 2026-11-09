namespace DFLegacy.Server;

public readonly record struct PvpRatingDeltas(int RawWinnerDelta, int RawLoserDelta, int WinnerDelta, int LoserDelta);
public readonly record struct PvpPointChange(int Points, byte Grade, int Delta);

/// <summary>Recovered normal type4 individual expectations, adapted to ACT1 rank thresholds.
/// See duel/maps/points/points-contract.md. Every indicated float store is significant.</summary>
public static class PvpRatingPolicy
{
    public static PvpRatingDeltas CalculateDeltas(byte winnerGrade, byte loserGrade)
    {
        ValidateGrade(winnerGrade); ValidateGrade(loserGrade);
        var rw = (float)Expectation(winnerGrade, loserGrade, winnerGrade);
        var rl = (float)Expectation(loserGrade, winnerGrade, winnerGrade);
        var sum = (float)(rw + rl);
        var ratio = (float)(rl / sum);
        var units = (float)(2 * ratio);
        var rawWinner = (int)((double)units * 1000.0);
        var rawLoser = (int)(-(double)units * 1000.0);
        // Native x87 multiplies the full signed integer by the promoted float32 0.9;
        // storing this product to float first would incorrectly turn -899 into -900.
        var loser = loserGrade <= 9 ? 0 : (int)((double)rawLoser * (double)0.9f);
        return new(rawWinner, rawLoser, rawWinner, loser);
    }

    internal static PvpRatingDeltas CalculateTeamDeltas(IReadOnlyList<byte> winners, IReadOnlyList<byte> losers)
    {
        if (winners.Count is < 1 or > 4 || winners.Count != losers.Count) throw new ArgumentException("Balanced teams required.");
        float rw=0,rl=0;
        // Native accumulators store float after each cross-team pair.
        foreach (var w in winners)
            foreach (var l in losers)
            { ValidateGrade(w); ValidateGrade(l); rw=(float)(rw+Expectation(w,l,w)); rl=(float)(rl+Expectation(l,w,w)); }
        var sum=(float)(rw+rl);
        var ratio=(float)(rl/sum);
        var units=(float)((winners.Count+losers.Count)*ratio);
        var winner=(int)((double)units/winners.Count*1000.0);
        var loser=(int)(-(double)units/losers.Count*1000.0);
        return new(winner,loser,winner,(int)((double)loser*(double)0.9f));
    }

    public static int ForfeitDelta(byte leaverGrade, byte opponentGrade) => CalculateDeltas(opponentGrade, leaverGrade).LoserDelta;

    public static PvpPointChange Apply(PvpExperienceCatalog catalog, int points, byte grade, int delta)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ValidateGrade(grade);
        if (points < 0) throw new ArgumentOutOfRangeException(nameof(points));
        if (delta == 0 || (delta < 0 && grade <= 9)) return new(points, grade, 0);
        var revised = checked((int)Math.Clamp((long)points + delta, 0, int.MaxValue));
        var newGrade = grade > 29 ? grade : catalog.GetGradeForPoints(revised);
        if (grade is >= 10 and <= 15 && newGrade < grade)
        {
            revised = checked(catalog.GetCurrentRankPoint(grade) + 1);
            newGrade = grade;
        }
        return new(revised, newGrade, revised - points);
    }

    private static double Expectation(byte gradeA, byte gradeB, byte winnerGrade)
    {
        var a = Filter(gradeA); var b = Filter(gradeB);
        var special = ((a == 1 && b == 0) || (a == 0 && b == 1)) && winnerGrade > 9;
        if (special) { a = gradeA; b = gradeB; }
        if (a == b || special) return (a - b) * (double)0.024f + 0.5;
        if (a == 0) return 0;
        if (b == 0) return 1;
        return (a - b) * (double)0.024f + 0.5;
    }

    private static int Filter(byte grade) => grade > 20 ? 20 : grade > 9 ? grade - 9 : 0;
    private static void ValidateGrade(byte grade)
    {
        if (grade > 30) throw new ArgumentOutOfRangeException(nameof(grade), "ACT1 supports grades 0 through 30.");
    }
}
