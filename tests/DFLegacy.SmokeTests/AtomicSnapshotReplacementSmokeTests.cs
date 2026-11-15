using System.Security.Cryptography;
using System.Text.Json;
using DFLegacy.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal static partial class AtomicSnapshotReplacementSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> report)
    {
        var assertions = 0;
        void check(bool condition, string text) { assertions++; report(condition, text); }
        var root = Path.Combine(Path.GetFullPath("../../../../../.tmp/atomic-snapshot-replace", AppContext.BaseDirectory), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await Case("replacement policy", PolicyAsync);
        if (OperatingSystem.IsWindows())
        {
            await Case("failed target and temporary", PreservedFailureAsync);
            await Case("store gate and one business commit", StoreAsync);
            await Case("reward snapshot and ledger", RewardAsync);
        }
        Console.WriteLine($"Atomic snapshot replacement: {assertions} assertions; artifacts: {root}");

        async Task Case(string name, Func<Task> run)
        {
            try { await run(); }
            catch (Exception error) { check(false, $"{name}: unexpected {error.GetType().Name} HResult0x{error.HResult:X8}: {error.Message}"); }
        }
        async Task PolicyAsync()
        {
            var moves = 0; var waits = 0; var commits = 0;
            var transient = new AtomicSnapshotReplacement((source, target) =>
            {
                check(source == "snapshot.tmp" && target == "state.json", "every attempt renames the same source and destination");
                if (++moves < 3) throw new Denied();
                commits++;
            }, (delay, token) => { waits++; check(delay == AtomicSnapshotReplacement.RetryDelay, "rename retry has the fixed short delay"); return Task.CompletedTask; });
            if (OperatingSystem.IsWindows())
            {
                var recovered = false;
                try { await transient.ReplaceAsync("snapshot.tmp", "state.json", default); recovered = true; }
                catch (UnauthorizedAccessException) { }
                check(recovered && moves == 3 && waits == 2 && commits == 1,
                    "two transient Windows access-denied renames recover with one commit");
            }
            else
            {
                await ThrowsAsync<UnauthorizedAccessException>(() => transient.ReplaceAsync("snapshot.tmp", "state.json", default));
                check(moves == 1 && waits == 0 && commits == 0, "non-Windows access denial is never retried");
            }

            moves = waits = 0;
            var persistentError = new Denied();
            var persistent = new AtomicSnapshotReplacement((_, _) => { moves++; throw persistentError; },
                (_, _) => { waits++; return Task.CompletedTask; });
            var persistentCaught = await ThrowsAsync<UnauthorizedAccessException>(() => persistent.ReplaceAsync("tmp", "target", default));
            var limit = OperatingSystem.IsWindows() ? AtomicSnapshotReplacement.MaximumAttempts : 1;
            check(ReferenceEquals(persistentCaught, persistentError) && moves == limit && waits == limit - 1,
                "persistent denial stops at the exact attempt limit and rethrows the original exception");
            check(AtomicSnapshotReplacement.RetryDelay * (AtomicSnapshotReplacement.MaximumAttempts - 1) <= TimeSpan.FromMilliseconds(100),
                "the configured total retry wait is bounded to100ms");

            foreach (var error in new Exception[] { new IOException("unobserved sharing violation", unchecked((int)0x80070020)),
                         new Denied(unchecked((int)0x80070020)), new IOException("different exception type", unchecked((int)0x80070005)),
                         new InvalidOperationException("not a rename access denial") })
            {
                moves = waits = 0;
                var other = new AtomicSnapshotReplacement((_, _) => { moves++; throw error; }, (_, _) => { waits++; return Task.CompletedTask; });
                var caught = await ThrowsAsync<Exception>(() => other.ReplaceAsync("tmp", "target", default));
                check(ReferenceEquals(caught, error) && moves == 1 && waits == 0,
                    $"unmatched {error.GetType().Name}/0x{error.HResult:X8} propagates without retry");
            }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); moves = 0;
            var before = new AtomicSnapshotReplacement((_, _) => moves++, (_, token) => Task.Delay(1, token));
            await ThrowsAsync<OperationCanceledException>(() => before.ReplaceAsync("tmp", "target", cancelled.Token));
            check(moves == 0, "a pre-cancelled replacement does not attempt a rename");
            if (OperatingSystem.IsWindows())
            {
                using var during = new CancellationTokenSource(); moves = waits = 0;
                var pending = new AtomicSnapshotReplacement((_, _) => { moves++; throw new Denied(); }, (_, token) =>
                { waits++; during.Cancel(); return Task.FromCanceled(token); });
                var error = await ThrowsAsync<OperationCanceledException>(() => pending.ReplaceAsync("tmp", "target", during.Token));
                check(moves == 1 && waits == 1 && error.CancellationToken == during.Token,
                    "cancellation during the delay stops before another rename");
            }
        }
        async Task PreservedFailureAsync()
        {
            var folder = Path.Combine(root, "persistent"); Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "state.json"); var source = target + ".tmp";
            await File.WriteAllTextAsync(target, "old snapshot"); await File.WriteAllTextAsync(source, "new snapshot");
            var attempts = 0;
            var replace = new AtomicSnapshotReplacement((_, _) => { attempts++; throw new Denied(); }, (_, _) => Task.CompletedTask);
            await ThrowsAsync<UnauthorizedAccessException>(() => replace.ReplaceAsync(source, target, default));
            check(attempts == AtomicSnapshotReplacement.MaximumAttempts && await File.ReadAllTextAsync(target) == "old snapshot"
                    && await File.ReadAllTextAsync(source) == "new snapshot",
                "exhausted rename retries keep the previous target and complete temporary snapshot intact");
            using var cancel = new CancellationTokenSource(); attempts = 0;
            var cancelling = new AtomicSnapshotReplacement((_, _) => { attempts++; throw new Denied(); }, (_, token) =>
            { cancel.Cancel(); return Task.FromCanceled(token); });
            await ThrowsAsync<OperationCanceledException>(() => cancelling.ReplaceAsync(source, target, cancel.Token));
            check(attempts == 1 && await File.ReadAllTextAsync(target) == "old snapshot"
                    && await File.ReadAllTextAsync(source) == "new snapshot",
                "cancelling the retry delay keeps the target unchanged and temporary snapshot available");
        }
        async Task StoreAsync()
        {
            var path = Path.Combine(root, "store", "state.json");
            var armed = false; var attempts = 0;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hashes = new List<string>(); var writeTimes = new List<DateTime>();
            var replacer = new AtomicSnapshotReplacement((source, target) =>
            {
                if (armed)
                {
                    attempts++; hashes.Add(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))));
                    writeTimes.Add(File.GetLastWriteTimeUtc(source));
                    if (attempts == 1) throw new Denied();
                }
                File.Move(source, target, true);
            }, async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); });
            var logger = new SavedLogger();
            var options = new ServerOptions { DataPath = path, SeedTestAccount = false, ScriptPvfPath = "" };
            var store = new JsonGameStore(options, logger, replacer);
            await store.InitializeAsync(); await store.CreateAccountAsync(new("atomicaccount", "fixture-password", OicqCode: "123456"));
            await store.CreateCharacterAsync("atomicaccount", new("AtomicActor"));
            var baselineLogs = logger.Saves; armed = true;
            var grant = store.GrantCeraToAccountAsync("atomicaccount", 100);
            await Task.WhenAny(grant, entered.Task).WaitAsync(TimeSpan.FromSeconds(2));
            if (grant.IsFaulted) await grant;
            check(entered.Task.IsCompleted && !grant.IsCompleted && logger.Saves == baselineLogs,
                "a failed pre-commit rename publishes no completion log or successful business result");
            var reader = store.GetAccountCeraAsync("atomicaccount");
            check(!reader.IsCompleted, "the original store gate remains held throughout the retry wait");
            release.TrySetResult(); await grant;
            check(await reader == 100 && logger.Saves == baselineLogs + 1 && attempts == 2,
                "the grant executes once and its success log occurs only after atomic replacement");
            check(hashes.Distinct().Count() == 1 && writeTimes.Distinct().Count() == 1,
                "all rename attempts use the same already-written snapshot without serializing it again");
            armed = false;
            var disk = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance); await disk.InitializeAsync();
            check(await disk.GetAccountCeraAsync("atomicaccount") == 100, "reopening retains exactly one100-credit business commit");
        }
        async Task RewardAsync()
        {
            var path = Path.Combine(root, "reward", "state.json"); var armed = false; var attempts = 0;
            var hashes = new List<string>(); var writes = new List<DateTime>();
            var replacer = new AtomicSnapshotReplacement((source, target) =>
            {
                if (armed)
                {
                    attempts++; hashes.Add(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))); writes.Add(File.GetLastWriteTimeUtc(source));
                    if (attempts < 3) throw new Denied();
                }
                File.Move(source, target, true);
            }, (_, _) => Task.CompletedTask);
            var options = new ServerOptions { DataPath = path, SeedTestAccount = false, ScriptPvfPath = "" };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, replacer);
            await store.InitializeAsync(); await store.CreateAccountAsync(new("rewardaccount", "fixture-password", OicqCode: "123456"));
            var actor = (await store.CreateCharacterAsync("rewardaccount", new("RewardActor"))).Character!;
            var pickup = Guid.NewGuid();
            var members = new[] { new DungeonInstanceMember(actor.Id, 1, 0, actor.Name, _ => ValueTask.CompletedTask) };
            armed = true;
            var commit = await store.CommitDungeonGoldDistributionAsync(pickup, members, 0, 7, routingSource: 0);
            check(commit.Status == DungeonRewardLedgerStatus.Committed && attempts == 3
                    && hashes.Distinct().Count() == 1 && writes.Distinct().Count() == 1,
                "the reward writer retries only its rename and publishes one committed ledger snapshot");
            if (commit.Status != DungeonRewardLedgerStatus.Committed) return;
            var priorAttempts = attempts;
            var replay = await store.CommitDungeonGoldDistributionAsync(pickup, members, 0, 7, routingSource: 0);
            check(replay.Status == DungeonRewardLedgerStatus.Committed && attempts == priorAttempts,
                "replaying the committed reward never invokes the rename or business write again");
            armed = false;
            var paid = await store.ConsumeDungeonGoldCreditsAsync(actor.Id);
            check(paid?.Gold == actor.Gold + 7 && await store.ConsumeDungeonGoldCreditsAsync(actor.Id) is null,
                "the durably committed reward is credited exactly once after rename recovery");
        }
    }
    private static async Task<T> ThrowsAsync<T>(Func<Task> run) where T : Exception
    {
        try { await run(); }
        catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private sealed class Denied : UnauthorizedAccessException
    { internal Denied(int result = unchecked((int)0x80070005)) : base("Injected atomic rename denial") { HResult = result; } }
    private sealed class SavedLogger : ILogger<JsonGameStore>
    {
        internal int Saves;
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        { if (format(state, error).StartsWith("Saved emulator state", StringComparison.Ordinal)) Saves++; }
    }
}
