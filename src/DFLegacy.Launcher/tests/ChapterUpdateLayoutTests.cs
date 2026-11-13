using System.Security.Cryptography;
using System.Text.Json;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Updates;
using Core = DFLegacy.UpdateCore;

internal static class ChapterUpdateLayoutTests
{
    internal static void Run(string parent, Action<bool, string> check)
    {
        var root = Path.Combine(parent, "chapter-layout");
        Directory.CreateDirectory(root);
        var content = new Dictionary<string, byte[]>
        {
            [BootstrapLayout.EntryName] = "entry"u8.ToArray(), [BootstrapLayout.AppName] = "app"u8.ToArray(),
            [BootstrapLayout.UpdaterName] = "updater"u8.ToArray(), [BootstrapLayout.DescriptorName] = "descriptor"u8.ToArray(),
            ["DNF.exe"] = "game"u8.ToArray(), ["Script.pvf"] = "pvf"u8.ToArray()
        };
        content[Core.ClientChapterIdentity.FileName] = JsonSerializer.SerializeToUtf8Bytes(
            new Core.ClientChapterIdentity(1, Core.ClientChapterIdentity.ChapterTwo, BootstrapLayout.EntryName,
                [BootstrapLayout.EntryName, "DNF.exe", "Script.pvf"]), Core.ManifestCodec.Json);
        var files = content.Select(p =>
        {
            File.WriteAllBytes(Path.Combine(root, p.Key), p.Value);
            var hash = Convert.ToHexStringLower(SHA256.HashData(p.Value));
            return new Core.ManagedFile(p.Key, p.Value.Length, hash, [new(hash, p.Value.Length, "chapter-test", hash + ".bin")]);
        }).ToArray();
        var manifest = new Core.ClientManifest(4, "chapter-test", new string('a', 40), BootstrapLayout.Architecture, "test", "c2-test", 1, 0, files, [], []);
        var raw = Core.ManifestCodec.Serialize(manifest);
        var release = new Core.VerifiedRelease(new("local", "http://127.0.0.2:48123/c2", "c2-test", BootstrapLayout.Architecture, "test"), manifest.Version,
            Convert.ToHexStringLower(SHA256.HashData(raw)), raw);
        var store = new Core.InstallationStore(root);
        Directory.CreateDirectory(store.StateDirectory);
        File.WriteAllBytes(Path.Combine(store.StateDirectory, "installed.json"),
            JsonSerializer.SerializeToUtf8Bytes(new Core.InstalledReceipt(release, files.Select(file => file.Path).ToArray()), Core.ManifestCodec.Json));
        BootstrapLayout.RequireManifest(manifest);
        check(true, "chapter-two layout accepted without chapter-one DLL");
        check(ClientPackageManifest.LoadAndValidate(root).Version == manifest.Version, "chapter-two test build does not require chapter-one debug.dll");
        File.WriteAllText(Path.Combine(root, "DFLegacy.Client.dll"), "unknown old component");
        try { ClientPackageManifest.LoadAndValidate(root); check(false, "active unknown chapter-one component rejected"); }
        catch (InvalidDataException) { check(true, "active unknown chapter-one component rejected"); }
        File.Delete(Path.Combine(root, "DFLegacy.Client.dll"));
        File.WriteAllText(Path.Combine(root, Core.ClientChapterIdentity.FileName), "{tampered}");
        try { ClientPackageManifest.LoadAndValidate(root); check(false, "chapter identity must match authenticated release"); }
        catch (InvalidDataException) { check(true, "chapter identity must match authenticated release"); }
        File.Delete(Path.Combine(root, Core.ClientChapterIdentity.FileName));
        check(ChapterUpdatePolicy.IsManaged(root), "missing managed descriptor cannot turn the installation into an unmanaged client");
        check(RepairClient.ReadSettings(new(root)).Source == release.Source, "repair still uses the bound chapter-two source when descriptor is missing");
        try { BootstrapLayout.RequireManifest(manifest with { Files = files.Where(f => f.Path != Core.ClientChapterIdentity.FileName).ToArray() }); check(false, "legacy layout still requires original component"); }
        catch (InvalidDataException) { check(true, "legacy layout still requires original component"); }
    }
}
