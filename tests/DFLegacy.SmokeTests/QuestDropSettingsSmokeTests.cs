using DFLegacy.Server;

internal static class QuestDropSettingsSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-quest-rate-");
        try
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(root.FullName, "state.json"),
                Drop = new() { QuestItemRatePercent = 100 }
            };
            var settings = new QuestDropSettings(options);
            check(settings.RatePercent == 100 && settings.IsPersistent
                    && settings.ScaleChance(2) == 2
                    && settings.ScaleChance(40) == 40
                    && settings.ScaleChance(100) == 100
                    && settings.ScaleChance(0) == 0,
                "the default 100% keeps every PVF quest chance unchanged");

            check(await settings.SetRateAsync(250)
                    && settings.RatePercent == 250
                    && settings.ScaleChance(2) == 5
                    && settings.ScaleChance(40) == 100,
                "a 250% rate scales 2% to 5% and clamps 40% to 100%");
            check(await settings.SetRateAsync(0) && settings.ScaleChance(100) == 0,
                "a 0% rate disables quest task-item drops");
            check(!await settings.SetRateAsync(1001) && settings.RatePercent == 0,
                "an out-of-range rate is rejected without changing the current value");

            check(await settings.SetRateAsync(50), "a 50% rate persists");
            var reloaded = new QuestDropSettings(options);
            check(reloaded.RatePercent == 50
                    && File.Exists(Path.Combine(root.FullName, "settings", "quest-drop.json")),
                "the persisted rate is restored by a new instance and lives under the data directory");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch (IOException) { }
        }

        var fallbackRoot = Directory.CreateTempSubdirectory("dflegacy-quest-rate-default-");
        try
        {
            var configured = new QuestDropSettings(new ServerOptions
            {
                DataPath = Path.Combine(fallbackRoot.FullName, "state.json"),
                Drop = new() { QuestItemRatePercent = 300 }
            });
            check(configured.RatePercent == 300 && configured.ScaleChance(2) == 6,
                "server.json supplies the initial rate when no runtime file exists");
        }
        finally
        {
            try { fallbackRoot.Delete(recursive: true); } catch (IOException) { }
        }
    }
}
