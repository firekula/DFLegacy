using System.Text.Json;

namespace DFLegacy.Server;

/// <summary>
/// Runtime-adjustable multiplier for quest task-item drops.
///
/// Only the chance of <c>[monster reward item]</c> and <c>[clear reward item]</c>
/// rows is scaled; ordinary loot keeps using <see cref="DropOptions.RatePercent"/>.
/// 100 keeps the PVF percentage, 0 disables task-item drops, the ceiling is 1000.
///
/// The value is written to <c>&lt;data directory&gt;/settings/quest-drop.json</c> so
/// it survives image upgrades that keep the data volume, without rewriting the
/// operator-owned server.json. Sub-1% results round away from zero (2% at 25%
/// becomes 1%), and a scaled chance of 0 means no drop.
/// </summary>
public sealed class QuestDropSettings
{
    public const int MinimumRatePercent = 0;
    public const int MaximumRatePercent = 1000;
    public const int DefaultRatePercent = 100;

    private readonly object _rateGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string? _path;
    private readonly ILogger<QuestDropSettings>? _logger;
    private int _ratePercent;

    public QuestDropSettings(ServerOptions options, ILogger<QuestDropSettings>? logger = null)
    {
        _logger = logger;
        var configured = Math.Clamp(options.Drop.QuestItemRatePercent, MinimumRatePercent, MaximumRatePercent);
        try
        {
            var dataPath = Path.GetFullPath(options.DataPath);
            var directory = Path.GetDirectoryName(dataPath);
            if (directory is null) throw new IOException("The data path has no directory.");
            _path = Path.Combine(directory, "settings", "quest-drop.json");
            _ratePercent = Read(_path) ?? configured;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(exception, "Quest drop settings are read-only; using the configured {Rate}% for this run.", configured);
            _ratePercent = configured;
        }
    }

    public int RatePercent
    {
        get { lock (_rateGate) return _ratePercent; }
    }

    public bool IsPersistent => _path is not null;

    /// <summary>Scale one PVF chance percentage by the current rate.</summary>
    public int ScaleChance(int chancePercent)
    {
        if (chancePercent <= 0)
        {
            return 0;
        }

        var rate = RatePercent;
        if (rate == DefaultRatePercent)
        {
            return Math.Min(chancePercent, 100);
        }

        var scaled = (int)Math.Round(chancePercent * rate / 100.0, MidpointRounding.AwayFromZero);
        return Math.Clamp(scaled, 0, 100);
    }

    /// <summary>Persist a new rate; the in-memory value changes only after a successful write.</summary>
    public async Task<bool> SetRateAsync(int ratePercent, CancellationToken cancellationToken = default)
    {
        if (ratePercent is < MinimumRatePercent or > MaximumRatePercent || _path is null)
        {
            return false;
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(
                temporary,
                JsonSerializer.SerializeToUtf8Bytes(new QuestDropSettingRecord(ratePercent)),
                cancellationToken);
            File.Move(temporary, _path, overwrite: true);
            lock (_rateGate)
            {
                _ratePercent = ratePercent;
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(exception, "Could not persist the quest drop rate {Rate}%.", ratePercent);
            return false;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static int? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var record = JsonSerializer.Deserialize<QuestDropSettingRecord>(File.ReadAllBytes(path));
            return record is { RatePercent: >= MinimumRatePercent and <= MaximumRatePercent }
                ? record.RatePercent
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private sealed record QuestDropSettingRecord(int RatePercent);
}
