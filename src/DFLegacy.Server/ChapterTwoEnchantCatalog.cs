using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed record EnchantRecipe(
    ushort RecipeId,
    ushort CardId,
    byte RequiredLevel,
    int CardRarity,
    string[] EquipmentTypes,
    IReadOnlyList<CompoundItemAmount> Ingredients);

internal sealed class ChapterTwoEnchantCatalog(ScriptFileSystem scripts, ItemCatalog items)
{
    private sealed record Rules(
        (int Maximum, int Deduction)[] Decreases,
        (int Ceiling, int Chance)[] Levels,
        (int Maximum, int OnFailure, int OnSuccess)[] Increases,
        Dictionary<(int Job, int Growth), byte> Specialties,
        int SpecialtyChance, int SpecialtyExperience,
        float[] EquipmentRarity, float[] CardRarity, GameSkillEntry ProfessionSkill);

    private readonly ConcurrentDictionary<ushort, Lazy<EnchantRecipe?>> _recipes = new();
    private Rules? _rules;

    public EnchantRecipe? GetRecipe(ushort id) =>
        _recipes.GetOrAdd(id, key => new Lazy<EnchantRecipe?>(() => ReadRecipe(key))).Value;

    public GameSkillEntry? GetProfessionSkill(byte expertJob) =>
        expertJob == 1 ? (_rules ??= ReadRules())?.ProfessionSkill : null;

    public int Level(uint proficiency)
    {
        var rules = _rules ??= ReadRules();
        if (rules is null) return 0;
        var index = Array.FindIndex(rules.Levels, row => proficiency <= row.Ceiling);
        return index < 0 ? rules.Levels.Length : index + 1;
    }

    public ushort Chance(CharacterRecord enchanter, ItemDefinition equipment, EnchantRecipe recipe)
    {
        var rules = _rules ??= ReadRules();
        var rank = Level(enchanter.EnchantingProficiency);
        if (rules is null || rank == 0) return 0;
        var level = equipment.MinimumLevel ?? 0;
        var deduction = rules.Decreases.FirstOrDefault(row => level <= row.Maximum).Deduction;
        if (deduction == 0) deduction = rules.Decreases[^1].Deduction;
        var equipmentMultiplier = GetMultiplier(rules.EquipmentRarity, equipment.Rarity);
        var cardMultiplier = GetMultiplier(rules.CardRarity, recipe.CardRarity);
        var bonus = rules.Specialties.ContainsKey((enchanter.Job, enchanter.GrowType)) ? rules.SpecialtyChance : 0;
        return (ushort)Math.Clamp(rules.Levels[rank - 1].Chance + bonus
            - (int)((double)deduction * cardMultiplier * equipmentMultiplier), 0, 100);
    }

    public uint Increase(CharacterRecord enchanter, ushort chance, bool success)
    {
        var rules = _rules ??= ReadRules();
        if (rules is null) return 0;
        var increase = rules.Increases.First(row => chance <= row.Maximum);
        var bonus = success && rules.Specialties.ContainsKey((enchanter.Job, enchanter.GrowType))
            ? rules.SpecialtyExperience : 0;
        return (uint)((success ? increase.OnSuccess : increase.OnFailure) + bonus);
    }

    private static float GetMultiplier(float[] rows, int? index) =>
        index is int value && value >= 0 && value < rows.Length ? rows[value] : 1;

    private EnchantRecipe? ReadRecipe(ushort id)
    {
        if (!items.TryGetDefinition(id, out var item) || item.TypeTag != "recipe" || !scripts.FileExists(item.ScriptPath))
            return null;
        var text = Read(item.ScriptPath);
        var strings = Block(text, "string data");
        if (!strings.Contains("`[enchant]`", StringComparison.OrdinalIgnoreCase)) return null;
        var data = Integers(Block(text, "int data"));
        if (data.Length < 7 || data[0] is < 1 or > 32) return null;
        var n = data[0];
        if (data.Length != 1 + n * 2 + 4 || data[^4] != 0 || data[^3] != 1
            || data[^2] != 191 || data[^1] is < 1 or > 7)
            return null;

        var ingredients = new List<CompoundItemAmount>(n);
        ItemDefinition? card = null;
        for (var i = 1; i < 1 + n * 2; i += 2)
        {
            if (data[i] is <= 0 or > ushort.MaxValue || data[i + 1] <= 0
                || !items.TryGetDefinition((ushort)data[i], out var ingredient) || ingredient.IsEquipment)
                return null;
            ingredients.Add(new(ingredient.Id, (uint)data[i + 1]));
            if (ingredient.ScriptPath.StartsWith("stackable/monstercard/", StringComparison.OrdinalIgnoreCase))
            {
                if (card is not null) return null;
                card = ingredient;
            }
        }

        if (card is null || !scripts.FileExists(card.ScriptPath)) return null;
        var cardText = Read(card.ScriptPath);
        var types = Regex.Matches(Block(cardText, "string data"), "`\\[([^\\]]+)\\]`")
            .Select(match => match.Groups[1].Value.ToLowerInvariant()).ToArray();
        return types.Length > 0 && !string.IsNullOrWhiteSpace(Block(cardText, "enchant"))
            ? new(id, card.Id, (byte)data[^1], card.Rarity ?? 0, types, ingredients)
            : null;
    }

    private Rules? ReadRules()
    {
        const string path = "character/expertjob.etc";
        if (!scripts.FileExists(path)) return null;
        var text = Block(Read(path), "magic enchant");
        var decreases = Integers(Block(text, "rate of success decrease")).Chunk(3).ToArray();
        var levels = Integers(Block(text, "expertness exp")).Chunk(2).ToArray();
        var increases = Integers(Block(text, "expertness increase")).Chunk(4).ToArray();
        var equipment = Multipliers(Block(text, "equipment rarity"));
        var cards = Multipliers(Block(text, "monster card rarity"));
        var skill = Integers(Block(text, "skill"));
        var bonus = Regex.Match(text, @"\[speciality bonus\][ \t]+(\d+)[ \t]+(\d+)");
        var specialties = new Dictionary<(int, int), byte>();
        var jobs = new[] { "swordman", "fighter", "gunner", "mage", "priest" };
        foreach (Match row in Regex.Matches(Block(text, "speciality class"), "`([a-z]+)`[ \\t]+(\\d+)"))
        {
            var job = Array.IndexOf(jobs, row.Groups[1].Value);
            if (job >= 0) specialties[(job, int.Parse(row.Groups[2].Value, CultureInfo.InvariantCulture))] = 1;
        }

        if (decreases.Length == 0 || decreases.Any(row => row.Length != 3)
            || levels.Length != 7 || levels.Any(row => row.Length != 2)
            || increases.Length == 0 || increases.Any(row => row.Length != 4) || increases[^1][1] != 100
            || equipment.Length != 5 || cards.Length != 5 || !bonus.Success
            || skill is not [> 0 and <= byte.MaxValue, > 0 and <= byte.MaxValue])
            return null;
        return new(decreases.Select(row => (row[1], row[2])).ToArray(),
            levels.Select(row => (row[0], row[1])).ToArray(),
            increases.Select(row => (row[1], row[2], row[3])).ToArray(), specialties,
            int.Parse(bonus.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(bonus.Groups[2].Value, CultureInfo.InvariantCulture), equipment, cards,
            new((byte)skill[0], (byte)skill[1]));
    }

    private string Read(string path) => Regex.Replace(scripts.ReadAllText(path, Encoding.Latin1),
        @"(?m)//[^\r\n]*", string.Empty);

    private static string Block(string text, string tag)
    {
        var name = Regex.Escape(tag);
        // Ver_OB_216 also accepts the repeated opening tag after expertness exp.
        return Regex.Match(text, @"\[" + name + @"\](.*?)\[/?"
            + name + @"\]", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;
    }

    private static int[] Integers(string value) => Regex.Matches(value, @"-?\d+")
        .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();

    private static float[] Multipliers(string value) => Regex.Matches(value, @"\d+(?:\.\d+)?")
        .Select(match => float.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
}
