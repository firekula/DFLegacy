using System.Text;
using System.Text.RegularExpressions;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>ACT1 town data and gold-limit rows; missing data never grants a store permission.</summary>
public sealed class PrivateStoreCatalog(ScriptFileSystem scripts)
{
    private sealed record Rectangle(int X, int Y, int Width, int Height)
    { public bool Contains(int x, int y) => x >= X && y >= Y && x <= (long)X + Width && y <= (long)Y + Height; }
    private sealed record Area(bool Enabled, Rectangle[] Walkable, (int X, int Y)[] Npcs);
    private sealed record State(Dictionary<(byte, byte), Area> Areas, Dictionary<int, int> Gold);
    private readonly Lazy<State> _state = new(() => Load(scripts));
    public bool TryGetGoldLimit(int level, out int limit) => _state.Value.Gold.TryGetValue(Math.Clamp(level, 1, 99), out limit);
    public byte CheckPosition(PrivateStorePosition p)
    {
        if (!_state.Value.Areas.TryGetValue((p.Town, p.Area), out var area) || !area.Enabled
            || !area.Walkable.Any(r => r.Contains(p.X, p.Y))) return 62;
        return area.Npcs.Any(n => Math.Abs((long)p.X - n.X) < 80 && Math.Abs((long)p.Y - n.Y) < 150) ? (byte)82 : (byte)0;
    }
    public static bool Collides(PrivateStorePosition a, PrivateStorePosition b) => a.Town == b.Town && a.Area == b.Area
        && Math.Abs((int)a.X - b.X) <= 160 && Math.Abs((int)a.Y - b.Y) <= 100;
    private static State Load(ScriptFileSystem scripts)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string Read(string path) => scripts.FileExists(path)
            ? Regex.Replace(scripts.ReadAllText(path, Encoding.GetEncoding(949)), @"//[^\r\n]*", "") : "";
        static int[] Numbers(string text)
        {
            var values = new List<int>();
            foreach (Match m in Regex.Matches(text, @"-?\d+"))
            { if (!int.TryParse(m.Value, out var n)) return []; values.Add(n); }
            return values.ToArray();
        }
        static string Block(string text, string name) => Regex.Match(text,
            $@"\[{Regex.Escape(name)}\](.*?)\[/{Regex.Escape(name)}\]", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value;
        var areas = new Dictionary<(byte, byte), Area>();
        foreach (Match row in Regex.Matches(Read("town/town.lst"), @"(?m)^\s*(\d+)\s+`([^`]+)`"))
        {
            if (!byte.TryParse(row.Groups[1].Value, out var town)) continue;
            foreach (Match block in Regex.Matches(Read("town/" + row.Groups[2].Value), @"\[area\](.*?)\[/area\]", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var text = block.Groups[1].Value;
                var head = Regex.Match(text, @"^\s*(\d+)\s+`([^`]+)`");
                if (!head.Success || !byte.TryParse(head.Groups[1].Value, out var area)) continue;
                var map = Read("map/" + head.Groups[2].Value.Replace('\\', '/'));
                var enabled = !Regex.IsMatch(text, @"`?\[(?:dungeon gate|gate)\]`?", RegexOptions.IgnoreCase)
                    && !Regex.IsMatch(text, @"\[use private store\]`?\s*0\b", RegexOptions.IgnoreCase);
                var rectangles = Block(map, "virtual movable area").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(Numbers).Where(n => n.Length >= 4 && n[2] > 0 && n[3] > 0)
                    .Select(n => new Rectangle(n[0], n[1], n[2], n[3])).ToArray();
                var npcs = Block(map, "NPC").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(Numbers).Where(n => n.Length >= 4).Select(n => (n[1], n[2])).ToArray();
                areas[(town, area)] = new(enabled, rectangles, npcs);
            }
        }
        var gold = new Dictionary<int, int>();
        var source = Read("etc/goldlimitbylevel.etc");
        var tag = Regex.Match(source, @"\[gold limit from level\]([^\[]*)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (var row in tag.Groups[1].Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var n = Numbers(row);
            if (n.Length != 2 || n[0] is < 0 or > 99 || n[1] < 0 || !gold.TryAdd(n[0], n[1]))
            { gold.Clear(); break; }
        }
        return new(areas, gold);
    }
}
