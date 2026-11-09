using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Manager;

if (args.Contains("--logs-only")) return LogRecoveryTests.Run();
if (LogRecoveryTests.Run() != 0) return 1;

if (args.Contains("--startup-only")) return await StartupRecoveryTests.RunAsync();
if (args.Contains("--pvf-only")) return await PvfUploadTests.RunAsync();
if (await StartupRecoveryTests.RunAsync() != 0) return 1;
if (await PvfUploadTests.RunAsync() != 0) return 1;

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
    Check(original.GmPanelPort == 18081 && original.PlayerPanelPort == 18082, "panels default to dedicated ports");
    Reject(() => config.Save(original with { GmPanelPort = original.AdminPort }), "panel and API port collisions rejected");
    Check(ConfigurationStore.PanelUrl("http://[::1]:8081/base", 18082) == "http://[::1]:18082/", "panel link uses its root and preserves IPv6 host");
    Check(ConfigurationStore.PanelUrl("https://game.example/accounts", 18081) == "http://game.example:18081/", "direct panel listener uses HTTP independently of account proxy TLS");
    Reject(() => config.Save(original with { AdvertisedHost = "dns.example.test" }),
        "default native fallback refuses a DNS advertisement without an explicit IPv4 address");
    Check(Path.IsPathFullyQualified(original.ScriptPvfPath), "PVF resolves independently of current working directory");
    Check(Path.GetDirectoryName(paths.ServerConfig) == Path.Combine(paths.Data, "config"), "user config lives in the selected data directory");
    var edited = original with { BindHost = "0.0.0.0", AdvertisedHost = "game.example.test", FallbackRelayAdvertisedHost = "203.0.113.42", AccountServiceUrl = "https://accounts.example.test", ChannelName = "测试频道", MonsterExperienceMultiplier = 2.5 };
    config.Save(edited);
    foreach (var loopbackApi in new[] { "http://127.0.0.1:8081", "http://localhost:8081", "http://[::1]:8081" })
        Reject(() => ConfigurationStore.Validate(edited with { RelayEnabled = true, FallbackRelayAdvertisedHost = "", AccountServiceUrl = loopbackApi }),
            "remote relay player package must not send account requests to player loopback: " + loopbackApi);
    var savedDocument = JsonNode.Parse(File.ReadAllText(paths.ServerConfig))!;
    Check(savedDocument["Keep"]!["Nested"]!.GetValue<int>() == 42 && savedDocument["DFLegacy"]!["UnknownFuture"]!["retain"]!.GetValue<int>() == 123,
        "editing basic fields retains unknown future configuration");
    Check(savedDocument["DFLegacy"]!["SeedTestAccount"]!.GetValue<bool>() == false, "managed deployments do not seed public test credentials");
    Check(Directory.GetFiles(paths.Backups).Length >= 1, "configuration edits leave a recoverable backup");
    var loadedAgain = new ConfigurationStore(paths, new Dictionary<string, string?>()).Read().Saved;
    Check(loadedAgain.ChannelName == "测试频道" && loadedAgain.MonsterExperienceMultiplier == 2.5, "restarting manager does not replace user configuration");
    Reject(() => config.Save(edited with { EntrancePort = 0 }), "zero listening port is rejected");
    Reject(() => config.Save(edited with { AdminPort = edited.EntrancePort }), "colliding TCP listeners are rejected before starting");
    var configBeforeDuplicate = File.ReadAllText(paths.ServerConfig);
    var settingsBeforeDuplicate = File.ReadAllText(paths.SettingsFile);
    Reject(() => config.Save(edited with { AdditionalEntrancePort = edited.ChannelPort }), "additional entrance port cannot collapse into the channel port");
    Check(File.ReadAllText(paths.ServerConfig) == configBeforeDuplicate && File.ReadAllText(paths.SettingsFile) == settingsBeforeDuplicate,
        "rejected additional-port collision leaves both saved configuration files untouched");
    Reject(() => config.Save(edited with { AdvertisedHost = "0.0.0.0" }), "wildcard is not advertised to players");
    Reject(() => config.Save(edited with { BindHost = "example.test" }), "listening interface must be a literal IP");
    Reject(() => config.Save(edited with { MonsterExperienceMultiplier = double.NaN }), "non-finite multipliers cannot enter configuration");
    Reject(() => config.Save(edited with { AccountServiceUrl = "file:///private/secret" }), "account API URL is limited to HTTP and HTTPS");
    var overridden = new ConfigurationStore(paths, new Dictionary<string, string?> { ["DFLegacy__Admin__Port"] = "19081" });
    Check(overridden.Read().Effective.AdminPort == 19081 && overridden.Read().Saved.AdminPort == 8081,
        "effective environment settings and on-disk settings are shown separately");
    Reject(() => overridden.Save(edited with { AdminPort = 29081 }), "editing an environment-controlled field is explicitly rejected");
    var relayConfiguration = config.Save(edited with { RelayEnabled = true, FallbackRelayAdvertisedHost = "" });
    Check(relayConfiguration.Saved.AdvertisedHost == "game.example.test" && relayConfiguration.Effective.GameplayAdvertisedHost == "127.0.0.1"
        && config.ReadServerDocument()["DFLegacy"]!["GameplayDatagram"]!["AdvertisedHost"]!.GetValue<string>() == "127.0.0.1",
        "IPv6 relay retains the real destination while advertising the client-local relay to the game protocol");
    config.Save(edited);
    Reject(() => config.Save(edited with { FallbackRelayTcpPort = edited.EntrancePort }), "fallback TCP collision is rejected before starting");
    Reject(() => config.Save(edited with { FallbackRelayUdpPort = edited.CharacterPort }), "fallback UDP collision is rejected before starting");
    Reject(() => config.Save(edited with { PvpEnabled = true, PvpPort = edited.ChannelPort }), "PvP TCP collision is rejected before starting");
    Check((edited with { PvpEnabled = true }).ForwardedTcpPorts().Intersect(new[] { 7004,7110,7111,7112 }).Count() == 4,
        "generated manager and offline client ports cover four PvP channels");
    Reject(() => config.Save(edited with { PvpEnabled = true, GmPanelPort = 7110 }), "new PvP ports cannot collide with GM panel");
    Reject(() => config.Save(edited with { RelayEnabled = true }), "local client relay cannot receive a remote fallback IPv4 advertisement");
    var fallbackOverride = new ConfigurationStore(new ManagerPaths(Path.Combine(scratch, "fallback environment")),
        new Dictionary<string, string?> { ["DFLegacy__FallbackRelay__AdvertisedHost"] = "203.0.113.9" });
    var fallbackInput = fallbackOverride.Read().Saved with { BindHost = "0.0.0.0", AdvertisedHost = "game.example.test" };
    try
    {
        var result = fallbackOverride.Save(fallbackInput);
        Check(result.Saved.FallbackRelayAdvertisedHost == "" && result.Effective.FallbackRelayAdvertisedHost == "203.0.113.9",
            "valid effective fallback IPv4 allows DNS while preserving the blank locked file value");
    }
    catch (ArgumentException) { Check(false, "valid effective fallback IPv4 allows DNS while preserving the blank locked file value"); }
    Reject(() => fallbackOverride.Save(fallbackInput with { RelayEnabled = true }), "environment fallback advertisement cannot break local client relay");
    var fallbackDisabled = new ConfigurationStore(new ManagerPaths(Path.Combine(scratch, "fallback disabled environment")),
        new Dictionary<string, string?> { ["DFLegacy__FallbackRelay__Enabled"] = "false" });
    try
    {
        var result = fallbackDisabled.Save(fallbackDisabled.Read().Saved with { AdvertisedHost = "game.example.test" });
        Check(result.Saved.FallbackRelayEnabled && !result.Effective.FallbackRelayEnabled,
            "environment-disabled fallback allows DNS without changing the locked saved switch");
    }
    catch (ArgumentException) { Check(false, "environment-disabled fallback allows DNS without changing the locked saved switch"); }
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
        ["launcher/DFLegacy.Diagnostics.exe"] = "x64 diagnostic fixture",
        ["patch/DFLegacy.CrashCapture.exe"] = "x86 capture fixture",
        ["launcher/launcher.json"] = "{\"Account\":\"private-user\",\"Password\":\"do not export\"}",
        ["launcher/Config.ini"] = "[Features]\nEnableDebugOverlay=1\n[Network]\nClientServiceUrl=http://old-host\n",
        ["launcher/DFLegacy.Client.dll"] = "native patch",
        ["patch/debug.dll"] = "same release debug",
        ["patch/DFLegacy.Client.dll"] = "native patch",
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
    var exportProgress = new List<ClientExportProgress>();
    var zipPath = await exporter.ExportAsync("client", edited with { RelayEnabled = true, FallbackRelayAdvertisedHost = "" }, progress: exportProgress.Add);
    Check(exportProgress.Any(p => p.Stage == "validating") && exportProgress.Any(p => p.Stage == "compressing")
        && exportProgress.Last().Stage == "ready", "export reports validation, compression and completed stages");
    Check(exportProgress.Where(p => p.Stage == "compressing").All(p => p.CompletedBytes <= p.TotalBytes)
        && exportProgress.Any(p => p.Stage == "compressing" && p.CompletedBytes == p.TotalBytes && p.TotalBytes > 0),
        "compression progress counts actual input bytes through completion");
    using (var archive = ZipFile.OpenRead(zipPath))
    {
        string Read(string name) { using var reader = new StreamReader(archive.GetEntry(name)!.Open()); return reader.ReadToEnd(); }
        var launcher = JsonNode.Parse(Read("launcher.json"))!;
        var loaded = JsonSerializer.Deserialize<DFLegacy.Launcher.LauncherOptions>(Read("launcher.json"))!;
        Check(loaded.UpdateRepository == "firekula/DFLegacy" && loaded.IncludePrereleaseUpdates,
            "fresh exported player JSON resolves the real launcher's public update defaults");
        Check(loaded.ClientPath == "DNF.exe" && loaded.EntranceHost == "game.example.test"
            && loaded.AccountServiceUrl == "https://accounts.example.test" && loaded.Accounts.Count == 0
            && loaded.Account == "" && loaded.Password == "" && !loaded.StartServer && loaded.Relay.Enabled,
            "real launcher options preserve exported bootstrap, profile privacy and relay settings");
        Check(launcher["Account"]!.GetValue<string>() == "" && launcher["Password"]!.GetValue<string>() == "" && launcher["Accounts"]!.AsArray().Count == 0,
            "exported launcher never carries bundled accounts or passwords");
        Check(!launcher["StartServer"]!.GetValue<bool>() && launcher["ClientPath"]!.GetValue<string>() == "DNF.exe", "player bundle uses relative client path and remote-only startup");
        Check(launcher["EntranceHost"]!.GetValue<string>() == "game.example.test", "player bundle points to the configured server");
        Check(launcher["Relay"]!["TcpPorts"]!.GetValue<string>().Split(',').Contains("7005")
            && launcher["Relay"]!["UdpPorts"]!.GetValue<string>().Split(',').Contains("7005"),
            "default native fallback TCP and UDP ports are forwarded by exported local relay");
        Check(launcher["AccountServiceUrl"]!.GetValue<string>() == "https://accounts.example.test", "embedded client pages use the deployed account API origin");
        Check(archive.GetEntry("Config.ini") is null && archive.GetEntry("DFLegacy.Launcher.exe") is not null && archive.GetEntry("DFLegacy.Client.dll") is not null, "root player bundle includes launcher and patch without overwriting existing Config.ini");
        Check(archive.Entries.All(entry => !entry.FullName.StartsWith("DFLegacyLauncher/") && !entry.FullName.EndsWith("coreclr.dll")), "player ZIP has no wrapper directory or loose runtime files");
        Check(archive.GetEntry("SHA256SUMS") is not null && archive.GetEntry("README.txt") is not null, "client package includes checksums and install instructions");
        Check(archive.Entries.All(entry => !entry.FullName.Contains("state.json") && !entry.FullName.Contains("Script.pvf")), "exports never include saved games or private PVF resources");
    }
    using (var archive = ZipFile.OpenRead(zipPath))
    {
        Check(archive.GetEntry("client-package.json") is not null, "normal export includes installed schema2 manifest");
        Check(archive.GetEntry("ijl15.dll") is null && archive.GetEntry("debug.dll") is null, "normal export preserves original IJL and excludes debug");
    }
    var x86Asset = "launcher-x86/DFLegacy.Launcher.exe";
    Directory.CreateDirectory(Path.Combine(paths.AssetsDirectory, "launcher-x86"));
    File.WriteAllText(Path.Combine(paths.AssetsDirectory, x86Asset), "x86 launcher fixture");
    assetFiles.Add(x86Asset, "x86 launcher fixture");
    File.WriteAllText(Path.Combine(paths.AssetsDirectory,"launcher-x86/DFLegacy.Diagnostics.exe"),"x86 diagnostic fixture");
    assetFiles.Add("launcher-x86/DFLegacy.Diagnostics.exe","x86 diagnostic fixture");
    WriteAssets(commit);
    var updateStore = new ClientUpdateStore(paths, "http://games.example.test:18083");
    var dualPath = await exporter.ExportPairAsync(edited with { PvpEnabled = true }, false, updateStore, default, null);
    using (var dual = ZipFile.OpenRead(dualPath))
    {
        foreach (var architecture in new[] { "x86", "x64" })
        {
            using var inner = new ZipArchive(dual.GetEntry($"DFLegacy-client-win-{architecture}.zip")!.Open(), ZipArchiveMode.Read);
            using(var helperReader=new StreamReader(inner.GetEntry("DFLegacy.Diagnostics.exe")!.Open()))
                Check(helperReader.ReadToEnd()==(architecture=="x86"?"x86 diagnostic fixture":"x64 diagnostic fixture"),architecture+" exports correct architecture helper");
            Check(inner.GetEntry("DFLegacy.CrashCapture.exe") is not null,architecture+" exports native x86 capture helper");
            using var configReader = new StreamReader(inner.GetEntry("launcher.json")!.Open());
            var dualConfig = JsonDocument.Parse(configReader.ReadToEnd()).RootElement;
            Check(dualConfig.GetProperty("Relay").GetProperty("TcpPorts").GetString()!.Split(',')
                .Intersect(new[] { "7004","7110","7111","7112" }).Count() == 4, $"{architecture} generated configuration includes all four PvP channels");
            Check(dualConfig.GetProperty("UpdateServiceUrl").GetString() == "http://games.example.test:18083",
                $"{architecture} installer points to dedicated HTTP port");
            var current = updateStore.Latest(architecture, "normal")!;
            var package = updateStore.Package(architecture, "normal", current.Sha256)!;
            Check(current.Version == "v1.2.3" && current.Size == new FileInfo(package).Length
                && current.Sha256 == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(package))), $"{architecture} update metadata matches immutable archive");
            using var update = ZipFile.OpenRead(package);
            Check(update.Entries.Select(e => e.FullName).Order().SequenceEqual(new[] { "DFLegacy.Client.dll", "DFLegacy.CrashCapture.exe", "DFLegacy.Diagnostics.exe", "DFLegacy.Launcher.exe", "update-manifest.json" }),
                $"{architecture} update omits installer configuration");
        }
    }
    var previousUpdate = updateStore.Latest("x86", "normal");
    File.AppendAllText(Path.Combine(paths.AssetsDirectory, x86Asset), "corrupted");
    try
    {
        await exporter.ExportPairAsync(edited, false, updateStore, default, null);
        Check(false, "corrupt x86 must reject the complete export");
    }
    catch (InvalidDataException)
    {
        var reopened = new ClientUpdateStore(paths, "http://games.example.test:18083");
        Check(reopened.Latest("x86", "normal") == previousUpdate
            && reopened.Latest("x64", "normal")?.Id == previousUpdate?.Id,
            "failed second architecture leaves both persisted indexes intact");
    }
    File.Delete(Path.Combine(paths.AssetsDirectory, x86Asset));
    assetFiles.Remove(x86Asset);
    WriteAssets(commit);
    var testZip = await exporter.ExportAsync("client", edited, testBuild: true);
    using (var archive = ZipFile.OpenRead(testZip))
    {
        Check(archive.GetEntry("debug.dll") is not null && archive.GetEntry("ijl15.dll") is null, "test export includes debug but never IJL");
        using var reader = new StreamReader(archive.GetEntry("client-package.json")!.Open());
        var installed = JsonNode.Parse(reader.ReadToEnd())!;
        Check(installed["schemaVersion"]!.GetValue<int>() == 2 && installed["flavor"]!.GetValue<string>() == "test" && installed["files"]!.AsArray().Count == 5, "test manifest records exact schema2 flavor and files");
    }
    var debugPath = Path.Combine(paths.AssetsDirectory, "patch/debug.dll");
    File.Delete(debugPath);
    Check(File.Exists(await exporter.ExportAsync("client", edited)), "missing optional debug does not break normal export");
    try { await exporter.ExportAsync("client", edited, testBuild: true); Check(false, "test export requires debug payload"); }
    catch (InvalidDataException) { Check(true, "test export requires debug payload"); }
    File.WriteAllText(debugPath, "wrong debug version");
    try { await exporter.ExportAsync("client", edited, testBuild: true); Check(false, "test export rejects mixed debug hash"); }
    catch (InvalidDataException) { Check(true, "test export rejects mixed debug hash"); }
    File.WriteAllText(debugPath, assetFiles["patch/debug.dll"]);
    var aliasZip = await exporter.ExportAsync("patch", edited, testBuild: true);
    using (var archive = ZipFile.OpenRead(aliasZip)) Check(archive.GetEntry("debug.dll") is null, "old aliases always return normal");
    foreach (var (package, flavor) in new[] { (zipPath, "normal"), (testZip, "test") })
    {
        var unpacked = Path.Combine(scratch, "parser-" + flavor);
        ZipFile.ExtractToDirectory(package, unpacked);
        var installed = DFLegacy.Launcher.ClientPackageManifest.LoadAndValidate(unpacked);
        Check(installed.Flavor == flavor && installed.Commit == commit, "actual launcher parser validates Manager " + flavor + " export");
    }
    var pvpDocument = config.ReadServerDocument();
    var jobLog = new ManagerLog(paths);
    var jobs = new ClientExportJobs(exporter, jobLog);
    var startedJob = jobs.Start(edited);
    var jobDeadline = DateTime.UtcNow.AddSeconds(15);
    while (jobs.Read(startedJob.Id)!.State == "running" && DateTime.UtcNow < jobDeadline) await Task.Delay(10);
    Check(jobs.Read(startedJob.Id) is { State: "ready", Size: > 0 } && File.Exists(jobs.Download(startedJob.Id)),
        "background export publishes a completed, downloadable archive");
    Check(jobs.Download("../server.json") is null && jobs.Read("unknown") is null,
        "download identifiers cannot select arbitrary paths");
    Check(jobLog.Read(0).Any(line => line.Source == "export" && line.Text.Contains("生成完成")),
        "background generation writes lifecycle events into runtime logs");
    using var canceledJob = new CancellationTokenSource();
    canceledJob.Cancel();
    var canceled = jobs.Start(edited, canceledJob.Token);
    jobDeadline = DateTime.UtcNow.AddSeconds(15);
    while (jobs.Read(canceled.Id)!.State == "running" && DateTime.UtcNow < jobDeadline) await Task.Delay(10);
    Check(jobs.Read(canceled.Id) is { State: "failed", Error: not null } && jobs.Download(canceled.Id) is null,
        "host cancellation reports failure without exposing a partial download");
    pvpDocument["DFLegacy"]!["Pvp"] = new JsonObject { ["Enabled"] = true, ["Port"] = 9051 };
    File.WriteAllText(paths.ServerConfig, pvpDocument.ToJsonString());
    var pvpZip = await exporter.ExportAsync("launcher", config.Read().Effective);
    using (var archive = ZipFile.OpenRead(pvpZip))
    {
        using var reader = new StreamReader(archive.GetEntry("launcher.json")!.Open());
        var launcher = JsonNode.Parse(reader.ReadToEnd())!;
        Check(launcher["Relay"]!["TcpPorts"]!.GetValue<string>().Split(',').Contains("9051"),
            "exported relay includes the configured enabled PvP TCP listener");
    }
    pvpDocument["DFLegacy"]!["Pvp"]!["Enabled"] = false;
    File.WriteAllText(paths.ServerConfig, pvpDocument.ToJsonString());
    var patchPath = await exporter.ExportAsync("patch", edited);
    using (var archive = ZipFile.OpenRead(patchPath)) Check(archive.GetEntry("DFLegacy.Client.dll") is not null && archive.GetEntry("DFLegacy.Launcher.exe") is not null,
        "legacy patch export alias now returns the same complete root player bundle");
    async Task RejectExport(string message)
    {
        try { await exporter.ExportAsync("launcher", edited); Check(false, message); }
        catch (InvalidDataException) { Check(true, message); }
    }
    assetFiles.Remove("patch/debug.dll");
    WriteAssets(commit);
    Check(File.Exists(await exporter.ExportAsync("client", edited)), "normal export allows absent optional debug declaration");
    try { await exporter.ExportAsync("client", edited, testBuild: true); Check(false, "test export requires anchored debug declaration"); }
    catch (InvalidDataException) { Check(true, "test export requires anchored debug declaration"); }
    WriteAssets(commit, new { path = "patch/debug.dll", sha256 = "invalid" });
    await RejectExport("normal validates optional debug hash declaration without loading payload");
    WriteAssets(commit, new { path = "launcher/unapproved.dll", sha256 = new string('0', 64) });
    await RejectExport("normal rejects unknown metadata paths even if not selected");
    assetFiles["patch/debug.dll"] = "same release debug";
    WriteAssets(commit);
    var manifestFile = Path.Combine(paths.AssetsDirectory, "manifest.json");
    File.WriteAllText(manifestFile, File.ReadAllText(manifestFile).Replace("{", "{\"Version\":\"v1.2.3\",", StringComparison.Ordinal));
    WriteRelease();
    await RejectExport("case duplicate metadata fields are rejected even when anchored");
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
    File.AppendAllText(Path.Combine(paths.AssetsDirectory, "patch/DFLegacy.Client.dll"), "tampering");
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
