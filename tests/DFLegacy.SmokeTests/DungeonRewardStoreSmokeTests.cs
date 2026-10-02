using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class DungeonRewardStoreSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check, string root)
    {
        var path = Path.Combine(root, "reward-store.json");
        var store = new JsonGameStore(new ServerOptions { DataPath = path }, NullLogger<JsonGameStore>.Instance);
        await store.InitializeAsync();
        var character = (await store.GetCharactersAsync("test"))[0];
        DungeonRewardCommitRequest Request(string key) => new(key, "", "test", character.Id,
            1, 0, 0, 0, character.Gold + 100, character.Gold,
            (character.Inventory ?? []).ToArray(), (character.Equipment ?? []).ToArray(),
            (character.Inventory ?? []).ToArray(), (character.Equipment ?? []).ToArray(), [], null, null);
        async Task Reject(DungeonRewardCommitRequest request, string code, string name)
        {
            var before = await File.ReadAllTextAsync(path);
            var memory = JsonSerializer.Serialize(await store.GetCharacterAsync("test", character.Id));
            var result = await store.CommitDungeonRewardAsync(request);
            check(result.Status == DungeonRewardLedgerStatus.Rejected && result.RejectionCode == code, name);
            check(await File.ReadAllTextAsync(path) == before
                && JsonSerializer.Serialize(await store.GetCharacterAsync("test", character.Id)) == memory,
                name + " preserves disk and live state");
        }
        await Reject(Request("missing-account") with { UserName = "absent" }, "account-not-found", "missing account");
        await Reject(Request("missing-character") with { CharacterId = Guid.NewGuid() }, "character-not-found", "missing character");
        await Reject(Request("gold-cas") with { ExpectedGold = character.Gold - 1 }, "character-state-conflict", "gold CAS conflict");
        await Reject(Request("inventory-cas") with { ExpectedInventory = [] }, "character-state-conflict", "inventory CAS conflict");
        await Reject(Request("equipment-cas") with { ExpectedEquipment = [] }, "character-state-conflict", "equipment CAS conflict");
        await Reject(Request("capacity") with { ExpectedWarehouseCapacity = (ushort)(character.WarehouseCapacity + 1) }, "warehouse-capacity-conflict", "warehouse conflict");
        await Reject(Request("negative") with { TargetGold = -1 }, "invalid-gold", "negative target gold");
        await Reject(Request("negative-baseline") with { ExpectedGold = -1 }, "invalid-gold", "negative baseline gold");
        await Reject(Request("null") with { PlannedInventory = null! }, "invalid-request", "null collection");
        await Reject(Request("duplicate") with { PlannedInventory = [new(1, 1, 1), new(1, 2, 1)] }, "invalid-planning", "duplicate slot");
        await Reject(Request("metadata") with { PlannedInventory = [new(1, 0, 1)] }, "invalid-planning", "invalid item metadata");
        await Reject(Request("fingerprint") with { PayloadFingerprint = "forged" }, "payload-fingerprint-mismatch", "caller fingerprint mismatch");
        var pending = (ConcurrentDictionary<Guid, Guid>)typeof(JsonGameStore)
            .GetField("_pendingTradeDeliveries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        pending[character.Id] = Guid.NewGuid();
        await Reject(Request("pending"), "pending-trade-delivery", "pending trade");
        pending.Clear();

        var paid = Request("paid-stacking") with
        {
            TargetGold = character.Gold - 10,
            PlannedInventory = (character.Inventory ?? []).Select(i => i.Slot == character.Inventory![0].Slot
                ? i with { CountOrValue = i.CountOrValue + 1 } : i).ToArray()
        };
        // A blocked .tmp proves candidate failure cannot install account or ledger.
        Directory.CreateDirectory(path + ".tmp");
        var beforeFailure = JsonSerializer.Serialize(await store.GetCharacterAsync("test", character.Id));
        var threw = false;
        try { await store.CommitDungeonRewardAsync(paid); } catch (IOException) { threw = true; } catch (UnauthorizedAccessException) { threw = true; }
        check(threw && JsonSerializer.Serialize(await store.GetCharacterAsync("test", character.Id)) == beforeFailure,
            "writer failure propagates without installing candidate");
        Directory.Delete(path + ".tmp");
        var committed = await store.CommitDungeonRewardAsync(paid);
        check(committed.Status == DungeonRewardLedgerStatus.Committed && committed.GoldDelta == -10,
            "first paid commit succeeds with exact baseline");
        check(committed.UpdatedCharacter?.Inventory?.SequenceEqual(paid.PlannedInventory) == true,
            "stacking planned update is persisted");
        using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        {
            check(doc.RootElement.GetProperty("DungeonRewardLedger").GetArrayLength() == 1,
                "one committed ledger entry in account snapshot");
        }
        var current = committed.UpdatedCharacter!;
        await store.SaveCharacterInventoryAsync("test", current.Id, current.Gold + 7, current.Inventory!, current.Equipment!);
        var updated = await store.GetCharacterAsync("test", current.Id);
        // Any attempted replay writer call throws. This is stronger than observing equal file contents.
        Directory.CreateDirectory(path + ".tmp");
        var beforeReplay = await File.ReadAllTextAsync(path);
        var replay = await store.CommitDungeonRewardAsync(paid);
        check(replay.Status == DungeonRewardLedgerStatus.Committed && replay.UpdatedCharacter?.Gold == updated!.Gold,
            "committed replay returns current character without baseline CAS");
        check(await File.ReadAllTextAsync(path) == beforeReplay, "replay performs no snapshot write");
        await Reject(paid with { TargetGold = paid.TargetGold + 1 }, "idempotency-payload-mismatch", "same key changed frozen payload");
        Directory.Delete(path + ".tmp");
        var reopened = new JsonGameStore(new ServerOptions { DataPath = path }, NullLogger<JsonGameStore>.Instance);
        await reopened.InitializeAsync();
        check((await reopened.CommitDungeonRewardAsync(paid)).UpdatedCharacter?.Gold == updated!.Gold,
            "committed replay after restart does not restore stale character");

        var stateType = typeof(JsonGameStore).GetNestedType("JsonGameStoreState", BindingFlags.NonPublic)!;
        var state = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path), stateType)!;
        var ledger = (List<DungeonRewardLedgerEntry>)stateType.GetProperty("DungeonRewardLedger")!.GetValue(state)!;
        ledger[0] = ledger[0] with { Status = DungeonRewardLedgerStatus.Unknown, UnknownReason = "test-unknown" };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, stateType));
        var unknownStore = new JsonGameStore(new ServerOptions { DataPath = path }, NullLogger<JsonGameStore>.Instance);
        await unknownStore.InitializeAsync();
        check((await unknownStore.CommitDungeonRewardAsync(paid)).Status == DungeonRewardLedgerStatus.Unknown,
            "loaded Unknown receipt is not retried");
    }
}
