using System.Reflection;
using System.Text.Json;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class DungeonRewardCommitSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var statusJson = JsonSerializer.Serialize(DungeonRewardLedgerStatus.Unknown);
        check(statusJson == "2", "ledger status serializes as stable numeric enum");
        var status = JsonSerializer.Deserialize<DungeonRewardLedgerStatus>(statusJson);
        check(status == DungeonRewardLedgerStatus.Unknown, "ledger status round-trips");

        var result = new DungeonRewardCommitResult(
            DungeonRewardLedgerStatus.Rejected,
            null,
            null,
            0,
            "sample-rejection",
            null);
        var resultRoundTrip = JsonSerializer.Deserialize<DungeonRewardCommitResult>(
            JsonSerializer.Serialize(result));
        check(resultRoundTrip.Status == DungeonRewardLedgerStatus.Rejected
            && resultRoundTrip.RejectionCode == "sample-rejection"
            && resultRoundTrip.UpdatedCharacter is null,
            "commit result round-trips with default nullable fields");

        var tempRoot = Path.Combine(Path.GetTempPath(), "dflegacy-reward-commit-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var dayKey = typeof(JsonGameStore).GetMethod("GetFatigueDayKey", BindingFlags.Static | BindingFlags.Public)!
                .Invoke(null, [DateTimeOffset.Now]);
            var oldSave = "{\"Accounts\":[],\"NextCharacterNo\":1,\"NextNormalAccountUid\":100000,\"NextTestAccountUid\":1,\"FatigueDayKey\":" + dayKey + "}";
            var path = Path.Combine(tempRoot, "store.json");
            await File.WriteAllTextAsync(path, oldSave);
            var options = new ServerOptions { DataPath = path };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            var ledgerField = typeof(JsonGameStore).GetField("_dungeonRewardLedger", BindingFlags.Instance | BindingFlags.NonPublic);
            var ledger = ledgerField?.GetValue(store) as System.Collections.ICollection;
            check(ledger is not null && ledger.Count == 0, "old save without ledger loads as empty ledger");

            // Exercise the actual private root model without adding production test hooks.
            var stateType = typeof(JsonGameStore).GetNestedType("JsonGameStoreState", BindingFlags.NonPublic)!;
            var ledgerProperty = stateType.GetProperty("DungeonRewardLedger")!;
            var oldState = JsonSerializer.Deserialize(oldSave, stateType)!;
            check(ledgerProperty.GetValue(oldState) is System.Collections.ICollection { Count: 0 },
                "missing ledger field defaults to empty root collection");
            check(await File.ReadAllTextAsync(path) == oldSave,
                "ledger-only migration does not rewrite an otherwise current save");

            var entry = new DungeonRewardLedgerEntry("key", "fingerprint", Guid.NewGuid(), 123,
                1, 2, 3, DungeonRewardLedgerStatus.Unknown, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow, null, -10, null, "sample-unknown");
            ledgerProperty.SetValue(oldState, new List<DungeonRewardLedgerEntry> { entry });
            var rootJson = JsonSerializer.Serialize(oldState, stateType);
            var restored = JsonSerializer.Deserialize(rootJson, stateType)!;
            check(ledgerProperty.GetValue(restored) is List<DungeonRewardLedgerEntry> entries
                && entries.Count == 1 && entries[0] == entry,
                "root ledger entry round-trips with Unknown and nullable fields");
            await File.WriteAllTextAsync(path, rootJson);
            var reloadedStore = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await reloadedStore.InitializeAsync();
            check(ledgerField?.GetValue(reloadedStore) is List<DungeonRewardLedgerEntry> loaded
                && loaded.Count == 1 && loaded[0] == entry,
                "store loads ledger without interpreting Unknown as committed");
            var quarantineRejected = false;
            try { await reloadedStore.CreateAccountAsync(new CreateAccountRequest("blocked", "password", OicqCode: "123456")); }
            catch (InvalidOperationException) { quarantineRejected = true; }
            check(quarantineRejected && await File.ReadAllTextAsync(path) == rootJson,
                "restarted Unknown ledger blocks legacy disk writes");
            // Ordinary ledger preservation uses a separate committed fixture, not quarantined state.
            var committedEntry = entry with { Status = DungeonRewardLedgerStatus.Committed, UnknownReason = null };
            ledgerProperty.SetValue(oldState, new List<DungeonRewardLedgerEntry> { committedEntry });
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(oldState, stateType));
            var ordinaryStore = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await ordinaryStore.InitializeAsync();
            var created = await ordinaryStore.CreateAccountAsync(new CreateAccountRequest("ledger", "password", OicqCode: "123456"));
            check(created.Created, "ordinary save fixture actually creates an account");
            using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            check(persisted.RootElement.GetProperty("DungeonRewardLedger").GetArrayLength() == 1,
                "ordinary snapshot save preserves existing ledger in the same file");
            check(Directory.GetFiles(tempRoot).Length == 1,
                "no second ledger file is created");
            await DungeonRewardStoreSmokeTests.RunAsync(check, tempRoot);
            await DungeonRewardUnknownSmokeTests.RunAsync(check, tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
