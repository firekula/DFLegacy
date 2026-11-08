using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace DFLegacy.UpdateCore.Tests;

internal static class TransactionTests
{
    private static Type Required(string name) => typeof(ClientManifest).Assembly.GetType("DFLegacy.UpdateCore." + name) ?? throw new Exception("FAIL: missing " + name);
    private static object? Call(string method, params object?[] arguments)
    {
        try { return Required("InstallTransaction").GetMethod(method)!.Invoke(null, arguments); }
        catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
    }
    private static void Apply(string root, VerifiedRelease release, string cache, IProgress<UpdateProgress>? progress = null) =>
        Call("Apply", root, Activator.CreateInstance(Required("StagedUpdate"), release, cache), progress);
    private static bool Recover(string root) => (bool)Call("Recover", root, null)!;
    private static bool Pending(string root) => (bool)Call("HasPending", root)!;
    private static VerifiedRelease? Read(string root)
    {
        var type = Required("InstallationStore");
        try
        {
            var receipt = type.GetMethod("ReadInstalled")!.Invoke(Activator.CreateInstance(type, root), null);
            return receipt == null ? null : (VerifiedRelease)receipt.GetType().GetProperty("Release")!.GetValue(receipt)!;
        }
        catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
    }
    internal static VerifiedRelease Release(long sequence, params (string Path, string Content)[] files)
    {
        var manifest = ManifestTests.Sample() with { Version = "v4-test-" + sequence, Sequence = sequence, Files = files.Select(f => ManifestTests.Entry(f.Path, f.Content)).ToArray() };
        return Release(manifest);
    }
    private static VerifiedRelease Release(ClientManifest manifest)
    {
        var raw = ManifestCodec.Serialize(manifest);
        return new(new("gitee", "firekula/DFLegacy", "gitee-test", "x86", "test"), manifest.Version, TestRun.Hash(raw), raw);
    }
    private static void Cache(string cache, params string[] contents)
    {
        var store = new ContentCache(cache);
        foreach (var content in contents) { var bytes = TestRun.Bytes(content); File.WriteAllBytes(store.ObjectPath(TestRun.Hash(bytes), bytes.Length), bytes); }
    }
    private static string ConfigPath(string root) => Path.Combine(root, "launcher.json");
    private static string StatePath(string root) => Path.Combine(root, ".dflegacy", "v4");
    private sealed class Progress(Action<UpdateProgress> action) : IProgress<UpdateProgress> { public void Report(UpdateProgress value) => action(value); }
    internal static int? Dispatch(string[] args)
    {
        if (args.Length == 0 || args[0] != "transaction-child") return null;
        var release = JsonSerializer.Deserialize<VerifiedRelease>(File.ReadAllBytes(args[2]), ManifestCodec.Json)!;
        Apply(args[1], release, args[3], new Progress(p =>
        {
            if (p.Phase + ":" + p.Path != args[4]) return;
            File.WriteAllText(args[5], "ready");
            Thread.Sleep(Timeout.Infinite);
        }));
        return 0;
    }
    private static async Task Interrupt(string root, VerifiedRelease release, string cache, string checkpoint)
    {
        var input = Path.Combine(root, "child-input.json"); var ready = Path.Combine(root, "child-ready");
        File.WriteAllBytes(input, JsonSerializer.SerializeToUtf8Bytes(release, ManifestCodec.Json));
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") psi.ArgumentList.Add(typeof(TransactionTests).Assembly.Location);
        foreach (var argument in new[] { "transaction-child", root, input, cache, checkpoint, ready }) psi.ArgumentList.Add(argument);
        using var child = Process.Start(psi)!;
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(ready) && !child.HasExited && DateTime.UtcNow < until) await Task.Delay(20);
        if (!File.Exists(ready))
        {
            if (!child.HasExited) child.Kill(true);
            await child.WaitForExitAsync();
            throw new Exception("transaction child missed checkpoint: " + checkpoint + " " + await child.StandardError.ReadToEndAsync());
        }
        child.Kill(true); await child.WaitForExitAsync(); File.Delete(ready);
        TestRun.Check(Pending(root), "killed test helper retains pending transaction at " + checkpoint);
    }
    internal static async Task Run(string temporary)
    {
        _ = Required("InstallTransaction");
        var estimate = Required("InstallTransaction").GetMethod("EstimateRequiredSpace", BindingFlags.Static | BindingFlags.NonPublic);
        TestRun.Check(estimate != null, "transaction exposes its disk allocation estimate internally");
        long Estimate(long staging, long backup, long temporaryFile) => (long)estimate!.Invoke(null, [staging, backup, temporaryFile])!;
        TestRun.Check(Estimate(200, 150, 200) - Estimate(0, 0, 0) == 550, "space estimate includes staged payloads, all actual backups, and largest simultaneous temporary replacement");
        try { _ = Estimate(long.MaxValue, 1, 1); throw new Exception("space arithmetic overflow accepted"); }
        catch (TargetInvocationException ex) when (ex.InnerException is OverflowException) { TestRun.Check(true, "space estimate fails closed on overflow"); }
        var abandonedRoot = Path.Combine(temporary, "abandoned-preparation");
        var abandoned = Path.Combine(StatePath(abandonedRoot), "transactions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(abandoned); File.WriteAllText(Path.Combine(abandoned, "0.old"), "old evidence"); File.WriteAllText(Path.Combine(abandoned, "0.new"), "incomplete new stage");
        TestRun.Check(!Recover(abandonedRoot) && !Directory.Exists(abandoned), "startup removes abandoned pre-journal staging without reporting pending transaction");
        var root = Path.Combine(temporary, "transaction"); var cache = Path.Combine(temporary, "transaction-cache"); Directory.CreateDirectory(root);
        Cache(cache, "old", "new", "stable", "removed", "custom", "");
        var a = Release(1, ("engine.dll", "old"), ("nested/stable.npk", "stable"), ("old.dll", "removed"), ("custom.dat", "custom"));
        Apply(root, a, cache);
        TestRun.Check(Read(root)?.ManifestSha256 == a.ManifestSha256 && !Pending(root), "first install commits verified receipt without old metadata");
        TestRun.Check(!Directory.EnumerateFiles(StatePath(root), "*.new", SearchOption.AllDirectories).Any(), "completed transaction removes obsolete staging under its own directory");
        File.WriteAllText(ConfigPath(root), """{"Accounts":[{"Password":"secret"}],"Relay":{"TcpPorts":"9999"}}""");
        File.WriteAllText(Path.Combine(root, "player.dll"), "player-owned");
        var c = Release(Release(3, ("engine.dll", "new"), ("nested/stable.npk", "stable"), ("empty.dat", "")).Manifest with
        {
            Retired = [new("old.dll", "delete", 2), new("custom.dat", "unmanage", 2), new("player.dll", "delete", 2)],
            Migrations = [MigrationTests.Migration("unionPorts", "Relay.TcpPorts", "[7110,7111,7112]")]
        });
        // Delete unchanged cache: every transaction must recheck destination, yet never require a download for healthy content.
        File.Delete(new ContentCache(cache).ObjectPath(TestRun.Hash(TestRun.Bytes("stable")), 6));
        Apply(root, c, cache);
        TestRun.Check(!File.Exists(Path.Combine(root, "old.dll")) && File.ReadAllText(Path.Combine(root, "player.dll")) == "player-owned" && File.ReadAllText(Path.Combine(root, "custom.dat")) == "custom", "A to C cumulative retirement touches historical managed files only");
        TestRun.Check((await FileVerifier.VerifyAsync(root, c.Manifest)).IsValid && ConfigurationSatisfied(root, c), "all resource and migration postconditions verified");
        File.WriteAllText(Path.Combine(root, "engine.dll"), "bad"); Apply(root, c, cache);
        TestRun.Check(File.ReadAllText(Path.Combine(root, "engine.dll")) == "new", "same version repair hashes current bytes afresh");
        TestRun.Reject(() => Apply(root, a, cache), "sequence rollback");
        TestRun.Reject(() => Apply(root, Release(c.Manifest with { Commit = new string('b', 40) }), cache), "same sequence different content");
        TestRun.Reject(() => Apply(root, Release(4, ("engine.dll", "new")), cache), "silent omission without retirement");
        TestRun.Reject(() => Apply(root, Release(c.Manifest with { Version = "v4-test-4", Sequence = 4, MinimumSequence = 4 }), cache), "unsupported old baseline");
        var d = Release(c.Manifest with { Version = "v4-test-4", Sequence = 4, Retired = c.Manifest.Retired.Select(r => r.Path == "custom.dat" ? r with { Action = "delete", Sequence = 4 } : r).ToArray() });
        Apply(root, d, cache);
        TestRun.Check(File.Exists(Path.Combine(root, "custom.dat")), "unmanaged file cannot be deleted by later cumulative tombstone");
        var locked = Path.Combine(root, "engine.dll");
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { Apply(root, d, cache); throw new Exception("locked file accepted"); } catch (IOException) { }
        }
        TestRun.Check(!Pending(root), "locked preflight does not start transaction");
        using (var held = (IDisposable)Required("InstallationLease").GetMethod("Acquire")!.Invoke(null, [root])!)
        {
            try { Apply(root, d, cache); throw new Exception("concurrent installation accepted"); } catch (IOException) { }
        }
        TestRun.Check(!Pending(root), "shared directory lease excludes config/install writers");
        TestRun.Reject(() => Apply(root, d with { Source = d.Source with { Provider = "github" } }, cache), "cross-provider installation into known baseline");
        Cache(cache, "stable");
        var receiptRepair = Path.Combine(temporary, "receipt-repair"); Directory.CreateDirectory(receiptRepair); Apply(receiptRepair, a, cache);
        var receiptPath = Path.Combine(StatePath(receiptRepair), "installed.json");
        File.WriteAllText(receiptPath, "{damaged-receipt");
        TestRun.Reject(() => Read(receiptRepair), "malformed installed receipt");
        File.WriteAllText(Path.Combine(receiptRepair, "player.dll"), "player-owned");
        Apply(receiptRepair, c, cache);
        TestRun.Check(Read(receiptRepair)?.ManifestSha256 == c.ManifestSha256 && File.Exists(Path.Combine(receiptRepair, "old.dll")) && File.ReadAllText(Path.Combine(receiptRepair, "player.dll")) == "player-owned", "online repair with unreadable history preserves all unknown retired files");
        var receiptJson = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(receiptPath))!;
        receiptJson["release"]!["manifestSha256"] = new string('0', 64); File.WriteAllText(receiptPath, receiptJson.ToJsonString());
        TestRun.Reject(() => Read(receiptRepair), "receipt does not trust manifest with corrupt pinned hash");
        foreach (var checkpoint in new[] { "prepared:", "install:engine.dll", "install:empty.dat", "install:launcher.json", "commit:installed.json" })
        {
            var crash = Path.Combine(temporary, "crash-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(crash); Cache(cache, "stable"); Apply(crash, a, cache);
            var config = TestRun.Bytes("""{"Accounts":[{"Password":"secret"}]}"""); File.WriteAllBytes(ConfigPath(crash), config);
            await Interrupt(crash, c, cache, checkpoint);
            TestRun.Check(Recover(crash) && !Pending(crash), "recovery clears only fully restored journal");
            TestRun.Check((await FileVerifier.VerifyAsync(crash, a.Manifest)).IsValid && Read(crash)?.ManifestSha256 == a.ManifestSha256 && File.ReadAllBytes(ConfigPath(crash)).SequenceEqual(config) && !File.Exists(Path.Combine(crash, "empty.dat")), "interruption restores old files/config/receipt and removes new file");
        }
        var corrupt = Path.Combine(temporary, "corrupt-original"); Directory.CreateDirectory(corrupt); Apply(corrupt, a, cache);
        File.WriteAllText(Path.Combine(corrupt, "engine.dll"), "BAD");
        await Interrupt(corrupt, c, cache, "install:engine.dll"); Recover(corrupt);
        TestRun.Check(File.ReadAllText(Path.Combine(corrupt, "engine.dll")) == "BAD" && !(await FileVerifier.VerifyAsync(corrupt, a.Manifest)).IsValid, "rollback restores actual corrupt bytes without declaring healthy baseline");
        await Interrupt(corrupt, c, cache, "prepared:");
        using var pendingRecord = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(StatePath(corrupt), "pending.json")));
        var pendingWorkspace = Path.Combine(StatePath(corrupt), "transactions", pendingRecord.RootElement.GetProperty("id").GetString()!);
        var backup = Directory.EnumerateFiles(pendingWorkspace, "*.old").First(); File.WriteAllText(backup, "tampered");
        TestRun.Reject(() => Recover(corrupt), "corrupt backup recovery"); TestRun.Check(Pending(corrupt), "corrupt backup preserves pending recovery evidence");
        var corruptStage = Path.Combine(temporary, "corrupt-stage"); Directory.CreateDirectory(corruptStage); Apply(corruptStage, a, cache);
        await Interrupt(corruptStage, c, cache, "prepared:");
        using var stageRecord = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(StatePath(corruptStage), "pending.json")));
        var stageWorkspace = Path.Combine(StatePath(corruptStage), "transactions", stageRecord.RootElement.GetProperty("id").GetString()!);
        File.WriteAllText(Directory.EnumerateFiles(stageWorkspace, "*.new").First(), "tampered");
        TestRun.Reject(() => Recover(corruptStage), "corrupt staged recovery"); TestRun.Check(Pending(corruptStage), "bad staging preserves pending evidence and blocks launch");
        var invalidConfig = Path.Combine(temporary, "invalid-config"); Directory.CreateDirectory(invalidConfig); Apply(invalidConfig, a, cache);
        File.WriteAllText(ConfigPath(invalidConfig), "{credentials-broken");
        TestRun.Reject(() => Apply(invalidConfig, c, cache), "malformed config does not overwrite credentials");
        TestRun.Check(File.ReadAllText(ConfigPath(invalidConfig)) == "{credentials-broken" && (await FileVerifier.VerifyAsync(invalidConfig, a.Manifest)).IsValid && !Pending(invalidConfig), "malformed config leaves old resources and exact config untouched");
        var pinned = Path.Combine(temporary, "pinned-directory"); Directory.CreateDirectory(pinned); Apply(pinned, a, cache);
        var pinObserved = false;
        Apply(pinned, c, cache, new Progress(p =>
        {
            if (p.Phase != "prepared") return;
            try { Directory.Move(Path.Combine(pinned, "nested"), Path.Combine(pinned, "renamed")); }
            catch (IOException) { pinObserved = true; }
        }));
        TestRun.Check(pinObserved && Directory.Exists(Path.Combine(pinned, "nested")), "live transaction pins unchanged nested directory against rename/reparse swap");
        var stale = Path.Combine(temporary, "changed-since-download"); Directory.CreateDirectory(stale); Apply(stale, a, cache);
        File.WriteAllText(Path.Combine(stale, "nested/stable.npk"), "broken");
        File.Delete(new ContentCache(cache).ObjectPath(TestRun.Hash(TestRun.Bytes("stable")), 6));
        TestRun.Reject(() => Apply(stale, c, cache), "changed destination needs validated cache again");
        TestRun.Check(File.ReadAllText(Path.Combine(stale, "engine.dll")) == "old" && !Pending(stale), "fresh cache preflight precedes any target modifications");
        Cache(cache, "stable");
        var malformed = Path.Combine(temporary, "malformed-journal"); Directory.CreateDirectory(StatePath(malformed)); File.WriteAllText(Path.Combine(StatePath(malformed), "pending.json"), "{}");
        TestRun.Reject(() => Recover(malformed), "invalid journal"); TestRun.Check(Pending(malformed), "invalid journal never silently discarded");
    }
    private static bool ConfigurationSatisfied(string root, VerifiedRelease release)
    {
        var type = Required("ConfigurationMigration");
        return (bool)type.GetMethod("IsSatisfied")!.Invoke(null, [File.ReadAllBytes(ConfigPath(root)), release.Manifest.Migrations])!;
    }
}
