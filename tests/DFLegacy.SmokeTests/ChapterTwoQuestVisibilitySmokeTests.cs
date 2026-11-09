using System.Text;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoQuestVisibilitySmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(936);
        var root = Path.Combine(Path.GetTempPath(), $"dflegacy-quest-names-{Guid.NewGuid():N}");
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "quest")).FullName;
            var names = new[]
            {
                "<11::disabled>", "<11::missing>", "`不使用`", "<11::named>",
                "<12::another_table>", "`字面标题`", "<11::empty>"
            };
            File.WriteAllLines(Path.Combine(directory, "quest.lst"),
                names.Select((_, index) => $"{index + 1} `{index + 1}.qst`"), encoding);
            for (var index = 0; index < names.Length; index++)
                File.WriteAllText(Path.Combine(directory, $"{index + 1}.qst"),
                    $"[npc index] 2\n[name]\n{names[index]} // quest title\n[condition message]\n<11::disabled>\n", encoding);
            File.WriteAllText(Path.Combine(directory, "quest.chn.str"),
                "//missing>不应读取注释\r\ndisabled> 不使用 \r\nnamed>不使用复活币通关\r\nempty> \r\n", encoding);
            var catalog = Create(new ServerOptions { ScriptPvfPath = "", SkillScriptPath = root });
            var player = new CharacterRecord(Guid.NewGuid(), "Quest", 0, 0, 5, 0, 0);
            check(catalog.GetAcceptableQuestIds(player, [], [], chapterTwo: true).SequenceEqual(new ushort[] { 4, 5, 6 }),
                "Chapter Two hides disabled, empty and unresolved names and keeps valid names and unknown reference types");
            check(catalog.GetAcceptableQuestIds(player, [], []).SequenceEqual(new ushort[] { 1, 2, 3, 4, 5, 6, 7 }),
                "Chapter One quest availability remains unchanged with the same fixture");
            var active = new[] { new CharacterQuestRecord(1, Trigger: 7), new CharacterQuestRecord(4, Trigger: 5) };
            check(catalog.GetAcceptableQuestIds(player, active, [], chapterTwo: true).SequenceEqual(new ushort[] { 5, 6 })
                && active[0].Trigger == 7 && catalog.TryGetDefinition(1, out _),
                "Chapter Two keeps existing quest definitions and accepted progress while refreshing availability");
            File.Delete(Path.Combine(directory, "quest.chn.str"));
            var withoutLocale = Create(new ServerOptions { ScriptPvfPath = "", SkillScriptPath = root });
            check(withoutLocale.GetAcceptableQuestIds(player, [], [], chapterTwo: true).Contains((ushort)2),
                "A fixture without a Chinese table does not treat all references as missing");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static void RunActual(string pvfPath, Action<bool, string> check)
    {
        var catalog = Create(new ServerOptions { ScriptPvfPath = pvfPath });
        var player = new CharacterRecord(Guid.NewGuid(), "Seria", 0, 0, 5, 0, 0);
        ushort[] rejected = [1013, 4963, 4970];
        var chapterOne = catalog.GetAcceptableQuestIds(player, [], [], preferredNpcIndex: 2);
        var chapterTwo = catalog.GetAcceptableQuestIds(player, [], [], preferredNpcIndex: 2, chapterTwo: true);
        check(rejected.All(chapterOne.Contains) && rejected.All(id => !chapterTwo.Contains(id)),
            "The original PVF reproduces and filters the three reported Seria quest rows");
        check(new ushort[] { 101, 2441 }.All(chapterTwo.Contains) && rejected.All(id => catalog.TryGetDefinition(id, out _)),
            "The original PVF retains named Seria quests and keeps historical definitions readable");
        Console.WriteLine($"Actual Chapter Two Seria-prioritized availability: {chapterOne.Count} baseline, {chapterTwo.Count} filtered.");
    }

    private static QuestCatalog Create(ServerOptions options) => new(
        new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance), NullLogger<QuestCatalog>.Instance);
}
