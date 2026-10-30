using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class AvatarPurchaseSmokeTests
{
    internal static Task RunAsync(Action<bool, string> check)
    {
        using var f = new AvatarGiftFixture();
        foreach (var row in new (ushort Item, int Options, uint Commodity, byte Last)[]
            { (51020, 5, 270000, 4), (52641, 44, 290000, 43), (42201, 2, 160000, 1) })
        {
            check(f.Shop.TryGetAvatarAbilityOptionCount(row.Item, out var count) && count == row.Options,
                $"avatar {row.Item} counts original PVF option rows ({row.Options}), not numeric skill tokens");
            check(f.Shop.TryResolveAvatarPurchase(new(row.Last, 1, row.Commodity), out var product, out var definition)
                && product.ItemId == row.Item && definition.InventoryCategory == ItemInventoryCategory.Avatar,
                $"avatar {row.Item} accepts last zero-based option {row.Last}, including ordinary skin");
            foreach (var ability in new byte[] { (byte)row.Options, (byte)(row.Options + 1), 0xFF })
                check(!f.Shop.TryResolveAvatarPurchase(new(ability, 1, row.Commodity), out _, out _),
                    $"avatar {row.Item} rejects out-of-range or unselected ability {ability}");
            foreach (ushort term in new ushort[] { 0, 2, 4 })
                check(!f.Shop.TryResolveAvatarPurchase(new(row.Last, term, row.Commodity), out _, out _),
                    $"avatar {row.Item} rejects mismatched/invalid period {term}");
        }
        foreach (var row in new (uint Commodity, int Price)[]
            { (270000, 300), (270001, 660), (270002, 1980), (290000, 450), (290001, 990), (290002, 2970),
              (310000, 107), (310001, 235), (310002, 705) })
            check(f.Shop.TryGetProduct(row.Commodity, out var product) && product.CashPrice == row.Price,
                $"avatar commodity {row.Commodity} uses ACT1 price {row.Price}");
        check(!f.Shop.TryResolveAvatarPurchase(new(0, 1, 999999), out _, out _)
            && !f.Shop.TryResolveAvatarPurchase(new(0, 0, 100018), out _, out _)
            && !f.Shop.TryGetAvatarAbilityOptionCount(15, out _),
            "ordinary-avatar resolver rejects unknown commodities and physical definitions");
        check(f.Shop.TryGetProduct(160000, out var skin) && skin.Sticker == 1
            && f.Shop.TryGetProduct(270011, out var stickerWithDescription) && stickerWithDescription.Sticker == 99
            && f.Shop.TryGetProduct(270012, out var stickerWithoutDescription) && stickerWithoutDescription.Sticker == 99,
            "avatar catalog preserves skin sticker1 and sticker99 with or without description");
        check(f.Shop.TryGetProduct(270010, out var forbidden) && forbidden.GiftDisallowed,
            "avatar gift disallowance is retained as product policy");
        check(f.Shop.TryResolveAvatarPurchase(new(4, 1, 270010), out _, out _),
            "shared avatar validation leaves gift-only disallowance enforcement to the gift transaction");
        foreach (uint commodity in new uint[] { 320000, 320001, 320002 })
            check(!f.Shop.TryResolveAvatarPurchase(new(0, 1, commodity), out _, out _),
                $"avatar commodity {commodity} with missing, empty or unclosed options has no default ability");
        check(f.Shop.TryGetAvatarAbilityOptionCount(51024, out var commentCount) && commentCount == 1,
            "option reader removes comments before matching section boundaries and ignores numeric continuation lines");
        Wire(check);
        return Task.CompletedTask;
    }

    private static void Wire(Action<bool, string> check)
    {
        byte[] gift = [0x12, 0, 1, 4, 0, 0, 0, 0xCA, 0xD5, 0xC0, 0xF1, 1, 0x04, 0x01, 0x00, 0xB0, 0x1E, 0x04, 0x00];
        byte[] self = [0x12, 0, 0, 1, 0x2B, 0x03, 0x00, 0xD2, 0x6C, 0x04, 0x00];
        check(GameProtocolEngine.TryReadCeraShopPurchase(gift, out var g) && g.IsGift && g.RecipientName == "收礼"
            && g.Products.Single().AvatarAbilityIndex == 4 && g.Products.Single().AttributeValue == 1
            && g.Products.Single().CommodityNo == 270000,
            "literal CMD67 gift1 seven-byte avatar row preserves zero-based ability4 and seven-day commodity");
        check(GameProtocolEngine.TryReadCeraShopPurchase(self, out var s) && !s.IsGift
            && s.Products.Single().AvatarAbilityIndex == 43 && s.Products.Single().AttributeValue == 3
            && s.Products.Single().CommodityNo == 290002,
            "literal CMD67 gift0 row preserves ability43 and permanent commodity");
        check(!GameProtocolEngine.TryReadCeraShopPurchase([.. gift, 0], out _)
            && !GameProtocolEngine.TryReadCeraShopPurchase(gift[..^1], out _)
            && !GameProtocolEngine.TryReadCeraShopPurchase([0, 0, 0, 0], out _)
            && !GameProtocolEngine.TryReadCeraShopPurchase([0, 0, 0, 33, .. Enumerable.Repeat(self[4..], 33).SelectMany(x => x)], out _),
            "CMD67 rejects trailing/truncated avatar rows and counts0/33");
        check(GameProtocolEngine.TryReadCeraShopPurchase([0, 0, 0, 32, .. Enumerable.Repeat(self[4..], 32).SelectMany(x => x)], out var maximum)
            && maximum.Products.Length == 32 && maximum.Products.All(item => item.AvatarAbilityIndex == 43),
            "CMD67 accepts all32 exact seven-byte avatar rows at the parser limit");
        byte[] physical = [0, 0, 0, 1, 0xFF, 0, 0, 0xB2, 0x86, 1, 0];
        check(GameProtocolEngine.TryReadCeraShopPurchase(physical, out var p) && p.Products.Single().ProductType == 0xFF,
            "CMD67 continues to parse physical FF rather than globally comparing the byte to category");
    }
}

internal sealed class AvatarGiftFixture : IDisposable
{
    internal readonly string Root = Path.Combine(Path.GetTempPath(), "dflegacy-avatar-gift-" + Guid.NewGuid().ToString("N"));
    internal readonly ServerOptions Options;
    internal readonly ItemCatalog Items;
    internal readonly CeraShopCatalog Shop;

    internal AvatarGiftFixture(int? max = null)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests", "DFLegacy.SmokeTests", "Fixtures", "AvatarGift")))
            directory = directory.Parent;
        var source = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("AvatarGift fixture source"),
            "tests", "DFLegacy.SmokeTests", "Fixtures", "AvatarGift");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var equipmentList = Path.Combine(Root, "equipment/equipment.lst");
        for (var i = 0; i < 5; i++) File.AppendAllText(equipmentList, $"{51021 + i} `synthetic{i}.equ`\n");
        var prefix = "[name] `Synthetic boundary fixture`\n[equipment type] `[hat avatar]`\n[cash] 107\n";
        File.WriteAllText(Path.Combine(Root, "equipment/synthetic0.equ"), prefix + "[avatar select ability]\n`[HP MAX]` 100\n[/avatar select ability]\n");
        File.WriteAllText(Path.Combine(Root, "equipment/synthetic1.equ"), prefix);
        File.WriteAllText(Path.Combine(Root, "equipment/synthetic2.equ"), prefix + "[avatar select ability]\n[/avatar select ability]\n");
        File.WriteAllText(Path.Combine(Root, "equipment/synthetic3.equ"), prefix + "// [avatar select ability]\n// `[FAKE]` 99\n// [/avatar select ability]\n[avatar select ability]\n`[SKILL]` 3 69\n1\n// `[FAKE]` 10\n[/avatar select ability]\n");
        File.WriteAllText(Path.Combine(Root, "equipment/synthetic4.equ"), prefix + "[avatar select ability]\n`[HP MAX]` 100\n// [/avatar select ability]\n");
        Directory.CreateDirectory(Path.Combine(Root, "stackable"));
        File.WriteAllText(Path.Combine(Root, "stackable/stackable.lst"), "15 `physical.stk`\n");
        File.WriteAllText(Path.Combine(Root, "stackable/physical.stk"), "[name] `Physical`\n[stackable type] `[equipment variation]`\n[attach type] `[free]`\n[stack limit] 1000\n[weight] 0\n");
        File.AppendAllText(Path.Combine(Root, "etc/newcashshop.etc"),
            "[avatar]\n270010 51020 1 0 0 -1\n270011 51020 1 `Sticker` 99 0 -1\n270012 51020 1 99 0 -1\n"
            + "310000 51021 1 0 0 -1\n310001 51021 2 0 0 -1\n310002 51021 3 0 0 -1\n"
            + "320000 51022 1 0 0 -1\n320001 51023 1 0 0 -1\n320002 51025 1 0 0 -1\n[/avatar]\n"
            + "[gift disallowance]\n270010\n[/gift disallowance]\n[item]\n100018 15 1 0 0 150 `Physical` 0 0\n[/item]\n"
            + (max is null ? "" : $"[max gift count]\n{max}\n[/max gift count]\n"));
        Options = new() { DataPath = Path.Combine(Root, "state.json"), SkillScriptPath = Root, ScriptPvfPath = "" };
        var scripts = new ScriptFileSystem(Options, NullLogger<ScriptFileSystem>.Instance);
        Items = new(scripts, NullLogger<ItemCatalog>.Instance);
        Shop = new(scripts, Items, NullLogger<CeraShopCatalog>.Instance);
    }

    public void Dispose() => Directory.Delete(Root, true);
}
