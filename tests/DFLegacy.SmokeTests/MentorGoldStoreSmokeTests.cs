using System.Text.Json;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class MentorGoldStoreSmokeTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        foreach (var (level, limit) in new[] { (18, 7800000), (20, 10100000), (30, 27700000) })
        {
            await using var f = await MentorGoldTestFixture.CreateAsync(level, configure: (root, master, child) =>
            {
                var c = MentorGoldTestFixture.CharacterNode(root, master);
                c["Gold"] = limit - 30;
                c["PendingDungeonGoldCredits"] = JsonSerializer.SerializeToNode(new[] { new DungeonGoldCredit(Guid.NewGuid(), 7) });
                c["Inventory"] = JsonSerializer.SerializeToNode(new[] { new CharacterItemRecord(73, TradeFixtures.PotionItemId, 2, InstanceId: Guid.NewGuid()) });
            }, commerce: true);
            var storeToken = Guid.NewGuid();
            f.Store.Commerce.TryAcquire(CommerceActivity.Store, storeToken, f.Master.Id);
            var display = await f.Store.DisplayPrivateStoreAsync(f.Master.Id, storeToken, [new(0, 8, 0, 73, 2)]);
            var sale = await f.Store.BuyPrivateStoreAsync(f.Child.Id, f.Master.Id, storeToken, 0, 1);
            check(display.Status == DungeonRewardLedgerStatus.Committed && sale.Status == DungeonRewardLedgerStatus.Committed
                && (await f.ReadMaster()).PendingPrivateStoreGoldCredits?.Single().Amount == 8,
                "real private-store sale reserves eight unapplied gold alongside dungeon credits");
            await f.Pickup(1000); var first = await f.Flush();
            await f.Pickup(1000); var second = await f.Flush();
            check(first.Receipt!.CreditedGold + second.Receipt!.CreditedGold == 15
                && (await f.ReadMaster()).Gold == limit - 30,
                $"level{level} uses native wallet limit {limit}, reserving dungeon/store and earlier mentor awards together");
            check((await f.Store.BuyPrivateStoreAsync(f.Child.Id, f.Master.Id, storeToken, 0, 1)).Status == DungeonRewardLedgerStatus.Rejected,
                "private-store seller capacity includes already-awarded mentor reservations");
            var applied = await f.Store.ConsumeMentorGoldCreditsAsync(f.Master.Id, f.MasterToken);
            await f.Store.ConsumeDungeonGoldCreditsAsync(f.Master.Id);
            await f.Store.ConsumePrivateStoreGoldCreditsAsync(f.Master.Id);
            check((await f.ReadMaster()).Gold == limit && applied.Awards!.Sum(a => a.CreditedGold) == 15
                && (await f.Store.ConsumeMentorGoldCreditsAsync(f.Master.Id, f.MasterToken)).Character is null,
                "reserved awarded money consumes once without stealing another credit family's promised capacity");
            await f.Pickup(); var capped = await f.Flush();
            check(capped.Receipt is { CreditedGold: 0, Applied: true } && (await f.ReadMaster()).Gold == limit,
                "full native wallet consumes its batch as applied zero with no deferred remainder");
        }
        await using (var f = await MentorGoldTestFixture.CreateAsync())
        {
            await f.Pickup(); var id = Guid.NewGuid(); var first = await f.Flush(id);
            check((await f.Store.FlushMentorGoldAsync(f.Master.Id, f.MasterToken, id)).Status == DungeonRewardLedgerStatus.Rejected,
                "a frozen flush receipt cannot be stolen by another child identity");
            var reloaded = new JsonGameStore(f.Harness.Options, NullLogger<JsonGameStore>.Instance);
            await reloaded.InitializeAsync();
            check((await reloaded.ConsumeMentorGoldCreditsAsync(f.Master.Id)).Character?.Gold == f.Master.Gold + 14
                && (await reloaded.FlushMentorGoldAsync(f.Child.Id, f.ChildToken, id)).Receipt is { Applied: true },
                "awarded credit survives restart; immutable flush replay ignores later Applied state without regranting");
            await f.Pickup();
            reloaded = new JsonGameStore(f.Harness.Options, NullLogger<JsonGameStore>.Instance); await reloaded.InitializeAsync();
            await reloaded.SetMentorGoldEndpointAsync(f.Master.Id, f.MasterToken, true);
            await reloaded.SetMentorGoldEndpointAsync(f.Child.Id, f.ChildToken, true);
            check((await reloaded.FlushMentorGoldAsync(f.Child.Id, f.ChildToken, Guid.NewGuid())).Receipt is null,
                "source receipts never reconstruct unflushed tax after restart");
        }
        await CheckFailuresAsync(check);
        await CheckCanonicalRateAsync(check);
        foreach (var invalid in new[] { "underlevel", "missing-cap", "over-cap" })
        {
            await using var f = await MentorGoldTestFixture.CreateAsync(invalid == "underlevel" ? 17 : 30,
                configure: (root, master, child) => { if (invalid == "over-cap") MentorGoldTestFixture.CharacterNode(root, master)["Gold"] = 27700100; });
            if (invalid == "missing-cap") File.Delete(Path.Combine(f.Harness.Root, "etc/goldlimitbylevel.etc"));
            await f.Pickup(); var result = await f.Flush();
            check((result.Receipt?.CreditedGold ?? 0) == 0 && (await f.ReadMaster()).Gold == f.Master.Gold,
                $"{invalid} master never receives invented capacity or has an existing balance reduced");
        }
    }

    private static async Task CheckCanonicalRateAsync(Action<bool,string> check)
    {
        await using var f = await MentorGoldTestFixture.CreateAsync(configure: (root, master, child) =>
        {
            foreach (var account in root["Accounts"]!.AsArray())
            foreach (var character in account!["Characters"]!.AsArray())
            {
                var name = account["UserName"]!.GetValue<string>();
                if (name is "carol" or "dave")
                {
                    character!["Level"] = 6; character["MentorCharacterId"] = master;
                    character["MentorRelationshipExperience"] = name == "carol" ? 3 : 880;
                }
                if (name == "erin")
                {
                    character!["Level"] = 45;
                    MentorGoldTestFixture.CharacterNode(root, master)["MentorCharacterId"] = character["Id"]!.GetValue<Guid>();
                }
            }
        });
        var sibling = (await f.Store.GetCharactersAsync("carol"))[0];
        var upper = (await f.Store.GetCharactersAsync("erin"))[0];
        var siblingSession = Guid.NewGuid();
        var siblingToken = (await f.Service.AttachChannelAsync("carol", sibling.Id, 3, siblingSession, _ => ValueTask.CompletedTask))!.Value;
        var upperToken = (await f.Service.AttachChannelAsync("erin", upper.Id, 5, Guid.NewGuid(), _ => ValueTask.CompletedTask))!.Value;
        f.Clock.Advance(600); await f.Service.SweepChangesAsync();
        check(await f.Service.GetExperienceBonusRateAsync(f.Child.Id, f.ChildSession) == (double)0.02f,
            "higher sibling relationship growth actually promotes the reporting child's distinct EXP cache");
        await f.Pickup(); var first = await f.Flush();
        check(first.Receipt is { RelationshipLevel: 1, ConnectedChildren: 2, Capacity: 10, CreditedGold: 19 },
            "gold uses the child's own canonical edge L1 despite sibling cache L2, counting online children only");
        await f.Store.CommitDungeonGoldDistributionAsync(Guid.NewGuid(),
            [new(sibling.Id, 3, 0, sibling.Name, _ => ValueTask.CompletedTask, "carol", siblingToken)], 0, 1000);
        var second = await f.Store.FlushMentorGoldAsync(sibling.Id, siblingToken, Guid.NewGuid());
        check(second.Receipt is { RelationshipLevel: 2, CreditedGold: 29 } && second.Receipt.SourceName == sibling.Name
            && first.Receipt!.SourceName == f.Child.Name, "each online child flush retains its own L, name and truncation");
        await f.Store.ConsumeMentorGoldCreditsAsync(f.Master.Id, f.MasterToken);
        check((await f.Store.FlushMentorGoldAsync(f.Master.Id, f.MasterToken, Guid.NewGuid())).Receipt is null,
            "installing received mentor tax never recursively accrues for the master's own parent");
        await f.Pickup();
        await f.Service.DetachChannelAsync(sibling.Id, siblingSession);
        await f.Store.SaveCharacterProgressionAsync("alice", f.Master.Id, 20, 0, 0);
        var late = await f.Flush();
        check(late.Receipt is { ConnectedChildren: 1, Capacity: 8, CreditedGold: 16 },
            "flush samples current connected count and changed master level instead of accrual-time N/C");
    }

    internal sealed class FaultWriter(bool afterReplace) : IDungeonRewardSnapshotWriter
    {
        internal bool Armed;
        internal int Calls;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> snapshot, CancellationToken ct)
        {
            Calls++;
            if (!Armed) { await new DungeonRewardSnapshotWriter().WriteAsync(path, snapshot, ct); return; }
            if (afterReplace) await new DungeonRewardSnapshotWriter().WriteAsync(path, snapshot, ct);
            throw new IOException("mentor fixture write uncertainty");
        }
    }
    private static async Task CheckFailuresAsync(Action<bool,string> check)
    {
        foreach (var after in new[] { false, true })
        foreach (var consumption in new[] { false, true })
        {
            var writer = new FaultWriter(after);
            await using var f = await MentorGoldTestFixture.CreateAsync(writer: writer);
            await f.Pickup();
            if (consumption) await f.Flush();
            writer.Armed = true;
            var id = Guid.NewGuid();
            var status = consumption ? (await f.Store.ConsumeMentorGoldCreditsAsync(f.Master.Id, f.MasterToken)).Status
                : (await f.Flush(id)).Status;
            var writes = writer.Calls;
            var retried = consumption ? (await f.Store.ConsumeMentorGoldCreditsAsync(f.Master.Id, f.MasterToken)).Status
                : (await f.Flush(id)).Status;
            check(status == DungeonRewardLedgerStatus.Unknown && retried == DungeonRewardLedgerStatus.Unknown && writer.Calls == writes,
                $"mentor {(consumption ? "consume" : "award")} afterReplace={after} quarantines unknown writes without success or retry");
            var reloaded = new JsonGameStore(f.Harness.Options, NullLogger<JsonGameStore>.Instance); await reloaded.InitializeAsync();
            await reloaded.ConsumeMentorGoldCreditsAsync(f.Master.Id);
            var saved = (await reloaded.GetCharacterByNameAsync(f.Master.Name))!;
            check(saved.Gold == f.Master.Gold + (consumption || after ? 14 : 0)
                && (await reloaded.ConsumeMentorGoldCreditsAsync(f.Master.Id)).Character is null,
                "restart follows official atomic snapshot: exactly one already-awarded amount or none, never duplicate credit");
        }
    }
}
