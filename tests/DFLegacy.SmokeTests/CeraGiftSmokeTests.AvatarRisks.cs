using DFLegacy.Protocol;
using DFLegacy.Server;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;

internal static partial class CeraGiftSmokeTests
{
    private static async Task AvatarExpiryRaces(Action<bool, string> check)
    {
        foreach (var settleFirst in new[] { false, true })
        {
            await using var f = new AvatarFixture(max: 2); var store = f.Open(); var (a, b) = await f.Seed(store);
            var gift = await store.GiftCeraShopAsync("sender", a.Id, b.Name,
                [new(4, 1, 270000), new(4, 3, 270002)], f.Shop);
            check(gift.Status == DungeonRewardLedgerStatus.Committed, "finite and permanent gifts share one successful transaction");
            if (gift.Status != DungeonRewardLedgerStatus.Committed) continue;
            var before = await Character(store, "target", b.Id);
            var finiteMail = before.Mailbox!.Single(m => m.Attachment!.AvatarExpiresAtUnixSeconds is not null);
            var finite = finiteMail.Attachment!;
            var permanent = before.Mailbox!.Single(m => m.Attachment!.AvatarExpiresAtUnixSeconds is null).Attachment!;
            CharacterAvatarInventoryPlanner.TryPlace(new Dictionary<ushort, CharacterItemRecord>(), finite, f.Items, out var plan, out _);
            var planned = plan.Inventory.Values.Single();
            f.Clock.Utc = DateTimeOffset.FromUnixTimeSeconds(finite.AvatarExpiresAtUnixSeconds!.Value);
            await store.SettleAvatarExpirationsAsync("target", b.Id);
            var equal = await Character(store, "target", b.Id);
            var wireBoundary = CharacterItemWireProjection.Project(planned with {
                AvatarExpiresAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, f.Items);
            check(equal.Mailbox!.Single(m => m.Id == finiteMail.Id).Attachment?.InstanceId == finite.InstanceId
                && AvatarLifetimePolicy.GetRemainingSeconds(planned, f.Clock.Utc.ToUnixTimeSeconds()) == 1
                && wireBoundary.AddInfo == 1 && wireBoundary.Durability == 4,
                "gift finite attachment survives exact deadline and wire emits one second, never permanent zero");
            f.Clock.Utc = f.Clock.Utc.AddSeconds(1);
            if (settleFirst) await store.SettleAvatarExpirationsAsync("target", b.Id);
            await store.ClaimCharacterMailAsync("target", b.Id, finiteMail.Id, b.Gold,
                b.Inventory ?? [], b.Equipment ?? [], avatarInventory: plan.Inventory.Values);
            var expired = await Character(store, "target", b.Id);
            check((expired.AvatarInventory?.Count ?? 0) == 0
                && expired.Mailbox!.Single(m => m.Id == finiteMail.Id).Attachment is null
                && expired.ExpiredAvatarInstanceIds?.Contains(finite.InstanceId) == true
                && expired.Mailbox!.Any(m => m.Attachment?.InstanceId == permanent.InstanceId),
                $"gift claim planned before deadline and written after expiry settleFirst={settleFirst} cannot resurrect finite UUID; permanent survives");
            var cleared = planned with { AvatarExpiresAtUnixSeconds = null, AvatarRemainingSeconds = 0 };
            var stale = await store.SaveCharacterItemSpacesAsync("target", b.Id, b.Gold,
                b.Inventory ?? [], b.Equipment ?? [], b.Warehouse ?? [], avatarInventory: [cleared],
                generatedMails: [new("Old actor", "Stale gift attachment", Attachment: finite with {
                    AvatarExpiresAtUnixSeconds = null, AvatarRemainingSeconds = 0 })]);
            check(stale is not null && (stale.AvatarInventory?.Count ?? 0) == 0
                && stale.Mailbox!.All(m => m.Attachment?.InstanceId != finite.InstanceId),
                "stale actor and generated-mail save with cleared deadline cannot resurrect expired gift UUID");
            var restarted = f.Open(); await restarted.InitializeAsync();
            var persisted = await Character(restarted, "target", b.Id);
            check(persisted.ExpiredAvatarInstanceIds?.Contains(finite.InstanceId) == true
                && persisted.Mailbox!.Single(m => m.Attachment is not null).Attachment!.InstanceId == permanent.InstanceId,
                "restart preserves gift tombstone and permanent control after expired claim race");
        }
    }

    private static async Task AvatarAtomicLimits(Action<bool, string> check)
    {
        await using (var f = new AvatarFixture())
        {
            var store = f.Open(); var (a, _) = await f.Seed(store);
            var alt = (await store.CreateCharacterAsync("sender", new("GiftAlt"))).Character!;
            var result = await f.Gift(store, a, alt.Name);
            var saved = await Character(store, "sender", alt.Id);
            check(result.Status == DungeonRewardLedgerStatus.Committed && saved.Cash == 49700
                && saved.Mailbox?.Single().Attachment?.AvatarAbilityIndex == 4
                && (await store.QueryCeraGiftRecipientAsync(a.Id, alt.Name)).ReceivedCount == 1,
                "avatar same-account different-character gift preserves mailbox/count and debits the same copied account");
        }
        await using (var f = new AvatarFixture())
        {
            var store = f.Open(); var (a, b) = await f.Seed(store);
            var full = Enumerable.Range(0, 105).Select(i => new CharacterItemRecord((ushort)i, 51020, 1,
                AvatarRemainingSeconds: 0, InstanceId: Guid.NewGuid())).ToArray();
            await store.SaveCharacterItemSpacesAsync("target", b.Id, b.Gold, [], [], [], avatarInventory: full);
            check((await f.Gift(store, a, b.Name)).Status == DungeonRewardLedgerStatus.Committed
                && (await Character(store, "target", b.Id)).AvatarInventory!.Count == 105,
                "full avatar bag still accepts a gift into mail without inserting an extra item");
            var oldSave = await store.SaveCharacterItemSpacesAsync("target", b.Id, b.Gold, [], [], [], avatarInventory: full);
            check(oldSave?.Mailbox?.Single().Attachment?.AvatarAbilityIndex == 4,
                "stale target actor save preserves newly delivered avatar ability and canonical mailbox");
            var snapshot = await File.ReadAllBytesAsync(f.Options.DataPath);
            var physical = await store.GiftCeraShopAsync("sender", a.Id, b.Name, [new(255, 0, 100018)], f.Shop);
            var disk = await File.ReadAllBytesAsync(f.Options.DataPath);
            check(physical.Error == 4 && snapshot.SequenceEqual(disk), "avatar and physical gifts share the same default account cap1");
            f.Clock.Utc = f.Clock.Utc.AddSeconds(1); // Explicit Shanghai November boundary.
            check((await store.QueryCeraGiftRecipientAsync(a.Id, b.Name)).ReceivedCount == 0
                && (await store.GiftCeraShopAsync("sender", a.Id, b.Name, [new(255, 0, 100018)], f.Shop)).Status == DungeonRewardLedgerStatus.Committed,
                "Shanghai month rollover permits physical gift after prior-month avatar without resetting persisted state");
            f.Clock.Utc = new DateTimeOffset(2027, 11, 1, 0, 0, 0, TimeSpan.FromHours(8));
            check((await store.QueryCeraGiftRecipientAsync(a.Id, b.Name)).ReceivedCount == 0,
                "avatar/physical shared quota uses full year-month across a same-month different-year boundary");
        }
        await using (var f = new AvatarFixture(max: 3))
        {
            var store = f.Open(); var (a, b) = await f.Seed(store);
            foreach (var rows in new GameCeraShopPurchaseItem[][] {
                [new(4, 1, 270000), new(44, 1, 290000)],
                [new(255, 0, 100018), new(4, 1, 270000), new(255, 1, 270000)] })
            {
                var before = await File.ReadAllBytesAsync(f.Options.DataPath);
                var result = await store.GiftCeraShopAsync("sender", a.Id, b.Name, rows, f.Shop);
                var after = await File.ReadAllBytesAsync(f.Options.DataPath);
                check(result.Error == 8 && before.SequenceEqual(after),
                    "invalid avatar rolls back an entire multi-avatar or mixed physical/avatar checkout before debit");
            }
            check((await store.GiftCeraShopAsync("sender", a.Id, b.Name,
                [new(255, 0, 100018), new(4, 1, 270000)], f.Shop)).Status == DungeonRewardLedgerStatus.Committed
                && (await Character(store, "sender", a.Id)).Cash == 49550
                && (await store.GetCharacterMailsAsync(b.Id)).Length == 2
                && (await store.QueryCeraGiftRecipientAsync(a.Id, b.Name)).ReceivedCount == 2,
                "valid mixed physical/avatar checkout charges450 and counts two products in one transaction");
        }
        await using (var f = new AvatarFixture())
        {
            var store = f.Open(); var (a, b) = await f.Seed(store);
            await store.SaveCharacterItemSpacesAsync("target", b.Id, b.Gold, [], [], [],
                generatedMails: Enumerable.Range(0, 255).Select(i => new CreateCharacterMailRequest("System", "Full")));
            var before = await File.ReadAllBytesAsync(f.Options.DataPath);
            var result = await f.Gift(store, a, b.Name);
            var after = await File.ReadAllBytesAsync(f.Options.DataPath);
            check(result.Error == 4 && before.SequenceEqual(after), "full canonical mailbox rejects avatar gift without debit or quota consumption");
        }
    }

    private static async Task AvatarConcurrentAndFaults(Action<bool, string> check)
    {
        await using (var f = new AvatarFixture())
        {
            var store = f.Open(); var (a, b) = await f.Seed(store);
            await store.CreateAccountAsync(new("other", "password", OicqCode: "123456"));
            var other = (await store.CreateCharacterAsync("other", new("OtherBuyer"))).Character!;
            await store.GrantCeraToAccountAsync("other", 50_000);
            var outcomes = await Task.WhenAll(f.Gift(store, a, b.Name),
                store.GiftCeraShopAsync("other", other.Id, b.Name, [new(4, 1, 270000)], f.Shop));
            check(outcomes.Count(r => r.Status == DungeonRewardLedgerStatus.Committed) == 1
                && (await store.GetCharacterMailsAsync(b.Id)).Length == 1
                && (await store.GetAccountCeraAsync("sender")) + (await store.GetAccountCeraAsync("other")) == 99700,
                "two senders competing for final avatar gift quota commit exactly one mailbox/debit");
        }
        await using (var f = new AvatarFixture())
        {
            var store = f.Open(); var (a, b) = await f.Seed(store);
            await store.CreateAccountAsync(new("other", "password", OicqCode: "123456"));
            var target2 = (await store.CreateCharacterAsync("other", new("OtherTarget"))).Character!;
            await store.SaveCharacterCeraShopPurchaseAsync("sender", a.Id, 300, a.Gold, [], []);
            var outcomes = await Task.WhenAll(f.Gift(store, a, b.Name), f.Gift(store, a, target2.Name));
            check(outcomes.Count(r => r.Status == DungeonRewardLedgerStatus.Committed) == 1
                && outcomes.Any(r => r.Error == 11) && await store.GetAccountCeraAsync("sender") == 0
                && (await store.GetCharacterMailsAsync(b.Id)).Length + (await store.GetCharacterMailsAsync(target2.Id)).Length == 1,
                "two sender sessions cannot spend the same300 on two distinct recipient accounts");
        }
        foreach (var afterReplace in new[] { false, true })
        {
            await using var f = new AvatarFixture(); var writer = new FaultWriter(afterReplace);
            var store = f.Open(writer); var (a, b) = await f.Seed(store);
            writer.Armed = true;
            var result = await f.Gift(store, a, b.Name); var calls = writer.Calls;
            var retry = await f.Gift(store, a, b.Name);
            check(result.Status == DungeonRewardLedgerStatus.Unknown && retry.Status == DungeonRewardLedgerStatus.Unknown
                && writer.Calls == calls && (await Character(store, "sender", a.Id)).Cash == 50000,
                $"avatar snapshot afterReplace={afterReplace} returns Unknown and quarantines without retry/publication");
            var restarted = f.Open(); await restarted.InitializeAsync();
            var target = await Character(restarted, "target", b.Id);
            var mail = target.Mailbox?.SingleOrDefault()?.Attachment;
            check(await restarted.GetAccountCeraAsync("sender") == (afterReplace ? 49700 : 50000)
                && (target.Mailbox?.Count ?? 0) == (afterReplace ? 1 : 0)
                && (await restarted.QueryCeraGiftRecipientAsync(a.Id, b.Name)).ReceivedCount == (afterReplace ? 1 : 0)
                && (!afterReplace || mail is { AvatarAbilityIndex: 4, AvatarRemainingSeconds: 604800 }
                    && mail.AvatarExpiresAtUnixSeconds == f.Clock.Utc.ToUnixTimeSeconds() + 604800 && mail.InstanceId != Guid.Empty),
                $"avatar fault restart afterReplace={afterReplace} retains exactly zero or one complete cash/mail/quota/ability/deadline transaction");
        }
    }

    private static async Task AvatarSessionsAndEconomicState(Action<bool, string> check)
    {
        await using var f = new AvatarFixture(); var store = f.Open(); var (a, b) = await f.Seed(store);
        var alt = (await store.CreateCharacterAsync("sender", new("SameAccountOnline"))).Character!;
        var state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        var now = DateTimeOffset.UtcNow; var pickup = Guid.NewGuid(); var match = Guid.NewGuid();
        state["DungeonRewardLedger"] = JsonSerializer.SerializeToNode(new[] { new DungeonRewardLedgerEntry(
            "avatar-prior-card", "prior-fingerprint", a.Id, 12, 0, 1, 1, DungeonRewardLedgerStatus.Committed,
            now, now, null, 17, null, null) });
        state["DungeonGoldLedger"] = JsonSerializer.SerializeToNode(new[] { new DungeonGoldLedgerEntry(pickup, [a.Id], [0], [19], [19]) });
        state["PvpLedger"] = JsonSerializer.SerializeToNode(new[] { new PvpSettlementReceipt(match, "prior-pvp",
            [new(a.Id, 37, 12, 0, 1, 0, 1, 1, 12)]) });
        var sender = state["Accounts"]![0]!["Characters"]![0]!;
        sender["VictoryPoints"] = 37; sender["PvpPoints"] = 12; sender["PvpWins"] = 1;
        sender["PendingDungeonGoldCredits"] = JsonSerializer.SerializeToNode(new[] { new DungeonGoldCredit(pickup, 19) });
        sender["MentorRegistration"] = JsonSerializer.SerializeToNode(new MentorRegistrationState(1790000000, 0));
        var apprentice = state["Accounts"]![1]!["Characters"]![0]!;
        apprentice["MentorCharacterId"] = JsonSerializer.SerializeToNode(a.Id);
        apprentice["MentorRelationshipExperience"] = 77;
        await File.WriteAllTextAsync(f.Options.DataPath, state.ToJsonString());
        store = f.Open(); await store.InitializeAsync();
        // Compare normalized canonical state; initialization owns existing legacy normalization.
        state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        var ledgerBefore = new[] { "DungeonRewardLedger", "DungeonGoldLedger", "PvpLedger" }
            .ToDictionary(k => k, k => state[k]!.ToJsonString());
        var actorBefore = await Character(store, "sender", a.Id);
        var sessions = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
        var cashA = new CeraCashInvalidation(); var cashAlt = new CeraCashInvalidation(); var alarms = 0;
        var leaseA = await sessions.ClaimAsync(a.Id, Guid.NewGuid(), () => { }, default, ceraInfo: cashA.Invalidate);
        var leaseAlt = await sessions.ClaimAsync(alt.Id, Guid.NewGuid(), () => { }, default, ceraInfo: cashAlt.Invalidate);
        var leaseB = await sessions.ClaimAsync(b.Id, Guid.NewGuid(), () => { }, default, mailReceived: () => alarms++);
        try
        {
            await store.GrantCeraToAccountAsync("sender", 1000);
            var result = await f.Gift(store, a, b.Name);
            await CeraGiftSessionEffects.NotifyCommittedAsync(result, "sender", store, sessions);
            sessions.NotifyCera(a.Id, 51000); sessions.NotifyCera(alt.Id, 51000);
            check(await cashA.ConsumeAsync(store, "sender") == 50700
                && await cashAlt.ConsumeAsync(store, "sender") == 50700 && alarms == 1
                && (await Character(store, "sender", a.Id)).AvatarInventory?.Count is null or 0,
                "avatar gift sends target only mail invalidation, no buyer item, and late recharge notice rereads canonical50700 for both sender sessions");
            await CeraGiftSessionEffects.NotifyCommittedAsync(new(DungeonRewardLedgerStatus.Unknown), "sender", store, sessions);
            await CeraGiftSessionEffects.NotifyCommittedAsync(new(DungeonRewardLedgerStatus.Rejected), "sender", store, sessions);
            check(alarms == 1 && await cashAlt.ConsumeAsync(store, "sender") is null,
                "avatar Unknown/rejected result publishes no success cash or mail notification");
            var actorAfter = await Character(store, "sender", a.Id);
            var apprenticeAfter = await Character(store, "target", b.Id);
            state = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
            check(ledgerBefore.All(pair => state[pair.Key]!.ToJsonString() == pair.Value)
                && actorAfter.VictoryPoints == 37 && actorAfter.PvpPoints == 12 && actorAfter.PvpWins == 1
                && actorAfter.PendingDungeonGoldCredits!.Single().PickupId == pickup
                && actorAfter.MentorRegistration == actorBefore.MentorRegistration
                && apprenticeAfter.MentorCharacterId == a.Id && apprenticeAfter.MentorRelationshipExperience == 77,
                "avatar gift snapshot preserves PvP receipt/VP/results, gold credit, mentor registration and prior economic ledgers");
        }
        finally { sessions.Release(leaseA); sessions.Release(leaseAlt); sessions.Release(leaseB); }
    }
}
