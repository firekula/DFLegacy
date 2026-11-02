using DFLegacy.Manager;

internal static class LogRecoveryTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-log-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new ManagerPaths(root);
            var first = new ManagerLog(paths);
            first.Write("server", "game started");
            first.Write("export", "client package complete");
            var cursor = first.Read(0).Last().Id;
            var recovered = new ManagerLog(paths);
            var history = recovered.Read(0);
            if (!history.Any(x => x.Text == "game started") || !history.Any(x => x.Text == "client package complete"))
                throw new Exception("Restart must restore recent runtime and export logs.");
            recovered.Write("manager", "manager restarted");
            if (!recovered.Read(cursor).Any(x => x.Text == "manager restarted"))
                throw new Exception("A browser cursor from before restart must receive new logs.");
            recovered.Write("server", "sensitive bootstrap");
            recovered.AddSecret("sensitive bootstrap");
            if (recovered.Read(0).Any(x => x.Text.Contains("sensitive bootstrap")))
                throw new Exception("Recovered and buffered logs must redact registered secrets.");
            Console.WriteLine("PASS log recovery, restart cursors and secret redaction");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + error.Message); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
