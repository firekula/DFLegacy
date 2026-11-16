using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DFLegacy.Diagnostics;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
        Console.WriteLine($"LOCAL_GUILD_OPEN {Environment.ProcessId}");
        Console.WriteLine($"LOCAL_GUILD_OPEN {child.Id} http://example.invalid/");
        Console.WriteLine($"LOCAL_GUILD_OPEN {child.Id}");
        // Raw game debug strings must never provide native diagnostic evidence.
        Console.WriteLine($"EXCEPTION code=c0000005 first=0 at=0000000012345678 count=2 info=1,24");
        Console.WriteLine($"OWNED_PID {Environment.ProcessId}");
        Console.Out.Flush();
        await child.WaitForExitAsync();
        if (mode.StartsWith("native", StringComparison.Ordinal))
            await File.AppendAllTextAsync(events, $"EXCEPTION {child.Id} 7 C0000005 0000000012345678 msctf.dll 0000000000005678\n");
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
                var projected = new List<DiagnosticIncident>();
                DFLegacy.Launcher.Program.Incidents.AutomaticReport = projected.Add;
                var start = new ProcessStartInfo(nativeFixture ?? Environment.ProcessPath!)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                foreach (var argument in nativeFixture is null ? new[] { "--chapter-two-diagnostic-fixture", game, mode } : new[] { game, mode }) start.ArgumentList.Add(argument);
                using var bootstrap = Process.Start(start)!;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var observer = typeof(ChapterTwoClient).GetMethod("ObserveClientOutputAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                var browserUrls = new List<string>();
                await using var guild = await ChapterTwoGuildSite.StartAsync("http://127.0.0.1:1", timeout.Token, url =>
                {
                    browserUrls.Add(url);
                    return Task.CompletedTask;
                });
                try
                {
                    var reported = await (Task<bool>)observer.Invoke(null, [bootstrap, game, null, guild, null, timeout.Token])!;
                    await bootstrap.WaitForExitAsync(timeout.Token);
                    check(reported == (mode is not ("normal" or "retry")), "observer identifies only verified game faults to the startup caller");
                    if (reported) DFLegacy.Launcher.Program.ReportError("launcher-ui", new ChapterTwoGameExitException("fixture wrapper error"));
                }
                finally { if (!bootstrap.HasExited) { bootstrap.Kill(entireProcessTree: true); await bootstrap.WaitForExitAsync(); } }
                if (nativeFixture is null)
                    check(mode == "early" ? browserUrls.Count == 0 : browserUrls.SequenceEqual([guild.Url]),
                        mode + " dispatches the assigned guild URL only for an exact request from the active owned game");
                var incidents = Path.Combine(logs, "Incidents");
                var reports = Directory.Exists(incidents) ? Directory.GetFiles(incidents, "crash-*.log").Select(File.ReadAllText).ToArray() : [];
                check(reports.Length == (mode is "normal" or "retry" ? 0 : mode.StartsWith("native", StringComparison.Ordinal) ? 2 : 1), mode + " produces only the expected local crash reports");
                check(File.ReadAllText(Path.Combine(logs, "launcher.log")).Contains("Bound Chapter Two game process") || mode == "early", "live game binding is recorded when the child remains alive");
                var expectedCode = mode.StartsWith("native", StringComparison.Ordinal) ? "0xC0000005" : "0x00000005";
                if (mode is "normal" or "retry") check(projected.Count == 0, mode + " does not enter the automatic report queue");
                else
                {
                    check(projected.Count == 1 && projected[0].Architecture == "x86" && projected[0].ExceptionCode == expectedCode,
                        mode + " projects exactly one report with the original game code and architecture");
                    if (mode.StartsWith("native", StringComparison.Ordinal))
                        check(projected.Count == 1 && projected[0].Stage == "chapter-two-native-exception" &&
                            projected[0].Evidence == "second-chance-debug-event" && projected[0].ModuleOffset.StartsWith("0x") &&
                            projected[0].Module == (nativeFixture is null ? "msctf.dll" : "ChapterTwoPresentation.exe"),
                            mode + " projects broker-sourced module and relative offset instead of the absolute address");
                    else check(projected.Count == 1 && projected[0].Stage == "chapter-two-game-exit" && projected[0].Evidence == "abnormal-process-exit",
                        mode + " records an unexplained exit without inventing exception evidence");
                }
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

    internal static async Task RunUploadsAsync(string root, Action<bool, string> check)
    {
        check(ChapterTwoClient.IsBundled, "upload regression uses a bundled launcher");
        if (!ChapterTwoClient.IsBundled) return;

        const string receiptId = "0123456789abcdef0123456789abcdef";
        var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:48091");
        await using var host = builder.Build();
        host.MapPost("/api/client-diagnostics/reports", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            requests.Enqueue(body);
            if (requests.Count == 1) return Results.StatusCode(503);
            var eventId = JsonDocument.Parse(body).RootElement.GetProperty("eventId").GetString();
            return Results.Json(new { receiptId, eventId, status = "stored" }, statusCode: 201);
        });
        await host.StartAsync();
        var settings = new DiagnosticSettings(Path.Combine(root, "remote-diagnostic-user"));
        var game = Path.Combine(root, "remote-diagnostic-game");
        var logs = Path.Combine(game, "Logs", "DFLegacy");
        Directory.CreateDirectory(logs);
        var client = Path.Combine(game, "DNF.exe");
        var options = new LauncherOptions { ClientPath = client, ChapterTwoServerHost = "localhost." };
        options.Diagnostics.ServiceUrl = "https://copied-profile.invalid";
        var previousClient = DFLegacy.Launcher.Program.LogClientPath;
        DFLegacy.Launcher.Program.LogClientPath = client;
        try
        {
            var helperStarts = 0;
            AutomaticDiagnostics.Configure(options, settings, _ => { helperStarts++; return null; });
            var session = AutomaticDiagnostics.Session;
            check(session?.Registration.Site == "http://localhost.:48091" && helperStarts == 0,
                "remote Chapter Two uses its own account origin and its in-process uploader");
            RecordFault("fixture");
            await AutomaticDiagnostics.FinishChapterTwoAsync();

            var sent = requests.ToArray();
            var status = settings.GetStatus(game, "http://localhost.:48091");
            check(sent.Length == 2 && sent[0] == sent[1], $"503 preserves and retries the same single frozen Chapter Two report ({sent.Length} requests, {status.LastStatus})");
            if (sent.Length > 0)
            {
                using var body = JsonDocument.Parse(sent[0]);
                var report = body.RootElement;
                check(report.GetProperty("module").GetString() == "msctf.dll" && report.GetProperty("moduleOffset").GetString() == "0x5678" &&
                    report.GetProperty("exceptionCode").GetString() == "0xC0000005" && report.GetProperty("architecture").GetString() == "x86" &&
                    !sent[0].Contains("12345678", StringComparison.Ordinal) && !sent[0].Contains("gamePid", StringComparison.Ordinal),
                    "upload includes native module evidence without absolute addresses or PID");
            }
            check(status.Uploaded == 1 && status.ReceiptId == receiptId, "receiver acknowledgement updates the user-visible upload status");

            AutomaticDiagnostics.Configure(options, settings);
            using (var queueLock = new FileStream(Path.Combine(settings.Root, "queue.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                RecordFault("contended");
                await AutomaticDiagnostics.FinishChapterTwoAsync();
                check(requests.Count == sent.Length, "an exit while the queue is locked does not post an incomplete report");
            }
            AutomaticDiagnostics.Configure(options, settings);
            await AutomaticDiagnostics.FinishChapterTwoAsync();
            check(requests.Count == sent.Length + 1, "the next launch recovers an unfrozen report after queue lock contention");

            settings.SetEnabled(false);
            AutomaticDiagnostics.Configure(options, settings);
            check(AutomaticDiagnostics.Session?.Registration.LocalOnly == true && !options.Diagnostics.Enabled,
                "the current user's disabled preference applies to the remote launcher");
            RecordFault("disabled");
            await AutomaticDiagnostics.FinishChapterTwoAsync();
            check(requests.Count == sent.Length + 1 && Directory.GetFiles(Path.Combine(logs, "Incidents"), "crash-*.log").Length == 6,
                "opting out preserves the local journal and crash reports");
        }
        finally
        {
            AutomaticDiagnostics.Configure(new LauncherOptions { ClientPath = client }, settings);
            DFLegacy.Launcher.Program.LogClientPath = previousClient;
        }

        void RecordFault(string attempt)
        {
            var image = $"s1c2_chapter2_host-{attempt}.exe";
            var journal = $"chapter-two-events-{attempt}.log";
            using var diagnostics = new ChapterTwoLaunchDiagnostics(DateTime.Now, game);
            diagnostics.Begin(image, $"chapter-two-client-{attempt}.log", journal);
            File.WriteAllText(Path.Combine(logs, journal), "START 42\nEXCEPTION 42 7 C0000005 0000000012345678 msctf.dll 0000000000005678\nEXIT 42 C0000005\n");
            diagnostics.Finish(retry: false, cancelled: false);
        }
    }
}
