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
            if (request.IdempotencyKey != "empty-fingerprint" && request.PayloadFingerprint != "forged")
                request = request with { PayloadFingerprint = JsonGameStore.ComputeDungeonRewardFingerprint(request) };
            var result = await store.CommitDungeonRewardAsync(request);
            if (result.Status != DungeonRewardLedgerStatus.Rejected || result.RejectionCode != code)
                throw new InvalidOperationException($"{name}: expected rejection {code}, got {result.Status}/{result.RejectionCode}");
            check(result.Status == DungeonRewardLedgerStatus.Rejected && result.RejectionCode == code, name);
            check(await File.ReadAllTextAsync(path) == before
                && JsonSerializer.Serialize(await store.GetCharacterAsync("test", character.Id)) == memory,
                name + " preserves disk and live state");
        }
        await Reject(Request("missing-account") with { UserName = "absent" }, "account-not-found", "missing account");
        await Reject(Request("missing-character") with { CharacterId = Guid.NewGuid() }, "character-not-found", "missing character");
        await Reject(Request("gold-cas") with { ExpectedGold = character.Gold - 1 }, "character-state-conflict", "gold CAS conflict");
        await Reject(Request("inventory-cas") with { ExpectedInventory = [new(999, 1, 1)] }, "character-state-conflict", "inventory CAS conflict");
        await Reject(Request("equipment-cas") with { ExpectedEquipment = [] }, "character-state-conflict", "equipment CAS conflict");
        await Reject(Request("capacity") with { ExpectedWarehouseCapacity = (ushort)(character.WarehouseCapacity + 1) }, "warehouse-capacity-conflict", "warehouse conflict");
        await Reject(Request("negative") with { TargetGold = -1 }, "invalid-gold", "negative target gold");
        await Reject(Request("negative-baseline") with { ExpectedGold = -1 }, "invalid-gold", "negative baseline gold");
        await Reject(Request("null") with { PlannedInventory = null! }, "invalid-request", "null collection");
        await Reject(Request("duplicate") with { PlannedInventory = [new(1, 1, 1), new(1, 2, 1)] }, "invalid-planning", "duplicate slot");
        await Reject(Request("metadata") with { PlannedInventory = [new(1, 0, 1)] }, "invalid-planning", "invalid item metadata");
        await Reject(Request("empty-fingerprint") with { PayloadFingerprint = "" }, "payload-fingerprint-mismatch", "empty caller fingerprint");
        await Reject(Request("fingerprint") with { PayloadFingerprint = "forged" }, "payload-fingerprint-mismatch", "caller fingerprint mismatch");
        var pending = (ConcurrentDictionary<Guid, Guid>)typeof(JsonGameStore)
            .GetField("_pendingTradeDeliveries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        pending[character.Id] = Guid.NewGuid();
        await Reject(Request("pending"), "pending-trade-delivery", "pending trade");
        pending.Clear();

        var seededInventory = new[] { new CharacterItemRecord(1, 1, 1) };
        var seeded = await store.SaveCharacterInventoryAsync(
            "test", character.Id, character.Gold, seededInventory, character.Equipment ?? []);
        check(seeded is not null, "fixture seeds a real inventory item");
        character = (await store.GetCharactersAsync("test"))[0];
        var paid = Request("paid-stacking") with
        {
            TargetGold = character.Gold - 10,
            PlannedInventory = (character.Inventory ?? []).Select(item => item with { CountOrValue = item.CountOrValue + 1 }).ToArray()
        };
        await Reject(paid with { IdempotencyKey = "remove-item", PlannedInventory = [] }, "invalid-planning", "inventory removal");
        await Reject(paid with { IdempotencyKey = "replace-item", PlannedInventory = [new(1, 2, 1)] }, "invalid-planning", "inventory replacement");
        await Reject(paid with { IdempotencyKey = "change-equipment", PlannedEquipment = (character.Equipment ?? []).Select(item => item with { ItemId = (ushort)(item.ItemId + 1) }).ToArray() }, "invalid-planning", "equipment replacement");
        paid = paid with { PayloadFingerprint = JsonGameStore.ComputeDungeonRewardFingerprint(paid) };
        // A blocked .tmp proves candidate failure cannot install account or ledger.
        Directory.CreateDirectory(path + ".tmp");
        var beforeFailure = JsonSerializer.Serialize(await store.GetCharacterAsync("test", character.Id));
        var failed = await store.CommitDungeonRewardAsync(paid);
        check(failed.Status == DungeonRewardLedgerStatus.Unknown && JsonSerializer.Serialize(await store.GetCharacterAsync("test", character.Id)) == beforeFailure,
            "writer failure returns Unknown without installing candidate");
        Directory.Delete(path + ".tmp");
        check((await store.CommitDungeonRewardAsync(paid)).Status == DungeonRewardLedgerStatus.Unknown,
            "removing temp obstacle cannot implicitly retry unknown operation");
        // Successful commit/replay has an independent live fixture loaded from untouched official state.
        store = new JsonGameStore(new ServerOptions { DataPath = path }, NullLogger<JsonGameStore>.Instance);
        await store.InitializeAsync();
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
        if (committed.UpdatedCharacter is null) throw new InvalidOperationException("commit rejected: " + committed.RejectionCode);
        var current = committed.UpdatedCharacter;
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
        var stableRequest = paid with
        {
            IdempotencyKey = "drift",
            ExpectedGold = updated!.Gold,
            TargetGold = updated.Gold,
            ExpectedInventory = updated.Inventory!.ToArray(),
            PlannedInventory = updated.Inventory.ToArray()
        };
        stableRequest = stableRequest with { PayloadFingerprint = JsonGameStore.ComputeDungeonRewardFingerprint(stableRequest) };
        var drifting = new DriftingItems(stableRequest.PlannedInventory.ToArray(), new CharacterItemRecord(1, 2, 99));
        var driftResult = await store.CommitDungeonRewardAsync(stableRequest with { PlannedInventory = drifting });
        check(driftResult.Status == DungeonRewardLedgerStatus.Committed
            && driftResult.UpdatedCharacter!.Inventory!.SequenceEqual(stableRequest.PlannedInventory),
            "single enumeration freezes fingerprint and candidate despite mutable list");
    }

    private sealed class DriftingItems : IReadOnlyList<CharacterItemRecord>
    {
        private readonly CharacterItemRecord[] _stable;
        private readonly CharacterItemRecord _drift;
        private int _reads;
        public DriftingItems(CharacterItemRecord[] stable, CharacterItemRecord drift) { _stable = stable; _drift = drift; }
        public CharacterItemRecord this[int index] => (_reads++ >= 1 && index == 0) ? _drift : _stable[index];
        public int Count => _stable.Length;
        public IEnumerator<CharacterItemRecord> GetEnumerator()
        {
            var items = ++_reads == 1 ? _stable : new[] { _drift };
            return ((IEnumerable<CharacterItemRecord>)items).GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
