using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class DungeonRewardUnknownSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check, string root)
    {
        foreach (var stage in Enum.GetValues<FailureStage>())
        {
            var stageRoot = Path.Combine(root, $"unknown-{stage}");
            Directory.CreateDirectory(stageRoot);
            var path = Path.Combine(stageRoot, "store.json");
            var writer = new FailingWriter(stage);
            var store = new JsonGameStore(new ServerOptions { DataPath = path },
                NullLogger<JsonGameStore>.Instance, writer);
            await store.InitializeAsync();
            var character = (await store.GetCharactersAsync("test"))[0];
            var request = new DungeonRewardCommitRequest("unknown-key", "", "test", character.Id,
                1, 0, 0, 0, character.Gold + 100, character.Gold,
                (character.Inventory ?? []).ToArray(), (character.Equipment ?? []).ToArray(),
                (character.Inventory ?? []).ToArray(), (character.Equipment ?? []).ToArray(), [], null, null);
            request = request with { PayloadFingerprint = JsonGameStore.ComputeDungeonRewardFingerprint(request) };
            var result = await store.CommitDungeonRewardAsync(request);
            check(result.Status == DungeonRewardLedgerStatus.Unknown && !string.IsNullOrWhiteSpace(result.UnknownReason),
                $"{stage}: write uncertainty returns Unknown with reason");
            var calls = writer.Calls;
            var before = await File.ReadAllTextAsync(path);
            var replay = await store.CommitDungeonRewardAsync(request);
            check(replay.Status == DungeonRewardLedgerStatus.Unknown && writer.Calls == calls,
                $"{stage}: Unknown never retries writer");
            CharacterRecord? ordinary = null;
            var blocked = false;
            try { ordinary = await store.SaveCharacterInventoryAsync("test", character.Id,
                character.Gold + 1, character.Inventory ?? [], character.Equipment ?? []); }
            catch (InvalidOperationException) { blocked = true; }
            check(blocked && ordinary is null && await File.ReadAllTextAsync(path) == before,
                $"{stage}: quarantine blocks ordinary stale snapshot save");
            var recovered = await store.RecoverDungeonRewardAsync(request.IdempotencyKey);
            if (stage == FailureStage.AfterReplace)
            {
                check(recovered.Status == DungeonRewardLedgerStatus.Committed
                    && recovered.UpdatedCharacter?.Gold == request.TargetGold,
                    "after replacement recovery installs official committed state");
                var saved = await store.SaveCharacterInventoryAsync("test", character.Id,
                    request.TargetGold, recovered.UpdatedCharacter!.Inventory ?? [], recovered.UpdatedCharacter.Equipment ?? []);
                check(saved is not null, "committed recovery clears quarantine");
            }
            else
            {
                check(recovered.Status == DungeonRewardLedgerStatus.Unknown,
                    $"{stage}: absent committed receipt cannot clear quarantine");
                var stillBlocked = false;
                try { await store.SaveCharacterInventoryAsync("test", character.Id,
                    character.Gold, character.Inventory ?? [], character.Equipment ?? []); }
                catch (InvalidOperationException) { stillBlocked = true; }
                check(stillBlocked, $"{stage}: failed recovery remains quarantined");
            }
        }
    }

    private enum FailureStage { BeforeTemp, BeforeReplace, AfterReplace, CancelDuringWrite }

    // Internal snapshot persistence abstraction, not a public production test callback.
    private sealed class FailingWriter(FailureStage stage) : IDungeonRewardSnapshotWriter
    {
        public int Calls { get; private set; }
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken cancellationToken)
        {
            Calls++;
            if (stage == FailureStage.BeforeTemp) throw new IOException("injected before temp serialization/write");
            await File.WriteAllBytesAsync(path + ".tmp", snapshot.ToArray(), cancellationToken);
            if (stage == FailureStage.BeforeReplace) throw new IOException("injected before replacement");
            if (stage == FailureStage.CancelDuringWrite) throw new OperationCanceledException("injected write cancellation");
            File.Move(path + ".tmp", path, overwrite: true);
            throw new IOException("injected after official replacement");
        }
    }
}
