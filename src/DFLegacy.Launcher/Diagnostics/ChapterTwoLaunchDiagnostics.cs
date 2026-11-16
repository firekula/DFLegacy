using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using DFLegacy.Diagnostics;

namespace DFLegacy.Launcher.Diagnostics;

internal sealed class ChapterTwoGameExitException(string message) : InvalidOperationException(message);

// The bootstrap owns attempt names; the broker owns the non-inherited journal.
// Never promote a game-controlled OutputDebugString/stdout line to native evidence.
internal sealed class ChapterTwoLaunchDiagnostics(DateTime bootstrapStarted, string gameDirectory) : IDisposable
{
    private Process? game;
    private string? image, journal, nativeLog;
    private bool finished = true;
    internal bool HasAbnormalExit { get; private set; }

    internal void Begin(string imageName, string logName, string journalName)
    {
        if (!SafeName(imageName, "s1c2_chapter2_host-", ".exe") ||
            !SafeName(logName, "chapter-two-client-", ".log") ||
            !SafeName(journalName, "chapter-two-events-", ".log"))
            throw new InvalidDataException("第二章诊断文件名无效。");
        Dispose();
        image = Path.Combine(gameDirectory, imageName);
        nativeLog = logName;
        journal = Path.Combine(ClientLogPaths.GetDirectory(Path.Combine(gameDirectory, "DNF.exe")), journalName);
        finished = false;
        HasAbnormalExit = false;
        Program.WriteLauncherLog($"Chapter Two attempt image={imageName}; nativeLog={logName}; journal={journalName}.");
    }

    private static bool SafeName(string name, string prefix, string suffix) =>
        name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(suffix, StringComparison.Ordinal) &&
        name.Length <= 160 && Regex.IsMatch(name, @"\A[A-Za-z0-9_.-]+\z");

    internal void SetGame(int id)
    {
        if (image is null || finished || game is not null) return;
        Process? candidate = null;
        try
        {
            if (journal is not null && File.Exists(journal) &&
                ReadJournal().FirstOrDefault() != $"START {id}")
            {
                Program.WriteLauncherLog("Chapter Two PID did not match the broker-owned event journal.");
                return;
            }
            candidate = Process.GetProcessById(id);
            var path = ClientFileIdentity.ProcessImagePath(candidate.SafeHandle);
            if (candidate.StartTime < bootstrapStarted || !string.Equals(path, image, StringComparison.OrdinalIgnoreCase))
            {
                Program.WriteLauncherLog("Chapter Two game image did not match the active attempt.");
                return;
            }
            // Holding the original handle prevents PID reuse from changing the observed identity.
            game = candidate;
            candidate = null;
            Program.WriteLauncherLog($"Bound Chapter Two game process {id}; image={Path.GetFileName(image)}.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            Program.WriteLauncherLog("Chapter Two game exited before its live identity could be bound; awaiting broker evidence.");
        }
        finally { candidate?.Dispose(); }
    }

    internal bool IsRunning(int id) => game is not null && game.Id == id && !game.HasExited;

    internal void Finish(bool retry, bool cancelled)
    {
        if (finished) return;
        finished = true;
        if (cancelled) return;
        try
        {
            var lines = ReadJournal();
            int pid = 0;
            int? exit = null;
            var native = new List<(uint Code, ulong Address, uint Thread, string Module, string Offset)>();
            foreach (var line in lines)
            {
                var fields = line.Split(' ');
                if (fields.Length == 2 && fields[0] == "START" && pid == 0 &&
                    int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var started) && started > 0)
                    pid = started;
                else if (fields.Length is 5 or 7 && fields[0] == "EXCEPTION" && pid > 0 && exit is null &&
                    fields[1] == pid.ToString(CultureInfo.InvariantCulture) &&
                    uint.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var thread) &&
                    uint.TryParse(fields[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) &&
                    ulong.TryParse(fields[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
                {
                    var module = "unknown";
                    var offset = "unknown";
                    if (fields.Length == 7 && Regex.IsMatch(fields[5], @"\A[A-Za-z0-9_.-]{1,128}\z") &&
                        !fields[5].Contains("..", StringComparison.Ordinal) &&
                        ulong.TryParse(fields[6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var relative) && relative <= address)
                    {
                        module = fields[5].Equals(Path.GetFileName(image), StringComparison.OrdinalIgnoreCase)
                            ? "ChapterTwoPresentation.exe" : fields[5].ToLowerInvariant();
                        offset = $"0x{relative:X}";
                    }
                    native.Add((code, address, thread, module, offset));
                }
                else if (fields.Length == 3 && fields[0] == "EXIT" && pid > 0 && exit is null &&
                    fields[1] == pid.ToString(CultureInfo.InvariantCulture) &&
                    uint.TryParse(fields[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var status))
                    exit = unchecked((int)status);
                else throw new InvalidDataException("第二章 broker 诊断记录格式无效。");
            }
            if (game is not null)
            {
                if (pid != 0 && pid != game.Id) throw new InvalidDataException("第二章 broker 与游戏进程身份不一致。");
                pid = game.Id;
                if (game.HasExited)
                {
                    if (exit.HasValue && exit.Value != game.ExitCode)
                        throw new InvalidDataException("第二章 broker 与游戏退出码不一致。");
                    exit = game.ExitCode;
                }
            }
            // The retry marker originates in game debug text. It may only suppress
            // the known preferred-address failure, never a real second-chance fault.
            if (retry && exit == 5 && native.Count == 0) return;
            HasAbnormalExit = native.Count > 0 || exit is not null and not 0;
            string? nativeReport = null;
            var first = true;
            foreach (var fault in native.Take(8))
            {
                var report = Program.Incidents.Report(IncidentKind.Crash, "chapter-two-native-exception",
                    details: $"evidence=second-chance-debug-event\ngamePid={pid}\ngameImage={Path.GetFileName(image)}\nthreadId={fault.Thread}\nexceptionCode=0x{fault.Code:X8}\nexceptionAddress=0x{fault.Address:X16}\nfaultModule={fault.Module}\nfaultModuleOffset={fault.Offset}\nnativeLog={nativeLog}\nbrokerJournal={Path.GetFileName(journal)}",
                    structured: new DiagnosticIncident
                    {
                        Kind = "crash", Stage = "chapter-two-native-exception", Evidence = "second-chance-debug-event",
                        Architecture = "x86", ExceptionCode = $"0x{fault.Code:X8}", Module = fault.Module, ModuleOffset = fault.Offset
                    }, reportAutomatically: first);
                nativeReport ??= report;
                first = false;
            }
            if (pid > 0 && exit is not null)
            {
                Program.Incidents.ReportExit(pid, exit.Value, false, Path.GetFileName(nativeReport), new DiagnosticIncident
                {
                    Kind = "error", Stage = "chapter-two-game-exit", Evidence = "abnormal-process-exit",
                    Architecture = "x86", ExceptionCode = $"0x{exit.Value:X8}"
                }, reportAutomatically: native.Count == 0);
                Program.WriteLauncherLog($"Chapter Two game process {pid} exited with 0x{exit.Value:X8}; nativeLog={nativeLog}.");
            }
            else Program.WriteLauncherLog($"Chapter Two game exit was not captured; nativeLog={nativeLog}.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Program.ReportError("chapter-two-diagnostics", error);
        }
    }

    private string[] ReadJournal()
    {
        if (journal is null || !File.Exists(journal)) return [];
        if (ClientLogPaths.HasLink(journal)) throw new IOException("第二章诊断路径不能是链接。");
        using var file = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (file.Length > 16384) throw new InvalidDataException("第二章 broker 诊断记录超出大小限制。");
        using var reader = new StreamReader(file);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public void Dispose()
    {
        game?.Dispose();
        game = null;
    }
}
