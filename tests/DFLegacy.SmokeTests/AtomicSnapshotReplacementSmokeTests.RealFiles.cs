using System.Text.Json;
using DFLegacy.Server;

internal static partial class AtomicSnapshotReplacementSmokeTests
{
    internal static async Task RunRealFilesAsync(Action<bool, string> check, string parent)
    {
        var root = Path.Combine(Path.GetFullPath(parent), "real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var reports = new List<object>();
        var payload = Enumerable.Range(0, 4096).ToArray();
        const int iterations = 5_000;
        foreach (var mode in new[] { "async-serialize", "reward-writer" })
        {
            var folder = Path.Combine(root, mode); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "state.json");
            var moves = 0; var snapshots = 0; var completed = 0;
            var current = 0; var denials = new List<object>(); object? terminal = null;
            var replacement = new AtomicSnapshotReplacement((source, target) =>
            {
                moves++;
                try { File.Move(source, target, overwrite: true); }
                catch (UnauthorizedAccessException error)
                {
                    denials.Add(new { Iteration = current, error.Message, HResult = $"0x{error.HResult:X8}", Source = source, Target = target });
                    throw;
                }
            }, Task.Delay);
            var writer = new DungeonRewardSnapshotWriter(replacement);
            var started = DateTimeOffset.UtcNow;
            for (current = 0; current < iterations; current++)
            {
                try
                {
                    snapshots++;
                    if (mode == "async-serialize")
                    {
                        await using (var output = File.Create(path + ".tmp"))
                            await JsonSerializer.SerializeAsync(output, new { iteration = current, payload });
                        await replacement.ReplaceAsync(path + ".tmp", path, default);
                    }
                    else
                    {
                        var snapshot = JsonSerializer.SerializeToUtf8Bytes(new { iteration = current, payload });
                        await writer.WriteAsync(path, snapshot, default);
                    }
                    completed++;
                }
                catch (Exception error)
                {
                    terminal = new { Iteration = current, Type = error.GetType().FullName, error.Message,
                        HResult = $"0x{error.HResult:X8}", Full = error.ToString(),
                        TemporaryRetained = File.Exists(path + ".tmp"), TargetRetained = File.Exists(path) };
                    break;
                }
            }
            int? finalIteration = null;
            if (File.Exists(path))
            {
                using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
                finalIteration = saved.RootElement.GetProperty("iteration").GetInt32();
            }
            check(terminal is null && completed == iterations && snapshots == iterations && finalIteration == iterations - 1,
                $"{mode}: real Windows files complete{iterations} snapshots exactly once, final snapshot correct, transient denials={denials.Count}");
            var result = new { mode, started, Ended = DateTimeOffset.UtcNow, Requested = iterations, completed,
                snapshots, moves, finalIteration, denials, terminal, Target = path };
            reports.Add(result);
            await File.WriteAllTextAsync(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{mode}: snapshots={snapshots}, completed={completed}, moves={moves}, transient denials={denials.Count}, terminal={terminal is not null}");
        }
        await File.WriteAllTextAsync(Path.Combine(root, "summary.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Atomic real-file artifacts: " + root);
    }
}
