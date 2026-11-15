namespace DFLegacy.Server;

/// <summary>The original get_rand_int(300) bounded draw, with caller-provided rand31 entropy.</summary>
internal static class MentorGoldTimerPolicy
{
    internal static int NextJitter(Func<int> nextRand31)
    {
        ArgumentNullException.ThrowIfNull(nextRand31);
        int Draw()
        {
            var sample = nextRand31();
            if (sample < 0) throw new ArgumentOutOfRangeException(nameof(nextRand31), "rand31 samples must be nonnegative.");
            return sample;
        }

        var state = Draw();
        if (state > 300) return Draw() % 300;

        // 086B1BD2..086B1C77 wraps signed32 LCG states. Integer /65536
        // truncates toward zero; an arithmetic right shift differs for negatives.
        unchecked
        {
            state = state * 1103515245 + 12345;
            var result = (state / 65536) & 0x7FF;
            state = state * 1103515245 + 12345;
            result = (result << 10) ^ ((state / 65536) & 0x3FF);
            state = state * 1103515245 + 12345;
            result = (result << 10) ^ ((state / 65536) & 0x3FF);
            var bounded = (uint)result;
            return (int)(bounded <= 300 ? bounded : bounded % 300);
        }
    }

    internal static int DelaySeconds(int jitter)
    {
        if ((uint)jitter > 300) throw new ArgumentOutOfRangeException(nameof(jitter));
        return 600 + jitter;
    }
}
