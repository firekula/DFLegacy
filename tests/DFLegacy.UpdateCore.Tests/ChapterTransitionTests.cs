using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace DFLegacy.UpdateCore.Tests;

internal static class ChapterTransitionTests
{
    internal static int Run()
    {
        IdentityIsBoundToManagedManifest();
        PublicUpgradeRejectsUnboundSources();
        PublicUpgradeDownloadsMigratesAndRollsBack();
        CheckpointSurvivesSecondChapterUpdates();
        Console.WriteLine("Chapter transition tests passed");
        return 0;
    }

    internal static int? Dispatch(string[] args)
    {
        if (args is not ["--chapter-install-child", var root, var cache, var releasePath, var signal]) return null;
        var release = JsonSerializer.Deserialize<VerifiedRelease>(File.ReadAllBytes(releasePath), ManifestCodec.Json)!;
        InstallTransaction.ApplyChapterUpgrade(root, new(release, cache), release.Source, true,
            new AtPhase("install", "launcher.chapter2.json", () =>
            {
                File.WriteAllText(signal, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Thread.Sleep(Timeout.Infinite);
            }));
        return 1;
    }

    private static void CheckpointSurvivesSecondChapterUpdates()
    {
        const string c1 = """
            {"schema":1,"chapter":"chapter1","entry":"DFLegacy.Launcher.exe",
             "requiredFiles":["DFLegacy.Launcher.exe","DFLegacy.Client.dll","Script.pvf"]}
            """;
        const string c2 = """
            {"schema":1,"chapter":"chapter2","entry":"DFLegacy.Launcher.exe",
             "requiredFiles":["DFLegacy.Launcher.exe","DNF.exe","Script.pvf"]}
            """;
        var one = Release("chapter-one", 2, c1, ("DFLegacy.Launcher.exe", "first"),
            ("DFLegacy.Client.dll", "c1-game"), ("Script.pvf", "c1-script"));
        var localSource = new ReleaseSource("local", "http://127.0.0.2:48127/c2", "chapter-two", "x64", "test");
        var two = Release(localSource, 3, c2, ("DFLegacy.Launcher.exe", "second"),
            ("DNF.exe", "c2-game"), ("Script.pvf", "c2-script"));
        var later = WithRetirement(Release(localSource, 4, c2, ("DFLegacy.Launcher.exe", "third"),
            ("DNF.exe", "c2-game"), ("Script.pvf", "new-script"), ("sounds/a.ogg", "sound")));
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-chapter-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Put(root, "launcher.json", """{"EntranceHost":"chapter-one.local","Account":"old","Password":"original"}""");
            Put(root, "launcher.chapter2.json", """{"EntranceHost":"chapter-two.local","ChapterTwoDebugEnabled":true}""");
            Put(root, "Screenshot/one.png", "private screenshot");
            Seed(root, one, c1, ("DFLegacy.Launcher.exe", "first"), ("DFLegacy.Client.dll", "c1-game"), ("Script.pvf", "c1-script"));
            var cache = new ContentCache(Path.Combine(root, ".dflegacy", "v4", "cache"));
            two = WithRetirement(two);
            var twoManifest = two.Manifest;
            foreach (var (path, body) in new[] { ("DFLegacy.Chapter.json", c2), ("DFLegacy.Launcher.exe", "second"),
                ("DNF.exe", "c2-game"), ("Script.pvf", "c2-script") })
            {
                var file = twoManifest.Files.Single(file => file.Path == path);
                File.WriteAllText(cache.ObjectPath(file.Sha256, file.Size), body);
            }
            var staged = new StagedUpdate(two, cache.DirectoryPath);
            TestRun.Reject(() => ChapterTransition.PrepareUpgrade(root, staged, one.Source, true), "mismatched target source");
            TestRun.Reject(() => ChapterTransition.PrepareUpgrade(root, staged, two.Source, false), "implicit chapter switch");
            Put(root, "DNF.exe", "foreign executable");
            TestRun.Reject(() => ChapterTransition.PrepareUpgrade(root, staged, two.Source, true), "unknown chapter executable");
            File.Delete(Path.Combine(root, "DNF.exe"));
            var prepared = ChapterTransition.PrepareUpgrade(root, staged, two.Source, true);
            using (var migrated = System.Text.Json.JsonDocument.Parse(prepared))
            {
                var configuration = migrated.RootElement;
                TestRun.Check(configuration.GetProperty("Password").GetString() == "original"
                    && configuration.GetProperty("EntranceHost").GetString() == "chapter-two.local"
                    && configuration.GetProperty("ChapterTwoDebugEnabled").GetBoolean(),
                    "migration copies current credentials while preserving C2 settings");
            }

            var currentConfig = File.ReadAllBytes(Path.Combine(root, "launcher.chapter2.json"));
            var disguised = two with { Source = one.Source };
            var disguisedBytes = ManifestCodec.Serialize(two.Manifest with { Channel = one.Source.Channel });
            disguised = disguised with { ManifestBytes = disguisedBytes, ManifestSha256 = TestRun.Hash(disguisedBytes) };
            TestRun.Reject(() => InstallTransaction.Apply(root, staged with { Release = disguised }), "same-source ordinary cross-chapter install");
            var engine = new UpdateEngine(root, new EngineTests.MemoryProvider(two.Source, two));
            TestRun.Reject(() => engine.CheckChapterUpgradeAsync(false).GetAwaiter().GetResult(), "implicit cross-source discovery");
            var check = engine.CheckChapterUpgradeAsync(true).GetAwaiter().GetResult();
            TestRun.Check(check.CanLaunch && check.NeedsInstall && check.Target?.ManifestSha256 == two.ManifestSha256,
                "cross-source check keeps the verified C1 baseline launchable");
            staged = engine.DownloadAsync(check).GetAwaiter().GetResult();
            var firstConfigPath = Path.Combine(root, "launcher.json");
            var firstConfig = File.ReadAllBytes(firstConfigPath);
            try
            {
                InstallTransaction.ApplyChapterUpgrade(root, staged, two.Source, true,
                    new AtPhase("prepared", "", () => File.WriteAllText(firstConfigPath, """{"Password":"written during upgrade"}""")));
                throw new Exception("FAIL: forward install ignored a non-participating first chapter configuration write");
            }
            catch (InvalidDataException) { }
            TestRun.Check(!InstallTransaction.HasPending(root)
                && File.ReadAllText(firstConfigPath).Contains("written during upgrade")
                && File.ReadAllBytes(Path.Combine(root, "launcher.chapter2.json")).SequenceEqual(currentConfig)
                && File.ReadAllText(Path.Combine(root, "Script.pvf")) == "c1-script",
                "a racing C1 settings save stops the upgrade and keeps the newer configuration");
            File.WriteAllBytes(firstConfigPath, firstConfig);

            StopInstallerAfterConfigurationWrite(root, staged);
            TestRun.Check(InstallTransaction.HasPending(root) && InstallTransaction.Recover(root)
                && new InstallationStore(root).ReadInstalled()?.Release.ManifestSha256 == one.ManifestSha256
                && File.ReadAllBytes(Path.Combine(root, "launcher.chapter2.json")).SequenceEqual(currentConfig)
                && File.ReadAllText(Path.Combine(root, "Script.pvf")) == "c1-script",
                "a killed installer leaves a journal a new process can recover");

            foreach (var interruption in new[] { new AtPhase("install", "launcher.chapter2.json"), new AtPhase("commit", "installed.json") })
            {
                try { InstallTransaction.ApplyChapterUpgrade(root, staged, two.Source, true, interruption); throw new Exception("FAIL: installation interruption was ignored"); }
                catch (OperationCanceledException) { }
                TestRun.Check(!InstallTransaction.HasPending(root)
                    && new InstallationStore(root).ReadInstalled()?.Release.ManifestSha256 == one.ManifestSha256
                    && File.ReadAllBytes(Path.Combine(root, "launcher.chapter2.json")).SequenceEqual(currentConfig)
                    && File.ReadAllText(Path.Combine(root, "Script.pvf")) == "c1-script"
                    && File.Exists(Path.Combine(root, "DFLegacy.Client.dll")) && !File.Exists(Path.Combine(root, "DNF.exe")),
                    "forward transaction recovers original content, receipt and private configuration");
            }
            InstallTransaction.ApplyChapterUpgrade(root, staged, two.Source, true, deferReadiness: true);
            TestRun.Check(InstallTransaction.HasPending(root)
                && !engine.ValidateInstalledAsync().GetAwaiter().GetResult().CanLaunch,
                "a committed upgrade remains recoverable before launcher readiness");
            TestRun.Reject(() => InstallTransaction.ConfirmChapterReady(root, one.ManifestSha256), "readiness acknowledgement for wrong release");
            TestRun.Check(InstallTransaction.Recover(root)
                && new InstallationStore(root).ReadInstalled()?.Release.ManifestSha256 == one.ManifestSha256
                && File.ReadAllBytes(Path.Combine(root, "launcher.chapter2.json")).SequenceEqual(currentConfig),
                "recovery after a lost readiness worker restores the pre-upgrade C1 install and C2 settings");
            InstallTransaction.ApplyChapterUpgrade(root, staged, two.Source, true, deferReadiness: true);
            Put(root, "DFLegacy.Client.dll", "c1-game");
            TestRun.Reject(() => InstallTransaction.ValidateChapterReadyProbe(root, two.ManifestSha256),
                "readiness rejects a retired file that reappears before startup");
            File.Delete(Path.Combine(root, "DFLegacy.Client.dll"));
            Put(root, "Script.pvf", "damaged");
            TestRun.Reject(() => InstallTransaction.ValidateChapterReadyProbe(root, two.ManifestSha256),
                "readiness still hashes updated content and detects same-length corruption");
            Put(root, "Script.pvf", "c2-script");
            InstallTransaction.ValidateChapterReadyProbe(root, two.ManifestSha256);
            InstallTransaction.ConfirmChapterReady(root, two.ManifestSha256);
            TestRun.Check(engine.ValidateInstalledAsync().GetAwaiter().GetResult().CanLaunch
                && !File.Exists(Path.Combine(root, "DFLegacy.Client.dll"))
                && File.ReadAllText(Path.Combine(root, "launcher.chapter2.json")).Contains("original"),
                "same V4 pipeline commits C2 receipt, migrated settings and validated resources");
            foreach (var (path, body) in new[] { ("DFLegacy.Launcher.exe", "third"), ("Script.pvf", "new-script"), ("sounds/a.ogg", "sound") })
            {
                var file = later.Manifest.Files.Single(file => file.Path == path);
                File.WriteAllText(cache.ObjectPath(file.Sha256, file.Size), body);
            }
            Put(root, "DFLegacy.Client.dll", "foreign stale module");
            TestRun.Reject(() => InstallTransaction.Apply(root, new StagedUpdate(later, cache.DirectoryPath)),
                "ordinary C2 update tries to retire a foreign copy of a C1 module");
            TestRun.Check(File.ReadAllText(Path.Combine(root, "DFLegacy.Client.dll")) == "foreign stale module",
                "ordinary update preserves unknown C1 module for inspection");
            File.Delete(Path.Combine(root, "DFLegacy.Client.dll"));
            Put(root, "sounds/a.ogg", "foreign new path");
            TestRun.Reject(() => InstallTransaction.Apply(root, new StagedUpdate(later, cache.DirectoryPath)), "new C2 path collides with unknown local file");
            TestRun.Check(File.ReadAllText(Path.Combine(root, "sounds/a.ogg")) == "foreign new path", "new path conflict preserves unknown bytes");
            File.Delete(Path.Combine(root, "sounds/a.ogg"));
            InstallTransaction.Apply(root, new StagedUpdate(later, cache.DirectoryPath));
            var removalManifest = later.Manifest with
            {
                Sequence = 5,
                Files = later.Manifest.Files.Where(file => file.Path != "sounds/a.ogg").ToArray(),
                Retired = [.. later.Manifest.Retired, new("sounds/a.ogg", "delete", 5)]
            };
            var removalBytes = ManifestCodec.Serialize(removalManifest);
            var removal = later with { ManifestBytes = removalBytes, ManifestSha256 = TestRun.Hash(removalBytes) };
            Put(root, "sounds/a.ogg", "modified managed bytes");
            TestRun.Reject(() => InstallTransaction.Apply(root, new StagedUpdate(removal, cache.DirectoryPath)), "modified C2 retirement target");
            Put(root, "sounds/a.ogg", "sound");
            InstallTransaction.Apply(root, new StagedUpdate(removal, cache.DirectoryPath));
            Put(root, "sounds/a.ogg", "sound");
            TestRun.Reject(() => InstallTransaction.Apply(root, new StagedUpdate(removal, cache.DirectoryPath)),
                "historical tombstone cannot delete recreated file even with same bytes");
            File.Delete(Path.Combine(root, "sounds/a.ogg"));
            // Restore the pre-retirement fixture for the independent rollback cases below.
            Seed(root, later, c2, ("DFLegacy.Launcher.exe", "third"), ("DNF.exe", "c2-game"),
                ("Script.pvf", "new-script"), ("sounds/a.ogg", "sound"));
            Put(root, "launcher.chapter2.json", """{"Accounts":{},"Password":"changed"}""");
            TestRun.Reject(() => ChapterTransition.Rollback(root, two.Source), "malformed private C2 settings");
            TestRun.Check(!ChapterTransition.HasPending(root)
                && File.ReadAllText(Path.Combine(root, "Script.pvf")) == "new-script",
                "malformed account data stops before changing any managed file");
            Put(root, "launcher.chapter2.json", """{"EntranceHost":"chapter-two.local","Account":"new","Password":"changed"}""");
            Put(root, "sounds/a.ogg", "unknown replacement");
            TestRun.Reject(() => ChapterTransition.Rollback(root, two.Source), "modified C2-only resource has no matching ownership evidence");
            TestRun.Check(File.ReadAllText(Path.Combine(root, "DFLegacy.Launcher.exe")) == "third", "conflict leaves the current install untouched");
            Put(root, "sounds/a.ogg", "sound");
            using (var occupied = File.Open(Path.Combine(root, "Script.pvf"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { ChapterTransition.Rollback(root, two.Source); throw new Exception("FAIL: rollback ignored an occupied game resource"); }
                catch (IOException) { }
                TestRun.Check(!ChapterTransition.HasPending(root), "occupancy rejected before rollback starts");
            }
            var oldHash = one.Manifest.Files.Single(file => file.Path == "Script.pvf");
            var oldScriptBackup = Path.Combine(root, ".dflegacy", "chapter-transition", "baseline",
                oldHash.Sha256 + "-" + oldHash.Size + ".blob");
            File.WriteAllText(oldScriptBackup, "corrupted");
            TestRun.Reject(() => ChapterTransition.Rollback(root, two.Source), "corrupt backup data");
            TestRun.Check(!ChapterTransition.HasPending(root)
                && File.ReadAllText(Path.Combine(root, "DFLegacy.Launcher.exe")) == "third",
                "damaged backup is detected before rollback writes");
            File.WriteAllText(oldScriptBackup, "c1-script");
            File.Delete(Path.Combine(root, ClientChapterIdentity.FileName));
            try { ChapterTransition.Rollback(root, two.Source, new Interrupt()); throw new Exception("FAIL: rollback interruption was ignored"); }
            catch (OperationCanceledException) { }
            TestRun.Check(ChapterTransition.HasPending(root), "interrupted rollback retains recovery journal");
            TestRun.Check(!engine.ValidateInstalledAsync().GetAwaiter().GetResult().CanLaunch, "normal V4 launch blocks pending chapter rollback");
            TestRun.Reject(() => InstallTransaction.Apply(root, new StagedUpdate(later, cache.DirectoryPath)), "ordinary apply during chapter rollback");
            TestRun.Check(ChapterTransition.Recover(root) && !ChapterTransition.Recover(root), "rollback recovery finishes once and is repeatable");
            TestRun.Check(ChapterTransition.RequiresExplicitUpgrade(root), "manual rollback persists the reupgrade hold");
            TestRun.Check(new InstallationStore(root).ReadInstalled()?.Release.ManifestSha256 == one.ManifestSha256
                && File.ReadAllText(Path.Combine(root, "Script.pvf")) == "c1-script"
                && !File.Exists(Path.Combine(root, "DNF.exe")) && !File.Exists(Path.Combine(root, "sounds/a.ogg"))
                && File.ReadAllText(Path.Combine(root, "Screenshot/one.png")) == "private screenshot",
                "rollback restores original C1 resources and leaves screenshots in place");
            using var restored = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "launcher.json")));
            TestRun.Check(restored.RootElement.GetProperty("Password").GetString() == "changed"
                && restored.RootElement.GetProperty("Account").GetString() == "new"
                && restored.RootElement.GetProperty("EntranceHost").GetString() == "chapter-one.local"
                && File.ReadAllText(Path.Combine(root, "launcher.chapter2.json")).Contains("chapter-two.local"),
                "rollback retains current credentials and each chapter's connection settings");
            TestRun.Reject(() => ChapterTransition.PrepareUpgrade(root, staged, two.Source, false), "automatic reupgrade after manual rollback");
            InstallTransaction.ApplyChapterUpgrade(root, staged, two.Source, true);
            TestRun.Check(engine.ValidateInstalledAsync().GetAwaiter().GetResult().CanLaunch, "explicit re-upgrade succeeds after manual rollback");
        }
        finally { Directory.Delete(root, true); }
    }

    private const string PublicFirstDescriptor = """
        {"schema":1,"chapter":"chapter1","entry":"DFLegacy.Launcher.exe",
         "requiredFiles":["DFLegacy.Launcher.exe","DFLegacy.Client.dll","Script.pvf"]}
        """;
    private const string PublicSecondDescriptor = """
        {"schema":1,"chapter":"chapter2","entry":"DFLegacy.Launcher.exe",
         "requiredFiles":["DFLegacy.Launcher.exe","DNF.exe","Script.pvf"]}
        """;

    private static void PublicUpgradeRejectsUnboundSources()
    {
        var firstSource = new ReleaseSource("github", "firekula/DFLegacy", "stable", "x86", "normal");
        var official = new ReleaseSource("github", "firekula/DFLegacy", "chapter-two", "x86", "normal");
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-public-source-" + Guid.NewGuid().ToString("N"));
        var firstFiles = new[] { ("DFLegacy.Launcher.exe", "entry"), ("DFLegacy.Client.dll", "legacy"), ("Script.pvf", "first") };
        try
        {
            void RejectPair(ReleaseSource baseline, ReleaseSource target)
            {
                var first = Release(baseline, 2, PublicFirstDescriptor, firstFiles);
                Seed(root, first, PublicFirstDescriptor, firstFiles);
                var second = WithRetirement(Release(target, 3, PublicSecondDescriptor,
                    ("DFLegacy.Launcher.exe", "entry"), ("DNF.exe", "game"), ("Script.pvf", "second")));
                var engine = new UpdateEngine(root, new EngineTests.MemoryProvider(target, second));
                TestRun.Reject(() => engine.CheckChapterUpgradeAsync(true).GetAwaiter().GetResult(),
                    $"unsupported transition {baseline.Identity} to {target.Identity}");
                var cache = new ContentCache(Path.Combine(root, ".dflegacy", "v4", "cache"));
                foreach (var (path, body) in new[] { (ClientChapterIdentity.FileName, PublicSecondDescriptor),
                    ("DNF.exe", "game"), ("Script.pvf", "second") })
                {
                    var file = second.Manifest.Files.Single(file => file.Path == path);
                    File.WriteAllText(cache.ObjectPath(file.Sha256, file.Size), body);
                }
                TestRun.Reject(() => InstallTransaction.ApplyChapterUpgrade(root, new(second, cache.DirectoryPath), target, true),
                    $"transaction with unsupported source {target.Identity}");
                TestRun.Check(new InstallationStore(root).ReadInstalled()?.Release.Source == baseline,
                    "source rejection leaves the Chapter One receipt intact");
            }

            RejectPair(firstSource, official with { Provider = "gitee" });
            RejectPair(firstSource, official with { Channel = "other" });
            RejectPair(firstSource, official with { Architecture = "x64" });
            RejectPair(firstSource, official with { Flavor = "test" });
            RejectPair(firstSource with { Channel = "preview" }, official);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void PublicUpgradeDownloadsMigratesAndRollsBack()
    {
        var firstSource = new ReleaseSource("github", "firekula/DFLegacy", "stable", "x86", "normal");
        var official = new ReleaseSource("github", "firekula/DFLegacy", "chapter-two", "x86", "normal");
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-public-transition-" + Guid.NewGuid().ToString("N"));
        var firstFiles = new[] { ("DFLegacy.Launcher.exe", "entry"), ("DFLegacy.Client.dll", "legacy"), ("Script.pvf", "first") };
        var secondFiles = new[] { ("DFLegacy.Launcher.exe", "entry"), ("DNF.exe", "game"), ("Script.pvf", "second") };
        var first = Release(firstSource, 2, PublicFirstDescriptor, firstFiles);
        var second = WithRetirement(Release(official, 3, PublicSecondDescriptor, secondFiles));
        var stableAsset = firstSource.ManifestAssetName;
        var chapterAsset = "DFLegacy-client-update-v4-chapter-two-normal-win-x86.json";
        var parts = second.Manifest.Files.ToDictionary(file => file.Parts.Single().Asset, file => TestRun.Bytes(
            file.Path == ClientChapterIdentity.FileName ? PublicSecondDescriptor : secondFiles.Single(entry => entry.Item1 == file.Path).Item2));
        var downloads = new Dictionary<string, byte[]>(parts) { [chapterAsset] = second.ManifestBytes };
        using var http = new HttpClient(new GitHubFixture(request =>
        {
            var url = request.RequestUri!;
            TestRun.Check(request.Headers.Authorization == null && !request.Headers.Contains("Cookie"), "public transition sends no credentials");
            object Attachment(string name, byte[] bytes, string tag, int id) => new
            {
                id, name, size = bytes.Length, digest = "sha256:" + TestRun.Hash(bytes),
                browser_download_url = $"https://github.com/firekula/DFLegacy/releases/download/{tag}/{name}"
            };
            if (url.Host == "api.github.com" && url.AbsolutePath == "/repos/firekula/DFLegacy/releases")
                return Json(new[] {
                    new { tag_name = "stable-fixture", assets = new[] { Attachment(stableAsset, first.ManifestBytes, "stable-fixture", 1) } },
                    new { tag_name = second.Tag, assets = new[] { Attachment(chapterAsset, second.ManifestBytes, second.Tag, 2) } }
                });
            if (url.Host == "api.github.com" && url.AbsolutePath == "/repos/firekula/DFLegacy/releases/tags/v4-test")
                return Json(new { tag_name = "v4-test", assets = parts.Select((part, i) => Attachment(part.Key, part.Value, "v4-test", i + 3)) });
            if (url.Host == "github.com" && downloads.TryGetValue(Path.GetFileName(url.AbsolutePath), out var bytes))
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            throw new InvalidOperationException("Unexpected GitHub fixture URL: " + url);
        }));
        try
        {
            Seed(root, first, PublicFirstDescriptor, firstFiles);
            const string firstConfig = """
                {"EntranceHost":"chapter-one.local","Theme":"dark","Account":"first","Password":"old",
                 "Accounts":[{"Account":"first","Password":"old","DisplayName":"saved"}],"AccountIndex":0}
                """;
            const string priorConfig = """
                {"UpdateProvider":"gitee","UpdateRepository":"other","UpdateChannel":"c2-test","UpdateFlavor":"test",
                 "ChapterTwoServerIpv4":"192.0.2.5","chapterTwoServerHost":"test.invalid","StartServer":true,
                 "ClientPath":"other.exe","DisableGitHubUpdates":true,"UpdateServiceUrl":"http://test.invalid",
                 "ChapterTwoDebugEnabled":true,"ChapterTwoTestMuteEnabled":true,
                 "ChapterTwoDisplay":{"Width":1024,"Height":768,"Filter":"point"}}
                """;
            Put(root, "launcher.json", firstConfig);
            Put(root, "launcher.chapter2.json", priorConfig);
            var engine = new UpdateEngine(root, new ReleaseProvider(http, official));
            TestRun.Reject(() => engine.CheckChapterUpgradeAsync(false).GetAwaiter().GetResult(), "implicit public chapter selection");
            TestRun.Reject(() => engine.CheckAsync().GetAwaiter().GetResult(), "ordinary public update across chapters");
            var check = engine.CheckChapterUpgradeAsync(true).GetAwaiter().GetResult();
            TestRun.Check(check.CanLaunch && check.NeedsInstall && check.Target?.Source == official, "public GitHub lookup selects the matching Chapter Two manifest");
            var staged = engine.DownloadAsync(check).GetAwaiter().GetResult();
            TestRun.Check(File.ReadAllText(Path.Combine(root, "launcher.json")) == firstConfig
                && File.ReadAllText(Path.Combine(root, "launcher.chapter2.json")) == priorConfig,
                "public discovery and download leave both configuration files untouched");
            InstallTransaction.ApplyChapterUpgrade(root, staged, official, true, firstDisplay: new(800, 600, "sharp"), deferReadiness: true);
            InstallTransaction.ValidateChapterReadyProbe(root, second.ManifestSha256);
            InstallTransaction.ConfirmChapterReady(root, second.ManifestSha256);
            using (var migrated = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "launcher.chapter2.json"))))
            {
                var config = migrated.RootElement;
                TestRun.Check(config.GetProperty("UpdateProvider").GetString() == "github"
                    && config.GetProperty("UpdateRepository").GetString() == "firekula/DFLegacy"
                    && config.GetProperty("UpdateChannel").GetString() == "chapter-two"
                    && config.GetProperty("UpdateFlavor").GetString() == "normal", "public settings bind to the verified target");
                TestRun.Check(config.EnumerateObject().Single(prop => prop.Name.Equals("ChapterTwoServerHost", StringComparison.OrdinalIgnoreCase)).Value.GetString() == "games.firekula.site"
                    && !config.TryGetProperty("ChapterTwoServerIpv4", out _) && !config.GetProperty("StartServer").GetBoolean()
                    && config.GetProperty("ClientPath").GetString() == "DNF.exe" && !config.GetProperty("DisableGitHubUpdates").GetBoolean()
                    && config.GetProperty("UpdateServiceUrl").GetString() == "", "public settings replace conflicting launch, endpoint and update overrides");
                TestRun.Check(!config.GetProperty("ChapterTwoDebugEnabled").GetBoolean()
                    && !config.GetProperty("ChapterTwoTestMuteEnabled").GetBoolean(), "public settings disable debug and test mute");
                TestRun.Check(config.GetProperty("Theme").GetString() == "dark" && config.GetProperty("ChapterTwoDisplay").GetProperty("Width").GetInt32() == 1024
                    && config.GetProperty("Accounts")[0].GetProperty("DisplayName").GetString() == "saved"
                    && config.GetProperty("AccountIndex").GetInt32() == 0 && config.GetProperty("Account").GetString() == "first"
                    && config.GetProperty("Password").GetString() == "old", "public configuration retains account fields and display preferences");
            }
            TestRun.Check(File.ReadAllText(Path.Combine(root, "launcher.json")) == firstConfig, "public activation preserves Chapter One configuration");
            Put(root, "launcher.chapter2.json", """{"Account":"new","Password":"changed"}""");
            ChapterTransition.Rollback(root, official);
            using var restored = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "launcher.json")));
            TestRun.Check(new InstallationStore(root).ReadInstalled()?.Release.Source == firstSource && ChapterTransition.RequiresExplicitUpgrade(root)
                && restored.RootElement.GetProperty("Password").GetString() == "changed"
                && restored.RootElement.GetProperty("EntranceHost").GetString() == "chapter-one.local",
                "public rollback restores the stable receipt and settings and propagates updated credentials");
            TestRun.Reject(() => InstallTransaction.ApplyChapterUpgrade(root, staged, official, false), "public re-upgrade without selection");
            InstallTransaction.ApplyChapterUpgrade(root, staged, official, true);
            TestRun.Check(engine.ValidateInstalledAsync().GetAwaiter().GetResult().CanLaunch, "explicit public re-upgrade validates after rollback");
        }
        finally { Directory.Delete(root, true); }
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value))
    };

    private sealed class GitHubFixture(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class Interrupt : IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress _) => throw new OperationCanceledException();
    }

    private static void StopInstallerAfterConfigurationWrite(string root, StagedUpdate staged)
    {
        var fixtureDirectory = Path.Combine(root, ".dflegacy");
        var release = Path.Combine(fixtureDirectory, "test-release.json");
        var signal = Path.Combine(fixtureDirectory, "test-child-ready");
        File.WriteAllBytes(release, JsonSerializer.SerializeToUtf8Bytes(staged.Release, ManifestCodec.Json));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(ChapterTransitionTests).Assembly.Location);
        foreach (var argument in new[] { "--chapter-install-child", root, staged.CacheDirectory, release, signal })
            start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("Chapter installer test process did not start.");
        try
        {
            var wait = Stopwatch.StartNew();
            while (!File.Exists(signal) && !child.HasExited && wait.Elapsed < TimeSpan.FromSeconds(20)) Thread.Sleep(20);
            TestRun.Check(File.Exists(signal) && File.ReadAllText(signal) == child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "controlled child stopped after writing chapter configuration");
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            if (!child.WaitForExit(5000)) throw new IOException("Chapter installer test process did not exit.");
        }
    }

    private sealed class AtPhase(string phase, string path, Action? action = null) : IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress progress)
        {
            if (progress.Phase != phase || progress.Path != path) return;
            if (action == null) throw new OperationCanceledException();
            action();
        }
    }

    private static VerifiedRelease WithRetirement(VerifiedRelease release)
    {
        var bytes = ManifestCodec.Serialize(release.Manifest with { Retired = [new("DFLegacy.Client.dll", "delete", 3)] });
        return release with { ManifestBytes = bytes, ManifestSha256 = TestRun.Hash(bytes) };
    }

    private static void Seed(string root, VerifiedRelease release, string descriptor, params (string Path, string Content)[] files)
    {
        Put(root, "DFLegacy.Chapter.json", descriptor);
        foreach (var (path, content) in files) Put(root, path, content);
        var receipt = new InstalledReceipt(release, release.Manifest.Files.Select(file => file.Path).ToArray());
        Put(root, ".dflegacy/v4/installed.json", System.Text.Json.JsonSerializer.Serialize(receipt, ManifestCodec.Json));
    }

    private static void Put(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void IdentityIsBoundToManagedManifest()
    {
        const string descriptor = """
            {"schema":1,"chapter":"chapter2","entry":"DFLegacy.Launcher.exe",
             "requiredFiles":["DFLegacy.Launcher.exe","DNF.exe","Script.pvf"]}
            """;
        var release = Release("chapter-two", 2, descriptor, ("DFLegacy.Launcher.exe", "launcher"),
            ("DNF.exe", "game"), ("Script.pvf", "chapter-two-pvf"));
        var identity = ClientChapterIdentity.Parse(release, TestRun.Bytes(descriptor));
        TestRun.Check(identity.Chapter == "chapter2" && identity.Entry == "DFLegacy.Launcher.exe",
            "verified chapter descriptor identifies the existing launch entry");
        TestRun.Reject(() => ClientChapterIdentity.Parse(release,
            TestRun.Bytes(descriptor.Replace("chapter2", "chapter1"))), "chapter identity not bound to manifest digest");
        var incomplete = Release("chapter-two", 2, descriptor, ("DFLegacy.Launcher.exe", "launcher"),
            ("Script.pvf", "chapter-two-pvf"));
        TestRun.Reject(() => ClientChapterIdentity.Parse(incomplete, TestRun.Bytes(descriptor)),
            "descriptor names a required file missing from the managed manifest");
        var mixed = Release("chapter-two", 2, descriptor, ("DFLegacy.Launcher.exe", "launcher"),
            ("DNF.exe", "game"), ("Script.pvf", "chapter-two-pvf"), ("debug.dll", "first-chapter-debug"));
        TestRun.Reject(() => ClientChapterIdentity.Parse(mixed, TestRun.Bytes(descriptor)),
            "second-chapter manifest retains an active C1 debug module");
    }

    private static VerifiedRelease Release(string channel, int sequence, string descriptor,
        params (string Path, string Content)[] files) =>
        Release(new ReleaseSource("gitee", "firekula/DFLegacy", channel, "x64", "test"), sequence, descriptor, files);

    private static VerifiedRelease Release(ReleaseSource source, int sequence, string descriptor,
        params (string Path, string Content)[] files)
    {
        var manifest = ManifestTests.Sample() with
        {
            Architecture = source.Architecture,
            Channel = source.Channel,
            Flavor = source.Flavor,
            Sequence = sequence,
            Files = [ManifestTests.Entry("DFLegacy.Chapter.json", descriptor),
                .. files.Select(file => ManifestTests.Entry(file.Path, file.Content))],
            Retired = [],
            Migrations = []
        };
        var bytes = ManifestCodec.Serialize(manifest);
        return new(source, manifest.Version, TestRun.Hash(bytes), bytes);
    }
}
