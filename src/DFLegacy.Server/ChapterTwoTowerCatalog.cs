using System.Text.RegularExpressions;

namespace DFLegacy.Server;

public readonly record struct DungeonTowerTicket(ushort ItemId, uint Quantity, bool Consume);

public readonly record struct DungeonTowerSpawnPosition(int X, int Y, int Z);

public sealed record DungeonTowerApc(ushort ApcId, byte Level, int AppearancePoint, string ScriptPath);

public sealed record DungeonTowerApcSpawn(
    ushort ApcId,
    byte Level,
    DungeonTowerSpawnPosition Position,
    string ActorType,
    int Team,
    string Grade,
    int Option1,
    int Option2);

public sealed record DungeonTowerFloorDefinition(
    int FloorNumber,
    ushort MapId,
    IReadOnlyList<DungeonTowerApcSpawn> FixedApcs,
    int RandomApcPoint,
    IReadOnlyList<DungeonTowerSpawnPosition> MonsterSpawnPositions);

public sealed record DungeonTowerDefinition(
    ushort DungeonId,
    byte MinimumLevel,
    byte BasisLevel,
    DungeonTowerTicket Ticket,
    int ApcAppearRate,
    bool ItemDropEnabled,
    IReadOnlyList<DungeonTowerFloorDefinition> Floors,
    IReadOnlyList<DungeonTowerApc> ApcCandidates);

public sealed record DungeonTowerApcActor(
    byte MapListIndex,
    ushort ApcId,
    byte Level,
    int Team,
    DungeonTowerSpawnPosition Position);

public sealed record DungeonTowerFloor(
    DungeonTowerFloorDefinition Definition,
    DungeonRoomDefinition Room,
    IReadOnlyList<DungeonTowerApcActor> Apcs);

public sealed partial class DungeonCatalog
{
    public const ushort TowerOfIllusionId = 11001;

    private const string TowerFloorTag = "death tower map indexes";
    private const string ApcListPath = "aicharacter/aicharacter.lst";
    private const int RandomApcIndexStart = 64;
    private const int RandomApcIndexLimit = 128;
    private static readonly Regex TowerTypePattern = new(
        @"^[ \t]*\[dungeon type\]\s*`tower of death`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex ApcSpawnPattern = new(
        @"^\s*(\d+)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)\s+`\[([^\]]+)\]`\s+`\[([^\]]+)\]`\s+(-?\d+)\s+(-?\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ApcMinimumInfoPattern = new(
        @"^[ \t]*\[minimum info\][ \t]*\r?\n(.*?)(?=^[ \t]*\[|\z)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        | RegexOptions.Multiline | RegexOptions.Singleline);

    private readonly Lazy<TowerCatalogEntry?> _tower;

    public bool TryGetTower(ushort dungeonId, out DungeonTowerDefinition tower)
    {
        if (dungeonId == TowerOfIllusionId && _tower.Value is { } entry)
        {
            tower = entry.Definition;
            return true;
        }

        tower = null!;
        return false;
    }

    public bool TryCreateTowerFloor(
        ushort dungeonId,
        int floorNumber,
        out DungeonTowerFloor floor,
        IDropRandomSource? random = null)
    {
        floor = null!;
        if (dungeonId != TowerOfIllusionId
            || _tower.Value is not { } entry
            || floorNumber < 1
            || floorNumber > entry.Definition.Floors.Count)
        {
            return false;
        }

        var state = _state.Value;
        var tower = entry.Definition;
        var definition = tower.Floors[floorNumber - 1];
        random ??= GameRandomSource.Shared;
        if (!TryBuildRoom(
                state,
                tower.BasisLevel,
                entry.PassiveItemGroups,
                state.Maps[definition.MapId],
                '\0',
                0,
                random,
                isHellRoom: false,
                out var room))
        {
            return false;
        }

        floor = new DungeonTowerFloor(definition, room, SelectTowerFloorApcs(definition, entry.ApcBuckets, random));
        return true;
    }

    private static IReadOnlyList<DungeonTowerApcActor> SelectTowerFloorApcs(
        DungeonTowerFloorDefinition floor,
        ApcBucket[] buckets,
        IDropRandomSource random)
    {
        var actors = floor.FixedApcs.Select((apc, index) => new DungeonTowerApcActor(
            (byte)index, apc.ApcId, apc.Level, apc.Team, apc.Position)).ToList();
        var remainingPoints = floor.RandomApcPoint;
        if (remainingPoints == 0)
        {
            return actors.AsReadOnly();
        }

        var selected = actors.Select(actor => actor.ApcId).ToHashSet();
        for (var mapIndex = RandomApcIndexStart; mapIndex < RandomApcIndexLimit && remainingPoints >= buckets[0].Point; mapIndex++)
        {
            var apc = ChooseTowerApc(buckets, remainingPoints, selected, random);
            if (apc is null)
            {
                break;
            }

            var slot = (mapIndex - RandomApcIndexStart) % floor.MonsterSpawnPositions.Count;
            actors.Add(new DungeonTowerApcActor(
                (byte)mapIndex, apc.ApcId, apc.Level, 100, floor.MonsterSpawnPositions[slot]));
            selected.Add(apc.ApcId);
            remainingPoints -= apc.AppearancePoint;
        }

        return actors.AsReadOnly();
    }

    private static DungeonTowerApc? ChooseTowerApc(
        ApcBucket[] buckets,
        int remainingPoints,
        HashSet<ushort> selected,
        IDropRandomSource random)
    {
        // The original rolls 1..remainingPoints, selects the greatest point at
        // or below the roll (clamped at the first group), and rolls again when
        // the candidate already appeared on this floor. Weight each group's
        // roll interval by its fraction of unused actors to get the same draw
        // distribution with a finite number of draws.
        var groups = new List<(DungeonTowerApc[] Candidates, double Limit)>();
        var total = 0.0;
        for (var index = 0; index < buckets.Length && buckets[index].Point <= remainingPoints; index++)
        {
            var bucket = buckets[index];
            var available = bucket.Candidates.Where(apc => !selected.Contains(apc.ApcId)).ToArray();
            if (available.Length == 0)
            {
                continue;
            }

            var lower = index == 0 ? 1 : bucket.Point;
            var upper = index + 1 < buckets.Length ? Math.Min(remainingPoints, buckets[index + 1].Point - 1) : remainingPoints;
            total += (upper - lower + 1) * ((double)available.Length / bucket.Candidates.Length);
            groups.Add((available, total));
        }

        if (groups.Count == 0)
        {
            return null;
        }

        var roll = random.Next(int.MaxValue) * (total / int.MaxValue);
        var candidates = groups.First(group => roll < group.Limit).Candidates;
        return candidates.Length == 1 ? candidates[0] : candidates[random.Next(candidates.Length)];
    }

    private static bool IsTowerScript(string text) => TowerTypePattern.IsMatch(text);

    private TowerCatalogEntry? LoadTower(ScriptFileSystem scripts)
    {
        const string dungeonListPath = "dungeon/dungeon.lst";
        if (!scripts.FileExists(dungeonListPath))
        {
            return null;
        }

        try
        {
            var dungeonList = scripts.ReadAllText(dungeonListPath, ScriptEncoding);
            foreach (Match entry in ListEntryPattern.Matches(dungeonList))
            {
                if (!ushort.TryParse(entry.Groups[1].Value, out var id) || id != TowerOfIllusionId)
                {
                    continue;
                }

                var path = $"dungeon/{NormalizeRelativePath(entry.Groups[2].Value)}";
                if (!scripts.FileExists(path))
                {
                    return null;
                }

                var text = scripts.ReadAllText(path, ScriptEncoding);
                if (!IsTowerScript(text))
                {
                    return null;
                }

                var tower = ParseTower(scripts, text);
                if (tower is null)
                {
                    _logger.LogWarning("Failed to read the Tower of Illusion definition from {Path}.", path);
                }

                return tower;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            _logger.LogWarning(exception, "Failed to load the Tower of Illusion from {ScriptRoot}.", scripts.SourceDescription);
        }

        return null;
    }

    private TowerCatalogEntry? ParseTower(ScriptFileSystem scripts, string text)
    {
        var requiredItem = ParseTagRows(text, "required item").FirstOrDefault();
        var rows = ParseAllBlocks(text, TowerFloorTag).SelectMany(ParseDataLines).Select(ParseIntegers).ToArray();
        if (requiredItem is not [> 0 and <= ushort.MaxValue, > 0, 0 or 1]
            || rows.Length == 0
            || rows[0] is not [> 0 and <= ushort.MaxValue]
            || rows[0][0] != rows.Length - 1)
        {
            return null;
        }

        var minimumLevel = ParseInlineIntegers(text, "minimum required level").FirstOrDefault(1);
        var basisLevel = ParseInlineIntegers(text, "basis level").FirstOrDefault(minimumLevel);
        if (minimumLevel is < 1 or > byte.MaxValue || basisLevel is < 1 or > byte.MaxValue)
        {
            return null;
        }

        var apcs = LoadTowerApcs(scripts);
        var floors = new DungeonTowerFloorDefinition[rows[0][0]];
        var maps = _state.Value.Maps;
        for (var index = 1; index < rows.Length; index++)
        {
            var row = rows[index];
            if (row is not [> 0, > 0 and <= ushort.MaxValue]
                || row[0] > floors.Length
                || floors[row[0] - 1] is not null
                || !maps.TryGetValue((ushort)row[1], out var map)
                || map.ScriptPath is not { } mapPath
                || ReadTowerFloor(scripts, row[0], map.MapId, mapPath, apcs) is not { } floor)
            {
                return null;
            }

            floors[floor.FloorNumber - 1] = floor;
        }

        var ticket = new DungeonTowerTicket((ushort)requiredItem[0], (uint)requiredItem[1], requiredItem[2] != 0);
        var candidates = apcs.Values.Where(apc => apc.AppearancePoint > 0).ToArray();
        var tower = new DungeonTowerDefinition(
            TowerOfIllusionId,
            (byte)minimumLevel,
            (byte)basisLevel,
            ticket,
            ParseInlineIntegers(text, "ai character appear rate").FirstOrDefault(),
            ParseInlineIntegers(text, "tower item drop").FirstOrDefault() != 0,
            Array.AsReadOnly(floors),
            Array.AsReadOnly(candidates));
        var buckets = candidates.GroupBy(apc => apc.AppearancePoint)
            .OrderBy(group => group.Key)
            .Select(group => new ApcBucket(group.Key, group.ToArray()))
            .ToArray();
        return new TowerCatalogEntry(tower, ParsePassiveItemGroups(text), buckets);
    }

    private static Dictionary<ushort, DungeonTowerApc> LoadTowerApcs(ScriptFileSystem scripts)
    {
        var apcs = new Dictionary<ushort, DungeonTowerApc>();
        if (!scripts.FileExists(ApcListPath))
        {
            return apcs;
        }

        foreach (Match row in ListEntryPattern.Matches(scripts.ReadAllText(ApcListPath, ScriptEncoding)))
        {
            if (!ushort.TryParse(row.Groups[1].Value, out var apcId))
            {
                continue;
            }

            var path = $"aicharacter/{NormalizeRelativePath(row.Groups[2].Value)}";
            if (!scripts.FileExists(path))
            {
                continue;
            }

            var text = scripts.ReadAllText(path, ScriptEncoding);
            var minimumInfo = ApcMinimumInfoPattern.Match(text);
            var rows = ParseDataLines(minimumInfo.Groups[1].Value);
            // The rows are name, job, grow type, second grow type, avatar
            // visibility, then level.
            if (rows.Length < 6 || !byte.TryParse(rows[5], out var level) || level == 0)
            {
                continue;
            }

            var point = ParseInlineIntegers(text, "appearance point").FirstOrDefault();
            apcs.TryAdd(apcId, new DungeonTowerApc(apcId, level, point, path));
        }

        return apcs;
    }

    private static DungeonTowerFloorDefinition? ReadTowerFloor(
        ScriptFileSystem scripts,
        int floorNumber,
        ushort mapId,
        string mapPath,
        IReadOnlyDictionary<ushort, DungeonTowerApc> apcs)
    {
        var text = scripts.ReadAllText(mapPath, ScriptEncoding);
        var fixedApcs = new List<DungeonTowerApcSpawn>();
        foreach (var line in ParseAllBlocks(text, "ai character").SelectMany(ParseDataLines))
        {
            var match = ApcSpawnPattern.Match(line);
            var values = ParseIntegers(line);
            if (!match.Success || values is not [> 0 and <= ushort.MaxValue, _, _, _, _, _]
                || !apcs.TryGetValue((ushort)values[0], out var apc)
                || fixedApcs.Count >= RandomApcIndexStart)
            {
                return null;
            }

            var actorType = match.Groups[5].Value;
            var team = actorType.ToLowerInvariant() switch
            {
                "character" => 0,
                "monster" => 100,
                "neutral" => 200,
                _ => -1
            };
            if (team < 0)
            {
                return null;
            }

            fixedApcs.Add(new DungeonTowerApcSpawn(
                apc.ApcId,
                apc.Level,
                new DungeonTowerSpawnPosition(values[1], values[2], values[3]),
                actorType,
                team,
                match.Groups[6].Value,
                values[4],
                values[5]));
        }

        var randomApcPoint = ParseInlineIntegers(text, "apc random point").FirstOrDefault();
        var positions = new List<DungeonTowerSpawnPosition>();
        var rows = ParseTagRows(text, "monster spawn pos").ToArray();
        if (rows.Length > 0)
        {
            if (rows[0] is not [>= 0] || rows[0][0] != rows.Length - 1)
            {
                return null;
            }

            foreach (var row in rows.Skip(1))
            {
                if (row is not [_, _, _])
                {
                    return null;
                }

                positions.Add(new DungeonTowerSpawnPosition(row[0], row[1], row[2]));
            }
        }

        if (randomApcPoint < 0
            || (randomApcPoint > 0 && (positions.Count == 0 || !apcs.Values.Any(apc => apc.AppearancePoint > 0))))
        {
            return null;
        }

        return new DungeonTowerFloorDefinition(
            floorNumber,
            mapId,
            fixedApcs.AsReadOnly(),
            randomApcPoint,
            positions.AsReadOnly());
    }

    private sealed record ApcBucket(int Point, DungeonTowerApc[] Candidates);

    private sealed record TowerCatalogEntry(
        DungeonTowerDefinition Definition,
        DungeonPassiveItemGroup[] PassiveItemGroups,
        ApcBucket[] ApcBuckets);
}
