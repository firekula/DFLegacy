using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

// Actual PVF entries are copied byte-for-byte; optional mutations are isolated boundary cases.
internal sealed class CreatureGiftFixture : IDisposable
{
    private readonly AvatarGiftFixture avatar;
    internal string Root => avatar.Root;
    internal ServerOptions Options => avatar.Options;
    internal readonly ItemCatalog Items;
    internal readonly CeraShopCatalog Shop;
    internal static string Source
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests/DFLegacy.SmokeTests/Fixtures/CreatureGift"))) dir = dir.Parent;
            return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("CreatureGift fixtures"), "tests/DFLegacy.SmokeTests/Fixtures/CreatureGift");
        }
    }
    internal CreatureGiftFixture(int? max = null, Action<string>? mutate = null)
    {
        avatar = new(max);
        foreach (var file in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith("manifest.json")) continue;
            var target = Path.Combine(Root, Path.GetRelativePath(Source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (file.EndsWith(".lst") || file.EndsWith(".etc")) { using var stream = new FileStream(target, FileMode.Append); stream.Write(File.ReadAllBytes(file)); }
            else File.Copy(file, target, true);
        }
        // Explicit ASCII boundary definitions for the excluded food/rename/effect/container paths.
        // The15 input eggs and outputs above remain original PVF bytes.
        File.AppendAllText(Path.Combine(Root, "stackable/stackable.lst"),
            "24 `feed.stk`\n25 `rename_card.stk`\n190 `cash/gold_voucher.stk`\n195 `package.stk`\n196 `booster.stk`\n197 `lottery.stk`\n");
        Directory.CreateDirectory(Path.Combine(Root, "stackable/cash"));
        foreach (var (path, type) in new[] { ("feed.stk", "feed"), ("rename_card.stk", "creature"),
            ("cash/gold_voucher.stk", "equipment variation"), ("package.stk", "cera package"),
            ("booster.stk", "cera booster"), ("lottery.stk", "legacy") })
            File.WriteAllText(Path.Combine(Root, "stackable", path),
                $"[name] `Synthetic excluded boundary`\n[stackable type] `[{type}]`\n[attach type] `[free]`\n[stack limit] 1000\n");
        File.AppendAllText(Path.Combine(Root, "stackable/lottery.stk"), "[int data]\n15 15 100\n[/int data]\n");
        File.AppendAllText(Path.Combine(Root, "stackable/package.stk"), "[package data]\n15 1\n[/package data]\n");
        File.AppendAllText(Path.Combine(Root, "stackable/booster.stk"), "[booster info]\n[etc]\n1\n15 100 1\n[/etc]\n[/booster info]\n");
        File.AppendAllText(Path.Combine(Root, "etc/newcashshop.etc"),
            "[creature]\n305190 190 1 0 0 1 `Effect` 0 0\n305195 195 1 0 0 1 `Package` 0 0\n305196 196 1 0 0 1 `Booster` 0 0\n305197 197 1 0 0 1 `Lottery` 0 0\n[/creature]\n");
        mutate?.Invoke(Root);
        var scripts = new ScriptFileSystem(Options, NullLogger<ScriptFileSystem>.Instance);
        Items = new(scripts, NullLogger<ItemCatalog>.Instance);
        Shop = new(scripts, Items, NullLogger<CeraShopCatalog>.Instance);
    }
    internal static bool VerifyProvenance()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Source, "manifest.json")))!;
        return manifest["pvfSha256"]!.GetValue<string>() == "288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f"
            && manifest["entries"]!.AsArray().Count == 30
            && manifest["derived"]!.AsArray().All(e => {
                // These scope-derived text rows may be checked out with CRLF.
                // Only the original equipment entries require byte identity.
                var bytes = File.ReadAllBytes(Path.Combine(Source, e!["path"]!.GetValue<string>()));
                var canonical = bytes.Where((b, i) => b != 13 || i + 1 >= bytes.Length || bytes[i + 1] != 10).ToArray();
                return Convert.ToHexStringLower(SHA256.HashData(canonical)) == e["fixtureSha256"]!.GetValue<string>();
            })
            && manifest["entries"]!.AsArray().All(e => {
                var b = File.ReadAllBytes(Path.Combine(Source, e!["path"]!.GetValue<string>()));
                return b.Length == e["length"]!.GetValue<int>()
                    && Convert.ToHexStringLower(SHA256.HashData(b)) == e["entrySha256"]!.GetValue<string>()
                    && e["fixtureSha256"]!.GetValue<string>() == e["entrySha256"]!.GetValue<string>();
            });
    }
    public void Dispose() => avatar.Dispose();
}
