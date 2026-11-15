using DFLegacy.Server;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

internal static class PvpMultiChannelSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var options = new ServerOptions();
        options.Pvp.Enabled = true;
        options.Pvp.Validate(options);
        var pvp = PvpChannelConfiguration.GameplayChannels(options).Where(c => c.Number >= 51).ToArray();
        check(pvp.Select(c => c.Number).SequenceEqual(new[] { 51, 52, 53, 55 }), "four rank-aware ACT1 PvP channels are advertised");
        check(pvp.Select(c => c.Port).SequenceEqual(new[] { 7004, 7110, 7111, 7112 }), "four PvP listeners use the approved port allocation");
        foreach (var grade in new[] { 0, 9, 10, 19, 20, 30 })
        {
            var expected = grade < 10 ? 51 : grade < 20 ? 52 : 53;
            check(PvpChannelCatalog.Profiles(options.Pvp).Where(c => c.Allows(grade, 30)).Select(c => c.Number).SequenceEqual(new[] { expected, 55 }), $"rank {grade} has exactly its graded channel and free channel");
        }
        await PvpIndividualSmokeTests.MultiChannelIsolation(check);
        await Admission(check);
        await DisplacedAdmission(check);
    }

    internal static async Task DisplacedAdmission(Action<bool, string> check)
    {
        using var f = new AdminRewardStoreSmokeTests.Fixture();
        f.Options.Pvp.Enabled = true;
        Directory.CreateDirectory(Path.Combine(f.Root, "etc"));
        File.WriteAllText(Path.Combine(f.Root, "etc/pvp_ref.etc"), "[pvp experience max grade]\n30\n[pvp experience]\n"
            + string.Join('\n', Enumerable.Range(1,30).Select(i => $"{i} {i*1000}")) + "\n[/pvp experience]");
        var store=f.Open(); await store.InitializeAsync();
        await store.CreateAccountAsync(new("racecheck", "password", OicqCode:"123456"));
        var current=(await store.CreateCharacterAsync("racecheck",new("RaceCheck"))).Character!;
        var other=(await store.CreateCharacterAsync("racecheck",new("RaceOther"))).Character!;
        var root=JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        foreach(var c in root["Accounts"]![0]!["Characters"]!.AsArray()) { c!["PvpPoints"]=19000; c["PvpGrade"]=20; }
        await File.WriteAllTextAsync(f.Options.DataPath,root.ToJsonString()); await store.InitializeAsync();
        await using var services=NewCharacterDefaultsSmokeTests.CreateSessionServices(f.Options,store,f.Items);
        var sessions=services.GetRequiredService<CharacterSessionRegistry>();
        var rating=services.GetRequiredService<PvpExperienceCatalog>();
        CharacterSessionLease? old=null; Task? settlement=null;
        old=await sessions.ClaimAsync(current.CharacterNo,current.Id,Guid.NewGuid(),()=>
        {
            settlement=Task.Run(async()=>
            {
                try { await store.CommitPvpMatchAsync(new(Guid.NewGuid(),
                    [new(current.Id,20,true,null,null),new(other.Id,20,false,new ushort[11],new uint[8])],other.Id,false),rating); }
                finally { sessions.Release(old); }
            });
        },CancellationToken.None);
        var service=ActivatorUtilities.CreateInstance<EntranceService>(services);
        await using var connection=await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service,f.Options,7111);
        await connection.LoginAsync("racecheck",0,expectSelection:false);
        if(settlement is not null) await settlement;
        check((await store.GetCharacterAsync("racecheck",current.Id))!.PvpGrade==19,"displaced active match settles a real rank20 to19 forfeit during claim");
        check(connection.SelectReply!.Payload[0]==0,"post-claim canonical rank blocks an advanced-channel boundary bypass");
        // A rejected lease must not leave the character owned by a dead selection.
        check(!sessions.NotifyVictoryPointsChanged(current.Id),"rejected post-claim selection releases its newly acquired lease");
    }

    private static async Task Admission(Action<bool, string> check)
    {
        using var f = new AdminRewardStoreSmokeTests.Fixture();
        f.Options.Pvp.Enabled = true;
        Directory.CreateDirectory(Path.Combine(f.Root, "etc"));
        File.WriteAllText(Path.Combine(f.Root, "etc/pvp_ref.etc"), "[pvp experience max grade]\n30\n[pvp experience]\n"
            + string.Join('\n', Enumerable.Range(1, 30).Select(i => $"{i} {i * 1000}")) + "\n[/pvp experience]");
        var store = f.Open(); await store.InitializeAsync();
        if (!(await store.CreateAccountAsync(new("rankcheck", "password", OicqCode: "123456"))).Created
            || !(await store.CreateCharacterAsync("rankcheck", new("RankCheck"))).Created)
            throw new InvalidOperationException("Rank fixture account/character creation failed.");
        foreach (var grade in new[] { 0, 9, 10, 19, 20, 30 })
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
            var character = root["Accounts"]![0]!["Characters"]![0]!;
            character["PvpPoints"] = grade == 0 ? 0 : (grade - 1) * 1000;
            character["PvpGrade"] = 0; // Admission must derive the canonical grade from experience.
            await File.WriteAllTextAsync(f.Options.DataPath, root.ToJsonString());
            await store.InitializeAsync();
            foreach (var profile in PvpChannelCatalog.Profiles(f.Options.Pvp))
            {
                await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(f.Options, store, f.Items);
                var service = ActivatorUtilities.CreateInstance<EntranceService>(services);
                await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(service, f.Options, profile.Port);
                var allowed = profile.Allows(grade, 30);
                await connection.LoginAsync("rankcheck", 0, expectSelection: allowed);
                check(connection.LoginReply!.Payload[3] == 4, $"channel {profile.Number} uses native PvP login mode");
                check((connection.SelectReply!.Payload[0] == 1) == allowed, $"encrypted select on channel {profile.Number} enforces canonical rank {grade}");
            }
        }
        await using var normalServices = NewCharacterDefaultsSmokeTests.CreateSessionServices(f.Options, store, f.Items);
        var normalService = ActivatorUtilities.CreateInstance<EntranceService>(normalServices);
        await using var normal = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(normalService, f.Options, f.Options.Channel.Port);
        await normal.LoginAsync("rankcheck", 0);
        check(normal.LoginReply!.Payload[3] == 0 && normal.SelectReply!.Payload[0] == 1, "top PvP rank still enters normal gameplay channel");
    }
}
