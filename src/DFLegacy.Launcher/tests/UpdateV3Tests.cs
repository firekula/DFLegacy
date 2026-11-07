using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Updates;

internal static class UpdateV3Tests
{
    private const string Pvf = "Script.pvf";
    private const string Bridge = "v0.1.5";
    private const string Final = "v0.1.6";
    private static readonly byte[] NewPvf = "release PVF data"u8.ToArray();
    private static readonly string[] ProtectedFiles = ["Config.ini", "launcher.json", "ijl15.dll", "DNF.exe",
        "player.NPK", "ImagePacks2/player.NPK", "Logs/DFLegacy/launcher.log", "Logs/DFLegacy/legacy/rs_log.txt", "unrelated.json", "notes.txt"];

    private static string Asset(int format, string architecture, string flavor = "normal")
        => $"DFLegacy-client-update-v{format}{(flavor == "test" ? "-test" : "")}-win-{architecture}.zip";

    private static byte[] Package(string version, string? pvfPath, bool includePvfEntry = true, bool corruptPvfEntry = false)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["DFLegacy.Launcher.exe"] = Encoding.UTF8.GetBytes("launcher " + version),
            ["DFLegacy.Client.dll"] = Encoding.UTF8.GetBytes("client " + version)
        };
        if (pvfPath != null) files.Add(pvfPath, NewPvf);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2, version, commit = new string('a', 40), flavor = "normal",
            files = files.Select(file => new { path = file.Key, size = file.Value.Length, sha256 = UpdateTransportTests.Hash(file.Value) })
        });
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Write(string path, byte[] bytes) { using var entry = archive.CreateEntry(path).Open(); entry.Write(bytes); }
            Write("update-manifest.json", manifest);
            foreach (var (path, bytes) in files)
                if (path != pvfPath || includePvfEntry)
                    Write(path, path == pvfPath && corruptPvfEntry ? "corrupt PVF data"u8.ToArray() : bytes);
        }
        return output.ToArray();
    }

    private static UpdateRelease Release(long id, string tag, string name, byte[] zip)
        => new(id, tag, DateTimeOffset.Parse(id == 105 ? "2026-11-01T00:00:00Z" : "2026-11-02T00:00:00Z"), false,
            new Uri($"https://github.com/firekula/DFLegacy/releases/download/{tag}/{name}"), zip.Length, UpdateTransportTests.Hash(zip));

    private static byte[] Feed(params UpdateRelease[] releases)
        => JsonSerializer.SerializeToUtf8Bytes(releases.GroupBy(release => release.Id).Select(group =>
        {
            var release = group.First();
            return new
            {
                id = release.Id, tag_name = release.Version, published_at = release.PublishedAt, draft = false, prerelease = false,
                assets = group.Select(asset => new { name = asset.DownloadUrl.Segments[^1], size = asset.Size,
                    digest = "sha256:" + asset.Sha256, browser_download_url = asset.DownloadUrl })
            };
        }));

    internal static async Task Run(string root, Action<bool, string> check)
    {
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "x86" : "x64";
        var bridgeZip = Package(Bridge, null);
        var finalZip = Package(Final, Pvf);
        var bridge = Release(105, Bridge, Asset(2, arch), bridgeZip);
        var final = Release(106, Final, Asset(3, arch), finalZip);
        var releases = Feed(bridge);
        using var http = new HttpClient(new UpdateTransportTests.Handler((request, _) =>
        {
            var response = request.RequestUri!.Host == "api.github.com" ? releases
                : request.RequestUri == bridge.DownloadUrl ? bridgeZip
                : request.RequestUri == final.DownloadUrl ? finalZip
                : throw new InvalidOperationException("Unexpected update request");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(response) });
        }));

        var target = UpdateV2Tests.Installation(root);
        using var session = new UpdateSession(target, new LauncherOptions(), http);
        var selected = await session.CheckAsync(0, CancellationToken.None);
        check(selected == bridge, "V3-capable launcher checks the V2 bridge from the public feed");
        if (selected != bridge) return;
        var progress = new Progress<UpdateProgress>();
        var preparedBridge = await session.DownloadAsync(selected, root, progress, CancellationToken.None);
        check(UpdateTransaction.Apply(target, preparedBridge, _ => true).Success
            && File.ReadAllText(Path.Combine(target, Pvf)) == "preserve Script.pvf"
            && session.ReadState()?.ReleaseId == 105, "V2 bridge advances the release ID and preserves the original PVF");

        releases = Feed(final, bridge);
        selected = await session.CheckAsync(session.ReadState()!.ReleaseId, CancellationToken.None);
        check(selected == final, "public feed selects a V3 release after installing the distinct V2 bridge");
        if (selected != final) return;
        var prepared = await session.DownloadAsync(selected, root, progress, CancellationToken.None);
        var installed = UpdateTransaction.Apply(target, prepared, _ => true);
        check(installed.Success && installed.BackupDirectory != null
            && File.ReadAllText(Path.Combine(installed.BackupDirectory, Pvf)) == "preserve Script.pvf"
            && File.ReadAllBytes(Path.Combine(target, Pvf)).SequenceEqual(NewPvf), "V3 installation replaces and backs up the existing untracked PVF");
        if (!installed.Success) return;
        check(ClientPackageManifest.LoadAndValidate(target).Files.Any(file => file.Path == Pvf)
            && session.ReadState()?.ReleaseId == 106 && await session.CheckAsync(106, CancellationToken.None) == null,
            "V3 records the installed PVF and release floor");
        check(await session.CheckAsync(0, CancellationToken.None) == null, "an already managed V3 PVF can use matching-version adoption");
        CheckProtected(target, check);

        foreach (var architecture in new[] { "x64", "x86" })
        foreach (var flavor in new[] { "normal", "test" })
        {
            var expected = Asset(3, architecture, flavor);
            using var names = UpdateTransportTests.Client(Feed(
                Release(106, Final, Asset(2, architecture, flavor), bridgeZip),
                Release(106, Final, Asset(3, "x64"), finalZip),
                Release(106, Final, Asset(3, "x86"), finalZip),
                Release(106, Final, Asset(3, "x64", "test"), finalZip),
                Release(106, Final, Asset(3, "x86", "test"), finalZip)));
            var slot = await new GitHubUpdateClient(names, "firekula/DFLegacy", flavor: flavor, architecture: architecture).CheckAsync();
            check(slot?.DownloadUrl.Segments[^1] == expected, "GitHub prefers exact V3 asset " + expected);
        }

        foreach (var (format, pvf, zipEntry, reason) in new[]
        {
            (2, (string?)Pvf, true, "V2 cannot declare Script.pvf"),
            (3, (string?)null, false, "V3 requires Script.pvf in the manifest"),
            (3, (string?)Pvf, false, "V3 requires Script.pvf bytes in the ZIP"),
            (3, (string?)"script.pvf", true, "V3 requires exact root Script.pvf spelling"),
            (3, (string?)"data/Script.pvf", true, "V3 does not allow nested PVF paths"),
            (3, (string?)"Config.ini", true, "V3 rejects protected files")
        })
        {
            var invalid = Package(Final, pvf, zipEntry);
            using var download = UpdateTransportTests.Client(invalid);
            var client = new GitHubUpdateClient(download, "firekula/DFLegacy", architecture: arch);
            try { await client.DownloadAsync(Release(106, Final, Asset(format, arch), invalid), root); check(false, reason); }
            catch (InvalidDataException) { check(true, reason); }
        }
        var corrupt = Package(Final, Pvf, corruptPvfEntry: true);
        using (var download = UpdateTransportTests.Client(corrupt))
        {
            var before = Directory.GetDirectories(root, "dflegacy-update-*").Length;
            var client = new GitHubUpdateClient(download, "firekula/DFLegacy", architecture: arch);
            try { await client.DownloadAsync(Release(106, Final, Asset(3, arch), corrupt), root); check(false, "V3 rejects a PVF that disagrees with the manifest hash"); }
            catch (InvalidDataException) { check(true, "V3 rejects a PVF that disagrees with the manifest hash"); }
            check(Directory.GetDirectories(root, "dflegacy-update-*").Length == before, "rejected V3 download removes staged PVF bytes");
        }

        var matching = UpdateV2Tests.Installation(root);
        foreach (var file in prepared.Manifest.Files)
            File.Copy(Path.Combine(prepared.Directory, file.Path), Path.Combine(matching, file.Path), true);
        UpdateV2Tests.InstallMetadata(matching, version: Final);
        using var matchingSession = new UpdateSession(matching, new LauncherOptions(), http);
        check(await matchingSession.CheckAsync(0, CancellationToken.None) == final && matchingSession.ReadState() == null,
            "same-version Manager package without a managed PVF still downloads V3");
        check(!matchingSession.Adopt(prepared), "matching PVF bytes still require installing V3 metadata when the PVF is untracked");

        foreach (var original in new[] { "original", "", null })
        {
            var recovering = UpdateV2Tests.Installation(root);
            var pvf = Path.Combine(recovering, Pvf);
            if (original == null) File.Delete(pvf);
            else File.WriteAllText(pvf, original);
            var metadata = Path.Combine(recovering, "client-package.json");
            var before = File.ReadAllBytes(metadata);
            using (var locked = new FileStream(metadata, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var failed = UpdateTransaction.Apply(recovering, prepared, _ => true);
                check(!failed.Success && failed.BackupDirectory != null && File.Exists(Path.Combine(failed.BackupDirectory, "receipt.json"))
                    && OriginalRestored(pvf, original), "metadata commit failure rolls back the V3 PVF for original state " + (original ?? "missing"));
            }
            var applied = UpdateTransaction.Apply(recovering, prepared, _ => true);
            check(applied.Success && File.ReadAllBytes(pvf).SequenceEqual(NewPvf), "V3 can install over original PVF state " + (original ?? "missing"));
            if (!applied.Success) continue;
            // Model power loss after writing the new state and before removing the durable pending receipt.
            var pending = Path.Combine(recovering, ".dflegacy", "pending.json");
            File.Copy(Path.Combine(applied.BackupDirectory!, "receipt.json"), pending);
            var recovery = UpdateTransaction.InspectRecovery(recovering);
            check(recovery != null && UpdateTransaction.RecoverAndRestart(recovering, recovery,
                UpdateSafety.Hash(Path.Combine(recovering, "DFLegacy.Launcher.exe")), _ => true).Success
                && OriginalRestored(pvf, original) && File.ReadAllBytes(metadata).SequenceEqual(before)
                && new UpdateInstaller(recovering).ReadState() == null && !File.Exists(pending),
                "startup recovery restores the original PVF, metadata and release state for " + (original ?? "missing"));
            CheckProtected(recovering, check);
        }
        var laterV2 = preparedBridge with { Release = preparedBridge.Release with { Id = 107 } };
        var v2AfterV3 = UpdateTransaction.Apply(target, laterV2, _ => true);
        check(v2AfterV3.Success && v2AfterV3.BackupDirectory != null
            && !File.Exists(Path.Combine(v2AfterV3.BackupDirectory, Pvf)) && File.ReadAllBytes(Path.Combine(target, Pvf)).SequenceEqual(NewPvf),
            "V2 can apply to a V3-managed installation while leaving the PVF outside the transaction");
        if (v2AfterV3.Success)
        {
            var pending = Path.Combine(target, ".dflegacy", "pending.json");
            File.Copy(Path.Combine(v2AfterV3.BackupDirectory!, "receipt.json"), pending);
            var recovery = UpdateTransaction.InspectRecovery(target);
            check(recovery != null && UpdateTransaction.RecoverAndRestart(target, recovery,
                UpdateSafety.Hash(Path.Combine(target, "DFLegacy.Launcher.exe")), _ => true).Success
                && ClientPackageManifest.LoadAndValidate(target).HasScriptPvf && File.ReadAllBytes(Path.Combine(target, Pvf)).SequenceEqual(NewPvf)
                && session.ReadState()?.ReleaseId == 106, "V2 rollback restores V3 metadata and validates the untouched PVF");
        }
        Console.WriteLine("Update V3 tests completed");
    }

    private static bool OriginalRestored(string pvf, string? original)
        => original == null ? !File.Exists(pvf) : File.ReadAllText(pvf) == original;

    private static void CheckProtected(string target, Action<bool, string> check)
    {
        foreach (var path in ProtectedFiles)
            check(File.ReadAllText(Path.Combine(target, path)) == "preserve " + path, "V3 preserves " + path);
    }
}
