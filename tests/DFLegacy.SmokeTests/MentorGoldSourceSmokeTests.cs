using DFLegacy.Server;

internal static class MentorGoldSourceSmokeTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        await using (var f = await MentorGoldTestFixture.CreateAsync(configure: (root, master, child) =>
        {
            var sibling = root["Accounts"]!.AsArray().Single(a => a!["UserName"]!.GetValue<string>() == "carol")!["Characters"]![0]!;
            sibling["Level"] = 6; sibling["MentorCharacterId"] = master;
        }))
        {
            var sibling = (await f.Store.GetCharactersAsync("carol"))[0];
            var token = (await f.Service.AttachChannelAsync("carol", sibling.Id, 3, Guid.NewGuid(), _ => ValueTask.CompletedTask))!.Value;
            DungeonInstanceMember[] members = [f.Member, new(sibling.Id, 3, 1, sibling.Name, _ => ValueTask.CompletedTask, "carol", token)];
            var pickup = Guid.NewGuid();
            var result = await f.Store.CommitDungeonGoldDistributionAsync(pickup, members, 0, 101);
            await f.Store.CommitDungeonGoldDistributionAsync(pickup, members, 0, 101);
            var first = await f.Flush();
            var second = await f.Store.FlushMentorGoldAsync(sibling.Id, token, Guid.NewGuid());
            check(result.Amounts[0] == 51 && result.Amounts[1] == 50
                && first.Receipt is { Basis: 51, CreditedGold: 1 } && second.Receipt is { Basis: 50, CreditedGold: 0 },
                "real two-child party distribution retains picker remainder and independently truncates each once-only requested basis");
        }
        using (var groundFixture = new DungeonFixture())
        foreach (var party in new[] { false, true })
        {
            await using var f = await MentorGoldTestFixture.CreateAsync();
            var instance = party ? groundFixture.CreatePartyInstance(2810) : groundFixture.CreateSoloInstance(f.Child.Id);
            instance.AddMember(f.Member); instance.TryStartRun(groundFixture.Layout, 0, 0, out _);
            var ground = instance.AddGroundItem(new(true, 0, 1000), 1, 1, 0, 0, 0, 0);
            var reserved = instance.TryBeginLootPickup(f.Child.Id, ground, false, [], _ => 0, out var claim);
            var claimed = instance.TryClaimGoldPickup(f.Child.Id, ground, claim.PickupId, out var members);
            var commit = await f.Store.CommitDungeonGoldDistributionAsync(claim.PickupId, members, 0, ground.CountOrValue,
                routingSource: ground.RoutingSource);
            var installed = await f.Store.ConsumeDungeonGoldCreditsAsync(f.Child.Id, pickupId: claim.PickupId);
            var flushed = await f.Flush();
            check(reserved && claimed && commit.Status == DungeonRewardLedgerStatus.Committed
                && installed!.Gold == f.Child.Gold + 1000 && flushed.Receipt is { Basis: 1000, CreditedGold: 14 }
                && !instance.TryClaimGoldPickup(f.Child.Id, ground, claim.PickupId, out _),
                $"real {(party ? "party" : "solo")} generated ground claim, ledger and own-credit installation award child fully and tax source exactly once");
        }
        await using (var f = await MentorGoldTestFixture.CreateAsync())
        {
            await f.Store.SaveCharacterInventoryAsync("bobby", f.Child.Id, int.MaxValue, f.Child.Inventory ?? [], f.Child.Equipment ?? []);
            var id = Guid.NewGuid();
            var source = await f.Pickup(id: id);
            await f.Pickup(id: id);
            var flushId = Guid.NewGuid(); var flush = await f.Flush(flushId);
            var replay = await f.Flush(flushId);
            check(source.Amounts[0] == 0 && flush.Receipt is { Basis: 1000, CreditedGold: 14 }
                && replay.Receipt == flush.Receipt && !replay.NewlyCommitted,
                "capped child pickup uses requested basis once; persisted flush replay returns its frozen receipt");
            check((await f.Pickup(source: 0, id: id)).Status == DungeonRewardLedgerStatus.Rejected,
                "pickup identity cannot be replayed with a different source classification");
            await f.Pickup(1000, 0); await f.Pickup(0); await f.Pickup(uint.MaxValue);
            check((await f.Flush()).Receipt is null, "player drops, zero and negative-signed amounts never feed mentor basis");
            var stale = f.ChildToken;
            f.ChildToken = (await f.Service.AttachChannelAsync("bobby", f.Child.Id, 2, f.ChildSession, _ => ValueTask.CompletedTask))!.Value;
            await f.Pickup(1000, token: stale);
            check((await f.Flush()).Receipt is null, "late source with previous dungeon member incarnation cannot feed the new attachment");
        }
        // Actual trusted-roll committer is the shared solo/party source boundary.
        using var dungeon = new DungeonFixture();
        foreach (var type in new[] { PartyCardType.Free, PartyCardType.Paid })
        {
            await using var f = await MentorGoldTestFixture.CreateAsync();
            var capped = (await f.Store.SaveCharacterInventoryAsync("bobby", f.Child.Id, int.MaxValue - 5,
                f.Child.Inventory ?? [], f.Child.Equipment ?? []))!;
            var settlement = Guid.NewGuid();
            var roll = new DungeonClearRewardRoll(10, true, 1000, null, null, null);
            var first = await PartyCardOwnColumnCommitter.CommitAsync(f.Store, "bobby", capped, 0, type, 0, 1,
                settlement, roll, capped.Gold, (capped.Inventory ?? []).ToDictionary(i => i.Slot), capped.Equipment ?? [],
                capped.WarehouseCapacity, uint.MaxValue, dungeon.Items, default, f.ChildToken);
            var replay = await PartyCardOwnColumnCommitter.CommitAsync(f.Store, "bobby", capped, 0, type, 0, 1,
                settlement, roll, capped.Gold, (capped.Inventory ?? []).ToDictionary(i => i.Slot), capped.Equipment ?? [],
                capped.WarehouseCapacity, uint.MaxValue, dungeon.Items, default, f.ChildToken);
            var flush = await f.Flush();
            check(first.Outcome.Status == DungeonRewardLedgerStatus.Committed && replay.Outcome.Status == DungeonRewardLedgerStatus.Committed
                && (type == PartyCardType.Free ? flush.Receipt is { Basis: 1000, CreditedGold: 14 } : flush.Receipt is null),
                $"trusted {type} card first commit/replay uses original free amount before cap and never taxes paid cost");
        }
        await using (var f = await MentorGoldTestFixture.CreateAsync(configure: (root, master, child) =>
        {
            var c = MentorGoldTestFixture.CharacterNode(root, child);
            c["Gold"] = int.MaxValue - 10;
            c["PendingDungeonGoldCredits"] = System.Text.Json.JsonSerializer.SerializeToNode(new[] { new DungeonGoldCredit(Guid.NewGuid(), 10) });
        }))
        {
            var committed = await PartyCardOwnColumnCommitter.CommitAsync(f.Store, "bobby", f.Child, 0, PartyCardType.Free, 0, 1,
                Guid.NewGuid(), new(0, false, 1000, null, null, null), f.Child.Gold,
                (f.Child.Inventory ?? []).ToDictionary(i => i.Slot), f.Child.Equipment ?? [], f.Child.WarehouseCapacity,
                uint.MaxValue, dungeon.Items, default, f.ChildToken);
            check(committed.Outcome.Status == DungeonRewardLedgerStatus.Committed && committed.TargetGold == f.Child.Gold
                && (await f.Flush()).Receipt is { Basis: 1000, CreditedGold: 14 },
                "free card clips actual own gold against outstanding credits while preserving its requested mentor source");
        }
        await using (var f = await MentorGoldTestFixture.CreateAsync())
        {
            var oldToken = f.ChildToken;
            f.ChildToken = (await f.Service.AttachChannelAsync("bobby", f.Child.Id, 2, f.ChildSession, _ => ValueTask.CompletedTask))!.Value;
            var late = await PartyCardOwnColumnCommitter.CommitAsync(f.Store, "bobby", f.Child, 0, PartyCardType.Free, 0, 1,
                Guid.NewGuid(), new(0, false, 1000, null, null, null), f.Child.Gold,
                (f.Child.Inventory ?? []).ToDictionary(i => i.Slot), f.Child.Equipment ?? [], f.Child.WarehouseCapacity,
                uint.MaxValue, dungeon.Items, default, oldToken);
            check(late.Outcome.Status == DungeonRewardLedgerStatus.Committed && (await f.Flush()).Receipt is null,
                "late old-incarnation trusted card cannot transfer mentor basis to a replacement attachment");
        }
        foreach (var after in new[] { false, true })
        {
            var writer = new MentorGoldStoreSmokeTests.FaultWriter(after);
            await using var f = await MentorGoldTestFixture.CreateAsync(writer: writer);
            writer.Armed = true;
            var source = await f.Pickup();
            var flush = await f.Flush();
            check(source.Status == DungeonRewardLedgerStatus.Unknown && flush.Status == DungeonRewardLedgerStatus.Unknown
                && (await f.ReadMaster()).MentorGoldAwards is null,
                $"uncertain source afterReplace={after} cannot accrue or produce an award");
        }
    }
}
