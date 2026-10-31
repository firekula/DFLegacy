using System.Text.RegularExpressions;

namespace DFLegacy.Server;

public sealed class CharacterSpCatalog
{
    private const string ScriptPath = "etc/sptable.etc";
    private const int MaximumSupportedLevel = 200;

    // Built-in copy of the reference PVF's [sp table] rows, used when the active
    // Script.pvf omits etc/sptable.etc. rewards[level] is the SP granted upon
    // reaching that level.
    private static readonly uint[] FallbackRewards = BuildFallbackRewards();

    private static readonly Regex TableRowPattern = new(
        @"^\s*(\d+)\s+(\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ScriptFileSystem _scripts;
    private readonly ILogger<CharacterSpCatalog> _logger;
    private readonly Lazy<uint[]> _rewards;

    public CharacterSpCatalog(
        ScriptFileSystem scripts,
        ILogger<CharacterSpCatalog> logger)
    {
        _scripts = scripts;
        _logger = logger;
        _rewards = new Lazy<uint[]>(
            LoadRewards,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int Count => Math.Max(0, _rewards.Value.Length - 1);

    public void Initialize() => _ = _rewards.Value;

    public uint GetRewardForLevel(int level)
    {
        var rewards = _rewards.Value;
        return level > 0 && level < rewards.Length
            ? rewards[level]
            : 0;
    }

    public int CalculateLevelUpReward(int previousLevel, int level)
    {
        if (level <= previousLevel)
        {
            return 0;
        }

        long total = 0;
        for (var enteredLevel = previousLevel + 1; enteredLevel <= level; enteredLevel++)
        {
            total = checked(total + GetRewardForLevel(enteredLevel));
        }

        return checked((int)total);
    }

    public int CalculateTotalReward(int level) =>
        CalculateLevelUpReward(0, level);

    /// <summary>
    /// SP earned by a single experience grant. Mirrors the reference client's
    /// sp_entitlement delta: each level's SP budget is paid out progressively
    /// across that level's experience span instead of a lump sum at the
    /// level-up boundary, so gaining experience yields skill points mid-level.
    /// </summary>
    public int CalculateProgressiveSpGrant(
        CharacterExperienceGrant progression,
        IReadOnlyList<uint>? experienceThresholds)
    {
        if (progression.GrantedExperience == 0)
        {
            return 0;
        }

        var before = CalculateProgressiveEntitlement(
            progression.PreviousLevel,
            progression.PreviousExperience,
            experienceThresholds);
        var after = CalculateProgressiveEntitlement(
            progression.Level,
            progression.Experience,
            experienceThresholds);
        return Math.Max(0, after - before);
    }

    /// <summary>
    /// Cumulative SP a character has earned at (level, experience): the
    /// level-boundary total plus the share of the current level's SP budget
    /// proportional to experience progress through the level. Falls back to
    /// the level-boundary total when the experience table is unavailable.
    /// </summary>
    public int CalculateProgressiveEntitlement(
        int level,
        uint experience,
        IReadOnlyList<uint>? experienceThresholds)
    {
        level = Math.Max(1, level);
        var baseSp = CalculateTotalReward(level);
        var rewards = _rewards.Value;
        var nextLevel = level + 1;
        // SP budget for the level -> level+1 stretch, granted on reaching level+1.
        var budget = nextLevel > 0 && nextLevel < rewards.Length
            ? (long)rewards[nextLevel]
            : 0L;
        if (budget <= 0 || experienceThresholds is null || experienceThresholds.Count == 0)
        {
            return baseSp;
        }

        // Cumulative experience to reach `level` (thresholds[level-2], 0 at level 1).
        long spanLow = level >= 2 && level - 2 < experienceThresholds.Count
            ? experienceThresholds[level - 2]
            : 0L;
        // Cumulative experience to reach `level`+1 (thresholds[level-1]).
        long spanHigh = level - 1 >= 0 && level - 1 < experienceThresholds.Count
            ? experienceThresholds[level - 1]
            : 0L;
        if (spanHigh <= spanLow)
        {
            return baseSp;
        }

        var span = spanHigh - spanLow;
        var progress = Math.Min(Math.Max((long)experience - spanLow, 0L), span);
        return checked((int)(baseSp + progress * budget / span));
    }

    private uint[] LoadRewards()
    {
        if (!_scripts.FileExists(ScriptPath))
        {
            _logger.LogWarning(
                "Character SP table {ScriptPath} was not found in {Source}; using the built-in fallback SP table.",
                ScriptPath,
                _scripts.SourceDescription);
            return FallbackRewards;
        }

        var rewardsByLevel = new SortedDictionary<int, uint>();
        var inTable = false;
        foreach (var rawLine in _scripts.ReadLines(ScriptPath))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Equals("[sp table]", StringComparison.OrdinalIgnoreCase))
            {
                inTable = true;
                continue;
            }

            if (line.Equals("[/sp table]", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!inTable || line.Length == 0)
            {
                continue;
            }

            var match = TableRowPattern.Match(line);
            if (!match.Success
                || !int.TryParse(match.Groups[1].Value, out var level)
                || level <= 0
                || !uint.TryParse(match.Groups[2].Value, out var reward))
            {
                throw new InvalidDataException(
                    $"{ScriptPath} contains an invalid SP row: '{rawLine}'.");
            }

            if (!rewardsByLevel.TryAdd(level, reward))
            {
                throw new InvalidDataException(
                    $"{ScriptPath} contains duplicate level {level}.");
            }
        }

        if (rewardsByLevel.Count == 0)
        {
            throw new InvalidDataException(
                $"{ScriptPath} does not contain any [sp table] rows.");
        }

        var maximumLevel = rewardsByLevel.Keys.Max();
        var rewards = new uint[maximumLevel + 1];
        for (var level = 1; level <= maximumLevel; level++)
        {
            if (!rewardsByLevel.TryGetValue(level, out rewards[level]))
            {
                throw new InvalidDataException(
                    $"{ScriptPath} is missing level {level}.");
            }
        }

        _logger.LogInformation(
            "Cached {Count} DFLegacy level-up SP rewards from {Source}; supported levels=1-{MaximumLevel}.",
            maximumLevel,
            _scripts.SourceDescription,
            maximumLevel);
        return rewards;
    }

    private static string StripComment(string value)
    {
        var commentIndex = value.IndexOf("//", StringComparison.Ordinal);
        return commentIndex >= 0 ? value[..commentIndex] : value;
    }

    // Mirrors the [sp table] rows of the reference Script.pvf: 30 SP per level
    // 2-10, then +30 every ten levels (60 for 11-20, 90 for 21-30) with the
    // 31-50 stretch held at 120 and the 51-200 stretches stepping by 30.
    private static uint[] BuildFallbackRewards()
    {
        var rewards = new uint[MaximumSupportedLevel + 1];
        for (var level = 1; level <= MaximumSupportedLevel; level++)
        {
            rewards[level] = level switch
            {
                <= 1 => 0u,
                <= 10 => 30u,
                <= 20 => 60u,
                <= 30 => 90u,
                <= 50 => 120u,
                _ => checked((uint)(150 + (level - 51) / 10 * 30))
            };
        }

        return rewards;
    }
}
