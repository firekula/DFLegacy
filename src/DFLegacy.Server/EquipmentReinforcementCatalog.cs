using System.Globalization;
using System.Text;

namespace DFLegacy.Server;

public sealed record EquipmentReinforcementCost(
    ushort MaterialItemId,
    uint MaterialCount,
    int Gold,
    int FailureWeight,
    int PenaltyType,
    byte TargetLevel);

public sealed class EquipmentReinforcementCatalog
{
    private const string ScriptPath = "etc/upgrade.etc";

    public const byte MaximumLevel = 31;
    public const int FailureRollUpperBound = 100_000;

    private static readonly HashSet<string> SupportedSections =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "table",
            "cost",
            "cost weights by rarity",
            "type"
        };

    private readonly ScriptFileSystem _scripts;
    private readonly ILogger<EquipmentReinforcementCatalog> _logger;
    private readonly Lazy<CatalogState> _state;

    public EquipmentReinforcementCatalog(
        ScriptFileSystem scripts,
        ILogger<EquipmentReinforcementCatalog> logger)
    {
        _scripts = scripts;
        _logger = logger;
        _state = new Lazy<CatalogState>(
            Load,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => _state.Value.Rows.Count != 0;

    public int RowCount => _state.Value.Rows.Count;

    public void Initialize() => _ = _state.Value;

    public bool TryGetCost(
        byte currentLevel,
        ItemDefinition equipment,
        out EquipmentReinforcementCost cost,
        bool chapterTwo = false)
    {
        ArgumentNullException.ThrowIfNull(equipment);
        cost = null!;
        var state = _state.Value;
        var targetLevel = currentLevel + 1;
        if (currentLevel >= MaximumLevel
            || targetLevel > state.Rows.Count
            || equipment.ScriptKind != ItemScriptKind.Equipment
            || equipment.InventoryCategory != ItemInventoryCategory.Equipment)
        {
            return false;
        }

        var gold = chapterTwo ? CalculateChapterTwoGold(state, equipment) : CalculateGold(state, equipment);
        if (gold is null) return false;

        var row = state.Rows[targetLevel - 1];
        cost = new EquipmentReinforcementCost(
            row.MaterialItemId,
            row.MaterialCount,
            gold.Value,
            row.FailureWeight,
            row.PenaltyType,
            checked((byte)targetLevel));
        return true;
    }

    private CatalogState Load()
    {
        if (!_scripts.FileExists(ScriptPath))
        {
            _logger.LogWarning(
                "Reinforcement script {ScriptPath} was not found in {Source}; equipment reinforcement is disabled.",
                ScriptPath,
                _scripts.SourceDescription);
            return CatalogState.Empty;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sections = ReadSections(
            _scripts.ReadLines(
                ScriptPath,
                Encoding.GetEncoding(
                    936,
                    EncoderFallback.ReplacementFallback,
                    DecoderFallback.ReplacementFallback)));
        var rows = ReadTableRows(GetSection(sections, "table"));
        if (rows.Count == 0)
        {
            throw new InvalidDataException(
                $"{ScriptPath} does not contain any valid reinforcement rows.");
        }

        var costRows = GetSection(sections, "cost");
        var rarityRows = GetSection(sections, "cost weights by rarity");
        var typeRows = GetSection(sections, "type");
        var costsByLevel = ReadIntList(costRows);
        var rarityWeights = ReadDoubleList(rarityRows);
        var typeWeights = ReadDoubleList(typeRows);
        _logger.LogInformation(
            "Cached DFLegacy reinforcement rules from {Source}: rows={RowCount}, levelCosts={CostCount}, rarityWeights={RarityCount}, typeWeights={TypeCount}.",
            _scripts.SourceDescription,
            rows.Count,
            costsByLevel.Count,
            rarityWeights.Count,
            typeWeights.Count);
        return new CatalogState(rows, costsByLevel, rarityWeights, typeWeights,
            ReadChapterTwoPrices(costRows), ReadChapterTwoPrices(rarityRows), ReadChapterTwoPrices(typeRows));
    }

    private static int? CalculateChapterTwoGold(CatalogState state, ItemDefinition equipment)
    {
        // Ver_OB_216 uses [grade] as the [cost] index. It multiplies by the
        // equipment type and rarity in order, truncating each product.
        var typeIndex = equipment.TypeTag switch
        {
            "weapon" => 0,
            "title name" => 1,
            "coat" => 2,
            "shoulder" => 3,
            "pants" => 4,
            "shoes" => 5,
            "waist" => 6,
            "amulet" => 7,
            "wrist" => 8,
            "ring" => 9,
            _ => -1
        };
        if (typeIndex < 0 || typeIndex >= state.ChapterTwoTypeWeights.Length
            || equipment.Grade is not { } grade || grade < 0 || grade >= Math.Min(100, state.ChapterTwoGradePrices.Length)
            || equipment.Rarity is not { } rarity || rarity is < 0 or >= 5 || rarity >= state.ChapterTwoRarityWeights.Length)
        {
            return null;
        }

        var basePrice = state.ChapterTwoGradePrices[grade];
        var typeWeight = state.ChapterTwoTypeWeights[typeIndex];
        var rarityWeight = state.ChapterTwoRarityWeights[rarity];
        if (basePrice is < 0 or > int.MaxValue || !double.IsInteger(basePrice)
            || typeWeight is < 0 or > float.MaxValue || !double.IsFinite(typeWeight)
            || rarityWeight is < 0 or > float.MaxValue || !double.IsFinite(rarityWeight))
        {
            return null;
        }

        var typeAdjusted = Math.Truncate(basePrice * (float)typeWeight);
        var total = Math.Truncate(typeAdjusted * (float)rarityWeight);
        return typeAdjusted <= int.MaxValue && total <= int.MaxValue ? (int)total : null;
    }

    private static double[] ReadChapterTwoPrices(IReadOnlyList<string> rows) =>
        rows.Select(row =>
            double.TryParse(SplitValues(row)[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value : double.NaN).ToArray();

    private static int CalculateGold(
        CatalogState state,
        ItemDefinition equipment)
    {
        var minimumLevel = Math.Max(1, equipment.MinimumLevel ?? 1);
        var rarity = Math.Clamp(equipment.Rarity ?? 0, 0, 4);
        var costIndex = checked(minimumLevel + Math.Max(0, rarity - 1) * 2);
        if (costIndex < 0
            || costIndex >= state.CostsByLevel.Count
            || state.CostsByLevel[costIndex] <= 0)
        {
            return 0;
        }

        var rarityWeight = rarity < state.RarityWeights.Count
            ? state.RarityWeights[rarity]
            : 1d;
        if (rarityWeight <= 0)
        {
            rarityWeight = 1d;
        }

        var typeIndex = GetTypeIndex(equipment.ScriptPath);
        var typeWeight = typeIndex < state.TypeWeights.Count
            ? state.TypeWeights[typeIndex]
            : 1d;
        if (typeWeight < 0)
        {
            typeWeight = 0;
        }

        var value = state.CostsByLevel[costIndex] * rarityWeight * typeWeight;
        if (double.IsNaN(value) || value <= 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(value) || value > int.MaxValue)
        {
            throw new InvalidDataException(
                $"{ScriptPath} produced a reinforcement gold cost above Int32.MaxValue.");
        }

        return Math.Max(0, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static int GetTypeIndex(string path)
    {
        var value = (path ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
        if (ContainsAny(
                value,
                "/weapon/",
                "/sword/",
                "/blunt/",
                "/spear/",
                "/claw/",
                "/gun/",
                "/bow/",
                "/staff/",
                "/rod/",
                "/pole/"))
        {
            return 0;
        }

        if (value.Contains("/title/", StringComparison.Ordinal))
        {
            return 1;
        }

        if (ContainsAny(value, "/jacket/", "/coat/"))
        {
            return 2;
        }

        if (value.Contains("/shoulder/", StringComparison.Ordinal))
        {
            return 3;
        }

        if (value.Contains("/pants/", StringComparison.Ordinal))
        {
            return 4;
        }

        if (value.Contains("/shoes/", StringComparison.Ordinal))
        {
            return 5;
        }

        if (value.Contains("/belt/", StringComparison.Ordinal))
        {
            return 6;
        }

        if (ContainsAny(value, "/amulet/", "/necklace/"))
        {
            return 7;
        }

        if (ContainsAny(value, "/wrist/", "/bracelet/"))
        {
            return 8;
        }

        return value.Contains("/ring/", StringComparison.Ordinal) ? 9 : 0;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadSections(
        IEnumerable<string> sourceLines)
    {
        var sections = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);
        string? currentSection = null;
        foreach (var sourceLine in sourceLines)
        {
            var line = StripComment(sourceLine);
            if (line.Length == 0)
            {
                continue;
            }

            if (TryReadTag(line, out var tag, out var remainder))
            {
                if (tag.StartsWith("/", StringComparison.Ordinal))
                {
                    currentSection = null;
                    continue;
                }

                currentSection = SupportedSections.Contains(tag) ? tag : null;
                if (currentSection is null)
                {
                    continue;
                }

                if (!sections.TryGetValue(currentSection, out var rows))
                {
                    rows = [];
                    sections[currentSection] = rows;
                }

                if (remainder.Length != 0)
                {
                    rows.Add(remainder);
                }

                continue;
            }

            if (currentSection is not null)
            {
                sections[currentSection].Add(line);
            }
        }

        return sections.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetSection(
        IReadOnlyDictionary<string, IReadOnlyList<string>> sections,
        string name) =>
        sections.TryGetValue(name, out var rows) ? rows : [];

    private static IReadOnlyList<ReinforcementRow> ReadTableRows(
        IReadOnlyList<string> rows)
    {
        var result = new List<ReinforcementRow>();
        foreach (var row in rows)
        {
            var values = SplitValues(row);
            if (values.Length < 10
                || !TryRoundInt(values[5], out var failureWeight)
                || !TryRoundInt(values[6], out var penaltyType)
                || !TryRoundInt(values[8], out var materialItemId)
                || !TryRoundLong(values[9], out var materialCount))
            {
                continue;
            }

            if (materialItemId is <= 0 or > ushort.MaxValue
                || materialCount is <= 0 or > uint.MaxValue)
            {
                throw new InvalidDataException(
                    $"{ScriptPath} has an invalid material row '{row}'.");
            }

            result.Add(new ReinforcementRow(
                checked((ushort)materialItemId),
                checked((uint)materialCount),
                failureWeight,
                penaltyType));
        }

        return result.AsReadOnly();
    }

    private static IReadOnlyList<int> ReadIntList(IReadOnlyList<string> rows)
    {
        var result = new List<int>();
        foreach (var row in rows)
        {
            var values = SplitValues(row);
            if (values.Length != 0
                && int.TryParse(
                    values[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                result.Add(value);
            }
        }

        return result.AsReadOnly();
    }

    private static IReadOnlyList<double> ReadDoubleList(IReadOnlyList<string> rows)
    {
        var result = new List<double>();
        foreach (var row in rows)
        {
            var values = SplitValues(row);
            if (values.Length != 0
                && double.TryParse(
                    values[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                result.Add(value);
            }
        }

        return result.AsReadOnly();
    }

    private static string[] SplitValues(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool TryRoundInt(string value, out int result)
    {
        result = 0;
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed)
            || parsed < int.MinValue
            || parsed > int.MaxValue)
        {
            return false;
        }

        result = checked((int)Math.Round(parsed, MidpointRounding.AwayFromZero));
        return true;
    }

    private static bool TryRoundLong(string value, out long result)
    {
        result = 0;
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed)
            || parsed < long.MinValue
            || parsed > long.MaxValue)
        {
            return false;
        }

        result = checked((long)Math.Round(parsed, MidpointRounding.AwayFromZero));
        return true;
    }

    private static string StripComment(string line)
    {
        var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
        return (commentIndex >= 0 ? line[..commentIndex] : line).Trim();
    }

    private static bool TryReadTag(
        string line,
        out string tag,
        out string remainder)
    {
        tag = string.Empty;
        remainder = string.Empty;
        var value = line.Trim();
        if (value.Length == 0 || value[0] != '[')
        {
            return false;
        }

        var closing = value.IndexOf(']');
        if (closing <= 1)
        {
            return false;
        }

        tag = value[1..closing].Trim();
        remainder = value[(closing + 1)..].Trim();
        return true;
    }

    private sealed record ReinforcementRow(
        ushort MaterialItemId,
        uint MaterialCount,
        int FailureWeight,
        int PenaltyType);

    private sealed record CatalogState(
        IReadOnlyList<ReinforcementRow> Rows,
        IReadOnlyList<int> CostsByLevel,
        IReadOnlyList<double> RarityWeights,
        IReadOnlyList<double> TypeWeights,
        double[] ChapterTwoGradePrices,
        double[] ChapterTwoRarityWeights,
        double[] ChapterTwoTypeWeights)
    {
        public static CatalogState Empty { get; } = new([], [], [], [], [], [], []);
    }
}
