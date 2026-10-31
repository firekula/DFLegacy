using System.Text.RegularExpressions;

namespace DFLegacy.Server;

public sealed record PvpMapEntry(byte Index, int ResourceId, string Path, int MinimumPlayers, int MaximumPlayers);

/// <summary>Native compact ordinal catalog: existing Map.lst IDs 50000..50999 sorted numerically.
/// Slot zero is the native random request placeholder, never a selected map.</summary>
public sealed class PvpMapCatalog
{
    private static readonly Regex ListRow = new(@"^\s*(\d+)\s+`([^`]+)`", RegexOptions.CultureInvariant);
    private static readonly Regex PlayerBounds = new(@"\[player number\]\s+(\d+)\s+(\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private readonly Lazy<IReadOnlyList<PvpMapEntry>> _entries;

    public PvpMapCatalog(ScriptFileSystem scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        _entries = new(() => Load(scripts), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<PvpMapEntry> Entries => _entries.Value;

    public bool IsValidRequested(ushort requestedMap, int playerCount = 2) =>
        requestedMap == 0 ? Entries.Skip(1).Any(e => Supports(e, playerCount))
        : requestedMap < Entries.Count && Supports(Entries[requestedMap], playerCount);

    public byte Select(ushort requestedMap, int playerCount = 2, Random? random = null)
    {
        if (!IsValidRequested(requestedMap, playerCount)) throw new ArgumentOutOfRangeException(nameof(requestedMap), "Map is not available for this player count.");
        if (requestedMap != 0) return checked((byte)requestedMap);
        var choices = Entries.Skip(1).Where(e => Supports(e, playerCount)).ToArray();
        return choices[(random ?? Random.Shared).Next(choices.Length)].Index;
    }

    private static bool Supports(PvpMapEntry entry, int count) => entry.Index != 0 && count >= entry.MinimumPlayers && count <= entry.MaximumPlayers;

    private static IReadOnlyList<PvpMapEntry> Load(ScriptFileSystem scripts)
    {
        if (!scripts.FileExists("map/map.lst")) throw new InvalidOperationException("Native PvP Map.lst is unavailable.");
        var paths = new SortedDictionary<int, string>();
        foreach (var line in scripts.ReadLines("map/map.lst"))
        {
            var match = ListRow.Match(line);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var id) || id is < 50000 or >= 51000) continue;
            if (!paths.TryAdd(id, match.Groups[2].Value)) throw new InvalidDataException($"Duplicate PvP map resource {id}.");
        }
        if (paths.Count is < 2 or > 256 || paths.First().Key != 50000)
            throw new InvalidDataException("PvP compact catalog must begin with resource 50000 and fit the native byte index.");
        var entries = new List<PvpMapEntry>(paths.Count);
        foreach (var (id, path) in paths)
        {
            if (id == 50000) { entries.Add(new(0, id, path, 0, 0)); continue; }
            var normalized = "map/" + path.Replace('\\', '/');
            if (!scripts.FileExists(normalized)) throw new InvalidDataException($"PvP map resource is missing: {normalized}");
            var match = PlayerBounds.Match(string.Join('\n', scripts.ReadLines(normalized)));
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var minimum)
                || !int.TryParse(match.Groups[2].Value, out var maximum) || minimum < 1 || maximum < minimum || maximum > 8)
                throw new InvalidDataException($"PvP map player bounds are invalid: {normalized}");
            entries.Add(new(checked((byte)entries.Count), id, path, minimum, maximum));
        }
        return entries.AsReadOnly();
    }
}
