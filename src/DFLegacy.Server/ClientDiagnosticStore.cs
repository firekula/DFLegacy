using System.Security.Cryptography;
using System.Text.Json;

namespace DFLegacy.Server;

/// <summary>Independent bounded diagnostic storage; never takes JsonGameStore's save lock.</summary>
public sealed class ClientDiagnosticStore : BackgroundService
{
    private readonly ClientDiagnosticsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ClientDiagnosticStore>? _logger;
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _uploads = new(2, 2);
    private readonly object _rateGate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _sources = new(StringComparer.Ordinal);
    private readonly Queue<DateTimeOffset> _global = new();
    private readonly Dictionary<string, Stored> _records = new(StringComparer.Ordinal);
    private bool _loaded;
    private bool _failed;
    private long _bytes;
    private long ByteLimit => Math.Clamp(_options.MaxStorageBytes, 1024, 256L * 1024 * 1024);
    private long RewriteReserve => Math.Min(2 * 1024 * 1024, ByteLimit / 2);
    private int CountLimit => Math.Clamp(_options.MaxReportCount, 1, 2000);
    private int DedupLimit => Math.Clamp(_options.MaxDeduplicationEntries, 1, 50000);
    private static readonly JsonSerializerOptions DiskJson = new() { MaxDepth = 12 };

    private sealed record Stored(string EventId, string ReceiptId, string Hash, string Fingerprint,
        DateTimeOffset ReceivedAt, DateTimeOffset? DeletedAt, byte[]? Body, string Status = "new",
        string? UpdatedBy = null, DateTimeOffset? UpdatedAt = null)
    {
        [System.Text.Json.Serialization.JsonIgnore] public ClientDiagnosticReport? Report { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public long DiskBytes { get; set; }
    }

    public ClientDiagnosticStore(ServerOptions options, TimeProvider clock, ILogger<ClientDiagnosticStore>? logger = null)
    {
        _options = options.ClientDiagnostics;
        _clock = clock;
        _logger = logger;
        var root = Path.GetDirectoryName(Path.GetFullPath(options.DataPath))!;
        _directory = Path.Combine(root, "diagnostics");
        try {
            if (!string.IsNullOrWhiteSpace(_options.StoragePath)) _directory = Path.GetFullPath(_options.StoragePath);
            // Invalid diagnostic configuration fails closed without preventing the game host from starting.
            if (string.Equals(_directory, Path.GetFullPath(options.DataPath), StringComparison.OrdinalIgnoreCase)) _loaded = _failed = true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { _loaded = _failed = true; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Even the initial recovery/expiry sweep runs off the startup path, including when receiving is disabled.
        await Task.Yield();
        var warningLogged = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _clock);
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, deadline.Token);
                await _gate.WaitAsync(budget.Token);
                try {
                    if (!await MaintainAsync(budget.Token)) throw new IOException("Diagnostic storage unavailable.");
                    warningLogged = false;
                }
                finally { _gate.Release(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) {
                // Faults stay within diagnostics; never fault the hosted task or repeatedly log report data.
                if (!warningLogged) _logger?.LogWarning("Client diagnostic maintenance unavailable ({Failure}); game service continues.", exception.GetType().Name);
                warningLogged = true;
            }
            try { await Task.Delay(TimeSpan.FromMinutes(1), _clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public bool TryBeginUpload(string source, out IDisposable? lease)
    {
        lease = null;
        if (!_uploads.Wait(0)) return false;
        lock (_rateGate)
        {
            var now = _clock.GetUtcNow();
            void Trim(Queue<DateTimeOffset> queue) { while (queue.TryPeek(out var first) && first <= now.AddHours(-1)) queue.Dequeue(); }
            Trim(_global);
            foreach (var key in _sources.Where(pair => pair.Value.Count == 0 || pair.Value.Last() <= now.AddHours(-1)).Select(pair => pair.Key).ToArray()) _sources.Remove(key);
            if (!_sources.TryGetValue(source, out var budget)) {
                if (_sources.Count >= 4096) { _uploads.Release(); return false; }
                budget = new(); _sources.Add(source, budget);
            }
            Trim(budget);
            bool Full(Queue<DateTimeOffset> queue, int minute, int hour) => queue.Count >= Math.Clamp(hour, 1, 10000) || queue.Count(value => value > now.AddMinutes(-1)) >= Math.Clamp(minute, 1, 10000);
            if (Full(budget, _options.SourcePerMinute, _options.SourcePerHour) || Full(_global, _options.GlobalPerMinute, _options.GlobalPerHour)) { _uploads.Release(); return false; }
            budget.Enqueue(now); _global.Enqueue(now);
        }
        lease = new UploadLease(_uploads); return true;
    }
    private sealed class UploadLease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }

    private string FilePath(string eventId) => Path.Combine(_directory, eventId + ".json");
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_loaded) return;
        try { await LoadIndexAsync(cancellationToken); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { _failed = true; }
        catch (OperationCanceledException) {
            // A timed-out partial recovery may retry, but must reread the complete index before admitting anything.
            _records.Clear(); _bytes = 0; _loaded = false; throw;
        }
    }
    private async Task LoadIndexAsync(CancellationToken cancellationToken)
    {
        if (_loaded) return;
        _loaded = true;
        Directory.CreateDirectory(_directory);
        // Read at most the configured bounded index. Unexpected files/sizes quarantine receive instead of silently forgetting a receipt.
        var files = Directory.EnumerateFiles(_directory).Take(DedupLimit + 101).ToArray();
        if (files.Length > DedupLimit + 100) { _failed = true; return; }
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { _failed = true; return; }
            if (path.EndsWith(".tmp", StringComparison.Ordinal)) { File.Delete(path); continue; }
            var id = Path.GetFileNameWithoutExtension(path);
            var length = new FileInfo(path).Length;
            if (!ClientDiagnosticReport.Id(id) || Path.GetExtension(path) != ".json" || length > 2 * 1024 * 1024 || _records.Count >= DedupLimit || _bytes + length > ByteLimit) { _failed = true; return; }
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            Stored? record;
            try { record = JsonSerializer.Deserialize<Stored>(bytes, DiskJson); } catch (JsonException) { _failed = true; return; }
            if (record is null || record.EventId != id || !ClientDiagnosticReport.Id(record.ReceiptId) || record.Hash is not { Length: 64 } || record.Fingerprint is not { Length: 64 }
                || record.Status is not ("new" or "investigating" or "resolved") || record.ReceivedAt == default
                || (record.DeletedAt is null) != (record.Body is not null)) { _failed = true; return; }
            if (record.Body is not null) {
                if (!ClientDiagnosticReport.TryParse(record.Body, out var report) || report!.EventId != id || record.Hash != Convert.ToHexStringLower(SHA256.HashData(record.Body)) || report.Fingerprint() != record.Fingerprint) { _failed = true; return; }
                record.Report = report;
            }
            record.DiskBytes = length; _bytes += length; _records.Add(id, record);
        }
        if (_records.Values.Count(record => record.Body is not null) > 2000) _failed = true;
    }

    private async Task<bool> WriteAsync(Stored record, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, DiskJson);
        var previousBytes = _records.TryGetValue(record.EventId, out var prior) ? prior.DiskBytes : 0;
        // The old file and new temporary file coexist until the atomic replace. Both count against quota.
        // Keep a rewrite reserve so expiry/deletion can always commit its tombstone even at the admission limit.
        if (_bytes + bytes.Length > ByteLimit || _bytes - previousBytes + bytes.Length > ByteLimit - RewriteReserve) return false;
        var temporary = Path.Combine(_directory, record.EventId + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous)) {
                await file.WriteAsync(bytes, cancellationToken); file.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath(record.EventId), overwrite: true);
            _bytes -= previousBytes;
            record.DiskBytes = bytes.Length; _bytes += bytes.Length; _records[record.EventId] = record;
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<bool> MaintainAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (_failed) return false;
        var now = _clock.GetUtcNow();
        foreach (var record in _records.Values.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.DeletedAt is not null && record.DeletedAt <= now.AddDays(-30)) {
                File.Delete(FilePath(record.EventId)); _records.Remove(record.EventId); _bytes -= record.DiskBytes;
            }
            else if (record.Body is not null && record.ReceivedAt <= now.AddDays(-Math.Clamp(_options.RetentionDays, 1, 14))) {
                if (!await WriteAsync(record with { Body = null, DeletedAt = now, Report = null }, cancellationToken)) return false;
            }
        }
        return true;
    }

    public async Task<ClientDiagnosticReceiveResult> ReceiveAsync(byte[] body, CancellationToken cancellationToken = default)
    {
        if (!ClientDiagnosticReport.TryParse(body, out var report)) return new(400);
        await _gate.WaitAsync(cancellationToken);
        try {
            if (!await MaintainAsync(cancellationToken)) return new(503);
            var hash = Convert.ToHexStringLower(SHA256.HashData(body));
            if (_records.TryGetValue(report!.EventId, out var old)) {
                if (old.DeletedAt is not null) return new(410);
                if (old.Hash != hash) return new(409);
                return new(200, new(old.ReceiptId, old.EventId, "stored"));
            }
            if (_records.Count >= DedupLimit || _records.Values.Count(value => value.Body is not null) >= CountLimit) return new(503);
            var fingerprint = report.Fingerprint();
            var group = _records.Values.FirstOrDefault(value => value.Body is not null && value.Fingerprint == fingerprint);
            var record = new Stored(report.EventId, Guid.NewGuid().ToString("N"), hash, fingerprint, _clock.GetUtcNow(), null, body.ToArray(), group?.Status ?? "new", group?.UpdatedBy, group?.UpdatedAt) { Report = report };
            if (!await WriteAsync(record, cancellationToken)) return new(503);
            return new(201, new(record.ReceiptId, record.EventId, "stored"));
        }
        finally { _gate.Release(); }
    }

    public async Task<ClientDiagnosticPage> ListAsync(ClientDiagnosticQuery query, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try {
            if (!await MaintainAsync(cancellationToken)) throw new IOException("Diagnostic storage unavailable.");
            var active = _records.Values.Where(value => value.Report is not null);
            if (query.Kind is not null) active = active.Where(value => value.Report!.Kind == query.Kind);
            if (query.Version is not null) active = active.Where(value => value.Report!.ClientVersion == query.Version);
            if (query.System is not null) active = active.Where(value => value.Report!.System.GetValueOrDefault("osVersion", "unknown").Contains(query.System, StringComparison.OrdinalIgnoreCase));
            if (query.Gpu is not null) active = active.Where(value => value.Report!.System.GetValueOrDefault("gpuModel", "unknown").Contains(query.Gpu, StringComparison.OrdinalIgnoreCase));
            if (query.From is not null) active = active.Where(value => value.Report!.OccurredAt >= query.From);
            if (query.To is not null) active = active.Where(value => value.Report!.OccurredAt <= query.To);
            var groups = active.GroupBy(value => value.Fingerprint).Select(group => {
                var newest = group.OrderByDescending(value => value.UpdatedAt ?? DateTimeOffset.MinValue).First(); var report = newest.Report!;
                return new ClientDiagnosticGroup(group.Key, report.Kind, report.ClientVersion, report.Evidence, report.Module, newest.Status,
                    group.Sum(value => (long)value.Report!.Occurrences), group.Count(), group.Max(value => value.Report!.OccurredAt),
                    group.OrderByDescending(value => value.ReceivedAt).Select(value => value.ReceiptId).ToArray(), newest.UpdatedBy, newest.UpdatedAt);
            });
            if (query.Status is not null) groups = groups.Where(value => value.Status == query.Status);
            var ordered = groups.OrderByDescending(value => value.LastSeen).ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
            var page = Math.Clamp(query.Page, 1, 1000000); var size = Math.Clamp(query.PageSize, 1, 100);
            return new(ordered.Length, page, size, ordered.Skip((page - 1) * size).Take(size).ToArray());
        }
        finally { _gate.Release(); }
    }

    public async Task<ClientDiagnosticDetail?> GetAsync(string receipt, CancellationToken cancellationToken = default)
    {
        if (!ClientDiagnosticReport.Id(receipt)) return null;
        await _gate.WaitAsync(cancellationToken);
        try {
            if (!await MaintainAsync(cancellationToken)) throw new IOException("Diagnostic storage unavailable.");
            var record = _records.Values.FirstOrDefault(value => value.ReceiptId == receipt && value.Report is not null);
            return record is null ? null : new(record.ReceiptId, record.Fingerprint, record.Status, record.ReceivedAt, record.Report!);
        }
        finally { _gate.Release(); }
    }
    public async Task<byte[]?> DownloadAsync(string receipt, CancellationToken cancellationToken = default)
    {
        if (!ClientDiagnosticReport.Id(receipt)) return null;
        await _gate.WaitAsync(cancellationToken);
        try {
            if (!await MaintainAsync(cancellationToken)) throw new IOException("Diagnostic storage unavailable.");
            return _records.Values.FirstOrDefault(value => value.ReceiptId == receipt && value.Body is not null)?.Body?.ToArray();
        }
        finally { _gate.Release(); }
    }
    public async Task<bool> SetStatusAsync(string fingerprint, string status, string actor, CancellationToken cancellationToken = default)
    {
        if (status is not ("new" or "investigating" or "resolved")) return false;
        await _gate.WaitAsync(cancellationToken);
        try {
            if (!await MaintainAsync(cancellationToken)) throw new IOException("Diagnostic storage unavailable.");
            var group = _records.Values.Where(value => value.Fingerprint == fingerprint && value.Body is not null).ToArray();
            foreach (var record in group) if (!await WriteAsync(record with { Status = status, UpdatedBy = actor, UpdatedAt = _clock.GetUtcNow() }, cancellationToken)) throw new IOException("Diagnostic capacity exhausted.");
            return group.Length > 0;
        }
        finally { _gate.Release(); }
    }
    public async Task<bool> DeleteAsync(string receipt, string actor, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try {
            if (!await MaintainAsync(cancellationToken)) throw new IOException("Diagnostic storage unavailable.");
            var record = _records.Values.FirstOrDefault(value => value.ReceiptId == receipt && value.Body is not null);
            if (record is null) return false;
            if (!await WriteAsync(record with { Body = null, Report = null, DeletedAt = _clock.GetUtcNow(), UpdatedAt = _clock.GetUtcNow(), UpdatedBy = actor }, cancellationToken)) throw new IOException("Diagnostic capacity exhausted.");
            return true;
        }
        finally { _gate.Release(); }
    }
}
