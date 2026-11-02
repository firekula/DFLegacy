using System.Text.RegularExpressions;

namespace DFLegacy.Manager;

public sealed record LogLine(long Id, DateTimeOffset Time, string Source, string Text);

public sealed class ManagerLog
{
    private readonly ManagerPaths _paths;
    private readonly object _gate = new();
    private readonly Queue<LogLine> _lines = new();
    private readonly HashSet<string> _secrets = new();
    // Keep JSON IDs exactly representable by browser Numbers, including after restart.
    private long _id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
    public ManagerLog(ManagerPaths paths)
    {
        _paths = paths;
        // Restore the bounded tail, including the previous rotation when needed.
        try
        {
            var files = new[] { Path.Combine(paths.Logs, "manager.log.1"), Path.Combine(paths.Logs, "manager.log") };
            foreach (var text in files.Where(File.Exists).SelectMany(File.ReadLines).TakeLast(500))
            {
                var match = Regex.Match(text, @"^(\S+) \[([^\]]+)\] (.*)$");
                if (match.Success && DateTimeOffset.TryParse(match.Groups[1].Value, out var time))
                    _lines.Enqueue(new LogLine(++_id, time, match.Groups[2].Value, match.Groups[3].Value));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public void AddSecret(string value) { if (value.Length > 0) lock (_gate) _secrets.Add(value); }
    public void Write(string source, string? text)
    {
        if (text is null) return;
        lock (_gate)
        {
            foreach (var secret in _secrets) text = text.Replace(secret, "[hidden]", StringComparison.Ordinal);
            text = Regex.Replace(text, "(?i)(password|authorization|token)\\s*[:=]\\s*[^ ,;]+", "$1=[hidden]", RegexOptions.CultureInvariant);
            if (text.Length > 8192) text = text[..8192] + "…";
            var line = new LogLine(++_id, DateTimeOffset.UtcNow, source, text);
            _lines.Enqueue(line); while (_lines.Count > 500) _lines.Dequeue();
            var file = Path.Combine(_paths.Logs, "manager.log");
            try
            {
                if (File.Exists(file) && new FileInfo(file).Length > 2 * 1024 * 1024)
                {
                    if (File.Exists(file + ".3")) File.Delete(file + ".3");
                    for (var i = 2; i >= 1; i--) if (File.Exists(file + "." + i)) File.Move(file + "." + i, file + "." + (i + 1));
                    File.Move(file, file + ".1");
                }
                File.AppendAllText(file, $"{line.Time:O} [{source}] {text}{Environment.NewLine}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* Keep the in-memory log available when the log directory becomes unwritable. */ }
        }
    }
    public LogLine[] Read(long after = 0)
    {
        lock (_gate) return _lines.Where(line => line.Id > after).Select(line =>
        {
            var text = line.Text;
            foreach (var secret in _secrets) text = text.Replace(secret, "[hidden]", StringComparison.Ordinal);
            return line with { Text = text };
        }).ToArray();
    }
}
