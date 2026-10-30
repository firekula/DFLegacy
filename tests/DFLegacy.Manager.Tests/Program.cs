using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Manager;

var failures = new List<string>();
var checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) { failures.Add(message); Console.Error.WriteLine("FAIL: " + message); }
}
void Reject(Action action, string message)
{
    try { action(); Check(false, message); }
    catch (ArgumentException) { Check(true, message); }
    catch (InvalidOperationException) { Check(true, message); }
    catch (InvalidDataException) { Check(true, message); }
}

var scratch = Path.Combine(Path.GetTempPath(), "dflegacy manager tests " + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    var paths = new ManagerPaths(scratch, Path.Combine(scratch, "persistent data"));
    Directory.CreateDirectory(Path.Combine(paths.Root, "templates"));
    File.WriteAllText(Path.Combine(paths.Root, "templates", "server.json"), """
        {"Keep":{"Nested":42},"DFLegacy":{"UnknownFuture":{"retain":123},"SeedTestAccount":false}}
        """);
    var config = new ConfigurationStore(paths, new Dictionary<string, string?>());
    var original = config.Read().Saved;
    Check(Path.IsPathFullyQualified(original.ScriptPvfPath), "PVF resolves independently of current working directory");
    Check(Path.GetDirectoryName(paths.ServerConfig) == Path.Combine(paths.Data, "config"), "user config lives in the selected data directory");
    var edited = original with { AdvertisedHost = "game.example.test", AccountServiceUrl = "https://accounts.example.test", ChannelName = "测试频道", MonsterExperienceMultiplier = 2.5 };
    config.Save(edited);
    var savedDocument = JsonNode.Parse(File.ReadAllText(paths.ServerConfig))!;
    Check(savedDocument["Keep"]!["Nested"]!.GetValue<int>() == 42 && savedDocument["DFLegacy"]!["UnknownFuture"]!["retain"]!.GetValue<int>() == 123,
        "editing basic fields retains unknown future configuration");
    Check(savedDocument["DFLegacy"]!["SeedTestAccount"]!.GetValue<bool>() == false, "managed deployments do not seed public test credentials");
    Check(Directory.GetFiles(paths.Backups).Length >= 1, "configuration edits leave a recoverable backup");
    var loadedAgain = new ConfigurationStore(paths, new Dictionary<string, string?>()).Read().Saved;
    Check(loadedAgain.ChannelName == "测试频道" && loadedAgain.MonsterExperienceMultiplier == 2.5, "restarting manager does not replace user configuration");
    Reject(() => config.Save(edited with { EntrancePort = 0 }), "zero listening port is rejected");
    Reject(() => config.Save(edited with { AdminPort = edited.EntrancePort }), "colliding TCP listeners are rejected before starting");
    Reject(() => config.Save(edited with { AdvertisedHost = "0.0.0.0" }), "wildcard is not advertised to players");
    Reject(() => config.Save(edited with { BindHost = "example.test" }), "listening interface must be a literal IP");
    Reject(() => config.Save(edited with { MonsterExperienceMultiplier = double.NaN }), "non-finite multipliers cannot enter configuration");
    Reject(() => config.Save(edited with { AccountServiceUrl = "file:///private/secret" }), "account API URL is limited to HTTP and HTTPS");
    var overridden = new ConfigurationStore(paths, new Dictionary<string, string?> { ["DFLegacy__Admin__Port"] = "19081" });
    Check(overridden.Read().Effective.AdminPort == 19081 && overridden.Read().Saved.AdminPort == 8081,
        "effective environment settings and on-disk settings are shown separately");
    Reject(() => overridden.Save(edited with { AdminPort = 29081 }), "editing an environment-controlled field is explicitly rejected");
    var relayConfiguration = config.Save(edited with { RelayEnabled = true });
    Check(relayConfiguration.Saved.AdvertisedHost == "game.example.test" && relayConfiguration.Effective.GameplayAdvertisedHost == "127.0.0.1"
        && config.ReadServerDocument()["DFLegacy"]!["GameplayDatagram"]!["AdvertisedHost"]!.GetValue<string>() == "127.0.0.1",
        "IPv6 relay retains the real destination while advertising the client-local relay to the game protocol");
    config.Save(edited);
    Reject(() => config.Save(edited with { AccountServiceUrl = "https://accounts.example.test/%s" }), "embedded client formatter cannot receive percent directives in page URLs");
    Reject(() => config.Save(edited with { AccountServiceUrl = "http://0.0.0.0:8081" }), "account registration cannot target a wildcard address");
    var relayOverride = new ConfigurationStore(paths, new Dictionary<string, string?> { ["DFLegacy__GameplayDatagram__AdvertisedHost"] = "2001:db8::42" });
    Reject(() => relayOverride.Save(relayOverride.Read().Saved with { RelayEnabled = true }), "environment override cannot silently break the relay's indirect loopback setting");
    var numericName = new ConfigurationStore(paths, new Dictionary<string, string?> { ["DFLegacy__Channel__Name"] = "12345" });
    try { Check(numericName.Read().Effective.ChannelName == "12345", "numeric-looking string environment values keep their configured string type"); }
    catch (InvalidDataException) { Check(false, "numeric-looking string environment values keep their configured string type"); }
    var advanced = config.ReadServerDocument();
    advanced["DFLegacy"]!["Entrance"]!["AdditionalPorts"] = new JsonArray(7001, 8080, 9001);
    advanced["DFLegacy"]!["CharacterDatagram"]!["Host"] = "::";
    File.WriteAllText(paths.ServerConfig, advanced.ToJsonString());
    config.Save(config.Read().Saved with { ChannelName = "only-name-changed" });
    Check(config.ReadServerDocument()["DFLegacy"]!["Entrance"]!["AdditionalPorts"]!.ToJsonString() == "[7001,8080,9001]",
        "ordinary config edits preserve unexposed additional entrance ports");
    Check(config.ReadServerDocument()["DFLegacy"]!["CharacterDatagram"]!["Host"]!.GetValue<string>() == "::",
        "ordinary config edits preserve an advanced per-protocol listening interface");
    var changedPorts = config.Read().Saved with { ChannelPort = 17001, AdditionalEntrancePort = 18080 };
    config.Save(changedPorts);
    Check(config.ReadServerDocument()["DFLegacy"]!["Entrance"]!["AdditionalPorts"]!.ToJsonString() == "[17001,18080,9001]",
        "editing visible ports updates their entries and retains extra listeners");
    config.Save(edited);

    var identity = new ManagerIdentity(paths);
    var bootstrap = identity.SetupToken;
    Reject(() => identity.CompleteSetup("wrong", "operator", "A strong password 123!"), "setup requires possession of the bootstrap token");
    identity.CompleteSetup(bootstrap, "operator", "A strong password 123!");
    Check(identity.IsConfigured && identity.VerifyLogin("operator", "A strong password 123!", "source-one"), "setup creates independent management credentials");
    Check(!identity.VerifyLogin("operator", "incorrect", "source-one"), "an incorrect password does not authenticate");
    Reject(() => identity.CompleteSetup(bootstrap, "attacker", "Another password 123!"), "consumed setup token cannot replace the administrator");
    Check(!File.ReadAllText(paths.IdentityFile).Contains("A strong password", StringComparison.Ordinal), "management passwords are stored only as salted hashes");
    Check(!File.Exists(paths.SetupTokenFile), "bootstrap plaintext token is removed after successful setup");
    var restoredIdentity = new ManagerIdentity(paths);
    Check(restoredIdentity.VerifyLogin("operator", "A strong password 123!", "source-two"), "management login survives restart while game service is stopped");
    for (var i = 0; i < 10; i++) restoredIdentity.VerifyLogin("operator", "incorrect", "noisy-source");
    Check(!restoredIdentity.VerifyLogin("operator", "A strong password 123!", "noisy-source"), "a rate-limited source cannot bypass the failed-login budget");

    Directory.CreateDirectory(paths.AssetsDirectory);
    var assetFiles = new Dictionary<string, string>
    {
        ["launcher/DFLegacy.Launcher.exe"] = "fake self-contained launcher host",
        ["launcher/DFLegacy.Launcher.dll"] = "fake managed launcher",
        ["launcher/DFLegacy.Launcher.deps.json"] = "{}",
        ["launcher/DFLegacy.Launcher.runtimeconfig.json"] = "{}",
        ["launcher/coreclr.dll"] = "fake runtime marker",
        ["launcher/launcher.json"] = "{\"Account\":\"private-user\",\"Password\":\"do not export\"}",
        ["launcher/Config.ini"] = "[Features]\nEnableDebugOverlay=1\n[Network]\nClientServiceUrl=http://old-host\n",
        ["launcher/ijl15.dll"] = "native patch",
        ["patch/ijl15.dll"] = "native patch",
        ["patch/Config.ini"] = "[Resolution]\nWidth=800\nHeight=600\n[Features]\nEnableDebugOverlay=0\n"
    };
    foreach (var (name, body) in assetFiles)
    {
        var target = Path.Combine(paths.AssetsDirectory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, body);
    }
    var commit = new string('a', 40);
    void WriteRelease()
    {
        File.WriteAllText(Path.Combine(paths.Root, "release-manifest.json"), JsonSerializer.Serialize(new { version = "v1.2.3", commit,
            files = new[] { new { path = "client-assets/manifest.json", sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(paths.AssetsDirectory, "manifest.json")))) } } }));
    }
    void WriteAssets(string assetCommit, object? extra = null)
    {
        var files = assetFiles.Keys.Select(name => new { path = name, sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(paths.AssetsDirectory, name)))) }).ToList();
        var manifest = JsonSerializer.SerializeToNode(new { version = "v1.2.3", commit = assetCommit, files })!;
        if (extra is not null) manifest["files"]!.AsArray().Add(JsonSerializer.SerializeToNode(extra));
        File.WriteAllText(Path.Combine(paths.AssetsDirectory, "manifest.json"), manifest.ToJsonString());
        if (File.Exists(Path.Combine(paths.Root, "release-manifest.json"))) WriteRelease();
    }
    WriteAssets(commit);
    WriteRelease();
    var exporter = new ClientPackageExporter(paths);
    var zipPath = await exporter.ExportAsync("launcher", edited);
    using (var archive = ZipFile.OpenRead(zipPath))
    {
        string Read(string name) { using var reader = new StreamReader(archive.GetEntry(name)!.Open()); return reader.ReadToEnd(); }
        var launcher = JsonNode.Parse(Read("DFLegacyLauncher/launcher.json"))!;
        Check(launcher["Account"]!.GetValue<string>() == "" && launcher["Password"]!.GetValue<string>() == "" && launcher["Accounts"]!.AsArray().Count == 0,
            "exported launcher never carries bundled accounts or passwords");
        Check(!launcher["StartServer"]!.GetValue<bool>() && launcher["ClientPath"]!.GetValue<string>() == "../DNF.exe", "player bundle uses relative client path and remote-only startup");
        Check(launcher["EntranceHost"]!.GetValue<string>() == "game.example.test", "player bundle points to the configured server");
        Check(Read("DFLegacyLauncher/Config.ini").Contains("ClientServiceUrl=https://accounts.example.test"), "embedded client pages use the deployed account API origin");
        Check(Read("DFLegacyLauncher/Config.ini").Contains("EnableDebugOverlay=0"), "debug overlay remains disabled for distributed players");
        Check(archive.GetEntry("SHA256SUMS") is not null && archive.GetEntry("README.txt") is not null, "client package includes checksums and install instructions");
        Check(archive.Entries.All(entry => !entry.FullName.Contains("state.json") && !entry.FullName.Contains("Script.pvf")), "exports never include saved games or private PVF resources");
    }
    var patchPath = await exporter.ExportAsync("patch", edited);
    using (var archive = ZipFile.OpenRead(patchPath)) Check(archive.GetEntry("ijl15.dll") is not null && archive.GetEntry("DFLegacyLauncher/DFLegacy.Launcher.exe") is null,
        "patch-only export contains client DLL without the full launcher");
    async Task RejectExport(string message)
    {
        try { await exporter.ExportAsync("launcher", edited); Check(false, message); }
        catch (InvalidDataException) { Check(true, message); }
    }
    WriteAssets(new string('b', 40));
    await RejectExport("assets from another commit are rejected");
    WriteAssets(commit, new { path = "../secret.txt", sha256 = new string('0', 64) });
    await RejectExport("manifest traversal cannot read outside bundled assets");
    WriteAssets(commit);
    var nested = Path.Combine(paths.AssetsDirectory, "launcher", "profiles", "launcher.json");
    Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
    File.WriteAllText(nested, "{\"Account\":\"private-user\",\"Password\":\"private-pass\"}");
    WriteAssets(commit, new { path = "launcher/profiles/launcher.json", sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nested))) });
    await RejectExport("nested credential-bearing profiles are outside the fixed export layout");
    WriteAssets(commit);
    File.AppendAllText(Path.Combine(paths.AssetsDirectory, "manifest.json"), "\n");
    await RejectExport("resource manifest must match the immutable release manifest digest");
    WriteAssets(commit);
    File.AppendAllText(Path.Combine(paths.AssetsDirectory, "patch/ijl15.dll"), "tampering");
    await RejectExport("modified client DLL is rejected before packaging");

    if (args.Contains("--http")) await HttpBehaviorTests.RunAsync(Check);
    var serverArgument = Array.IndexOf(args, "--server-dir");
    if (serverArgument >= 0) await SupervisorBehaviorTests.RunAsync(Check, args[serverArgument + 1]);
}
catch (Exception exception)
{
    failures.Add(exception.ToString());
    Console.Error.WriteLine(exception);
}
finally
{
    try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
}
Console.WriteLine($"Manager tests: {checks} checks, {failures.Count} failures");
return failures.Count == 0 ? 0 : 1;
