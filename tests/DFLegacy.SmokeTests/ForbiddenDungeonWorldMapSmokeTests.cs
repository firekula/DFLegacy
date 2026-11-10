using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ForbiddenDungeonWorldMapSmokeTests
{
    internal static void Run(string pvf, Action<bool, string> check)
    {
        if (!File.Exists(pvf)) throw new FileNotFoundException("Pass an actual PVF through DF2008_REAL_PVF_PATH.", pvf);
        var options = new ServerOptions { ScriptPvfPath = pvf };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var maps = new WorldMapCatalog(scripts);
        foreach (byte area in new byte[] { 4, 6 })
        {
            check(maps.TryGetForGate(3, area, out var map) && map.Id == 4
                && map.DungeonIds.Order().SequenceEqual(new ushort[] { 21, 22, 23, 24, 25, 26, 27 }),
                "Behemoth gate 3/" + area + " includes the forbidden dungeon while retaining the six existing entries");
        }
        var quests = new QuestCatalog(scripts, NullLogger<QuestCatalog>.Instance);
        check(quests.TryGetDefinition(452, out var quest)
            && quest.ScriptPath.Equals("quest/value/blood_hell6.qst", StringComparison.OrdinalIgnoreCase)
            && quest.PrerequisiteQuestIds.Contains((ushort)451)
            && QuestDungeonClearRequirement.Matches(quest, 27, 0),
            "quest 452 retains its prerequisite and requires a normal-or-higher clear of dungeon 27");
        var character = new CharacterRecord(Guid.NewGuid(), "ForbiddenFixture", 0, 1, 60, 0, 0);
        check(quests.GetAcceptableQuestIds(character, [], [451]).Contains((ushort)452),
            "the unlock quest is offered after its immediate prerequisite");
        var dungeons = new DungeonCatalog(scripts, new ObjectDropGenerator(options,
            new ObjectDropCatalog(scripts, NullLogger<ObjectDropCatalog>.Instance),
            new GoldDropCatalog(scripts, NullLogger<GoldDropCatalog>.Instance)), NullLogger<DungeonCatalog>.Instance);
        check(dungeons.TryGetDefinition(27, out var dungeon) && dungeon.MinimumLevel == 34,
            "the restored entry points to the existing level-34 dungeon definition");
        check(HiddenDungeonUnlockCatalog.ResolveUnlockedDungeonIds([452], quests, dungeons).Contains((ushort)27),
            "existing active or completed quest 452 can restore dungeon access through the login migration");
    }
}
