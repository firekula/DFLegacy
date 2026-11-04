using System.Text.Json;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class AccountKindSmokeTests
{
    // Catches UID-only mutations that orphan characters, refill wallets or
    // accidentally create test presets, plus reuse of allocated identities.
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-account-kind-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = new ServerOptions { DataPath = Path.Combine(root, "state.json"), SeedTestAccount = false,
            ScriptPvfPath = "", SkillScriptPath = root };
        JsonGameStore Open() => new(options, NullLogger<JsonGameStore>.Instance);
        var store = Open();
        await store.InitializeAsync();
        var original = (await store.CreateAccountAsync(new("kind-user", "password", OicqCode: "123456"))).Account!;
        await store.SetRoleAsync("kind-user", AccountRole.Admin, "operator");
        var character = (await store.CreateCharacterAsync("kind-user", new("KindHero"))).Character!;
        await store.SaveCharacterItemSpacesAsync("kind-user", character.Id, 4321,
            [new(1, 1, 3), new(9, 8, 7)], [new(10, 26043, 1)], [new(0, 8, 3)],
            avatarInventory: [new(0, 1000, 1)], creatureInventory: [new(0, 2000, 1)]);
        await store.GrantCeraToAccountAsync("kind-user", 23400);
        var before = (await store.GetCharactersAsync("kind-user")).Single();
        var online = await Change("kind-user", AccountKind.Test, () => true);
        check(!online.Updated && (await store.GetAccountAsync("kind-user"))!.AccountUid == original.AccountUid,
            "online game connections reject a kind change without mutating identity");
        var changed = await Change(" kind-user ", AccountKind.Test);
        check(changed.Updated && changed.Account is { Kind: AccountKind.Test, Role: AccountRole.Admin, Cash: 23400 }
            && changed.Account.Id == original.Id && changed.Account.Characters.Count == 1,
            "normal-to-test allocates a test identity while preserving account, role, cash and roster");
        if (!changed.Updated || changed.Account is null) return;
        var testUid = changed.Account.AccountUid;
        check(testUid is >= 1 and <= 10_000_000 && testUid != original.AccountUid,
            "normal-to-test moves into the test UID range");
        Preserved(changed.Account.Characters.Single(), testUid, "normal-to-test");
        check(!(await store.CreateCharacterAsync(original.AccountUid, new("StaleUid"))).Created,
            "the old normal UID cannot address the converted account");
        var same = await Change("KIND-USER", AccountKind.Test);
        check(same.Updated && same.Account?.AccountUid == testUid, "reapplying a kind is idempotent");
        foreach (var invalid in new[] { AccountKind.Invalid, AccountKind.Reserved, (AccountKind)77 })
            check(!(await Change("kind-user", invalid)).Updated, "invalid account kinds cannot be assigned: " + invalid);
        check(!(await Change("missing", AccountKind.Test)).Updated, "missing accounts cannot be converted");
        store = Open();
        await store.InitializeAsync();
        var persisted = (await store.GetAccountAsync("kind-user"))!;
        check(persisted.Kind == AccountKind.Test && persisted.AccountUid == testUid && persisted.Characters.Count == 1,
            "reload keeps converted test identity without generating a test preset");
        Preserved(persisted.Characters.Single(), testUid, "test reload");
        var nextTest = (await store.CreateAccountAsync(new("next-test", "password", TestAccount: true, OicqCode: "123456"))).Account!;
        check(nextTest.AccountUid > testUid, "persisted test UID allocator advances beyond converted identities");
        var ordinary = await Change("kind-user", AccountKind.Normal);
        check(ordinary.Updated && ordinary.Account is { Kind: AccountKind.Normal, Role: AccountRole.Admin, Cash: 23400 }
            && ordinary.Account.AccountUid > original.AccountUid,
            "test-to-normal allocates a new normal UID while preserving authority and cash");
        if (!ordinary.Updated || ordinary.Account is null) return;
        var normalUid = ordinary.Account.AccountUid;
        Preserved(ordinary.Account.Characters.Single(), normalUid, "test-to-normal");
        check(!(await store.CreateCharacterAsync(testUid, new("StaleTestUid"))).Created,
            "the old test UID cannot address the converted account");
        store = Open();
        await store.InitializeAsync();
        var final = (await store.GetAccountAsync("kind-user"))!;
        check(final.AccountUid == normalUid && final.Kind == AccountKind.Normal && final.Cash == 23400
            && final.Role == AccountRole.Admin && final.Characters.Count == 1,
            "round-trip conversion persists wallet, role, roster and identity");
        Preserved(final.Characters.Single(), normalUid, "normal reload");
        var nextNormal = (await store.CreateAccountAsync(new("next-normal", "password", OicqCode: "123456"))).Account!;
        check(nextNormal.AccountUid > normalUid, "persisted normal UID allocator advances beyond converted identities");
        var savedBytes = await File.ReadAllBytesAsync(options.DataPath);
        Directory.CreateDirectory(options.DataPath + ".tmp");
        try
        {
            var failed = await Change("kind-user", AccountKind.Test);
            var bytesAfterFailure = await File.ReadAllBytesAsync(options.DataPath);
            check(!failed.Updated && (await store.GetAccountAsync("kind-user"))!.AccountUid == normalUid
                && savedBytes.SequenceEqual(bytesAfterFailure),
                "a failed snapshot never publishes an in-memory UID change or corrupts the committed file");
            var refused = false;
            try { await Change("kind-user", AccountKind.Test); }
            catch (InvalidOperationException) { refused = true; }
            check(refused, "uncertain account-kind writes quarantine further mutations until reload");
        }
        finally { Directory.Delete(options.DataPath + ".tmp"); }
        store = Open();
        await store.InitializeAsync();
        var recovered = await Change("kind-user", AccountKind.Test);
        check(recovered.Updated && recovered.Account!.AccountUid == nextTest.AccountUid + 1,
            "reload after failed kind write preserves the destination UID allocator");
        if (recovered.Updated) Preserved(recovered.Account!.Characters.Single(), recovered.Account.AccountUid, "write recovery");
        Console.WriteLine("Account-kind artifacts: " + root);

        async Task<(bool Updated, PublicAccountRecord? Account, string? Error)> Change(string name, AccountKind kind, Func<bool>? online = null) =>
            await store.SetAccountKindAsync(name, kind, CancellationToken.None, online);
        void Preserved(CharacterRecord after, uint uid, string boundary) =>
            check(JsonSerializer.Serialize(after with { AccountUid = before.AccountUid }) == JsonSerializer.Serialize(before)
                && after.AccountUid == uid,
                boundary + " preserves every character field, including spent revival coins, while changing its owner UID");
    }
}
