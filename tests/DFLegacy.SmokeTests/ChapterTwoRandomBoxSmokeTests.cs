using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoRandomBoxSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check, string? actualPvf = null, string? replyPath = null)
    {
        using var fixture = new Fixture();
        var catalog = fixture.Catalog;
        check(catalog.TryGetDefinition(200, out var box) && box.RandomBox is { Groups.Count: 3 }
            && box.RandomBox.RequiredItems[201] == 4 && box.RandomBox.RequiredItems[200] == 1
            && box.Lottery is null && box.CeraBooster is null,
            "the catalog reads three Amazing Box pools and both consumed items separately from jars and Cera packages");
        var groups = box.RandomBox!.Groups;
        check(groups[0].Select(new Rolls(2999)).ItemId == 15
            && groups[0].Select(new Rolls(3000)).ItemId == 42
            && groups[0].Select(new Rolls(99999)).Count == 1,
            "random-box weighted boundaries use the original 100000 range and fallback");

        byte[] command = [0x12, 0x34, 41, 0, 73, 0];
        check(GameProtocolEngine.TryReadRandomBoxCommand(command, out var parsed)
            && parsed == new GameRandomBoxCommand(0x3412, 41, 73)
            && GameProtocolEngine.TryReadRandomBoxCommand([.. command, .. new byte[12]], out _)
            && !GameProtocolEngine.TryReadRandomBoxCommand(command[..5], out _)
            && !GameProtocolEngine.TryReadRandomBoxCommand([.. command, 0], out _)
            && !GameProtocolEngine.TryReadRandomBoxCommand([.. command, .. new byte[11], 1], out _),
            "CMD191 reads sequence and two slots, accepts only minimal C2 zero padding, and rejects damaged bodies");
        var packet = GameProtocolEngine.CreateRandomBoxReply(41, 73,
            [new(42, 15, 4, 0, 1), new(9, 26153, 1, 23, 0)]);
        byte[] expected = [1, 41, 0, 73, 0, 2, 0,
            42, 0, 15, 0, 4, 0, 0, 0, 0, 0, 1,
            9, 0, 41, 102, 1, 0, 0, 0, 23, 0, 0];
        check(packet.ProtocolId == 191 && packet.Payload.SequenceEqual(expected)
            && GameProtocolEngine.CreateRandomBoxError(4).Payload.SequenceEqual(new byte[] { 0, 4 }),
            "CMD191 writes the native two-consumption-slot header and exact 11-byte reward records");
        if (replyPath is not null) await File.WriteAllBytesAsync(replyPath, packet.Payload);
        var original = Inventory();
        check(RandomBoxOpeningPlanner.TryCreate(original, 41, 73, 1, catalog, uint.MaxValue,
                new Rolls(0, 99999, 0), out var plan, out _)
            && plan.Inventory[41].CountOrValue == 1 && plan.Inventory[73].CountOrValue == 5
            && plan.Rewards.Sum(reward => reward.CountOrValue) == 4
            && original[41].CountOrValue == 2 && original[73].CountOrValue == 8,
            "one opening consumes one box and four hammers, places one reward per pool and preserves its input");
        check(!RandomBoxOpeningPlanner.TryCreate(original, 41, 41, 1, catalog, uint.MaxValue,
                new Rolls(), out _, out _)
            && !RandomBoxOpeningPlanner.TryCreate(original, 73, 41, 1, catalog, uint.MaxValue,
                new Rolls(), out _, out _), "swapped and overlapping consumption slots cannot open a box");
        var tooFew = Inventory(); tooFew[73] = new(73, 201, 3);
        check(!RandomBoxOpeningPlanner.TryCreate(tooFew, 41, 73, 1, catalog, uint.MaxValue,
                new Rolls(), out _, out _) && tooFew[73].CountOrValue == 3,
            "insufficient hammers leave both source stacks intact");
        var full = Inventory();
        foreach (var slot in Enumerable.Range(3, 6).Concat(Enumerable.Range(42, 31)))
            full[(ushort)slot] = new((ushort)slot, 16, 1);
        check(!RandomBoxOpeningPlanner.TryCreate(full, 41, 73, 1, catalog, uint.MaxValue,
                new Rolls(), out _, out var fullError) && fullError == 4 && full[41].CountOrValue == 2,
            "a full reward category rejects the whole opening with native error4");
        check(!RandomBoxOpeningPlanner.TryCreate(original, 41, 73, 1, catalog, 83,
                new Rolls(), out _, out var weightError) && weightError == 21,
            "overweight rewards reject the whole opening with error21 before any mutation");
        await Persistence(fixture, check);
        if (actualPvf is not null) CheckActual(actualPvf, check);
    }

    private static void CheckActual(string path, Action<bool, string> check)
    {
        var options = new ServerOptions { ScriptPvfPath = path, SkipUnsupportedChapterTwoCashContainers = true };
        var catalog = new ItemCatalog(new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance),
            NullLogger<ItemCatalog>.Instance, options);
        check(catalog.TryGetDefinition(200, out var definition) && definition.RandomBox is { Groups.Count: 3 }
            && definition.RandomBox.Groups.Select(group => group.Rewards.Count).SequenceEqual(new[] { 24, 24, 8 }),
            "the actual Chapter Two PVF supplies 56 weighted rows and three fallbacks for item200");
        if (definition.RandomBox is not { } box) return;
        var exercised = 0;
        for (var groupIndex = 0; groupIndex < box.Groups.Count; groupIndex++)
        {
            var group = box.Groups[groupIndex];
            var cumulative = 0;
            foreach (var reward in group.Rewards.Append(group.Fallback))
            {
                var rolls = Enumerable.Repeat(99999, box.Groups.Count).ToArray();
                rolls[groupIndex] = reward == group.Fallback ? 99999 : cumulative;
                var expectedCounts = new Dictionary<ushort, uint> { [200] = 1, [201] = 4 };
                for (var index = 0; index < box.Groups.Count; index++)
                {
                    var selected = index == groupIndex ? reward : box.Groups[index].Fallback;
                    expectedCounts[selected.ItemId] = expectedCounts.GetValueOrDefault(selected.ItemId) + selected.Count;
                }
                var success = RandomBoxOpeningPlanner.TryCreate(Inventory(), 41, 73, 60, catalog,
                    uint.MaxValue, new Rolls(rolls), out var plan, out var error);
                check(success && error == 0 && expectedCounts.All(pair =>
                        plan.Inventory.Values.Where(item => item.ItemId == pair.Key).Sum(item => item.CountOrValue) == pair.Value)
                    && plan.Inventory.Values.Where(item => catalog.IsEquipment(item.ItemId)).All(item => item.InstanceId != Guid.Empty),
                    $"actual random-box pool {groupIndex} row {exercised} delivers complete item {reward.ItemId}x{reward.Count}");
                cumulative += reward.Weight;
                exercised++;
            }
        }
        Console.WriteLine($"Actual random-box reward choices exercised: {exercised}");
    }

    private static Dictionary<ushort, CharacterItemRecord> Inventory() =>
        new() { [41] = new(41, 200, 2), [73] = new(73, 201, 8) };

    private static async Task Persistence(Fixture fixture, Action<bool, string> check)
    {
        var failSave = false;
        var replacement = new AtomicSnapshotReplacement((source, target) =>
        {
            if (failSave) throw new IOException("random-box injected failure");
            File.Move(source, target, true);
        }, (_, _) => Task.CompletedTask);
        var store = new JsonGameStore(fixture.Options, NullLogger<JsonGameStore>.Instance, replacement,
            itemCatalog: fixture.Catalog);
        await store.CreateAccountAsync(new("boxfixture", "fixture-only", OicqCode: "123456"));
        var character = (await store.CreateCharacterAsync("boxfixture", new("BoxFixture"))).Character!;
        character = (await store.SaveCharacterItemSpacesAsync("boxfixture", character.Id, 123,
            Inventory().Values, [], []))!;
        var session = new RandomBoxOpeningSession(fixture.Catalog);
        var before = await File.ReadAllBytesAsync(fixture.Options.DataPath);
        failSave = true;
        var failed = await session.OpenAsync(store, "boxfixture", character, new(12, 41, 73),
            uint.MaxValue, new Rolls(), CancellationToken.None);
        var afterFailedSave = await File.ReadAllBytesAsync(fixture.Options.DataPath);
        var afterFailedCharacter = await store.GetCharacterAsync("boxfixture", character.Id);
        check(failed.Character is null && failed.Reply.Payload[0] == 0
            && before.SequenceEqual(afterFailedSave)
            && afterFailedCharacter!.Inventory!
                .Single(item => item.ItemId == 200).CountOrValue == 2,
            "failed atomic replacement returns a closing error and preserves persisted and in-memory stacks");
        failSave = false;
        var success = await session.OpenAsync(store, "boxfixture", character, new(12, 41, 73),
            uint.MaxValue, new Rolls(), CancellationToken.None);
        check(success.Character is not null && success.Reply.Payload[0] == 1,
            "retrying a failed save can commit the opening once");
        if (success.Character is null) return;
        var committed = await File.ReadAllBytesAsync(fixture.Options.DataPath);
        var repeated = await session.OpenAsync(store, "boxfixture", success.Character, new(12, 41, 73),
            uint.MaxValue, new Rolls(), CancellationToken.None);
        var afterRepeated = await File.ReadAllBytesAsync(fixture.Options.DataPath);
        check(repeated.Character is null && repeated.Reply.Payload[0] == 0
            && committed.SequenceEqual(afterRepeated),
            "a repeated successful sequence returns a closing error without consuming or granting twice");
        var stale = await session.OpenAsync(store, "boxfixture", character, new(13, 41, 73),
            uint.MaxValue, new Rolls(), CancellationToken.None);
        var afterStale = await File.ReadAllBytesAsync(fixture.Options.DataPath);
        check(stale.Character is null && committed.SequenceEqual(afterStale),
            "a different sequence with a stale character cannot overwrite a committed opening");
        var second = await session.OpenAsync(store, "boxfixture", success.Character, new(13, 41, 73),
            uint.MaxValue, new Rolls(), CancellationToken.None);
        check(second.Character is not null && second.Character.Inventory!.All(item => item.ItemId != 200),
            "the next native sequence can open the remaining box normally");
        var reloaded = new JsonGameStore(fixture.Options, NullLogger<JsonGameStore>.Instance,
            itemCatalog: fixture.Catalog);
        await reloaded.InitializeAsync();
        var durable = await reloaded.GetCharacterAsync("boxfixture", character.Id);
        check(durable is not null && durable.Gold == 123 && durable.Inventory!.All(item => item.ItemId != 200)
            && durable.Inventory!.Single(item => item.ItemId == 15).CountOrValue == 8,
            "both openings and their rewards survive a fresh store load");
        var concurrentCharacter = (await store.CreateCharacterAsync("boxfixture", new("ConcurrentBox"))).Character!;
        concurrentCharacter = (await store.SaveCharacterItemSpacesAsync("boxfixture", concurrentCharacter.Id, 0,
            Inventory().Values, [], []))!;
        var concurrent = await Task.WhenAll(
            session.OpenAsync(store, "boxfixture", concurrentCharacter, new(14, 41, 73), uint.MaxValue, new Rolls(), CancellationToken.None),
            session.OpenAsync(store, "boxfixture", concurrentCharacter, new(14, 41, 73), uint.MaxValue, new Rolls(), CancellationToken.None));
        check(concurrent.Count(result => result.Character is not null) == 1
            && (await store.GetCharacterAsync("boxfixture", concurrentCharacter.Id))!.Inventory!
                .Single(item => item.ItemId == 200).CountOrValue == 1,
            "concurrent deliveries of one sequence commit exactly one opening");
    }

    private sealed class Rolls(params int[] values) : IDropRandomSource
    {
        private int index;
        public int Next(int maximum) => index < values.Length ? values[index++] : 0;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("dflegacy-random-box-");
        internal ServerOptions Options { get; }
        internal ItemCatalog Catalog { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "stackable"));
            File.WriteAllText(Path.Combine(root.FullName, "stackable", "stackable.lst"),
                "200 `box.stk`\n201 `hammer.stk`\n15 `reward.stk`\n42 `fallback.stk`\n16 `filler.stk`\n");
            File.WriteAllText(Path.Combine(root.FullName, "stackable", "box.stk"), """
                [name] `Amazing Box`
                [stackable type] `[random upgradable legacy]` 1
                [weight] 1
                [minimum level] 1
                [stack limit] 10
                [RANDOMBOX]
                [int data]
                42 1 0
                15 3000 2 0
                [/int data]
                [int data]
                42 1 0
                15 3000 2 0
                [/int data]
                [int data]
                201 1 0
                201 2000 1 0
                [/int data]
                [sealing removal item]
                2
                201 4
                200 1
                [/sealing removal item]
                [/RANDOMBOX]
                """);
            foreach (var (file, type, weight) in new[] { ("hammer", "material", 10), ("reward", "consumable", 100),
                         ("fallback", "consumable", 1), ("filler", "consumable", 1) })
                File.WriteAllText(Path.Combine(root.FullName, "stackable", file + ".stk"),
                    $"[name] `{file}`\n[stackable type] `[{type}]` 0\n[weight] {weight}\n[stack limit] 1000\n");
            Options = new ServerOptions { ScriptPvfPath = "", SkillScriptPath = root.FullName,
                DataPath = Path.Combine(root.FullName, "state.json"), SeedTestAccount = false };
            Catalog = new ItemCatalog(new ScriptFileSystem(Options, NullLogger<ScriptFileSystem>.Instance),
                NullLogger<ItemCatalog>.Instance, Options);
        }
        public void Dispose() => root.Delete(true);
    }
}
