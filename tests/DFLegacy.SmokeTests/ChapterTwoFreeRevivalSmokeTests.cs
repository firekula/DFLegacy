using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoFreeRevivalSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(GameProtocolEngine.CreateFreeRevivalCoinEligibility(true).Encode().Length == 7
            && GameProtocolEngine.CreateFreeRevivalCoinEligibility(true).Payload.SequenceEqual(new byte[] { 0 })
            && GameProtocolEngine.CreateFreeRevivalCoinEligibility(false).Payload.SequenceEqual(new byte[] { 1 })
            && GameProtocolEngine.CreateFreeRevivalCoinReply(0).Payload.SequenceEqual(new byte[] { 1, 0 })
            && GameProtocolEngine.CreateFreeRevivalCoinReply(126).Payload.SequenceEqual(new byte[] { 1, 126 }),
            "Chapter Two free revival uses the observed eligibility and count payloads");

        await using var harness = await PartyHarness.CreateAsync();
        var store = harness.Store;
        var account = "alice";
        var character = (await store.GetCharactersAsync(account))[0];
        var stackable = Path.Combine(harness.Options.SkillScriptPath, "stackable");
        Directory.CreateDirectory(stackable);
        await File.WriteAllTextAsync(Path.Combine(stackable, "stackable.lst"), "1 `coin.stk`\n");
        await File.WriteAllTextAsync(Path.Combine(stackable, "coin.stk"), "[name]\n`Coin`\n");
        var catalog = new ItemCatalog(new ScriptFileSystem(harness.Options, NullLogger<ScriptFileSystem>.Instance),
            NullLogger<ItemCatalog>.Instance);
        var reset = new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.FromHours(8));
        var before = reset.AddSeconds(-1);
        Task<CharacterRecord?> Claim(DateTimeOffset now, byte amount = 1) =>
            store.ClaimFreeRevivalCoinsAsync(account, character.Id, amount, now, catalog, CancellationToken.None);

        check(await Claim(before, 127) is null
            && await store.CanClaimFreeRevivalCoinsAsync(account, before, CancellationToken.None),
            "an invalid wire grant amount never consumes eligibility");

        // File.Create cannot open a directory as the temporary snapshot. The
        // original snapshot and the in-memory claim must both remain usable.
        var temporary = harness.Options.DataPath + ".tmp";
        Directory.CreateDirectory(temporary);
        var failed = false;
        try { await Claim(before); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        finally { Directory.Delete(temporary); }
        check(failed && await store.CanClaimFreeRevivalCoinsAsync(account, before, CancellationToken.None),
            "a failed atomic write does not consume the free revival claim");

        var first = await Claim(before);
        check(first is not null && await Claim(before) is null
            && await store.CanClaimFreeRevivalCoinsAsync(account, reset, CancellationToken.None),
            "the isolated account policy reopens at the Beijing 06:00 boundary");
        var second = await Claim(reset);
        check(second?.Inventory?.SingleOrDefault(CharacterInventoryLayout.IsRevivalCoin)?.CountOrValue == 2
            && await Claim(before) is null && await Claim(reset) is null,
            "two different days each grant once and a backward clock cannot reopen a previous day");
    }
}
