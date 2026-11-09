using System.Net;

namespace DFLegacy.UpdateCore.Tests;

internal static class EngineTests
{
    internal static async Task Run(string scratch)
    {
        var root = Path.Combine(scratch, "engine"); Directory.CreateDirectory(root);
        var source = new ReleaseSource("gitee", "firekula/DFLegacy", "gitee-test", "x86", "test");
        var m = ManifestTests.Sample(); var raw = ManifestCodec.Serialize(m);
        var release = new VerifiedRelease(source, m.Version, TestRun.Hash(raw), raw);
        var provider = new MemoryProvider(source, release);
        foreach (var text in new[] { "abc", "pvf", "dll", "" }) provider.Blobs[TestRun.Hash(TestRun.Bytes(text))] = TestRun.Bytes(text);
        var engine = new UpdateEngine(root, provider);
        File.WriteAllText(Path.Combine(root, "client-package.json"), "old corrupted metadata");
        var check = await engine.CheckAsync();
        TestRun.Check(check.NeedsInstall && check.Verification!.Problems.Count == 4, "online target repairs missing files without old metadata");
        var stage = await engine.DownloadAsync(check);
        InstallTransaction.Apply(root, stage);
        TestRun.Check((await engine.ValidateInstalledAsync()).CanLaunch, "committed installation can launch after complete validation");
        provider.Downloads.Clear();
        File.WriteAllText(Path.Combine(root, "ImagePacks2/a.npk"), "bad");
        check = await engine.CheckAsync();
        TestRun.Check(check.NeedsInstall && check.Verification!.Problems.Single().Path == "ImagePacks2/a.npk", "same version corrupt file requires repair");
        // Remove only this object's cache to prove exactly one remote fetch is needed.
        File.Delete(new ContentCache(stage.CacheDirectory).ObjectPath(m.Files[0].Sha256, m.Files[0].Size));
        stage = await engine.DownloadAsync(check); InstallTransaction.Apply(root, stage);
        TestRun.Check(provider.Downloads.SequenceEqual(new[] { m.Files[0].Sha256 }), "same-version repair fetches only corrupt managed file");
        check = await engine.CheckAsync();
        TestRun.Check(!check.NeedsInstall && check.CanLaunch && !check.IsOffline, "same version fully verified on online check");
        provider.Unavailable = true;
        check = await engine.CheckAsync();
        TestRun.Check(check.CanLaunch && check.IsOffline && check.Message.Contains("未确认线上最新版本"), "valid installed baseline allowed offline with explicit warning");
        File.WriteAllText(Path.Combine(root, "DFLegacy.Client.dll"), "xxx");
        check = await engine.CheckAsync();
        TestRun.Check(!check.CanLaunch, "offline corrupt baseline cannot launch");
        provider.Unavailable = false; stage = await engine.DownloadAsync(await engine.CheckAsync()); InstallTransaction.Apply(root, stage);
        var nextFile = ManifestTests.Entry("ImagePacks2/a.npk", "changed");
        var next = m with { Version = "v4.0.2-test", Sequence = 3, Files = [nextFile, .. m.Files.Skip(1)] };
        raw = ManifestCodec.Serialize(next); provider.Target = new(source, next.Version, TestRun.Hash(raw), raw);
        provider.Blobs[nextFile.Sha256] = TestRun.Bytes("changed");
        check = await engine.CheckAsync();
        TestRun.Check(check.NeedsInstall && check.CanLaunch, "discovering B does not invalidate verified installed A");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try { await engine.DownloadAsync(check, token: cancel.Token); throw new Exception("cancel ignored"); } catch (OperationCanceledException) { }
        }
        provider.Unavailable = true;
        check = await new UpdateEngine(root, provider).CheckAsync();
        TestRun.Check(check.CanLaunch && check.Installed!.Release.Manifest.Version == m.Version, "cancelled B and offline restart still validate A, not B");
        provider.Unavailable = false;
        var before = provider.Target;
        var old = m with { Sequence = 1 }; raw = ManifestCodec.Serialize(old); provider.Target = new(source, old.Version, TestRun.Hash(raw), raw);
        await TestRun.RejectAsync(async () => await engine.CheckAsync(), "remote sequence rollback");
        provider.Target = before;
        // A publisher omitting retirement must never silently drop an old DLL from verification.
        var omitted = next with { Files = next.Files.Where(f => f.Path != "DFLegacy.Client.dll").ToArray() }; raw = ManifestCodec.Serialize(omitted);
        provider.Target = new(source, omitted.Version, TestRun.Hash(raw), raw);
        await TestRun.RejectAsync(async () => await engine.CheckAsync(), "undeclared managed omission");
        var retired=next with {Files=omitted.Files,Retired=[new("DFLegacy.Client.dll","delete",3)]};
        raw=ManifestCodec.Serialize(retired); provider.Target=new(source,retired.Version,TestRun.Hash(raw),raw);
        stage=await engine.DownloadAsync(await engine.CheckAsync()); InstallTransaction.Apply(root,stage);
        var losesHistory=retired with {Version="v4.0.3-test",Sequence=4,Retired=[]};
        raw=ManifestCodec.Serialize(losesHistory); provider.Target=new(source,losesHistory.Version,TestRun.Hash(raw),raw);
        await TestRun.RejectAsync(async()=>await engine.CheckAsync(),"historical tombstone omission rejected before downloading");
    }
    internal sealed class MemoryProvider(ReleaseSource source, VerifiedRelease release) : IReleaseProvider
    {
        public ReleaseSource Source => source;
        public string Identity => source.Identity;
        public VerifiedRelease? Target { get; set; } = release;
        public bool Unavailable { get; set; }
        public Dictionary<string, byte[]> Blobs { get; } = new();
        public List<string> Downloads { get; } = [];
        public Task<VerifiedRelease?> FindAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (Unavailable) throw new HttpRequestException("fixture offline");
            return Task.FromResult(Target);
        }
        public Task<HttpResponseMessage> OpenAsync(ResourcePart part, long offset, string? etag, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Downloads.Add(part.Sha256);
            var body = Blobs[part.Sha256]; var response = new HttpResponseMessage(offset == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = new ByteArrayContent(body[(int)offset..]) };
            if (offset > 0) response.Content.Headers.ContentRange = new(offset, body.Length - 1, body.Length);
            return Task.FromResult(response);
        }
    }
}
