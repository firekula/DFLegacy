using System.Text.Json.Nodes;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal sealed class MentorGoldTestFixture : IAsyncDisposable
{
    internal required PartyHarness Harness;
    internal required JsonGameStore Store;
    internal required CharacterRecord Master;
    internal required CharacterRecord Child;
    internal required MentorService Service;
    internal required MentorGrowthSmokeTests.Clock Clock;
    internal Guid MasterSession = Guid.NewGuid(), ChildSession = Guid.NewGuid();
    internal Guid MasterToken, ChildToken;
    internal static async Task<MentorGoldTestFixture> CreateAsync(int level = 30, uint experience = 0,
        Action<JsonNode, Guid, Guid>? configure = null, Func<int>? rand = null, IDungeonRewardSnapshotWriter? writer = null,
        bool commerce = false)
    {
        var harness = await PartyHarness.CreateAsync();
        Directory.CreateDirectory(Path.Combine(harness.Root, "etc"));
        File.Copy(Path.Combine(MentorGoldPolicySmokeTests.FixtureRoot, "etc/goldlimitbylevel.etc"),
            Path.Combine(harness.Root, "etc/goldlimitbylevel.etc"));
        var (_, master, child) = await MentorGrowthSmokeTests.SeedAsync(harness, experience);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(harness.Options.DataPath))!;
        CharacterNode(root, master.Id)["Level"] = level;
        configure?.Invoke(root, master.Id, child.Id);
        await File.WriteAllTextAsync(harness.Options.DataPath, root.ToJsonString());
        if (commerce) TradeFixtures.Create(harness.Root);
        var store = writer is null ? new JsonGameStore(harness.Options, NullLogger<JsonGameStore>.Instance,
                itemCatalog: commerce ? TradeFixtures.CreateItems(harness.Root) : null,
                statCatalog: commerce ? TradeFixtures.CreateStats(harness.Root) : null)
            : new JsonGameStore(harness.Options, NullLogger<JsonGameStore>.Instance, writer);
        await store.InitializeAsync();
        var clock = new MentorGrowthSmokeTests.Clock();
        var service = new MentorService(store, clock, NullLogger<MentorService>.Instance, rand ?? (() => 1000));
        var fixture = new MentorGoldTestFixture { Harness = harness, Store = store, Master = (await store.GetCharacterByNameAsync(master.Name))!,
            Child = (await store.GetCharacterByNameAsync(child.Name))!, Clock = clock, Service = service };
        fixture.MasterToken = (await service.AttachChannelAsync("alice", master.Id, 1, fixture.MasterSession, _ => ValueTask.CompletedTask))!.Value;
        fixture.ChildToken = (await service.AttachChannelAsync("bobby", child.Id, 2, fixture.ChildSession, _ => ValueTask.CompletedTask))!.Value;
        return fixture;
    }
    internal static JsonNode CharacterNode(JsonNode root, Guid id) => root["Accounts"]!.AsArray()
        .SelectMany(a => a!["Characters"]!.AsArray()).Single(c => c!["Id"]!.GetValue<Guid>() == id)!;
    internal DungeonInstanceMember Member => new(Child.Id, 2, 0, Child.Name, _ => ValueTask.CompletedTask, "bobby", ChildToken);
    internal Task<DungeonGoldCommitResult> Pickup(uint amount = 1000, byte source = 1, Guid? id = null, Guid? token = null) =>
        Store.CommitDungeonGoldDistributionAsync(id ?? Guid.NewGuid(), [Member with { MentorGoldIncarnation = token ?? ChildToken }], 0, amount, routingSource: source);
    internal Task<MentorGoldFlushResult> Flush(Guid? id = null, Guid? token = null) =>
        Store.FlushMentorGoldAsync(Child.Id, token ?? ChildToken, id ?? Guid.NewGuid());
    internal async Task<CharacterRecord> ReadMaster() => (await Store.GetCharacterByNameAsync(Master.Name))!;
    public async ValueTask DisposeAsync() { Service.Dispose(); await Harness.DisposeAsync(); }
}
