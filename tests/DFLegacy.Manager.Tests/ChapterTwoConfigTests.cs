using DFLegacy.Manager;

internal static class ChapterTwoConfigTests
{
    internal static int Run()
    {
        var failures = 0;
        void Check(bool passed, string name)
        {
            Console.WriteLine((passed ? "PASS" : "FAIL") + ": " + name);
            if (!passed) failures++;
        }
        var root = Path.Combine(Path.GetTempPath(), "dflegacy chapter two manager " + Guid.NewGuid().ToString("N"));
        var paths = new ManagerPaths(root);
        try
        {
            var templates = Directory.CreateDirectory(Path.Combine(root, "templates"));
            File.WriteAllText(Path.Combine(templates.FullName, "manager.json"), """
                {"ChapterTwoLocal":true,"AdvertisedHost":"127.0.0.2","AccountServiceUrl":"http://127.0.0.2:48091","Configured":true}
                """);
            File.WriteAllText(Path.Combine(templates.FullName, "server.json"), """
                {"DFLegacy":{"Entrance":{"Host":"127.0.0.2"},"Admin":{"Host":"127.0.0.2","Port":48091},
                "GameplayDatagram":{"AdvertisedHost":"127.0.0.2"},"FallbackRelay":{"AdvertisedHost":"127.0.0.2"},
                "WebPanels":{"Gm":{"Enabled":false},"Player":{"Enabled":false}}}}
                """);
            var store = new ConfigurationStore(paths, new Dictionary<string, string?> { ["DFLegacy__Entrance__Host"] = "0.0.0.0" });
            var initial = store.Read();
            Check(initial.Configured && initial.ChapterTwoLocal && initial.Saved.AdvertisedHost == "127.0.0.2"
                && initial.Saved.AccountServiceUrl == "http://127.0.0.2:48091", "Chapter Two package can supply a configured manager template");
            Check(initial.Effective.BindHost == "127.0.0.2" && initial.EnvironmentOverrides.Length == 0,
                "Chapter Two local settings ignore inherited service overrides");
            var edited = store.Save(initial.Saved with { MonsterExperienceMultiplier = 2 });
            Check(edited.ChapterTwoLocal && edited.Saved.MonsterExperienceMultiplier == 2, "Chapter Two retains editable gameplay settings");
            var enabled = store.Save(edited.Saved with { ChapterTwoTestTopUpEnabled = true });
            Check(store.ReadServerDocument()["DFLegacy"]!["EnableTestCeraTopUp"]?.GetValue<bool>() == true
                && enabled.Saved.ChapterTwoTestTopUpEnabled,
                "Chapter Two manager enables and reads back the test top-up switch");
            store.Save(edited.Saved);
            Check(store.ReadServerDocument()["DFLegacy"]!["EnableTestCeraTopUp"]?.GetValue<bool>() == false,
                "Chapter Two manager can turn the test top-up switch back off");
            try
            {
                store.Save(edited.Saved with { BindHost = "0.0.0.0" });
                Check(false, "Chapter Two refuses to replace its local listener with a wildcard");
            }
            catch (ArgumentException)
            {
                Check(store.Read().Saved.BindHost == "127.0.0.2", "Chapter Two refuses to replace its local listener with a wildcard");
            }

            var remote = new ConfigurationStore(paths, new Dictionary<string, string?>
            {
                ["DFLEGACY_CHAPTER_TWO_PUBLIC_IPV4"] = "192.0.2.20",
                ["DFLegacy__Pvp__Enabled"] = "false"
            });
            var remoteConfig = remote.Read().Effective;
            Check(remoteConfig is { BindHost: "0.0.0.0", AdminHost: "0.0.0.0", PvpEnabled: true,
                    GameplayAdvertisedHost: "192.0.2.20", FallbackRelayAdvertisedHost: "192.0.2.20",
                    AccountServiceUrl: "http://192.0.2.20:48091", ChapterTwoTestTopUpEnabled: false },
                "explicit Chapter Two remote setup binds player listeners and advertises the specified IPv4");
            Check(remoteConfig.ForwardedTcpPorts().ToHashSet().IsSupersetOf([7004, 7110, 7111, 7112]),
                "Chapter Two remote setup enables all four PK channels");
            Check(new ConfigurationStore(paths, new Dictionary<string, string?>()).Read().Effective is
                { BindHost: "127.0.0.2", PvpEnabled: false }, "reading remote configuration leaves local profile files usable");
            try
            {
                _ = new ConfigurationStore(paths, new Dictionary<string, string?>
                    { ["DFLEGACY_CHAPTER_TWO_PUBLIC_IPV4"] = "127.0.0.2" });
                Check(false, "the remote deployment rejects a loopback destination");
            }
            catch (ArgumentException) { Check(true, "the remote deployment rejects a loopback destination"); }

            var domain = new ConfigurationStore(paths, new Dictionary<string, string?>
            {
                ["DFLEGACY_CHAPTER_TWO_SERVER_HOST"] = "game.example.test"
            });
            var domainConfig = domain.Read().Effective;
            Check(domainConfig is { BindHost: "::", AdminHost: "::", RelayEnabled: true, PvpEnabled: true,
                AdvertisedHost: "game.example.test", GameplayAdvertisedHost: "127.0.0.1",
                FallbackRelayAdvertisedHost: "127.0.0.1", AccountServiceUrl: "http://game.example.test:48091" },
                "domain deployment listens on both families and advertises stable player-local addresses");
            Check(domain.Read().ChapterTwoRemote, "domain deployment is marked remote");
            foreach (var invalid in new[] { "https://game.example.test", "0.0.0.0", "::", "127.0.0.1", " game.example.test", "" })
            {
                try
                {
                    _ = new ConfigurationStore(paths, new Dictionary<string, string?> { ["DFLEGACY_CHAPTER_TWO_SERVER_HOST"] = invalid });
                    Check(false, "domain profile rejects invalid destination " + invalid);
                }
                catch (ArgumentException) { Check(true, "domain profile rejects invalid destination " + invalid); }
            }
            var ipv6 = new ConfigurationStore(paths, new Dictionary<string, string?>
                { ["DFLEGACY_CHAPTER_TWO_SERVER_HOST"] = "2001:db8::20" }).Read().Effective;
            Check(ipv6.AccountServiceUrl == "http://[2001:db8::20]:48091", "IPv6 account service is bracketed");
            var v4Bind = new ConfigurationStore(paths, new Dictionary<string, string?>
            {
                ["DFLEGACY_CHAPTER_TWO_SERVER_HOST"] = "game.example.test",
                ["DFLEGACY_CHAPTER_TWO_BIND_HOST"] = "0.0.0.0"
            }).Read().Effective;
            Check(v4Bind.BindHost == "0.0.0.0" && v4Bind.GameplayAdvertisedHost == "127.0.0.1", "IPv4-only listening keeps domain relay mode");
            try
            {
                _ = new ConfigurationStore(paths, new Dictionary<string, string?>
                {
                    ["DFLEGACY_CHAPTER_TWO_SERVER_HOST"] = "game.example.test",
                    ["DFLEGACY_CHAPTER_TWO_PUBLIC_IPV4"] = "192.0.2.20"
                });
                Check(false, "conflicting legacy and domain modes are rejected");
            }
            catch (ArgumentException) { Check(true, "conflicting legacy and domain modes are rejected"); }
            var switches = new ConfigurationStore(paths, new Dictionary<string, string?>
            {
                ["DFLEGACY_CHAPTER_TWO_SERVER_HOST"] = "game.example.test",
                ["DFLegacy__EnableTestCeraTopUp"] = "true",
                ["DFLegacy__Channel__RegionalChannelsEnabled"] = "false",
                ["DFLegacy__Fatigue__WeekendDoubleEnabled"] = "false"
            });
            Check(switches.Read().Effective is { ChapterTwoTestTopUpEnabled: true, RegionalChannelsEnabled: false },
                "explicit domain deployment gameplay switches are visible in effective settings");
            Directory.CreateDirectory(paths.ServerDirectory);
            File.WriteAllBytes(Path.Combine(paths.ServerDirectory, "DFLegacy.Server.dll"), []);
            var supervisor = new ServerSupervisor(paths, switches, new ManagerLog(paths));
            try
            {
                var start = (System.Diagnostics.ProcessStartInfo)typeof(ServerSupervisor)
                    .GetMethod("CreateStartInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(supervisor, null)!;
                Check(start.Environment["DFLegacy__Entrance__Host"] == "::"
                    && start.Environment["DFLegacy__GameplayDatagram__AdvertisedHost"] == "127.0.0.1"
                    && start.Environment["DFLegacy__FallbackRelay__AdvertisedHost"] == "127.0.0.1"
                    && start.Environment["DFLEGACY_UDP_RELAY"] == "1" && start.Environment["DFLegacy__Pvp__Enabled"] == "true",
                    "actual managed child receives stable addresses and all gameplay relay/PK settings");
                Check(start.Environment["DFLegacy__EnableTestCeraTopUp"] == "true"
                    && start.Environment["DFLegacy__ChapterTwoLocalTestPaymentsOnly"] == "false"
                    && start.Environment["DFLegacy__Channel__RegionalChannelsEnabled"] == "false"
                    && start.Environment["DFLegacy__Fatigue__WeekendDoubleEnabled"] == "false",
                    "actual managed child honors explicit test payment, region and fatigue choices");
            }
            finally { supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            return failures == 0 ? 0 : 1;
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
