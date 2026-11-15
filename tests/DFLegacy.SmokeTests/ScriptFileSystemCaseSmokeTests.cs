using System.Text;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ScriptFileSystemCaseSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-script-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scripts = Path.Combine(root, "scripts");
            var skillDirectory = Path.Combine(scripts, "Skill", "Swordman");
            Directory.CreateDirectory(skillDirectory);
            var active = Path.Combine(skillDirectory, "Active.skl");
            File.WriteAllText(active, "mixed-case-script", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(skillDirectory, "Other.SKL"), "other");
            var mageDirectory = Path.Combine(scripts, "Skill", "Swordmage");
            Directory.CreateDirectory(mageDirectory);
            File.WriteAllText(Path.Combine(mageDirectory, "Spell.skl"), "spell");
            File.WriteAllText(Path.Combine(root, "outside.skl"), "outside-root");

            var files = Open(scripts);
            check(files.FileExists("skill/swordman/active.skl")
                    && files.FileExists("SKILL\\SWORDMAN\\ACTIVE.SKL"),
                "unpacked scripts resolve real mixed-case directories and files case-insensitively");
            check(ReadOrMissing(() => files.ReadAllText("sKiLl/SWORDMAN/AcTiVe.skl", Encoding.UTF8)) == "mixed-case-script",
                "unpacked mixed-case reads return the actual file bytes");
            check(files.EnumeratePaths("SKILL/SWORD").SequenceEqual(new[]
                { "skill/swordmage/spell.skl", "skill/swordman/active.skl", "skill/swordman/other.skl" }),
                "unpacked enumeration accepts a virtual prefix inside a directory name like PVF");
            check(files.EnumeratePaths("Skill/Swordman/Ac").SequenceEqual(new[] { "skill/swordman/active.skl" }),
                "unpacked enumeration accepts a virtual prefix inside a file name like PVF");
            check(files.EnumeratePaths("SKILL/SWORDMAN/").SequenceEqual(new[]
                { "skill/swordman/active.skl", "skill/swordman/other.skl" }),
                "unpacked directory prefix enumeration is sorted and normalized");
            File.WriteAllText(active, "edited-script", new UTF8Encoding(false));
            check(ReadOrMissing(() => files.ReadAllTextUncached("SKILL/SWORDMAN/ACTIVE.SKL", Encoding.UTF8)) == "edited-script",
                "uncached reads still observe content edits through the indexed real path");
            foreach (var traversal in new[] { "../outside.skl", "..\\outside.skl", "skill/../../outside.skl" })
                check(!files.FileExists(traversal)
                        && ReadOrMissing(() => files.ReadAllText(traversal)) == "(missing)",
                    "unpacked lookup rejects root escape: " + traversal);

            CheckAmbiguity(root, check);
            if (!OperatingSystem.IsWindows()) CheckSymlinks(root, check);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void CheckAmbiguity(string root, Action<bool, string> check)
    {
        var ambiguous = Path.Combine(root, "ambiguous");
        Directory.CreateDirectory(ambiguous);
        File.WriteAllText(Path.Combine(ambiguous, "Twin.skl"), "one");
        File.WriteAllText(Path.Combine(ambiguous, "TWIN.SKL"), "two");
        // A case-insensitive host filesystem cannot represent this ambiguous fixture.
        if (Directory.GetFiles(ambiguous).Length != 2) return;
        var rejected = false;
        try { _ = Open(ambiguous).FileExists("twin.skl"); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "unpacked scripts reject two real files with the same case-insensitive virtual path");
    }

    private static void CheckSymlinks(string root, Action<bool, string> check)
    {
        var linked = Path.Combine(root, "links");
        var external = Path.Combine(root, "external-directory");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "external.skl"), "external");
        Directory.CreateDirectory(linked);
        Directory.CreateSymbolicLink(Path.Combine(linked, "outside-directory"), external);
        File.CreateSymbolicLink(Path.Combine(linked, "outside-file.skl"), Path.Combine(root, "outside.skl"));
        var files = Open(linked);
        check(!files.FileExists("outside-file.skl") && !files.EnumeratePaths().Any(),
            "unpacked index cannot escape through file or directory symbolic links");
    }

    private static ScriptFileSystem Open(string directory) => new(
        new ServerOptions { ScriptPvfPath = "", SkillScriptPath = directory },
        NullLogger<ScriptFileSystem>.Instance);

    private static string ReadOrMissing(Func<string> read)
    {
        try { return read(); }
        catch (FileNotFoundException) { return "(missing)"; }
    }
}
