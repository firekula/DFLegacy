using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.UpdateCore.Tests;

/// <summary>Opt-in acceptance of real publisher output, using local streams and no game/network execution.</summary>
internal static class LocalBundleTests
{
    private const string ProbePath = "UpdateTestAssets/probe.npk";
    private static readonly JsonSerializerOptions ReportJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private sealed record Bundle(string Directory, VerifiedRelease Release, Dictionary<string, (ResourcePart Part, string Path)> Assets);
    private sealed record PhaseResult(string Phase, int VerifiedFiles, long BytesRead, RequestEvidence[] Requests, string? Message = null);
    private sealed record FileEvidence(string Path, long ExpectedSize, long ActualSize, string ExpectedSha256, string ActualSha256);
    private sealed class RequestEvidence(string release, string asset, string sha256, long size, long offset)
    {
        private long bytes;
        public string Release { get; } = release;
        public string Asset { get; } = asset;
        public string Sha256 { get; } = sha256;
        public long Size { get; } = size;
        public long Offset { get; } = offset;
        public long BytesRead => Interlocked.Read(ref bytes);
        internal void Read(int count) => Interlocked.Add(ref bytes, count);
    }
    internal static async Task<int?> Dispatch(string[] args)
    {
        if (args.Length == 0 || args[0] != "--verify-release-bundles") return null;
        if (args.Length != 4) { Console.Error.WriteLine("Usage: --verify-release-bundles <bundleA> <bundleB> <NEW-isolated-target-root>"); return 2; }
        var phase = "validate-inputs"; string? root = null; BundleProvider? provider = null;
        var phases = new List<PhaseResult>(); var finalFiles = new List<FileEvidence>();
        Bundle? a = null, b = null;
        try
        {
            a = await ReadBundle(args[1]); b = await ReadBundle(args[2]);
            Require(a.Release.Source == b.Release.Source && a.Release.Manifest.Sequence < b.Release.Manifest.Sequence, "A/B must use the same source, architecture and flavor, with increasing sequence.");
            Require(a.Release.Manifest.MinimumSequence == 0 && b.Release.Manifest.MinimumSequence <= a.Release.Manifest.Sequence, "Bundle baselines do not support a fresh A installation followed by B.");
            var probeA = Probe(a.Release.Manifest); var probeB = Probe(b.Release.Manifest);
            Require(probeA.Sha256 != probeB.Sha256, "B must change the dedicated unused probe asset.");
            Require(probeB.Parts[0].Release == b.Release.Tag, "B probe must use its new release's own asset.");
            Require(b.Release.Manifest.Files.Any(f => a.Release.Manifest.Files.Any(old => old.Path == f.Path && old.Size == f.Size && old.Sha256 == f.Sha256))
                && b.Release.Manifest.Files.SelectMany(f => f.Parts).Any(p => p.Release == a.Release.Tag), "Acceptance requires unchanged files and immutable A-part reuse in B.");
            var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[3])); PathPolicy.NoLinks(candidate);
            Require(!Directory.Exists(candidate) && !File.Exists(candidate), "Acceptance target must be a NEW isolated directory.");
            foreach (var bundle in new[] { a, b })
            {
                Require(!candidate.StartsWith(bundle.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Acceptance target cannot be inside either input bundle.");
                Require(!bundle.Release.Manifest.Files.Any(f => f.Path.Equals("acceptance-result.json", StringComparison.OrdinalIgnoreCase)), "Managed assets cannot overwrite the acceptance report.");
            }
            provider = new BundleProvider(a, b); Directory.CreateDirectory(candidate); root = candidate;
            var password = Guid.NewGuid().ToString("N");
            File.WriteAllBytes(Path.Combine(root, "launcher.json"), JsonSerializer.SerializeToUtf8Bytes(new
            {
                Accounts = new[] { new { Username = "local-bundle-fixture", Password = password } },
                Relay = new { TcpPorts = "9000", UdpPorts = "9000", ServerAddress = "127.0.0.1" }, UnknownFixture = 77
            }));
            var engine = new UpdateEngine(root, provider);
            phase = "install-A"; Console.WriteLine($"Local bundle acceptance: {phase} ({a.Release.Source.Architecture}/{a.Release.Source.Flavor})");
            var first = await engine.CheckAsync(); Require(first.NeedsInstall && !first.CanLaunch, "Fresh target must require installation.");
            var staged = await engine.DownloadAsync(first); InstallTransaction.Apply(root, staged);
            await Verify(root, a.Release); Require((await engine.ValidateInstalledAsync()).CanLaunch, "A cannot launch after verified installation.");
            Record(phases, provider, phase, 0, a.Release.Manifest.Files.Length);

            phase = "same-version-zero-resource-requests"; var start = provider.Requests.Count;
            var healthy = await engine.CheckAsync();
            Require(healthy.CanLaunch && !healthy.NeedsInstall && !healthy.IsOffline && provider.Requests.Count == start, "Same-version full verification unexpectedly fetched resources or required repair.");
            Record(phases, provider, phase, start, a.Release.Manifest.Files.Length);

            phase = "repair-only-unused-probe"; Console.WriteLine("Local bundle acceptance: " + phase); start = provider.Requests.Count;
            var probeFile = PathPolicy.Resolve(root, probeA.Path);
            using (var stream = new FileStream(probeFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            { var firstByte = stream.ReadByte(); Require(firstByte >= 0, "Probe cannot be empty."); stream.Position = 0; stream.WriteByte((byte)(firstByte ^ 0x5a)); stream.Flush(true); }
            Require(new FileInfo(probeFile).Length == probeA.Size, "Probe corruption changed its length.");
            var cache = new ContentCache(staged.CacheDirectory); var probeObject = cache.ObjectPath(probeA.Sha256, probeA.Size);
            Require(File.Exists(probeObject), "A did not materialize the probe cache object."); File.Delete(probeObject);
            var repair = await engine.CheckAsync();
            Require(repair.NeedsInstall && repair.Verification!.Problems.Count == 1 && repair.Verification.Problems[0].Path == probeA.Path, "Corrupting the unused probe did not isolate exactly one repair.");
            staged = await engine.DownloadAsync(repair); InstallTransaction.Apply(root, staged); await Verify(root, a.Release);
            var repairRequests = provider.Requests.Skip(start).ToArray();
            Require(repairRequests.Length > 0 && repairRequests.All(r => probeA.Parts.Any(p => p.Release == r.Release && p.Asset == r.Asset && p.Sha256 == r.Sha256)), "Probe repair fetched unrelated assets.");
            Record(phases, provider, phase, start, a.Release.Manifest.Files.Length);

            phase = "discover-B-cancel-after-first-byte"; Console.WriteLine("Local bundle acceptance: " + phase);
            provider.Target = b.Release; var bStart = provider.Requests.Count;
            var next = await engine.CheckAsync(); Require(next.NeedsInstall && next.CanLaunch, "Discovering B must keep verified installed A usable.");
            using (var cancel = new CancellationTokenSource())
            {
                provider.MaximumReadSize = 1;
                try
                {
                    await engine.DownloadAsync(next, new InlineProgress(p => { if (p.Phase == "download" && p.BytesReceived > 0) cancel.Cancel(); }), cancel.Token);
                    throw new InvalidDataException("B download did not stop at the injected cancellation.");
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                finally { provider.MaximumReadSize = 128 * 1024; }
            }
            Require(provider.Requests.Skip(bStart).Sum(r => r.BytesRead) > 0 && !InstallTransaction.HasPending(root), "Cancellation did not exercise an actual partial transfer before installation.");
            Require(new InstallationStore(root).ReadInstalled()?.Release.ManifestSha256 == a.Release.ManifestSha256, "Downloading B overwrote the installed A receipt.");
            Record(phases, provider, phase, bStart, 0);

            phase = "offline-restart-valid-A"; start = provider.Requests.Count; provider.Offline = true;
            var offline = await new UpdateEngine(root, provider).CheckAsync();
            Require(offline.CanLaunch && offline.IsOffline && offline.Installed?.Release.ManifestSha256 == a.Release.ManifestSha256 && offline.Message.Contains("未确认线上最新版本"), "Offline restart did not explicitly validate installed A.");
            Require(provider.Requests.Count == start, "Offline validation attempted a resource transfer.");
            Record(phases, provider, phase, start, a.Release.Manifest.Files.Length, offline.Message);

            phase = "resume-and-install-B"; Console.WriteLine("Local bundle acceptance: " + phase); start = provider.Requests.Count; provider.Offline = false;
            staged = await engine.DownloadAsync(await engine.CheckAsync()); InstallTransaction.Apply(root, staged);
            await Verify(root, b.Release); Require((await engine.ValidateInstalledAsync()).CanLaunch, "B cannot launch after verified installation.");
            var bRequests = provider.Requests.Skip(bStart).ToArray();
            Require(bRequests.Length > 0 && bRequests.All(r => r.Release == b.Release.Tag), "B upgrade fetched an unchanged A-tag asset.");
            Require(provider.Requests.Skip(start).Any(r => r.Offset > 0), "B installation did not resume the saved partial transfer.");
            Require(b.Release.Manifest.Files.SelectMany(f => f.Parts).Where(p => p.Release == a.Release.Tag)
                .All(p => File.Exists(cache.ObjectPath(p.Sha256, p.Size))), "B preparation evicted a selected reusable A transport object.");
            Record(phases, provider, phase, start, b.Release.Manifest.Files.Length);
            using (var config = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "launcher.json"))))
            {
                var json = config.RootElement;
                Require(json.GetProperty("Accounts")[0].GetProperty("Password").GetString() == password
                    && json.GetProperty("Accounts")[0].GetProperty("Username").GetString() == "local-bundle-fixture"
                    && json.GetProperty("UnknownFixture").GetInt32() == 77, "Configuration migration changed credentials or unknown fields.");
                var ports = json.GetProperty("Relay").GetProperty("TcpPorts").GetString()!.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                Require(new[] { 9000, 7110, 7111, 7112 }.All(ports.Contains), "Required/custom TCP ports were not preserved.");
                Require(json.GetProperty("Relay").GetProperty("UdpPorts").GetString()!.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Contains("9000"), "Custom UDP port was not preserved.");
            }
            foreach (var file in b.Release.Manifest.Files)
            {
                var path = PathPolicy.Resolve(root, file.Path); using var stream = File.OpenRead(path);
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
                Require(hash == file.Sha256 && stream.Length == file.Size, "Final report hash differs from the installed target.");
                finalFiles.Add(new(file.Path, file.Size, stream.Length, file.Sha256, hash));
            }
            WriteReport(root, true, "complete", a, b, phases, finalFiles, provider, null);
            Console.WriteLine("Local bundle acceptance passed; report: " + Path.Combine(root, "acceptance-result.json")); return 0;
        }
        catch (Exception ex)
        {
            if (root != null) WriteReport(root, false, phase, a, b, phases, finalFiles, provider, ex.GetType().Name);
            Console.Error.WriteLine($"Local bundle acceptance failed in {phase}: {ex.GetType().Name}: {ex.Message}"); return 1;
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static ManagedFile Probe(ClientManifest manifest)
    {
        var probe = manifest.Files.SingleOrDefault(f => f.Path == ProbePath);
        Require(probe is { Size: > 1 } && probe.Parts.Length == 1 && probe.Parts[0].Sha256 == probe.Sha256 && probe.Parts[0].Size == probe.Size,
            "Each bundle needs a dedicated single-part UpdateTestAssets/probe.npk larger than one byte.");
        return probe!;
    }
    private static async Task Verify(string root, VerifiedRelease release)
    {
        Require((await FileVerifier.VerifyAsync(root, release.Manifest)).IsValid, "Complete installed-file verification failed.");
        Require(!InstallTransaction.HasPending(root), "Installation left a pending transaction.");
    }
    private static void Record(List<PhaseResult> phases, BundleProvider provider, string phase, int first, int verified, string? message = null)
    {
        var requests = provider.Requests.Skip(first).ToArray(); phases.Add(new(phase, verified, requests.Sum(r => r.BytesRead), requests, message));
    }
    private static void WriteReport(string root, bool passed, string phase, Bundle? a, Bundle? b, List<PhaseResult> phases,
        List<FileEvidence> files, BundleProvider? provider, string? failureType)
    {
        var result = new
        {
            schemaVersion = 1, passed, phase, completedUtc = DateTime.UtcNow, transport = "local-bundle-stream",
            remoteSourceVerified = false, published = false, gameExecuted = false, intendedSource = a?.Release.Source,
            bundleA = a == null ? null : new { a.Release.Tag, a.Release.ManifestSha256, files = a.Release.Manifest.Files.Length },
            bundleB = b == null ? null : new { b.Release.Tag, b.Release.ManifestSha256, files = b.Release.Manifest.Files.Length },
            phases, finalFiles = files, requests = provider?.Requests.ToArray(), transferredBytes = provider?.Requests.Sum(r => r.BytesRead),
            configurationPreserved = passed, failureType
        };
        using var stream = new FileStream(Path.Combine(root, "acceptance-result.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, result, ReportJson); stream.Flush(true);
    }
    private static async Task<Bundle> ReadBundle(string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); PathPolicy.NoLinks(directory);
        using var receipt = JsonDocument.Parse(ReadBounded(Path.Combine(directory, "receipt.json"), 64 * 1024 * 1024));
        RejectDuplicates(receipt.RootElement); var json = receipt.RootElement;
        var manifestName = json.GetProperty("manifestAsset").GetString(); Require(ManifestCodec.IsName(manifestName), "Invalid manifest attachment name.");
        var raw = ReadBounded(Path.Combine(directory, manifestName!), ManifestCodec.MaxBytes); var manifest = ManifestCodec.Parse(raw);
        var source = new ReleaseSource(json.GetProperty("provider").GetString()!, json.GetProperty("repository").GetString()!, manifest.Channel, manifest.Architecture, manifest.Flavor);
        Require(source.Provider == "gitee" && source.Channel == "gitee-test" && source.Repository == "firekula/DFLegacy", "Only explicit firekula/DFLegacy Gitee test bundles are accepted.");
        var release = new VerifiedRelease(source, manifest.Version, json.GetProperty("manifestSha256").GetString()!, raw); release.Validate(source);
        Require(manifestName == source.ManifestAssetName && raw.LongLength == json.GetProperty("manifestSize").GetInt64(), "Manifest receipt length or attachment identity differs.");
        var receiptManifest = ManifestCodec.Parse(System.Text.Encoding.UTF8.GetBytes(json.GetProperty("manifest").GetRawText()));
        Require(ManifestCodec.Serialize(receiptManifest).AsSpan().SequenceEqual(ManifestCodec.Serialize(manifest)), "Receipt and raw manifest have different content.");
        var expected = manifest.Files.SelectMany(f => f.Parts).Where(p => p.Release == release.Tag).GroupBy(p => p.Asset).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var inventory = json.GetProperty("assets").EnumerateObject().ToArray();
        Require(inventory.Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected.Keys), "Bundle attachment inventory differs from its manifest.");
        var assets = new Dictionary<string, (ResourcePart, string)>(StringComparer.Ordinal);
        foreach (var entry in inventory)
        {
            var part = expected[entry.Name]; var path = Path.Combine(directory, "assets", entry.Name);
            Require(entry.Value.GetProperty("sha256").GetString() == part.Sha256 && entry.Value.GetProperty("size").GetInt64() == part.Size
                && await FileVerifier.MatchesAsync(path, part.Size, part.Sha256), "Bundle attachment has invalid hash or length: " + entry.Name);
            assets.Add(entry.Name, (part, path));
        }
        return new(directory, release, assets);
    }
    private static byte[] ReadBounded(string path, int limit)
    {
        PathPolicy.NoLinks(path); using var stream = File.OpenRead(path);
        Require(stream.Length > 0 && stream.Length <= limit, "Bundle metadata exceeds its size limit.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in element.EnumerateObject()) { Require(keys.Add(entry.Name), "Duplicate bundle receipt property."); RejectDuplicates(entry.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var entry in element.EnumerateArray()) RejectDuplicates(entry);
    }
    private sealed class InlineProgress(Action<UpdateProgress> action) : IProgress<UpdateProgress> { public void Report(UpdateProgress value) => action(value); }
    private sealed class BundleProvider : IReleaseProvider
    {
        private readonly Dictionary<(string Release, string Asset), (ResourcePart Part, string Path)> assets = [];
        public ReleaseSource Source { get; }
        public string Identity => "local-bundle-stream:" + Source.Identity;
        internal VerifiedRelease Target { get; set; }
        internal bool Offline { get; set; }
        internal int MaximumReadSize { get; set; } = 128 * 1024;
        internal List<RequestEvidence> Requests { get; } = [];
        internal BundleProvider(Bundle a, Bundle b)
        {
            Source = a.Release.Source; Target = a.Release;
            foreach (var bundle in new[] { a, b }) foreach (var entry in bundle.Assets) assets.Add((bundle.Release.Tag, entry.Key), entry.Value);
            foreach (var part in new[] { a, b }.SelectMany(v => v.Release.Manifest.Files).SelectMany(f => f.Parts))
                Require(assets.TryGetValue((part.Release, part.Asset), out var local) && local.Part.Sha256 == part.Sha256 && local.Part.Size == part.Size, "A/B bundles do not supply every referenced immutable resource.");
        }
        public Task<VerifiedRelease?> FindAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); if (Offline) throw new HttpRequestException("Local acceptance injects unavailable source."); return Task.FromResult<VerifiedRelease?>(Target); }
        public Task<HttpResponseMessage> OpenAsync(ResourcePart part, long offset, string? etag, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if (Offline) throw new HttpRequestException("Local acceptance injects unavailable source.");
            Require(assets.TryGetValue((part.Release, part.Asset), out var local) && local.Part.Sha256 == part.Sha256 && local.Part.Size == part.Size, "Unknown local bundle resource requested.");
            Require(offset >= 0 && offset < part.Size || offset == 0 && part.Size == 0, "Local acceptance received invalid resume offset.");
            PathPolicy.NoLinks(local.Path); var file = new FileStream(local.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            file.Position = offset; var evidence = new RequestEvidence(part.Release, part.Asset, part.Sha256, part.Size, offset); Requests.Add(evidence);
            var response = new HttpResponseMessage(offset == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            { Content = new StreamContent(new CountedStream(file, MaximumReadSize, evidence), 128 * 1024) };
            response.Headers.ETag = new EntityTagHeaderValue("\"sha256-" + part.Sha256 + "\""); response.Content.Headers.ContentLength = part.Size - offset;
            if (offset > 0) response.Content.Headers.ContentRange = new(offset, part.Size - 1, part.Size);
            return Task.FromResult(response);
        }
    }
    private sealed class CountedStream(Stream inner, int maximum, RequestEvidence evidence) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) { var read = inner.Read(buffer, offset, Math.Min(count, maximum)); evidence.Read(read); return read; }
        public override int Read(Span<byte> buffer) { var read = inner.Read(buffer[..Math.Min(buffer.Length, maximum)]); evidence.Read(read); return read; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { var read = await inner.ReadAsync(buffer[..Math.Min(buffer.Length, maximum)], token); evidence.Read(read); return read; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
