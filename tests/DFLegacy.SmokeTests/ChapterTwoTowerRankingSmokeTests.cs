using System.Globalization;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoTowerRankingSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = new TemporaryDirectory();
        var options = new ServerOptions
        {
            DataPath = Path.Combine(fixture.Path, "state.json"),
            SeedTestAccount = false
        };
        var store = MakeStore();
        await store.InitializeAsync();
        const ushort tower = 11001;
        var people = new ChapterTwoTowerRankingParticipant[15];
        for (var index = 0; index < people.Length; index++)
        {
            var account = $"tower_rank{index:00}";
            await store.CreateAccountAsync(new(account, "password", OicqCode: "123456"));
            var created = await store.CreateCharacterAsync(account, new($"Rank{index:00}", Level: 50));
            var character = created.Character ?? throw new InvalidOperationException($"Could not create {account}.");
            people[index] = new(character.Id, new(character.Name, 50, 0));
        }

        var day = At("2026-06-12T04:00:00+08:00");
        var legacy = await store.SaveChapterTwoTowerPersonalBestAsync("tower_rank00", people[0].CharacterId,
            new([people[0].Member], 25, 700), default);
        var unseeded = await Read(1, 0, day);
        check(legacy.Best?.TotalMilliseconds == 700 && unseeded.PersonalBest?.TotalMilliseconds == 700
            && unseeded.PersonalRank is null && unseeded.TopFive.Length == 0,
            "a preexisting personal record retains its value without entering the new public ranking");

        var low = await Settle(Guid.NewGuid(), 9, 900, day.AddMinutes(-10), 1);
        var high = await Settle(Guid.NewGuid(), 10, 2000, day.AddMinutes(-10), 2);
        var beforeRefresh = await Read(1, 2, day.AddTicks(-1));
        var afterRefresh = await Read(1, 2, day);
        check(low.PersonalBest?.ClearedFloor == 9 && low.VirtualRank is null
            && high.PersonalBest?.ClearedFloor == 10 && high.VirtualRank == 1
            && beforeRefresh.PersonalRank is null && beforeRefresh.TopFive.Length == 0
            && afterRefresh.PersonalRank == 1 && afterRefresh.TopFive is [{ ClearedFloor: 10 }]
            && (await Read(1, 1, day)).PersonalRank is null,
            "clearing 10F qualifies at the 04:00 China refresh, while clearing 9F remains personal only");

        var third = await Settle(Guid.NewGuid(), 25, 3100, day.AddMinutes(1), 3);
        await Settle(Guid.NewGuid(), 25, 3100, day.AddMinutes(2), 4);
        await Settle(Guid.NewGuid(), 25, 4700, day.AddMinutes(3), 5);
        await Settle(Guid.NewGuid(), 25, 2300, day.AddMinutes(4), 6);
        await Settle(Guid.NewGuid(), 23, 1200, day.AddMinutes(5), 7);
        await Settle(Guid.NewGuid(), 23, 1300, day.AddMinutes(6), 8);
        var unchangedToday = await Read(1, 6, day.AddHours(23));
        var tomorrow = day.AddDays(1);
        var published = await Read(1, 3, tomorrow);
        check(third.PersonalRank is null && third.VirtualRank == 1
            && unchangedToday.TopFive is [{ ClearedFloor: 10 }]
            && published.TopFive.Select(record => record.Members[0].Name)
                .SequenceEqual(new[] { "Rank06", "Rank03", "Rank04", "Rank05", "Rank07" }),
            "daily top five stays stable until refresh, then sorts by floor followed by time and keeps a stable tie order");
        check(published.PersonalRank == 2 && (await Read(1, 4, tomorrow)).PersonalRank == 2
            && (await Read(1, 5, tomorrow)).PersonalRank == 4 && (await Read(1, 8, tomorrow)).PersonalRank == 6,
            "equal floor and time share a competition rank and personal rank can exceed the public five");
        var virtualScore = await Settle(Guid.NewGuid(), 25, 3100, tomorrow.AddMinutes(1), 8);
        check(virtualScore.PersonalRank == 6 && virtualScore.VirtualRank == 2 && virtualScore.Improved,
            "the current attempt's virtual rank uses its own score against the published board even before refresh");

        var firstParty = Guid.NewGuid();
        var partyResult = await Settle(firstParty, 18, 3200, day.AddMinutes(7), 9, 10);
        var otherMember = await store.SettleChapterTwoTowerRankingAsync(firstParty, tower, [people[9], people[10]],
            18, 3200, day.AddMinutes(7), people[10].CharacterId, default);
        check(!partyResult.Improved && !otherMember.Improved && otherMember.PersonalBest?.ClearedFloor == 18,
            "one party settlement updates each original member and repeats can read their own personal best");
        var retry = Guid.NewGuid();
        var retryTime = day.AddMinutes(8);
        var retryResults = await Task.WhenAll(
            Settle(retry, 18, 3100, retryTime, 10, 9),
            store.SettleChapterTwoTowerRankingAsync(retry, tower, [people[10], people[9]],
                18, 3100, retryTime, people[9].CharacterId, default));
        check(retryResults.All(result => result.Improved && result.PersonalBest?.TotalMilliseconds == 3100),
            "concurrent member settlement shares one retry and persists both personal improvements");

        await Settle(Guid.NewGuid(), 19, 4300, day.AddMinutes(9), 9, 10, 11);
        await Settle(Guid.NewGuid(), 24, 4400, day.AddMinutes(10), 9, 10, 11, 12);
        var duo = await Read(2, 10, tomorrow);
        check(duo.PersonalRank == 1 && duo.TopFive is [{ TotalMilliseconds: 3100 }]
            && duo.TopFive[0].Members.Select(member => member.Name).SequenceEqual(new[] { "Rank10", "Rank09" })
            && (await Read(3, 9, tomorrow)).TopFive is [{ ClearedFloor: 19 }]
            && (await Read(4, 9, tomorrow)).TopFive is [{ ClearedFloor: 24 }],
            "the ladder separates all four team sizes and keeps the fastest attempt per unordered team");

        await store.SaveCharacterLocationAsync("tower_rank00", people[0].CharacterId, 3, 7, 1, 2, 0);
        store = MakeStore();
        await store.InitializeAsync();
        var reloaded = await Read(2, 9, tomorrow);
        var repeated = await store.SettleChapterTwoTowerRankingAsync(retry, tower, [people[10], people[9]],
            18, 3100, retryTime, people[9].CharacterId, default);
        var rejected = false;
        try
        {
            await Settle(retry, 25, 1, retryTime, 10, 9);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        check(reloaded.TopFive is [{ TotalMilliseconds: 3100 }] && reloaded.PersonalRank == 1
            && repeated.Improved && rejected && (await Read(2, 9, tomorrow)).TopFive is [{ ClearedFloor: 18 }],
            "ordinary saves and reload preserve the public board and reject a conflicting replay of the same attempt");

        var nextQuarter = At("2026-07-01T04:00:00+08:00");
        await Settle(Guid.NewGuid(), 22, 1000, nextQuarter.AddTicks(-1), 13);
        var beforeQuarter = await Read(1, 3, nextQuarter.AddTicks(-1));
        await Settle(Guid.NewGuid(), 21, 1500, nextQuarter, 14);
        var reset = await Read(1, 3, nextQuarter);
        var afterQuarter = await Read(1, 14, nextQuarter.AddDays(1));
        check(beforeQuarter.TopFive.Length == 5 && reset.TopFive.Length == 0 && reset.PersonalRank is null
            && reset.PersonalBest?.ClearedFloor == 25 && afterQuarter.PersonalRank == 1
            && afterQuarter.TopFive is [{ ClearedFloor: 21 }],
            "the 04:00 China calendar quarter boundary resets the public board and preserves private records");

        var sameDay = nextQuarter.AddDays(1).AddMinutes(1);
        await Settle(Guid.NewGuid(), 22, 1600, sameDay, 14);
        await Settle(Guid.NewGuid(), 23, 1700, sameDay.AddMinutes(1), 14);
        check((await Read(1, 14, sameDay.AddHours(12))).TopFive is [{ ClearedFloor: 21 }]
            && (await Read(1, 14, sameDay.AddDays(1))).TopFive is [{ ClearedFloor: 23 }],
            "a new team best preserves the already published score and replaces only the pending score");

        foreach (var afterReplace in new[] { false, true })
            await CheckInterruptedWriteAsync(fixture.Path, afterReplace, check);

        JsonGameStore MakeStore() => new(options, NullLogger<JsonGameStore>.Instance);

        Task<ChapterTwoTowerRankingResult> Settle(Guid id, ushort floor, uint milliseconds,
            DateTimeOffset when, params int[] members) =>
            store.SettleChapterTwoTowerRankingAsync(id, tower, members.Select(index => people[index]).ToArray(),
                floor, milliseconds, when, people[members[0]].CharacterId, default);

        Task<ChapterTwoTowerRankingResult> Read(int size, int member, DateTimeOffset when) =>
            store.GetChapterTwoTowerRankingAsync(tower, size, people[member].CharacterId, when, default);
    }

    private static async Task CheckInterruptedWriteAsync(string directory, bool afterReplace, Action<bool, string> check)
    {
        var options = new ServerOptions
        {
            DataPath = Path.Combine(directory, $"interrupted-{afterReplace}.json"),
            SeedTestAccount = false
        };
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
        await store.InitializeAsync();
        var members = new ChapterTwoTowerRankingParticipant[2];
        for (var index = 0; index < members.Length; index++)
        {
            var account = $"tower_atomic{index}";
            await store.CreateAccountAsync(new(account, "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync(account, new($"Atomic{index}", Level: 50))).Character!;
            members[index] = new(character.Id, new(character.Name, 50, 0));
        }

        const ushort tower = 11001;
        var completed = At("2026-06-12T04:00:00+08:00");
        var attemptId = Guid.NewGuid();
        var broken = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, new InterruptedWriter(afterReplace));
        await broken.InitializeAsync();
        var uncertain = await broken.SettleChapterTwoTowerRankingAsync(attemptId, tower, members, 17, 3300,
            completed, members[0].CharacterId, default);
        var inMemory = await broken.GetChapterTwoTowerRankingAsync(tower, 2, members[1].CharacterId, completed.AddDays(1), default);
        check(uncertain.PersonalBest is null && inMemory.PersonalBest is null && inMemory.TopFive.Length == 0,
            $"an uncertain ranking write exposes neither personal nor public updates in memory afterReplace={afterReplace}");

        var loaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
        await loaded.InitializeAsync();
        var board = await loaded.GetChapterTwoTowerRankingAsync(tower, 2, members[0].CharacterId, completed.AddDays(1), default);
        var partner = await loaded.GetChapterTwoTowerRankingAsync(tower, 2, members[1].CharacterId, completed.AddDays(1), default);
        check(board.TopFive.Length == (afterReplace ? 1 : 0)
            && (board.PersonalBest?.ClearedFloor == 17) == afterReplace
            && (partner.PersonalBest?.ClearedFloor == 17) == afterReplace,
            $"reloading sees matching personal and public state for both members afterReplace={afterReplace}");

        var retry = await loaded.SettleChapterTwoTowerRankingAsync(attemptId, tower, members, 17, 3300,
            completed, members[1].CharacterId, default);
        var final = await loaded.GetChapterTwoTowerRankingAsync(tower, 2, members[1].CharacterId, completed.AddDays(1), default);
        check(!retry.Improved && retry.PersonalBest?.ClearedFloor == 17 && final.TopFive is [{ ClearedFloor: 17 }],
            $"retry after uncertain persistence produces one public team row and no false personal improvement afterReplace={afterReplace}");
    }

    private sealed class InterruptedWriter(bool afterReplace) : IDungeonRewardSnapshotWriter
    {
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken cancellationToken)
        {
            if (afterReplace)
                await new DungeonRewardSnapshotWriter().WriteAsync(path, snapshot, cancellationToken);
            throw new IOException("Injected tower ranking snapshot error.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = Directory.CreateTempSubdirectory("dfl-tower-ranking-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);
}
