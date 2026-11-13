using DFLegacy.Protocol;

internal static class ChapterTwoCeraGiftPaddingSmokeTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Captured from the isolated C2 recipient confirmation, 2026-11-13.
        byte[] captured = Convert.FromHexString("090002000000717100000000000000000000");
        check(GameProtocolEngine.TryReadCeraGiftRecipient(captured, out var name, true)
                && name == "qq", "C2 accepts captured aligned recipient query");
        check(!GameProtocolEngine.TryReadCeraGiftRecipient(captured, out _),
            "C1 retains strict recipient length");
        check(GameProtocolEngine.TryReadCeraGiftRecipient(captured[..8], out name)
                && name == "qq", "unpadded recipient remains supported");
        var nonzero = (byte[])captured.Clone(); nonzero[^1] = 1;
        check(!GameProtocolEngine.TryReadCeraGiftRecipient(nonzero, out _, true),
            "C2 rejects nonzero recipient suffix");
        check(!GameProtocolEngine.TryReadCeraGiftRecipient(captured[..^1], out _, true),
            "C2 rejects misaligned recipient suffix");
        check(!GameProtocolEngine.TryReadCeraGiftRecipient([..captured, ..new byte[16]], out _, true),
            "C2 rejects extra recipient block");
        foreach (var invalid in new byte[][] {
            [], [0], [0,0,0,0,0,0], [0,0,31,0,0,0],
            [0,0,1,0,0,0,0xCA], [0,0,1,0,0,0,0] })
            check(!GameProtocolEngine.TryReadCeraGiftRecipient(invalid, out _, true),
                "C2 retains malformed name rejection");

        // C2 94B770 writes an explicit zero selection count after EACH
        // 7-byte commodity row, for self-purchase and gifting alike.
        byte[] row1 = [0xFF,0,0,0x31,0x9A,1,0,0]; // 105009
        byte[] row2 = [2,1,0,0xF9,0xA9,1,0,0];   // 109049, chosen ability 2
        byte[] gift = [9,0,1,2,0,0,0,0x71,0x71,1,..row1];
        check(GameProtocolEngine.TryReadCeraShopPurchase(gift, out var purchase, true)
                && purchase.IsGift && purchase.RecipientName=="qq"
                && purchase.Products.Single()==new GameCeraShopPurchaseItem(0xFF,0,105009),
            "C2 gift consumes explicit empty selection list");
        check(!GameProtocolEngine.TryReadCeraShopPurchase(gift, out _),
            "C1 gift keeps its original seven-byte records");
        byte[] multiple = [9,0,1,2,0,0,0,0x71,0x71,2,..row1,..row2,..new byte[8]];
        check(GameProtocolEngine.TryReadCeraShopPurchase(multiple, out purchase, true)
                && purchase.Products.SequenceEqual(new[] {
                    new GameCeraShopPurchaseItem(0xFF,0,105009), new GameCeraShopPurchaseItem(2,1,109049) }),
            "C2 multiple gifts keep the second commodity aligned");
        byte[] self = [9,0,0,2,..row1,..row2,..new byte[14]];
        check(GameProtocolEngine.TryReadCeraShopPurchase(self, out purchase, true)
                && !purchase.IsGift && purchase.Products.Length==2
                && purchase.Products[1]==new GameCeraShopPurchaseItem(2,1,109049),
            "C2 multiple self purchases keep the second commodity aligned");
        var unsupported=(byte[])gift.Clone(); unsupported[17]=1;
        check(!GameProtocolEngine.TryReadCeraShopPurchase(unsupported,out _,true),
            "C2 unsupported nonempty package selections remain rejected");
        var dirty=(byte[])multiple.Clone(); dirty[^1]=1;
        check(!GameProtocolEngine.TryReadCeraShopPurchase(dirty,out _,true)
                && !GameProtocolEngine.TryReadCeraShopPurchase(multiple[..^1],out _,true)
                && !GameProtocolEngine.TryReadCeraShopPurchase([..multiple,..new byte[16]],out _,true),
            "C2 gifts reject dirty, misaligned and excessive padding");
        var secondSelection=(byte[])multiple.Clone(); secondSelection[25]=1;
        check(!GameProtocolEngine.TryReadCeraShopPurchase(secondSelection,out _,true),
            "C2 rejects unsupported selections in a later row");
        byte[] maximum=[9,0,0,32,..Enumerable.Repeat(row1,32).SelectMany(x=>x),..new byte[14]];
        check(GameProtocolEngine.TryReadCeraShopPurchase(maximum,out purchase,true)
                && purchase.Products.Length==32,
            "C2 accepts the parser maximum of 32 fixed products");
        maximum[3]=33;
        check(!GameProtocolEngine.TryReadCeraShopPurchase(maximum,out _,true),
            "C2 refuses 33 products before reading records");
        maximum[3]=0;
        check(!GameProtocolEngine.TryReadCeraShopPurchase(maximum,out _,true),
            "C2 refuses zero products");
    }
}
