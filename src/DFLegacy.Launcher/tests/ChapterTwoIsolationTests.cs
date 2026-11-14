using System.Diagnostics;
using System.Text.Json.Nodes;
using DFLegacy.Diagnostics;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Diagnostics;

internal static class ChapterTwoIsolationTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        check(ChapterTwoClient.IsBundled, "isolation checks exercise a bundled Chapter Two launcher");
        if (!ChapterTwoClient.IsBundled) return;

        ConfigurationStaysLocal(root, check);
        ConfigurationCanOptIntoRemote(root, check);
        ConfigurationUsesDomainRelay(root, check);
        AutomaticDiagnosticsStayInactive(root, check);
        await UpdateEntriesLeaveTargetUntouched(root, check);
    }

    private static void ConfigurationUsesDomainRelay(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "chapter-two-domain.json");
        File.WriteAllText(path, """{"ChapterTwoServerHost":"game.example.test","Relay":{"Preference":"ipv4"}}""");
        var store = new LauncherAccountStore(path);
        var settings = store.Load();
        check(settings.EntranceHost == "game.example.test" && settings.Relay.Enabled && settings.Relay.Preference == "ipv4"
            && settings.ResolveAccountServiceUrl() == "http://game.example.test:48091", "domain profile survives account-store normalization");
        store.Save(settings);
        check(store.Load().ChapterTwoServerHost == "game.example.test", "saving account/settings retains domain relay choice");
        using var lease = store.AcquireReadLease();
        check(lease.Options.Relay.Enabled && lease.Options.EntranceHost == "game.example.test", "launch read lease uses the same domain profile");
    }

    private static void ConfigurationCanOptIntoRemote(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "chapter-two-remote.json");
        File.WriteAllText(path, """
            {"ChapterTwoServerIpv4":"192.0.2.20", "EntranceHost":"198.51.100.99", "StartServer":true}
            """);
        var store = new LauncherAccountStore(path);
        var selected = store.Load();
        check(selected.EntranceHost == "192.0.2.20" && selected.EntrancePort == 42312
            && selected.ResolveAccountServiceUrl() == "http://192.0.2.20:48091" && !selected.StartServer
            && selected.SelectedServerId == "chapter-two-remote", "Chapter Two routes only to the explicitly opted-in remote server");
        using (var lease = store.AcquireReadLease())
            check(lease.Options.EntranceHost == "192.0.2.20", "Chapter Two read lease preserves the remote target");
        File.WriteAllText(path, """{"ChapterTwoServerIpv4":"127.0.0.2"}""");
        try { store.Load(); check(false, "Chapter Two rejects a loopback remote override"); }
        catch (InvalidDataException) { check(true, "Chapter Two rejects a loopback remote override"); }
    }

    private static void ConfigurationStaysLocal(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "chapter-two-config");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "launcher.chapter2.json");
        File.WriteAllText(path, """
            {
              "ClientPath": "other-client.exe",
              "EntranceHost": "remote.invalid",
              "DisableGitHubUpdates": false,
              "UpdateRepository": "other/client",
              "Diagnostics": {
                "Enabled": true,
                "ServiceUrl": "https://remote.invalid",
                "FuturePolicy": { "Value": "preserve nested policy" }
              },
              "ChapterTwoDialogue": { "FontSize": 14, "LineGap": 2 },
              "FutureConfig": { "Value": "preserve" }
            }
            """);
        var store = new LauncherAccountStore(path);
        var first = store.Load();
        check(first.ClientPath == "DNF.exe" && first.EntranceHost == "127.0.0.2"
            && first.EntrancePort == 42312 && first.ResolveAccountServiceUrl() == "http://127.0.0.2:48091",
            "copied configuration cannot redirect the bundled Chapter Two client or service");
        check(first.DisableGitHubUpdates && first.UpdateRepository == "" && first.UpdateServiceUrl == "",
            "copied first chapter update sources are disabled");
        check(!first.Diagnostics.Enabled && first.Diagnostics.ServiceUrl == "",
            "copied automatic diagnostics destinations are disabled for the bundled local profile");
        using (var lease = store.AcquireReadLease())
            check(lease.Options.ClientPath == "DNF.exe" && lease.Options.EntranceHost == "127.0.0.2",
                "configuration read leases enforce the same Chapter Two target as normal loads");

        var second = store.Load();
        second.ChapterTwoDialogue = new(18, 1);
        store.Save(second);
        var external = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        external["FutureConfig"]!["Value"] = "external change";
        File.WriteAllText(path, external.ToJsonString());
        first.Theme = "dark";
        store.Save(first);
        var merged = store.Load();
        check(merged.Theme == "dark" && merged.ChapterTwoDialogue == new ChapterTwoDialogueOptions(18, 1),
            "a stale settings save preserves another writer's Chapter Two dialogue change");
        check(JsonNode.Parse(File.ReadAllText(path))!["FutureConfig"]!["Value"]!.GetValue<string>() == "external change",
            "a settings save retains another writer's unknown configuration fields");
        check(JsonNode.Parse(File.ReadAllText(path))!["Diagnostics"]?["FuturePolicy"]?["Value"]?.GetValue<string>()
            == "preserve nested policy", "settings saves preserve unknown fields inside a known configuration object");
    }

    private static void AutomaticDiagnosticsStayInactive(string root, Action<bool, string> check)
    {
        var settings = new DiagnosticSettings(Path.Combine(root, "chapter-two-diagnostic-user"));
        var client = Path.Combine(root, "chapter-two-diagnostic-client", "DNF.exe");
        var options = new LauncherOptions { ClientPath = client };
        options.Diagnostics.ServiceUrl = "https://remote.invalid";
        var helperStarts = 0;
        AutomaticDiagnostics.Configure(options, settings, _ => { helperStarts++; return null; }, root);
        check(AutomaticDiagnostics.Session is null && helperStarts == 0,
            "bundled Chapter Two never registers an automatic diagnostics session or starts an upload helper");
        check(!Directory.Exists(settings.Root),
            "bundled Chapter Two does not write the user's automatic diagnostics state");
        AutomaticDiagnostics.Session?.Complete();
    }

    private static async Task UpdateEntriesLeaveTargetUntouched(string root, Action<bool, string> check)
    {
        var executable = Path.ChangeExtension(typeof(App).Assembly.Location, ".exe");
        foreach (var kind in new[] { "bootstrap", "repair", "apply" })
        {
            var directory = Path.Combine(root, "chapter-two-update-" + kind);
            Directory.CreateDirectory(directory);
            var configuration = Path.Combine(directory, "launcher.json");
            const string original = "{invalid configuration, preserved verbatim";
            File.WriteAllText(configuration, original);
            var arguments = kind switch
            {
                "bootstrap" => new[] { "--bootstrap-client", directory, "--", "--no-dialog" },
                "repair" => new[] { "--repair-client", directory, "--no-dialog" },
                _ => new[] { "--dflegacy-apply-update", "--no-dialog" }
            };
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Launcher fixture did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("Bundled Chapter Two update entry did not reject " + kind + ".");
            }
            await stdout;
            await stderr;
            check(process.ExitCode == 1, "bundled Chapter Two rejects the first chapter " + kind + " entry");
            check(File.ReadAllText(configuration) == original
                && Directory.GetFileSystemEntries(directory).Length == 1,
                "rejected " + kind + " entry leaves the requested installation untouched");
        }
    }
}
