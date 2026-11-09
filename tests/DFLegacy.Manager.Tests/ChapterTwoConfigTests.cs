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
            return failures == 0 ? 0 : 1;
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
