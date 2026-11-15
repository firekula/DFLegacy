using System.Globalization;
using System.Text.RegularExpressions;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed record ChapterTwoTowerRewardPlan(
    uint Experience,
    uint Gold,
    IReadOnlyList<GameChapterTwoTowerRewardItem> Cards);

// Ver_OB_216 supplies common tower reward rows. The later reference server
// reads separate Illusion-specific rows that are absent from this PVF.
public sealed class ChapterTwoTowerRewardPlanner(
    ScriptFileSystem scripts,
    DungeonExperienceCatalog experience,
    GoldDropCatalog goldDrops,
    ClearRewardCatalog equipment,
    ILogger<ChapterTwoTowerRewardPlanner> logger)
{
    private const string ScriptPath = "etc/deathtower.etc";
    private const string DungeonScriptPath = "dungeon/towers/towerofillusion.dgn";
    private const int CardRollRange = 1_000;
    private const int MaximumCardProbabilityFloor = 10;
    private const int MaximumCardSlots = 10;
    private static readonly GameChapterTwoTowerRewardItem EmptyCard = new(ushort.MaxValue, 0);
    private readonly Lazy<Tables?> _tables = new(() => LoadTables(scripts, logger));

    public ChapterTwoTowerRewardPlan Plan(
        int clearedFloor,
        byte characterLevel,
        IDropRandomSource? random = null) =>
        Plan(clearedFloor, MaximumCardSlots, characterLevel, random);

    public ChapterTwoTowerRewardPlan Plan(
        int clearedFloor,
        int maxItemCount,
        byte characterLevel,
        IDropRandomSource? random = null)
    {
        if (clearedFloor < 1
            || _tables.Value is not { } tables
            || clearedFloor > tables.LastFloor)
        {
            return new ChapterTwoTowerRewardPlan(0, 0, []);
        }

        random ??= GameRandomSource.Shared;
        var baseExperience = experience.GetMonsterBaseReward(characterLevel);
        var experienceWeight = tables.ExperienceWeights[clearedFloor - 1];
        var totalExperience = ToUInt32((double)clearedFloor * baseExperience * experienceWeight);

        var cardCount = Math.Min(
            tables.CardCounts[clearedFloor - 1],
            Math.Clamp(maxItemCount, 0, tables.CardLimit));
        var probabilityFloor = Math.Min(clearedFloor, MaximumCardProbabilityFloor);
        var goldThreshold = tables.GoldProbability * probabilityFloor;
        var equipmentThreshold = (tables.GoldProbability + tables.ItemProbability) * probabilityFloor;
        var cards = new GameChapterTwoTowerRewardItem[cardCount];
        uint gold = 0;
        for (var index = 0; index < cards.Length; index++)
        {
            var roll = random.Next(CardRollRange);
            cards[index] = EmptyCard;
            if (roll < goldThreshold)
            {
                var amount = Math.Min(
                    RollGold(characterLevel, tables.GoldWeight, random),
                    uint.MaxValue - gold);
                if (amount > 0)
                {
                    cards[index] = new GameChapterTwoTowerRewardItem(0, amount);
                    gold += amount;
                }
            }
            else if (roll < equipmentThreshold
                     && equipment.GenerateEquipment(ClearRewardProbabilityCategory.Default,
                         characterLevel, dungeonDifficulty: 0, partyMemberCount: 1, random) is { } itemId)
            {
                cards[index] = new GameChapterTwoTowerRewardItem(itemId, 0);
            }
        }

        return new ChapterTwoTowerRewardPlan(totalExperience, gold, cards);
    }

    private uint RollGold(byte characterLevel, float goldWeight, IDropRandomSource random)
    {
        if (!goldDrops.TryGetLevel(characterLevel, out var definition))
        {
            return 0;
        }

        // The official tower builds the clear-gold request with one normal,
        // one champion and one boss kill. Its grade index 1 uses the second
        // entry in the C2 clear-rank weights (the A grade).
        var modifier = (float)(equipment.GetGoldMultiplier(0, 1) * (1 + experience.GetRankRate(70)));
        var goldUnit = 175L * definition.BaseAmount / 1_000;
        var pool = Math.Truncate((goldUnit * 7 / 2) * (double)modifier);
        var perMember = Math.Truncate(pool / 2 * modifier);
        var spread = definition.SpreadPercent;
        var difference = spread == 0 ? 0 : random.Next(spread * 2 + 1) - spread;
        var spreadAmount = Math.Truncate(perMember * (100 + difference) / 100);
        return ToUInt32(spreadAmount * goldWeight);
    }

    private static uint ToUInt32(double value) => (uint)Math.Clamp(value, 0, uint.MaxValue);

    private static Tables? LoadTables(ScriptFileSystem scripts, ILogger logger)
    {
        var expWeights = ReadFloats("reward exp weight");
        var cardCounts = ReadIntegers("reward card num");
        var probabilities = ReadIntegers("reward item prob");
        var goldWeights = ReadFloats("reward gold weight");
        var floorRow = DropScriptParser.ReadSection(
            scripts, DungeonScriptPath, "death tower map indexes", logger).FirstOrDefault();
        var cardLimitMatch = scripts.FileExists(DungeonScriptPath)
            ? Regex.Match(scripts.ReadAllText(DungeonScriptPath),
                @"^[ \t]*\[tower max clear item num\][ \t]+(\d+)[ \t]*(?://[^\r\n]*)?\r?$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
            : Match.Empty;
        if (floorRow is not [var floorCount]
            || !int.TryParse(floorCount, NumberStyles.None, CultureInfo.InvariantCulture, out var lastFloor)
            || !int.TryParse(cardLimitMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var cardLimit)
            || lastFloor < 1
            || lastFloor > expWeights.Length
            || lastFloor > cardCounts.Length
            || expWeights.Any(weight => !float.IsFinite(weight) || weight < 0)
            || cardCounts.Any(count => count < 0)
            || probabilities is not [>= 0 and <= 100, >= 0 and <= 100, >= 0]
            || probabilities[0] + probabilities[1] > 100
            || goldWeights is not [> 0]
            || !float.IsFinite(goldWeights[0]))
        {
            logger.LogWarning("The Chapter Two tower reward tables in {Source} are incomplete.", scripts.SourceDescription);
            return null;
        }

        return new Tables(lastFloor, Math.Min(cardLimit, MaximumCardSlots),
            expWeights, cardCounts, probabilities[0], probabilities[1], goldWeights[0]);

        string[] ReadTokens(string section) =>
            DropScriptParser.ReadSection(scripts, ScriptPath, section, logger).SelectMany(row => row).ToArray();

        int[] ReadIntegers(string section) => ReadTokens(section)
            .Select(token => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value : -1)
            .ToArray();

        float[] ReadFloats(string section) => ReadTokens(section)
            .Select(token => float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value : float.NaN)
            .ToArray();
    }

    private sealed record Tables(
        int LastFloor,
        int CardLimit,
        float[] ExperienceWeights,
        int[] CardCounts,
        int GoldProbability,
        int ItemProbability,
        float GoldWeight);
}
