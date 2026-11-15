using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoGuildProgressionSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-c2-guild-progression-");
        try
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(root.FullName, "state.json"),
                SkillScriptPath = root.FullName,
                SeedTestAccount = false
            };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("progress", "password", OicqCode: "123456"));
            var founder = (await store.CreateCharacterAsync("progress", new("Founder"))).Character!;
            await store.SaveCharacterItemSpacesAsync("progress", founder.Id, 2000000, [], [], []);
            var creation = await store.CreateGuildAsync("progress", founder.Id, "Guild", "progress", 300000);
            var guild = creation.Guild!;
            founder = creation.Character!;
            var snapshot = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
            snapshot["Guilds"]![0]!["Experience"] = 1350;
            await File.WriteAllTextAsync(options.DataPath, snapshot.ToJsonString());

            store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            var session = new GuildSession(store, new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance),
                new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance));
            var request = new PacketFrame(1, 76, 0, [0, 0]);
            var upgrade = await session.HandleAsync(request, "progress", founder, 1, true, default);
            check(upgrade.Packets.Any(packet => packet.Type == 1 && packet.ProtocolId == 76 && packet.Payload[0] == 1),
                "the original C2 guild upgrade command succeeds at 1350 guild experience");
            check((await store.GetCharacterAsync("progress", founder.Id))!.Gold == 1400000,
                "guild upgrade deducts the original 300000 gold exactly once");
            var after = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!["Guilds"]![0]!;
            check((int?)after["Level"] == 2 && (int?)after["SkillPoints"] == 1,
                "guild level and the first GSP persist with the gold deduction");
            var repeat = await session.HandleAsync(request, "progress", founder, 1, true, default);
            check(repeat.Packets.Any(packet => packet.Type == 1 && packet.ProtocolId == 76 && packet.Payload[0] == 0)
                && (await store.GetCharacterAsync("progress", founder.Id))!.Gold == 1400000,
                "repeated upgrade below the next experience threshold does not charge gold");
            PacketFrame Buy(byte skill) => new(1, 31, 0, [0, 0, skill]);
            var learnedStatus = await session.HandleAsync(Buy(200), "progress", founder, 1, true, default, 42);
            check(learnedStatus.Packets[0].Payload is [1, 42, 0, 204, 200, 1]
                && learnedStatus.Character?.Gold == 1300000,
                "learning status spends GSP and 100000 gold and preserves the ordinary SP wire field");
            var noGsp = await session.HandleAsync(Buy(201), "progress", founder, 1, true, default, 42);
            check(noGsp.Packets[0].Payload is [0, 22] && (await store.GetCharacterAsync("progress", founder.Id))!.Gold == 1300000,
                "the other skill cannot reuse a consumed GSP or charge gold on rejection");
            check(await store.CreditGuildDungeonClearAsync("progress", founder.Id, guild.Id + 1, 7, default) is null
                && GuildProgression.DungeonContribution(12, 10, 7) == 7
                && GuildProgression.DungeonContribution(14, 10, 7) == 0
                && GuildProgression.DungeonContribution(4, 10, 7) == 0,
                "only a matching dungeon level band and current guild count for the contribution");
            await store.CreditGuildDungeonClearAsync("progress", founder.Id, guild.Id, 3544, default);
            await session.HandleAsync(request, "progress", founder, 1, true, default);
            var learnedExperience = await session.HandleAsync(Buy(201), "progress", founder, 1, true, default, 42);
            check(learnedExperience.Packets[0].Payload is [1, 42, 0, 205, 201, 1]
                && learnedExperience.Character?.Gold == 700000,
                "learning Rapid Growth projects its distinct native skill slot and 1% level");

            var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await reloaded.InitializeAsync();
            var persisted = (await reloaded.GetGuildAsync(guild.Id)).Guild;
            var beneficiary = GuildBenefits.For(persisted, 4);
            check(persisted is { Level: 3, Experience: 4894, SkillPoints: 0, StatusSkillLevel: 1, ExperienceSkillLevel: 1 }
                && beneficiary.Skills().Select(skill => skill.SkillId).SequenceEqual(new byte[] { 200, 201 })
                && beneficiary.BonusExperience(1950) == 19
                && GuildBenefits.For(persisted, 0) == default && GuildBenefits.For(null, 4) == default,
                "restart restores guild progression, and only current eligible ranks have its learned skills");
        }
        finally { root.Delete(true); }
    }
}
