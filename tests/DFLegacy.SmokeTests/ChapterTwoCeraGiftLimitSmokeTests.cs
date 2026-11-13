using DFLegacy.Protocol;
using DFLegacy.Server;
using System.Text.Json.Nodes;

internal static partial class CeraGiftSmokeTests
{
    private static async Task ChapterTwoGiftLimits(Action<bool, string> check)
    {
        // Native C2 D3F704 starts at20; Etc/NewCashShop.etc may explicitly override it.
        // C1 B539D4 retains default1. Resolve the catalog lazily before choosing the default.
        foreach (int? configured in new int?[] { null, 0, 1, 2, 255, -1, 256 })
        {
            await using var f = new Fixture(max: configured);
            var valid = configured is >= 0 and <= 255;
            check(f.Shop.GetMaxGiftCount(true) == (valid ? configured : 20)
                && f.Shop.GetMaxGiftCount(false) == (valid ? configured : 1)
                && f.Shop.MaxGiftCount == (valid ? configured : 1),
                $"gift limit lazy catalog configured={configured?.ToString() ?? "absent"} preserves C1/C2 defaults and explicit bounds");
        }

        foreach (var (max, received, count, succeeds) in new (int?, int, int, bool)[]
        {
            (null, 0, 2, true), (null, 1, 2, true), (null, 18, 2, true),
            (null, 19, 1, true), (null, 19, 2, false), (null, 20, 1, false),
            (0, 0, 1, false), (1, 0, 2, false), (255, 254, 1, true), (255, 255, 1, false)
        })
        {
            await using var f = new Fixture(max: max);
            var store = f.Open(); var (a, b, _) = await f.Seed(store);
            var state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
            var target = state["Accounts"]!.AsArray().Single(x => (string?)x!["UserName"] == "target")!;
            target["GiftReceiveYearMonth"] = 202610;
            target["GiftReceiveCount"] = received;
            await File.WriteAllTextAsync(f.Options.DataPath, state.ToJsonString());
            store = f.Open(); await store.InitializeAsync();
            var before = await File.ReadAllBytesAsync(f.Options.DataPath);
            var requests = Enumerable.Repeat(new GameCeraShopPurchaseItem(0xFF, 0, 100019), count).ToArray();
            var result = await store.GiftCeraShopAsync("sender", a.Id, b.Name, requests, f.Shop, isChapterTwo: true);
            var after = await File.ReadAllBytesAsync(f.Options.DataPath);
            check(succeeds
                ? result.Status == DungeonRewardLedgerStatus.Committed
                    && result.SenderCash == 50_000 - 1400 * count
                    && (await store.GetCharacterMailsAsync(b.Id)).Length == count
                    && (await store.GetCharacterMailsAsync(b.Id)).All(m => m.Attachment?.CountOrValue == 10)
                    && (await store.QueryCeraGiftRecipientAsync(a.Id, b.Name)).ReceivedCount == received + count
                : result.Status == DungeonRewardLedgerStatus.Rejected && result.Error == 4 && before.SequenceEqual(after),
                $"C2 gift max{max?.ToString() ?? "default"} received{received}+products{count} commits={succeeds}; quantity10 counts once and refusal preserves exact state");
        }

        await using var concurrent = new Fixture();
        var initial = concurrent.Open(); var (sender, receiver, sibling) = await concurrent.Seed(initial);
        await initial.CreateAccountAsync(new("sender2", "password", OicqCode: "123456"));
        var sender2 = (await initial.CreateCharacterAsync("sender2", new("Sender2", Level: 1))).Character!;
        await initial.GrantCeraToAccountAsync("sender2", 50_000);
        var seeded = JsonNode.Parse(await File.ReadAllTextAsync(concurrent.Options.DataPath))!;
        var account = seeded["Accounts"]!.AsArray().Single(x => (string?)x!["UserName"] == "target")!;
        account["GiftReceiveYearMonth"] = 202610; account["GiftReceiveCount"] = 19;
        await File.WriteAllTextAsync(concurrent.Options.DataPath, seeded.ToJsonString());
        var shared = concurrent.Open(); await shared.InitializeAsync();
        var results = await Task.WhenAll(
            shared.GiftCeraShopAsync("sender", sender.Id, receiver.Name, [new(0xFF, 0, 100018)], concurrent.Shop, isChapterTwo: true),
            shared.GiftCeraShopAsync("sender2", sender2.Id, sibling.Name, [new(0xFF, 0, 100018)], concurrent.Shop, isChapterTwo: true));
        check(results.Count(x => x.Status == DungeonRewardLedgerStatus.Committed) == 1
            && results.Count(x => x.Status == DungeonRewardLedgerStatus.Rejected && x.Error == 4) == 1
            && (await shared.GetAccountCeraAsync("sender")) + (await shared.GetAccountCeraAsync("sender2")) == 99_850
            && (await shared.GetCharacterMailsAsync(receiver.Id)).Length + (await shared.GetCharacterMailsAsync(sibling.Id)).Length == 1
            && (await shared.QueryCeraGiftRecipientAsync(sender.Id, receiver.Name)).ReceivedCount == 20,
            "C2 two senders racing19/20 across receiver-account siblings commit exactly one debit/mail/count");
    }
}
