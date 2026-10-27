using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class MentorGrowthSmokeTests
{
    internal sealed class Clock : TimeProvider
    {
        private long _seconds;
        private long _wallOffset;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _seconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(_seconds + _wallOffset);
        internal void Advance(int seconds) => _seconds += seconds;
        internal void JumpWall(int seconds) => _wallOffset += seconds;
    }

    // Catches using >= on upper-inclusive thresholds or projecting cumulative rather than interval EXP.
    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var (exp, level, current, next) in new (uint, byte, uint, uint)[]
                 { (0,1,0,3), (1,1,1,3), (3,1,3,3), (4,2,1,3), (6,2,3,3), (7,3,1,5), (879,10,411,412), (880,10,412,412) })
        {
            await using var f = await PartyHarness.CreateAsync();
            var (store, a, b) = await SeedAsync(f, exp);
            using var service = new MentorService(store, new Clock(), NullLogger<MentorService>.Instance);
            GameServerPacket? list = null;
            await service.AttachChannelAsync("bobby", b.Id, 2, Guid.NewGuid(), p => { list = p; return ValueTask.CompletedTask; });
            var payload = list!.Payload;
            var offset = 1 + 2 + 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(3)) + 1;
            check(payload[offset] == level && BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 1)) == current
                  && BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 5)) == next,
                $"mentor roster projects cumulative {exp} as {level}/{current}/{next}");
        }
        await CheckEndpointTimersAsync(check);
        await CheckCachesAndAwardsAsync(check);
        await CheckAtomicFailureAsync(check);
        await CheckLateRelationshipAndCleanupAsync(check);
        await CheckOrphanCompatibilityAsync(check);
        await CheckChainAndCapAsync(check);
        await CheckEconomyPreservationAsync(check);
        CheckClearPrecisionAndCapacity(check);
        await using var fixture = await PartyHarness.CreateAsync();
        var scripts = new ScriptFileSystem(fixture.Options, NullLogger<ScriptFileSystem>.Instance);
        var catalog = new CharacterExperienceCatalog(scripts, NullLogger<CharacterExperienceCatalog>.Instance);
        var character = (await fixture.Store.GetCharactersAsync("bobby"))[0] with { Level = 59, Experience = uint.MaxValue - 5 };
        var grant = catalog.Grant(character, 100);
        check(grant.Experience == uint.MaxValue && grant.GrantedExperience == 5,
            "uint saturation reports the actual five experience granted rather than requested 100");
        check(catalog.Grant(character with { Level = 60 },100).GrantedExperience == 0,
            "existing maximum-level characters receive no monster or mentor experience");
    }

    internal static uint Growth(CharacterRecord c) => JsonSerializer.SerializeToNode(c)?["MentorRelationshipExperience"]?.GetValue<uint>() ?? 0;
    internal static async Task<(JsonGameStore Store, CharacterRecord A, CharacterRecord B)> SeedAsync(PartyHarness f, uint exp)
    {
        var a = (await f.Store.GetCharactersAsync("alice"))[0];
        var b = (await f.Store.GetCharactersAsync("bobby"))[0];
        var state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        foreach (var account in state["Accounts"]!.AsArray())
        foreach (var c in account!["Characters"]!.AsArray())
        {
            if (c!["Id"]!.GetValue<Guid>() == a.Id) c["Level"] = 45;
            if (c["Id"]!.GetValue<Guid>() == b.Id)
            { c["Level"] = 6; c["MentorCharacterId"] = a.Id; c["MentorRelationshipExperience"] = exp; }
        }
        await File.WriteAllTextAsync(f.Options.DataPath, state.ToJsonString());
        var store = new JsonGameStore(f.Options, NullLogger<JsonGameStore>.Instance);
        await store.InitializeAsync();
        return (store, (await store.GetCharacterByNameAsync(a.Name))!, (await store.GetCharacterByNameAsync(b.Name))!);
    }

    private static async Task CheckCachesAndAwardsAsync(Action<bool,string> check)
    {
        await using var f = await PartyHarness.CreateAsync();
        var (store, a, b) = await SeedAsync(f, 3);
        var c = (await store.GetCharactersAsync("carol"))[0];
        await store.SaveCharacterProgressionAsync("carol", c.Id, 6, 0, 0);
        await store.EstablishMentorAsync(a.Id, c.Id, DateTimeOffset.Now.AddDays(1));
        var clock = new Clock();
        using var service = new MentorService(store, clock, NullLogger<MentorService>.Instance);
        var sa = Guid.NewGuid(); var sb = Guid.NewGuid(); var sc = Guid.NewGuid();
        await service.AttachChannelAsync("alice", a.Id, 1, sa, _ => ValueTask.CompletedTask);
        await service.AttachChannelAsync("bobby", b.Id, 2, sb, _ => ValueTask.CompletedTask);
        await service.AttachChannelAsync("carol", c.Id, 3, sc, _ => ValueTask.CompletedTask);
        check(await service.GetExperienceBonusRateAsync(b.Id, sb) == (double)0.01f,
            "online mentor loads the child's own relationship level using float32 precision");
        check(MentorGrowthPolicy.Bonus(100, await service.GetExperienceBonusRateAsync(b.Id, sb)) == 0,
            "level-one basis 100 truncates the widened float32 rate to zero");
        clock.Advance(600); await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 5
              && Growth((await store.GetCharacterByNameAsync(c.Name))!) == 2
              && await service.GetExperienceBonusRateAsync(c.Id, sc) == (double)0.02f,
            "crossing 3 to 4 broadcasts level two to the master's online siblings without persisting sibling EXP");
        await service.SendListAsync(c.Id, sc);
        await service.SweepChangesAsync();
        check(await service.GetExperienceBonusRateAsync(c.Id, sc) == (double)0.02f,
            "roster refresh and non-upgrade growth do not downgrade a sibling's broadcast cache");
        await service.AttachChannelAsync("carol", c.Id, 3, sc, _ => ValueTask.CompletedTask);
        check(await service.GetExperienceBonusRateAsync(c.Id, sc) == (double)0.01f,
            "replacement child incarnation reloads its own edge rather than a sibling's propagated level");
        await service.DetachChannelAsync(a.Id, sa);
        check(await service.GetExperienceBonusRateAsync(b.Id, sb) == 0, "mentor logout clears every apprentice reward cache");
        await service.AttachChannelAsync("alice", a.Id, 1, sa, _ => ValueTask.CompletedTask);
        check(await service.GetExperienceBonusRateAsync(b.Id, sb) == (double)0.02f
              && await service.GetExperienceBonusRateAsync(c.Id, sc) == (double)0.01f,
            "mentor relogin reloads each apprentice's own canonical edge");
        await store.DeleteCharacterAsync("alice", 0);
        check(await service.GetExperienceBonusRateAsync(b.Id, sb) == 0,
            "canonical deletion removes award eligibility before the 500ms removal-notice sweep");
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 0,
            "deleting the parent clears surviving apprentice growth");
        foreach (var (basis, nominal, actual, want) in new (uint,uint,uint,uint)[]
                 { (100,9,109,9), (100,9,105,5), (100,9,99,0), (100,9,0,0) })
            check(MentorGrowthPolicy.CreditedBonus(basis,nominal,actual) == want,
                $"actual grant {actual} reserves base before mentor extraB={want}");
        var clip = MentorGrowthPolicy.ClipClear(new() { BaseExperience=10, PartyBonus=20, RankBonus=30,
            AvatarBonus=40, CreatureBonus=50, EventBonus=60, BlackDiamondBonus=70, ChannelBonus=80, MentorBonus=90 }, 355);
        check(clip.BaseExperience == 10 && clip.PartyBonus == 20 && clip.RankBonus == 30 && clip.AvatarBonus == 40
              && clip.CreatureBonus == 50 && clip.EventBonus == 60 && clip.BlackDiamondBonus == 70
              && clip.ChannelBonus == 75 && clip.MentorBonus == 0 && clip.TotalExperience == 355,
            "clear capacity clips components in Base Party Rank Avatar Creature Event BlackDiamond Channel Mentor order");
        var packet = GameProtocolEngine.CreateExperienceGained(6,109,0,9,0);
        check(packet.ProtocolId == 37 && BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(9)) == 9,
            "ACT1 EXP NOTI37 carries the credited mentor share in extraExperienceB");
    }

    private static async Task CheckChainAndCapAsync(Action<bool,string> check)
    {
        await using var f = await PartyHarness.CreateAsync();
        var (store,a,b) = await SeedAsync(f,3);
        var c = (await store.GetCharactersAsync("carol"))[0];
        await store.SaveCharacterProgressionAsync("bobby",b.Id,30,0,0);
        await store.SaveCharacterProgressionAsync("carol",c.Id,6,0,0);
        await store.EstablishMentorAsync(b.Id,c.Id,DateTimeOffset.Now);
        var clock = new Clock();
        using var service = new MentorService(store,clock,NullLogger<MentorService>.Instance);
        var sa=Guid.NewGuid(); var sb=Guid.NewGuid(); var sc=Guid.NewGuid();
        // Only the middle endpoint is due at 600: its parent and child edges grow in one commit.
        await service.AttachChannelAsync("bobby",b.Id,2,sb,_=>ValueTask.CompletedTask);
        clock.Advance(1);
        await service.AttachChannelAsync("alice",a.Id,1,sa,_=>ValueTask.CompletedTask);
        await service.AttachChannelAsync("carol",c.Id,3,sc,_=>ValueTask.CompletedTask);
        clock.Advance(599); await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 4
              && Growth((await store.GetCharacterByNameAsync(c.Name))!) == 1
              && await service.GetExperienceBonusRateAsync(b.Id,sb) == (double)0.02f
              && await service.GetExperienceBonusRateAsync(c.Id,sc) == (double)0.01f,
            "middle endpoint grows two independent chain edges and each master's broadcast targets its own children");
        await store.DeleteCharacterAsync("bobby",0);
        check(Growth((await store.GetCharacterByNameAsync(c.Name))!) == 0
              && (await store.GetCharacterByNameAsync(c.Name))!.MentorCharacterId is null,
            "deleting the middle character removes both incident edges and clears surviving child growth");

        await using var capFixture = await PartyHarness.CreateAsync();
        var (capStore,master,child) = await SeedAsync(capFixture,879);
        var sibling = (await capStore.GetCharactersAsync("carol"))[0];
        await capStore.SaveCharacterProgressionAsync("carol",sibling.Id,6,0,0);
        await capStore.EstablishMentorAsync(master.Id,sibling.Id,DateTimeOffset.Now);
        var capClock = new Clock();
        using var capService = new MentorService(capStore,capClock,NullLogger<MentorService>.Instance);
        var sm=Guid.NewGuid(); var sx=Guid.NewGuid(); var sy=Guid.NewGuid();
        await capService.AttachChannelAsync("alice",master.Id,1,sm,_=>ValueTask.CompletedTask);
        await capService.AttachChannelAsync("bobby",child.Id,2,sx,_=>ValueTask.CompletedTask);
        await capService.AttachChannelAsync("carol",sibling.Id,3,sy,_=>ValueTask.CompletedTask);
        capClock.Advance(600); await capService.SweepChangesAsync();
        check(Growth((await capStore.GetCharacterByNameAsync(child.Name))!) == 880
              && await capService.GetExperienceBonusRateAsync(sibling.Id,sy) == (double)(10 * 0.01f),
            "879 to 880 broadcasts the terminal level exactly on the successful increment");
        capClock.Advance(600); await capService.SweepChangesAsync();
        check(await capService.GetExperienceBonusRateAsync(sibling.Id,sy) == (double)(10 * 0.01f),
            "a later sibling's level-two broadcast cannot downgrade its prior nonzero level-ten reward cache");
        await capService.AttachChannelAsync("carol",sibling.Id,3,sy,_=>ValueTask.CompletedTask);
        capClock.Advance(600); await capService.SweepChangesAsync();
        check(Growth((await capStore.GetCharacterByNameAsync(child.Name))!) == 880
              && await capService.GetExperienceBonusRateAsync(sibling.Id,sy) == (double)0.02f,
            "already capped edge does not rebroadcast level ten; sibling only receives its own new level two");
    }

    private static void CheckClearPrecisionAndCapacity(Action<bool,string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-mentor-clear-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"etc"));
        try
        {
        File.WriteAllText(Path.Combine(root,"etc/serverparameter.etc"),
            "[party user number exp bonusrate] 1 1.5 2 2.5\n[clear rank exp bonusrate] 0.05 0.1 0.15 0.2 0.3\n");
        var options = new ServerOptions { ScriptPvfPath=string.Empty, SkillScriptPath=root,
            Experience=new ExperienceOptions { ClearMultiplier=1_000_000 } };
        var scripts = new ScriptFileSystem(options,NullLogger<ScriptFileSystem>.Instance);
        var catalog = new DungeonExperienceCatalog(options,scripts,NullLogger<DungeonExperienceCatalog>.Instance);
        var dungeon = new DungeonDefinition(1,1,1,1,1,0,0,0,0,"B");
        var high = catalog.CalculateClearExperience(1,dungeon,0,70,20,
            bonuses:new(MentorBonusRate:(double)0.1f,EventBonusRate:20));
        check(high.BaseExperience == 300_000_000 && high.RankBonus == 30_000_000
              && high.EventBonus == 3_964_967_295 && high.MentorBonus == 0 && high.TotalExperience == uint.MaxValue,
            "nominal clear uint overflow reserves every established component before mentor, before cumulative-cap clipping");
        var normalOptions = new ServerOptions { ScriptPvfPath=string.Empty, SkillScriptPath=root };
        var normal = new DungeonExperienceCatalog(normalOptions,scripts,NullLogger<DungeonExperienceCatalog>.Instance);
        var clear = normal.CalculateClearExperience(1,dungeon,0,70,20,2,
            bonuses:new(MentorBonusRate:(double)0.01f,EventBonusRate:0.5));
        // Solo base 300 / two members * party 1.5 = 225; 225*float32(.01) truncates to 2.
        check(clear.BaseExperience == 150 && clear.PartyBonus == 75 && clear.RankBonus == 22
              && clear.EventBonus == 112 && clear.MentorBonus == 2 && clear.TotalExperience == 361,
            "clear mentor share uses withParty before independent rank/event bonuses with float32 rate widened to double");
        var clipped = MentorGrowthPolicy.ClipClear(clear,360);
        check(clipped.MentorBonus == 1 && clipped.TotalExperience == 360,
            "cumulative capacity credits only the final one available mentor experience in NOTI35 components");
        }
        finally { Directory.Delete(root,true); }
    }

    private static async Task CheckEconomyPreservationAsync(Action<bool,string> check)
    {
        await using var f = await PartyHarness.CreateAsync();
        var (store,a,b) = await SeedAsync(f,0);
        using var dungeon = new DungeonFixture();
        await store.CommitDungeonGoldDistributionAsync(Guid.NewGuid(),
            [dungeon.Member(a.Id,0,a.Name),dungeon.Member(b.Id,1,b.Name)],0,19);
        var clock = new Clock();
        using var service = new MentorService(store,clock,NullLogger<MentorService>.Instance);
        await service.AttachChannelAsync("alice",a.Id,1,Guid.NewGuid(),_=>ValueTask.CompletedTask);
        await service.AttachChannelAsync("bobby",b.Id,2,Guid.NewGuid(),_=>ValueTask.CompletedTask);
        clock.Advance(600);
        await Task.WhenAll(service.SweepChangesAsync(), store.SaveCharacterInventoryAsync("bobby",b.Id,b.Gold+5,b.Inventory ?? [],b.Equipment ?? []));
        var reload = new JsonGameStore(f.Options,NullLogger<JsonGameStore>.Instance);
        await reload.InitializeAsync();
        var saved = (await reload.GetCharacterByNameAsync(b.Name))!;
        check(Growth(saved) == 2 && saved.Gold == b.Gold+5 && (saved.Inventory?.Count ?? 0) == (b.Inventory?.Count ?? 0),
            "concurrent growth and ordinary inventory writes preserve canonical EXP, wallet and inventory");
        check((await reload.ConsumeDungeonGoldCreditsAsync(a.Id))?.Gold == a.Gold+10
              && (await reload.ConsumeDungeonGoldCreditsAsync(b.Id))?.Gold == b.Gold+14,
            "growth snapshots preserve both durable party-gold credits and their ledger through restart");
    }

    private static async Task CheckOrphanCompatibilityAsync(Action<bool,string> check)
    {
        await using var f = await PartyHarness.CreateAsync();
        var state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        foreach (var account in state["Accounts"]!.AsArray())
        foreach (var c in account!["Characters"]!.AsArray())
        {
            c!["MentorCharacterId"] = null;
            if (account["UserName"]!.GetValue<string>() == "bobby") c["MentorRelationshipExperience"] = 12;
            else c.AsObject().Remove("MentorRelationshipExperience");
        }
        await File.WriteAllTextAsync(f.Options.DataPath,state.ToJsonString());
        var store = new JsonGameStore(f.Options,NullLogger<JsonGameStore>.Instance);
        await store.InitializeAsync();
        check(Growth((await store.GetCharactersAsync("bobby"))[0]) == 0,
            "loading orphan mentor growth without a parent normalizes it to zero");
        check(Growth((await store.GetCharactersAsync("alice"))[0]) == 0,
            "legacy JSON without growth remains compatible with default zero");
        var orphan = (await store.GetCharactersAsync("bobby"))[0] with
        { MentorCharacterId=null, MentorRelationshipExperience=12, MentorExperienceBonusPercent=7 };
        var replacement = await store.ReplaceAllCharactersAsync("bobby",[orphan]);
        var cleaned = (await store.GetCharacterByNameAsync(orphan.Name))!;
        check(replacement.Replaced && cleaned.MentorCharacterId is null && Growth(cleaned) == 0
              && cleaned.MentorExperienceBonusPercent == 0,
            "direct replacement clears isolated orphan growth and legacy percentage through registration cleanup");
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!["Accounts"]!.AsArray()
            .SelectMany(account => account!["Characters"]!.AsArray())
            .Single(c => c!["Id"]!.GetValue<Guid>() == orphan.Id)!;
        check(persisted["MentorRelationshipExperience"]!.GetValue<uint>() == 0
              && persisted["MentorExperienceBonusPercent"]!.GetValue<byte>() == 0,
            "direct replacement commits zero orphan growth and legacy percentage in the atomic JSON snapshot");
    }
    private sealed class FailingWriter(bool afterReplace) : IDungeonRewardSnapshotWriter
    {
        internal bool Armed;
        internal int Calls;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken ct)
        {
            Calls++;
            if (Armed && !afterReplace) throw new IOException("before growth replacement");
            await new DungeonRewardSnapshotWriter().WriteAsync(path, snapshot, ct);
            if (Armed) throw new IOException("after growth replacement");
        }
    }

    // A master event is one transaction across all online children, including unknown writer outcomes.
    private static async Task CheckAtomicFailureAsync(Action<bool,string> check)
    {
        foreach (var afterReplace in new[] { false, true })
        {
            await using var f = await PartyHarness.CreateAsync();
            var (seed, a, b) = await SeedAsync(f, 3);
            var c = (await seed.GetCharactersAsync("carol"))[0];
            await seed.SaveCharacterProgressionAsync("carol", c.Id, 6, 0, 0);
            await seed.EstablishMentorAsync(a.Id, c.Id, DateTimeOffset.Now.AddDays(1));
            var writer = new FailingWriter(afterReplace);
            var store = new JsonGameStore(f.Options, NullLogger<JsonGameStore>.Instance, writer);
            await store.InitializeAsync();
            var clock = new Clock();
            using var service = new MentorService(store,clock,NullLogger<MentorService>.Instance);
            var sb = Guid.NewGuid();
            await service.AttachChannelAsync("alice",a.Id,1,Guid.NewGuid(),_=>ValueTask.CompletedTask);
            clock.Advance(1);
            await service.AttachChannelAsync("bobby",b.Id,2,sb,_=>ValueTask.CompletedTask);
            await service.AttachChannelAsync("carol",c.Id,3,Guid.NewGuid(),_=>ValueTask.CompletedTask);
            writer.Armed = true;
            clock.Advance(599);
            await service.SweepChangesAsync();
            var calls = writer.Calls;
            await service.SweepChangesAsync();
            check(writer.Calls == calls && Growth((await store.GetCharacterByNameAsync(b.Name))!) == 3
                  && Growth((await store.GetCharacterByNameAsync(c.Name))!) == 0
                  && await service.GetExperienceBonusRateAsync(b.Id,sb) == (double)0.01f,
                "unknown growth outcome publishes neither canonical memory nor level-up cache and does not retry the claimed event");
            var reload = new JsonGameStore(f.Options,NullLogger<JsonGameStore>.Instance);
            await reload.InitializeAsync();
            check(Growth((await reload.GetCharacterByNameAsync(b.Name))!) == (afterReplace ? 4u : 3u)
                  && Growth((await reload.GetCharacterByNameAsync(c.Name))!) == (afterReplace ? 1u : 0u),
                "restart recovers all endpoint edges together on either side of atomic snapshot replacement");
        }
    }

    private static async Task CheckLateRelationshipAndCleanupAsync(Action<bool,string> check)
    {
        await using var f = await PartyHarness.CreateAsync();
        var (store,a,b) = await SeedAsync(f,0);
        await store.RemoveMentorAsync(b.Id,a.Name,DateTimeOffset.Now);
        var stale = (await store.GetCharacterByNameAsync(b.Name))!;
        var clock = new Clock();
        using var service = new MentorService(store,clock,NullLogger<MentorService>.Instance);
        var sa=Guid.NewGuid(); var sb=Guid.NewGuid();
        await service.AttachChannelAsync("alice",a.Id,1,sa,_=>ValueTask.CompletedTask);
        await service.AttachChannelAsync("bobby",b.Id,2,sb,_=>ValueTask.CompletedTask);
        clock.Advance(599);
        await service.RequestAsync(b.Id,sb,a.Name); await service.ReplyAsync(a.Id,sa,1);
        clock.Advance(1); await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 2,
            "an edge established at second 599 uses the existing world-entry timer deadlines");
        await store.SaveCharacterInventoryAsync("bobby",b.Id,stale.Gold,stale.Inventory ?? [],stale.Equipment ?? []);
        await store.SaveCharacterProgressionAsync("bobby",b.Id,stale.Level,stale.Experience,stale.Sp);
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 2,
            "stale inventory and progression snapshots preserve canonical new mentor growth");
        await service.RemoveAsync(b.Id,sb,a.Name);
        await store.SaveCharacterInventoryAsync("bobby",b.Id,stale.Gold,stale.Inventory ?? [],stale.Equipment ?? []);
        var clean = (await store.GetCharacterByNameAsync(b.Name))!;
        check(clean.MentorCharacterId is null && Growth(clean) == 0 && await service.GetExperienceBonusRateAsync(b.Id,sb) == 0,
            "unlink clears progress and eligibility and an old ordinary save cannot resurrect them");
    }
    // Catches global pair ticks, wall-clock timers, catch-up loops, and connection-ID timer reuse.
    private static async Task CheckEndpointTimersAsync(Action<bool,string> check)
    {
        await using var f = await PartyHarness.CreateAsync();
        var (store, a, b) = await SeedAsync(f, 0);
        var clock = new Clock();
        using var service = new MentorService(store, clock, NullLogger<MentorService>.Instance);
        var session = Guid.NewGuid();
        var masterSession = Guid.NewGuid();
        await service.AttachChannelAsync("alice", a.Id, 1, masterSession, _ => ValueTask.CompletedTask);
        clock.Advance(100);
        await service.AttachChannelAsync("bobby", b.Id, 2, session, _ => ValueTask.CompletedTask);
        clock.Advance(499);
        await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 0, "599 seconds does not grow a mentor edge");
        clock.JumpWall(86400);
        await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 0, "wall-clock jumps do not advance monotonic endpoint timers");
        clock.JumpWall(-172800);
        clock.Advance(1);
        await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 1, "the first endpoint's own 600-second event grows its online edge once");
        clock.Advance(100);
        await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 2, "the staggered second endpoint independently grows the same edge once");
        clock.Advance(2400);
        await service.SweepChangesAsync();
        await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 4, "late sweeps claim only one event per endpoint and immediate repeats do not catch up");
        clock.Advance(300);
        await service.AttachChannelAsync("bobby", b.Id, 2, session, _ => ValueTask.CompletedTask);
        clock.Advance(299); await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 4, "replacement attachment is not yet due");
        clock.Advance(1); await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 5, "only the master grows at the retired child's old deadline");
        clock.Advance(299); await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 5, "same-connection same-character reattachment retires the prior timer incarnation");
        clock.Advance(1); await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 6, "replacement attachment starts a fresh 600-second timer");
        await service.DetachChannelAsync(a.Id, masterSession);
        clock.Advance(1200); await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 6, "offline mentor edges do not grow during a late child event");
        await service.AttachChannelAsync("alice", a.Id, 1, masterSession, _ => ValueTask.CompletedTask);
        await service.SweepChangesAsync();
        check(Growth((await store.GetCharacterByNameAsync(b.Name))!) == 6, "mentor reattachment does not catch up the offline period");
    }
}
