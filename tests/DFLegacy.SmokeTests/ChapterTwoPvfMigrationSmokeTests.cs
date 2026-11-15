using System.Text.RegularExpressions;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoPvfMigrationSmokeTests
{
    internal static void Run(string pvfPath, Action<bool, string> check)
    {
        var options = new ServerOptions
        {
            ScriptPvfPath = pvfPath,
            SkipUnsupportedChapterTwoCashContainers = true
        };
        var scripts = new ScriptFileSystem(options,
            NullLogger<ScriptFileSystem>.Instance);
        check(scripts.IsPvf && scripts.FileCount == 69399,
            "the migrated Chapter Two server archive indexes all original paths and the offer exclusion list");
        var skills = new SkillCatalog(scripts, NullLogger<SkillCatalog>.Instance);
        var expectedSkills = new (int Job, byte Id, int[] Caps)[]
        {
            (3, 57, [0, 20, 10, 10, 0]),
            (3, 21, [0, 10, 20, 10, 0]),
            (3, 22, [0, 10, 20, 10, 0]),
            (3, 23, [0, 10, 20, 10, 0]),
            (3, 24, [0, 10, 20, 10, 0]),
            (3, 65, [0, 10, 10, 20, 0]),
            (4, 47, [0, 5, 5, 10, 0]),
            (4, 48, [0, 5, 5, 10, 0]),
            (4, 30, [0, 5, 10, 5, 0]),
            (4, 41, [0, 10, 0, 20, 0])
        };
        foreach (var (job, id, caps) in expectedSkills)
        {
            check(skills.TryGetDefinition(job, id, out var skill)
                && skill.GrowTypeMaximumLevels.SequenceEqual(caps)
                && skill.StealSpCostPercent == 200 && skill.StealRequiredLevelOffset == 4,
                $"Chapter Two skill {job}/{id} loads its own growth caps and the migrated learning policy");
        }

        check(skills.TryGetDefinition(3, 21, out var summon)
            && summon.RequiredLevel == 18 && summon.PurchaseCostFor(1, 2) == 20
            && summon.RequiredCharacterLevelFor(1, 1) == 22 && summon.PurchaseCostFor(1, 1) == 40,
            "Chapter Two summon learning retains its own base level and cost before applying the stolen-skill policy");
        var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance, options);
        foreach (var (id, price) in new (ushort, int)[] { (3037, 100), (3213, 10), (201, 180) })
        {
            check(items.TryGetPurchasePrice(id, out var actual) && actual == price,
                $"Chapter Two material {id} uses its own purchase price {price}");
        }

        var bosses = new[]
        {
            "dollmaster/masterdogri", "draconian/lakius", "evileye/evileye",
            "flydragon/selion", "golem/platany", "lordoflight/lordoflight", "soldier/heavenexpeller"
        };
        foreach (var boss in bosses)
        {
            var entries = Regex.Matches(scripts.ReadAllText($"monster/{boss}.mob"),
                @"(?m)^\s*3213\s+(\d+)\s*$");
            check(entries.Count == 1 && entries[0].Groups[1].Value == "5000",
                $"Chapter Two boss {boss} contains exactly one invitation drop at the migrated probability");
        }
    }
}
