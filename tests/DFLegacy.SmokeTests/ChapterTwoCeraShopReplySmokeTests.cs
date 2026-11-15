using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class ChapterTwoCeraShopReplySmokeTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var (section, category) in new[] { ("avatar", 0), ("coin", 1), ("item", 2), ("creature", 3), ("package", 2) })
        {
            var product = new CeraShopProduct(109049, 49, 1, 0, 13550, section);
            check(product.GetPurchaseResultCategory(false) == category
                && product.GetPurchaseResultCategory(true) == (section == "package" ? 4 : category),
                $"{section} catalog result mapping preserves C1 and changes only C2 packages");
        }
        // Original C2 9410F0/943CB0 selects the fifth raw catalog vector for
        // [package], independently of the visible item tab. Native fixture
        // native-cmd67-reply-fixture.py proves category 2 misses commodity 109049.
        byte[] expected = [1, 0, 4, 0, 0, 0,
            249, 169, 1, 0, 249, 169, 1, 0, 249, 169, 1, 0,
            255, 255, 2, 0, 46, 0, 1, 0, 0, 0, 3, 0, 1, 0, 0, 0,
            255, 255, 0, 0, 0, 0];
        var actual = GameProtocolEngine.CreateCeraShopBuyReply(4, 109049,
            [new(46, 1), new(3, 1)]);
        check(actual.Payload.SequenceEqual(expected),
            "C2 package completion selects its actual fifth catalog vector and retains both rewards");
        foreach (var category in new[] { 0, 1, 2, 3 })
        {
            expected[2] = (byte)category;
            check(GameProtocolEngine.CreateCeraShopBuyReply(category, 109049,
                    [new(46, 1), new(3, 1)]).Payload.SequenceEqual(expected),
                $"existing C1/C2 category {category} completion remains byte-identical");
        }
    }
}
