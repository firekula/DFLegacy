using System.Net;

namespace DFLegacy.UpdateCore.Tests;

internal static class DownloadTests
{
    internal static async Task Run(string root)
    {
        var bytes = Enumerable.Range(0, 400000).Select(i => (byte)(i % 251)).ToArray();
        var part = new ResourcePart(TestRun.Hash(bytes), bytes.Length, "v4-test", "resource.bin");
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var interrupted = true; var ignoreRange = false; var range416 = false; var badRange = false; var html = false; var corrupt = false; var delay = 0; var failures = 0;
        await using var server = new TestHttpServer(request =>
        {
            if (failures-- > 0) return new(503, [], new() { ["Retry-After"] = "0" });
            if (interrupted) return new(200, bytes, TruncateAt: 100000);
            if (html) return new(200, TestRun.Bytes("<html>login</html>"), new() { ["Content-Type"] = "text/html" });
            if (range416) return new(416, [], new() { ["Content-Range"] = $"bytes */{bytes.Length}" });
            if (request.Headers.TryGetValue("Range", out var range) && !ignoreRange)
            {
                var offset = int.Parse(range[6..].TrimEnd('-'));
                return new(206, bytes[offset..], new() { ["Content-Range"] = $"bytes {(badRange ? offset + 1 : offset)}-{bytes.Length - 1}/{bytes.Length}" });
            }
            return new(200, corrupt ? new byte[bytes.Length] : bytes, DelayBodyMs: delay);
        });
        var cache = new ContentCache(Path.Combine(root, "cache"));
        var options = new DownloadOptions(MaxAttempts: 1, IdleTimeout: TimeSpan.FromSeconds(2));
        ResourceDownloader New() => new(new LocalTransport(http, server.Origin), options);
        try { await New().FetchAsync(part, cache); throw new Exception("truncated body accepted"); }
        catch (IOException) { }
        TestRun.Check(File.Exists(cache.PartialPath(part)) && new FileInfo(cache.PartialPath(part)).Length == 100000, "interruption retains flushed partial bytes");
        interrupted = false;
        var result = await New().FetchAsync(part, cache);
        TestRun.Check(File.ReadAllBytes(result).SequenceEqual(bytes), "fresh downloader resumes to exact content");
        TestRun.Check(server.Requests.Last().Headers["Range"] == "bytes=100000-" && server.Requests.Last().Headers["If-Range"] == "\"fixture-v1\"", "206 resumes with saved validator");
        var requests = server.Requests.Count; await New().FetchAsync(part, cache);
        TestRun.Check(requests == server.Requests.Count, "verified content reused without GET");
        File.Delete(result); interrupted = true;
        try { await New().FetchAsync(part, cache); } catch (IOException) { }
        interrupted = false; ignoreRange = true;
        result = await New().FetchAsync(part, cache);
        TestRun.Check(File.ReadAllBytes(result).SequenceEqual(bytes), "server ignoring Range restarts file without appending");
        ignoreRange = false; File.Delete(result); interrupted = true;
        try { await New().FetchAsync(part, cache); } catch (IOException) { }
        interrupted = false; badRange = true;
        await TestRun.RejectAsync(() => New().FetchAsync(part, cache), "wrong Content-Range");
        badRange = false; range416 = true;
        try { await New().FetchAsync(part, cache); } catch (IOException) { }
        TestRun.Check(!File.Exists(cache.PartialPath(part)) || new FileInfo(cache.PartialPath(part)).Length == 0, "416 incomplete partial restarted safely");
        range416 = false; html = true;
        await TestRun.RejectAsync(() => New().FetchAsync(part, cache), "HTML login as resource");
        html = false; corrupt = true;
        await TestRun.RejectAsync(() => New().FetchAsync(part, cache), "wrong content hash");
        TestRun.Check(!File.Exists(cache.ObjectPath(part.Sha256, part.Size)), "bad hash not promoted");
        corrupt = false; failures = 2;
        result = await new ResourceDownloader(new LocalTransport(http, server.Origin), new DownloadOptions(MaxAttempts: 4, RetryDelay: TimeSpan.Zero)).FetchAsync(part, cache);
        TestRun.Check(File.ReadAllBytes(result).SequenceEqual(bytes), "transient 503 retries finish");
        File.Delete(result); delay = 1000;
        try { await new ResourceDownloader(new LocalTransport(http, server.Origin), new DownloadOptions(MaxAttempts: 1, IdleTimeout: TimeSpan.FromMilliseconds(100))).FetchAsync(part, cache); throw new Exception("stall accepted"); }
        catch (TimeoutException) { TestRun.Check(true, "idle timeout distinguished"); }
        using (var cancel = new CancellationTokenSource(100))
        {
            try { await New().FetchAsync(part, cache, token: cancel.Token); throw new Exception("cancel ignored"); }
            catch (OperationCanceledException) { TestRun.Check(cancel.IsCancellationRequested, "explicit cancellation preserved"); }
        }
        delay = 0;
        var empty = new ResourcePart(ManifestTests.EmptyHash, 0, "v4-test", "empty.bin");
        await using var emptyServer = new TestHttpServer(_ => new(200, []));
        var emptyPath = await new ResourceDownloader(new LocalTransport(http, emptyServer.Origin)).FetchAsync(empty, cache);
        TestRun.Check(new FileInfo(emptyPath).Length == 0, "empty resource verified");
        // Logical file construction reuses independent cached parts and verifies the final file hash.
        var tail = TestRun.Bytes("tail");
        var tailPart = new ResourcePart(TestRun.Hash(tail), tail.Length, "v4-test", "tail.bin");
        await using var tailServer = new TestHttpServer(_ => new(200, tail));
        await New().FetchAsync(part, cache);
        var combined = bytes.Concat(tail).ToArray();
        var file = new ManagedFile("large.npk", combined.Length, TestRun.Hash(combined), [part, tailPart]);
        var logical = await cache.MaterializeAsync(file, new ResourceDownloader(new LocalTransport(http, tailServer.Origin)));
        TestRun.Check(File.ReadAllBytes(logical).SequenceEqual(combined) && tailServer.Requests.Count == 1, "split transport reuses completed volumes and validates assembly");
    }
}
