using DFLegacy.Server;
using Microsoft.Extensions.Logging;
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
            var logger = new CaptureLogger();
            var store = new JsonGameStore(new ServerOptions { DataPath = path }, logger, writer);
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
            if (stage == FailureStage.BeforeReplace)
            {
                var funnelBlocked = false;
                try { await store.SetCharacterUsedFatigueAsync(character.Id, 1); }
                catch (InvalidOperationException) { funnelBlocked = true; }
                check(funnelBlocked && await File.ReadAllTextAsync(path) == before,
                    "funnel prevents uncertain ledger leaking through unguarded fatigue save");
                var reloaded = new JsonGameStore(new ServerOptions { DataPath = path }, NullLogger<JsonGameStore>.Instance);
                await reloaded.InitializeAsync();
                var savedAfterReload = await reloaded.SaveCharacterInventoryAsync("test", character.Id,
                    character.Gold, character.Inventory ?? [], character.Equipment ?? []);
                check(savedAfterReload is not null, "pre-replace failure plus unguarded save does not permanently quarantine reload");
            }
            var beforeClear = await File.ReadAllTextAsync(path);
            var rejectedClear = await store.ForceClearRewardQuarantineAsync(" ");
            check(rejectedClear.Decision == StoreRecoveryDecision.Refused && await File.ReadAllTextAsync(path) == beforeClear,
                "blank operator reason refuses without state changes");
            if (stage == FailureStage.BeforeReplace)
            {
                var discardFailed = false;
                var callsBeforeDiscard = writer.Calls;
                try { await store.ForceClearRewardQuarantineAsync("operator test"); }
                catch (IOException) { discardFailed = true; }
                var saveBlocked = false;
                try { await store.SaveCharacterInventoryAsync("test", character.Id, character.Gold, character.Inventory ?? [], character.Equipment ?? []); }
                catch (InvalidOperationException) { saveBlocked = true; }
                check(discardFailed && saveBlocked && writer.Calls == callsBeforeDiscard + 1
                    && !logger.Messages.Any(x => x.Contains("decision=Discarded")), "failed privileged discard retains quarantine and no success audit");
                check((await store.CommitDungeonRewardAsync(request)).Status == DungeonRewardLedgerStatus.Unknown, "failed discard retains Unknown ledger");
                writer.Succeed = true;
                var discarded = await store.ForceClearRewardQuarantineAsync("operator test");
                check(discarded.Decision == StoreRecoveryDecision.Discarded && discarded.DiscardedEntryCount >= 1, "BeforeReplace operator discard");
                var disk = await File.ReadAllTextAsync(path);
                check(disk.Contains("\"Status\": 1") && disk.Contains("operator-discarded"), "operator discard persisted ledger");
                check(await store.SaveCharacterInventoryAsync("test", character.Id, character.Gold, character.Inventory ?? [], character.Equipment ?? []) is not null, "discard ordinary write");
                check(logger.Messages.Any(x => x.Contains("operator test") && x.Contains("Discarded")), "operator audit logged");
                var restarted = new JsonGameStore(new ServerOptions { DataPath = path }, NullLogger<JsonGameStore>.Instance);
                await restarted.InitializeAsync();
                check(await restarted.SaveCharacterInventoryAsync("test", character.Id, character.Gold, character.Inventory ?? [], character.Equipment ?? []) is not null, "discard survives restart");
            }
            var recovered = await store.RecoverDungeonRewardAsync(request.IdempotencyKey);
            if (stage == FailureStage.AfterReplace)
            {
                var operatorRecovered = await store.ForceClearRewardQuarantineAsync("operator test");
                check(operatorRecovered.Decision == StoreRecoveryDecision.Recovered, "AfterReplace operator recovery");
                check(operatorRecovered.DiscardedEntryCount == 0, "AfterReplace does not discard");
                var diskAfterRecovery = await File.ReadAllTextAsync(path);
                check(diskAfterRecovery.Split("unknown-key").Length == 2, "AfterReplace ledger remains single committed entry");
                check(await store.SaveCharacterInventoryAsync("test", character.Id, request.TargetGold, character.Inventory ?? [], character.Equipment ?? []) is not null, "AfterReplace ordinary write succeeds");
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
                check(stage == FailureStage.BeforeReplace || stillBlocked, $"{stage}: failed recovery remains quarantined unless operator discarded");
            }
        }
    }

    private sealed class CaptureLogger : ILogger<JsonGameStore>
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class SuccessWriter : IDungeonRewardSnapshotWriter
    {
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken cancellationToken)
        {
            await File.WriteAllBytesAsync(path + ".tmp", snapshot.ToArray(), cancellationToken);
            File.Move(path + ".tmp", path, overwrite: true);
        }
    }

    private enum FailureStage { BeforeTemp, BeforeReplace, AfterReplace, CancelDuringWrite }

    // Internal snapshot persistence abstraction, not a public production test callback.
    private sealed class FailingWriter(FailureStage stage) : IDungeonRewardSnapshotWriter
    {
        public int Calls { get; private set; }
        public bool Succeed { get; set; }
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken cancellationToken)
        {
            Calls++;
            if (Succeed)
            {
                await File.WriteAllBytesAsync(path + ".tmp", snapshot.ToArray(), cancellationToken);
                File.Move(path + ".tmp", path, overwrite: true);
                return;
            }
            if (stage == FailureStage.BeforeTemp) throw new IOException("injected before temp serialization/write");
            await File.WriteAllBytesAsync(path + ".tmp", snapshot.ToArray(), cancellationToken);
            if (stage == FailureStage.BeforeReplace) throw new IOException("injected before replacement");
            if (stage == FailureStage.CancelDuringWrite) throw new OperationCanceledException("injected write cancellation");
            File.Move(path + ".tmp", path, overwrite: true);
            throw new IOException("injected after official replacement");
        }
    }
}
