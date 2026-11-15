using System.Text;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoItemNamesSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(936);
        var root = Directory.CreateTempSubdirectory("dflegacy-item-names-");
        try
        {
            var equipment = Directory.CreateDirectory(Path.Combine(root.FullName, "equipment"));
            var stackable = Directory.CreateDirectory(Path.Combine(root.FullName, "stackable"));
            File.WriteAllText(Path.Combine(root.FullName, "string.lst"),
                "//3 `wrong.str`\n3 `equipment/equipment.chn.str`\n13 `stackable/stackable.chn.str`\n"
                + "99 `equipment/equipment.chn.str`\n99 `stackable/stackable.chn.str`\n", encoding);
            File.WriteAllText(Path.Combine(equipment.FullName, "equipment.chn.str"),
                "name_sword>炙炎梵天剑\n//comment>不应读取注释\nempty> \n"
                + "duplicate>前值\nduplicate>后值\nwrong>不应读取脚本注释\n", encoding);
            File.WriteAllText(Path.Combine(stackable.FullName, "stackable.chn.str"),
                "name_coin>命运硬币\n", encoding);
            (string Name, string Expected)[] cases =
            [
                ("[name] `字面 // 保留`", "字面 // 保留"),
                ("[name]\n<3::name_sword> // 名称引用", "炙炎梵天剑"),
                ("[name]\n<88::name_sword>", ""),
                ("[name]\n<3::missing>", ""),
                ("[name]\n<3::empty>", ""),
                ("[name]\n<3::comment>", ""),
                ("// [name] `注释`\n/*\n[name]\n<3::wrong>\n*/\n[name]\n<3::name_sword>", "炙炎梵天剑"),
                ("[name]\n<3::duplicate>", ""),
                ("[name]\n<99::name_sword>", ""),
                ("[description]\n<3::name_sword>", ""),
                ("[name]\n<3::name_sword> garbage", "")
            ];
            File.WriteAllLines(Path.Combine(equipment.FullName, "equipment.lst"),
                cases.Select((_, index) => $"{10000 + index} `{index}.equ`"), encoding);
            for (var index = 0; index < cases.Length; index++)
                File.WriteAllText(Path.Combine(equipment.FullName, $"{index}.equ"),
                    cases[index].Name + "\n[equipment type] `[weapon]`\n", encoding);
            File.WriteAllText(Path.Combine(stackable.FullName, "stackable.lst"), "1047 `coin.stk`\n", encoding);
            File.WriteAllText(Path.Combine(stackable.FullName, "coin.stk"),
                "[name]\n<13::name_coin>\n[stackable type] `[usable]`\n[stack limit] 1000\n", encoding);
            var catalog = Create(new ServerOptions { ScriptPvfPath = "", SkillScriptPath = root.FullName });
            for (var index = 0; index < cases.Length; index++)
                check(catalog.TryGetDefinition((ushort)(10000 + index), out var item) && item.Name == cases[index].Expected,
                    $"Item name fixture {index}: literals, references, missing/empty/duplicate names and comments resolve without false names");
            check(catalog.TryGetDefinition(1047, out var coin) && coin.Name == "命运硬币",
                "Chapter Two stackable names use their own category 13 Chinese table");
            check(catalog.Definitions.Values.Where(AdminMailComposer.IsSupportedAttachment)
                    .Where(item => item.Name.Contains("炙炎", StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Id).Order().SequenceEqual(new ushort[] { 10001, 10006 }),
                "The GM name-search input catalog finds equipment by resolved Chinese name");
        }
        finally { root.Delete(recursive: true); }
    }

    public static void RunActual(string pvfPath, Action<bool, string> check)
    {
        var catalog = Create(new ServerOptions
        {
            ScriptPvfPath = pvfPath,
            SkipUnsupportedChapterTwoCashContainers = true
        });
        foreach (var (id, expected) in new (ushort, string)[]
                 { (1047, "命运硬币"), (10400, "残破的牛皮背心"), (27093, "炙炎梵天剑") })
            check(catalog.TryGetDefinition(id, out var item) && item.Name == expected,
                $"Actual Chapter Two item {id} resolves its own Chinese name {expected}");
        check(catalog.Definitions.Values.Where(AdminMailComposer.IsSupportedAttachment)
                .Any(item => item.Id == 27093 && item.Name.Contains("炙炎梵天剑", StringComparison.OrdinalIgnoreCase)),
            "The actual Chapter Two GM attachment catalog supports Chinese equipment-name searches");
        Console.WriteLine($"Actual item names: {catalog.Count} definitions, "
            + $"{catalog.Definitions.Values.Count(item => !string.IsNullOrWhiteSpace(item.Name))} named.");
    }

    private static ItemCatalog Create(ServerOptions options) => new(
        new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance),
        NullLogger<ItemCatalog>.Instance, options);
}
