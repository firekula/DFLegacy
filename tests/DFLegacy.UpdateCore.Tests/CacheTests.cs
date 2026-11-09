using System.Reflection;

namespace DFLegacy.UpdateCore.Tests;

internal static class CacheTests
{
    private static async Task<object> Prepare(ContentCache cache, ClientManifest target, ManagedFile[] changed, long budget, CancellationToken token = default)
    {
        var method = typeof(ContentCache).GetMethod("PrepareAsync");
        TestRun.Check(method != null, "cache preparation performs retention and free-space preflight");
        try
        {
            var task = (Task)method!.Invoke(cache, [target, changed, budget, token])!;
            await task; return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }
        catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
    }
    private static string Put(ContentCache cache, string text)
    {
        var bytes = TestRun.Bytes(text); var path = cache.ObjectPath(TestRun.Hash(bytes), bytes.Length); File.WriteAllBytes(path, bytes); return path;
    }
    internal static async Task Run(string temporary)
    {
        var cache = new ContentCache(Path.Combine(temporary, "bounded-cache"));
        var selected = Put(cache, "selected"); var oldest = Put(cache, "older-historical"); var recent = Put(cache, "recent");
        File.SetLastWriteTimeUtc(oldest, DateTime.UtcNow.AddDays(-30)); File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddDays(-1));
        var foreign = Path.Combine(cache.DirectoryPath, "player-notes.txt"); File.WriteAllText(foreign, "private");
        var invalidBlob = cache.ObjectPath(new string('0', 64), 6); File.WriteAllText(invalidBlob, "wrong!");
        var manifest = ManifestTests.Sample() with { Files = [ManifestTests.Entry("nested/selected.npk", "selected")] };
        var preparation = await Prepare(cache, manifest, [], 14);
        TestRun.Check(!File.Exists(oldest) && File.Exists(recent) && File.Exists(selected), "oldest verified historical object evicted to budget while target and newest reusable content survive");
        TestRun.Check(File.ReadAllText(foreign) == "private" && File.ReadAllText(invalidBlob) == "wrong!", "foreign names and unverified hash-looking files are never swept up");
        var protectedObjects = (IReadOnlySet<string>)preparation.GetType().GetProperty("ProtectedObjects")!.GetValue(preparation)!;
        TestRun.Check(protectedObjects.Contains(selected), "preparation exposes selected logical and part identities as protected");
        await Prepare(cache, manifest, [], 0);
        TestRun.Check(File.Exists(selected) && !File.Exists(recent), "selected working set may exceed budget without eviction or failure");
        var held = Put(cache, "active-download");
        using (new FileStream(held + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await Prepare(cache, manifest, [], 0);
            TestRun.Check(File.Exists(held), "object owned by an active downloader is not deleted");
        }
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Prepare(cache, manifest, [], 0);
            TestRun.Check(File.Exists(held), "object actively read by materialization is not deleted");
        }
        var resumable = ManifestTests.Entry("resume.npk", "resume-target").Parts[0];
        File.WriteAllText(cache.PartialPath(resumable), "resum"); File.WriteAllText(cache.PartialPath(resumable) + ".json", "{fixture-identity}");
        await Prepare(cache, manifest, [], 0);
        TestRun.Check(File.ReadAllText(cache.PartialPath(resumable)) == "resum" && File.Exists(cache.PartialPath(resumable) + ".json"), "partial data and resume identity remain available across retention");
        var splitBytes = TestRun.Bytes("selectedtail"); var tail = ManifestTests.Entry("tail.dat", "tail").Parts[0];
        var split = new ManagedFile("split.npk", splitBytes.Length, TestRun.Hash(splitBytes), [manifest.Files[0].Parts[0], tail]);
        var splitTarget = manifest with { Files = [split] };
        var splitPreparation = await Prepare(cache, splitTarget, [split], 0);
        var splitProtected = (IReadOnlySet<string>)splitPreparation.GetType().GetProperty("ProtectedObjects")!.GetValue(splitPreparation)!;
        TestRun.Check(splitProtected.Contains(selected) && splitProtected.Contains(cache.ObjectPath(tail.Sha256, tail.Size)) && splitProtected.Contains(cache.ObjectPath(split.Sha256, split.Size)), "split target protects shared old parts plus missing parts and assembled object");
        var required = (long)splitPreparation.GetType().GetProperty("RequiredFreeBytes")!.GetValue(splitPreparation)!;
        TestRun.Check(required >= 4 + 12, "preflight reserves missing tail plus simultaneous assembled logical file");
        var pendingRoot = Path.Combine(temporary, "pending-cache"); var pendingCache = new ContentCache(Path.Combine(pendingRoot, ".dflegacy", "v4", "cache"));
        var pendingObject = Put(pendingCache, "pending-evidence"); File.WriteAllText(Path.Combine(pendingRoot, ".dflegacy", "v4", "pending.json"), "{unresolved");
        await TestRun.RejectAsync(async () => await Prepare(pendingCache, manifest, [], 0), "cache cleanup while recovery journal is pending");
        TestRun.Check(File.Exists(pendingObject), "pending transaction blocks any cache cleanup even with invalid journal");
        if (OperatingSystem.IsWindows())
        {
            var caseAlias = new ContentCache(Path.Combine(pendingRoot, ".DFLEGACY", "V4", "CACHE"));
            await TestRun.RejectAsync(async () => await Prepare(caseAlias, manifest, [], 0), "case alias cannot bypass pending transaction protection");
        }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await Prepare(new ContentCache(Path.Combine(temporary, "empty-cancelled-cache")), manifest, [], 0, cancelled.Token); throw new Exception("empty cache preflight ignored cancellation"); }
            catch (OperationCanceledException) { TestRun.Check(true, "cancellation is honored before retention even for empty cache"); }
        }
        var outside = Path.Combine(temporary, "outside-cache"); Directory.CreateDirectory(outside); var outsideFile = Path.Combine(outside, "player.txt"); File.WriteAllText(outsideFile, "keep-me");
        var link = cache.ObjectPath(new string('c', 64), 7);
        try { Directory.CreateSymbolicLink(link, outside); }
        catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314)
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            start.Arguments = $"/d /c mklink /J \"{link}\" \"{outside}\"";
            using var process = System.Diagnostics.Process.Start(start)!; await process.WaitForExitAsync();
            TestRun.Check(process.ExitCode == 0, "unprivileged cache junction fixture created");
        }
        await Prepare(cache, manifest, [], 0);
        TestRun.Check(Directory.Exists(link) && File.ReadAllText(outsideFile) == "keep-me", "hash-looking reparse entry and outside player content remain untouched");
        TestRun.Reject(() => new ContentCache(link), "reparse cache root"); Directory.Delete(link);
        var available = new DriveInfo(Path.GetPathRoot(cache.DirectoryPath)!).AvailableFreeSpace;
        var partCount = checked((int)(available / ManifestCodec.PartLimit + 2));
        TestRun.Check(partCount <= 100000, "disk exhaustion fixture fits manifest bounds without allocating disk");
        var giantPart = new ResourcePart(new string('a', 64), ManifestCodec.PartLimit, "v4-test", "large-part.bin");
        var giant = new ManagedFile("huge.npk", checked(partCount * ManifestCodec.PartLimit), new string('b', 64), Enumerable.Repeat(giantPart, partCount).ToArray());
        try { await Prepare(cache, manifest with { Files = [giant] }, [giant], 0); throw new Exception("insufficient free space accepted"); }
        catch (IOException ex) { TestRun.Check(ex.Message.Contains("空间"), "real-volume free-space preflight rejects impossible assembly before network or disk allocation"); }
    }
}
