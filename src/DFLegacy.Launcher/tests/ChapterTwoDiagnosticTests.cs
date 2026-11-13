using System.Diagnostics;
using System.Reflection;
using DFLegacy.Launcher;

internal static class ChapterTwoDiagnosticTests
{
    internal static async Task<int?> Dispatch(string[] args)
    {
        if (args.Length != 3 || args[0] != "--chapter-two-diagnostic-fixture") return null;
        var root = args[1];
        var mode = args[2];
        var image = Path.Combine(root, "s1c2_chapter2_host-fixture.exe");
        var events = Path.Combine(root, "Logs", "DFLegacy", "chapter-two-events-fixture.log");
        Console.WriteLine("CHAPTER_TWO_ATTEMPT s1c2_chapter2_host-fixture.exe chapter-two-client-fixture.log chapter-two-events-fixture.log");
        Console.Out.Flush();
        using var child = Process.Start(new ProcessStartInfo(image)
        {
            UseShellExecute = false, CreateNoWindow = true,
            Arguments = "/d /c \"ping -n 3 127.0.0.1 > nul & exit /b " + (mode == "normal" ? "0" : "5") + "\""
        })!;
        await File.WriteAllTextAsync(events, $"START {child.Id}\n");
        if (mode == "early") await child.WaitForExitAsync();
        Console.WriteLine($"OWNED_PID {child.Id}");
        // Raw game debug strings must never provide native diagnostic evidence.
        Console.WriteLine($"EXCEPTION code=c0000005 first=0 at=0000000012345678 count=2 info=1,24");
        Console.WriteLine($"OWNED_PID {Environment.ProcessId}");
        Console.Out.Flush();
        await child.WaitForExitAsync();
        if (mode.StartsWith("native", StringComparison.Ordinal))
            await File.AppendAllTextAsync(events, $"EXCEPTION {child.Id} 7 C0000005 0000000012345678\n");
        await File.AppendAllTextAsync(events, $"EXIT {child.Id} {child.ExitCode:X8}\n");
        Console.WriteLine("CHAPTER_TWO_FINISH " + (mode.EndsWith("retry", StringComparison.Ordinal) ? "retry" : "final"));
        return 0;
    }

    internal static async Task Run(string root, Action<bool, string> check, string? nativeFixture = null)
    {
        var savedClient = DFLegacy.Launcher.Program.LogClientPath;
        try
        {
            foreach (var mode in nativeFixture is null ? new[] { "normal", "abnormal", "native", "early", "retry", "native-retry" } : new[] { "normal", "abnormal", "native" })
            {
                var game = Path.Combine(root, "chapter-two-diagnostics-" + mode);
                var logs = Path.Combine(game, "Logs", "DFLegacy");
                Directory.CreateDirectory(logs);
                File.Copy(nativeFixture ?? Path.Combine(Environment.SystemDirectory, "cmd.exe"), Path.Combine(game, "s1c2_chapter2_host-fixture.exe"));
                DFLegacy.Launcher.Program.LogClientPath = Path.Combine(game, "DNF.exe");
                var start = new ProcessStartInfo(nativeFixture ?? Environment.ProcessPath!)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                foreach (var argument in nativeFixture is null ? new[] { "--chapter-two-diagnostic-fixture", game, mode } : new[] { game, mode }) start.ArgumentList.Add(argument);
                using var bootstrap = Process.Start(start)!;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var observer = typeof(ChapterTwoClient).GetMethod("ObserveClientOutputAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                try
                {
                    await (Task)observer.Invoke(null, [bootstrap, game, null, null, timeout.Token])!;
                    await bootstrap.WaitForExitAsync(timeout.Token);
                }
                finally { if (!bootstrap.HasExited) { bootstrap.Kill(entireProcessTree: true); await bootstrap.WaitForExitAsync(); } }
                var incidents = Path.Combine(logs, "Incidents");
                var reports = Directory.Exists(incidents) ? Directory.GetFiles(incidents, "crash-*.log").Select(File.ReadAllText).ToArray() : [];
                check(reports.Length == (mode is "normal" or "retry" ? 0 : mode.StartsWith("native", StringComparison.Ordinal) ? 2 : 1), mode + " produces only the expected local crash reports");
                check(File.ReadAllText(Path.Combine(logs, "launcher.log")).Contains("diagnostics=local-only") || mode == "early", "live game binding is recorded when the child remains alive");
                if (nativeFixture is not null)
                {
                    var evidence = Path.Combine(Path.GetDirectoryName(nativeFixture)!, "evidence", mode);
                    Directory.CreateDirectory(evidence);
                    foreach (var file in Directory.GetFiles(logs, "*.log", SearchOption.AllDirectories))
                        File.Copy(file, Path.Combine(evidence, Path.GetFileName(file)), overwrite: true);
                }
                if (mode is "normal" or "retry") continue;
                var journal = File.ReadAllLines(Path.Combine(logs, "chapter-two-events-fixture.log"));
                var pid = journal[0].Split(' ')[1];
                var expectedExit = nativeFixture is not null && mode == "native" ? "0xC0000005" : "0x00000005";
                check(reports.Any(r => r.Contains("gamePid=" + pid) && r.Contains("exitCode=" + expectedExit)), "exit report uses actual game identity rather than bootstrap identity");
                if (mode.StartsWith("native", StringComparison.Ordinal))
                    check(reports.Any(r => r.Contains("evidence=second-chance-debug-event") && r.Contains("exceptionCode=0xC0000005")) &&
                        reports.Any(r => r.Contains("nativeReport=crash-")), "trusted native event is retained and linked to its exit report");
                else check(reports.All(r => !r.Contains("exceptionCode=0xC0000005")), "raw stdout cannot forge a native exception");
            }
        }
        finally { DFLegacy.Launcher.Program.LogClientPath = savedClient; }
    }
}
