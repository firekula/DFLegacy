using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class MentorMasterGoldSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        MentorGoldPolicySmokeTests.Run(check);
        await CheckReplacementSignalAsync(check);
        await MentorGoldSourceSmokeTests.RunAsync(check);
        await MentorGoldLifecycleSmokeTests.RunAsync(check);
        await MentorGoldStoreSmokeTests.RunAsync(check);
        await MentorGoldExternalDeliverySmokeTests.RunAsync(check);
        await using var f = await PartyHarness.CreateAsync();
        // Original PVF entry bytes: see the fixture provenance manifest.
        Directory.CreateDirectory(Path.Combine(f.Root, "etc"));
        File.Copy(Path.Combine(MentorGoldPolicySmokeTests.FixtureRoot, "etc", "goldlimitbylevel.etc"),
            Path.Combine(f.Root, "etc", "goldlimitbylevel.etc"));
        var (store, master, child) = await MentorGrowthSmokeTests.SeedAsync(f, 0);
        using var service = new MentorService(store, new MentorGrowthSmokeTests.Clock(), NullLogger<MentorService>.Instance);
        var sm = Guid.NewGuid(); var sc = Guid.NewGuid();
        await service.AttachChannelAsync("alice", master.Id, 1, sm, _ => ValueTask.CompletedTask);
        var token = (await service.AttachChannelAsync("bobby", child.Id, 2, sc, _ => ValueTask.CompletedTask))!.Value;
        var member = new DungeonInstanceMember(child.Id, 2, 0, child.Name, _ => ValueTask.CompletedTask, "bobby", token);
        var pickup = Guid.NewGuid();
        var source = await store.CommitDungeonGoldDistributionAsync(pickup, [member], 0, 1000);
        await store.CommitDungeonGoldDistributionAsync(pickup, [member], 0, 1000);
        await service.DetachChannelAsync(child.Id, sc);
        var saved = (await store.GetCharacterByNameAsync(master.Name))!;
        var awards = saved.MentorGoldAwards;
        check(source.Status == DungeonRewardLedgerStatus.Committed && awards?.Count == 1
              && awards[0].CreditedGold == 14 && saved.Gold == master.Gold,
            "a committed generated pickup replay accrues once; normal departure awards 14 canonical credit without changing actor Gold");
        var newItem = new CharacterItemRecord(73, 1234, 2, InstanceId: Guid.NewGuid());
        var inventory = (master.Inventory ?? []).Where(i => i.Slot != 73).Append(newItem).ToArray();
        var actorSave = await store.SaveCharacterItemSpacesAsync("alice", master.Id, master.Gold + 7,
            inventory, master.Equipment ?? [], master.Warehouse ?? []);
        var skillsSave = await store.SaveCharacterSkillsAsync("alice", master.Id, 13, [new(0, 7, 2)]);
        check(actorSave is not null && skillsSave is not null && skillsSave.Gold == master.Gold + 7
              && skillsSave.Inventory!.Contains(newItem) && skillsSave.Skills!.Single().Level == 2
              && skillsSave.MentorGoldAwards?.Count == 1,
            "a real stale actor item/skill save succeeds between award and installation and retains both its new item and awarded credit");
        var mail = await store.SendCharacterMailAsync(master.Id, new("Fixture", "claim interleaving", 11));
        var claim = await store.ClaimCharacterMailAsync("alice", master.Id, mail.Mail!.Id,
            master.Gold + 18, inventory, master.Equipment ?? []);
        var purchasedItem = new CharacterItemRecord(74, 1235, 1, InstanceId: Guid.NewGuid());
        var purchase = await store.SaveCharacterCeraShopPurchaseAsync("alice", master.Id,
            master.Cash - 1, master.Gold + 18, inventory.Append(purchasedItem), master.Equipment ?? [],
            expectedCash: master.Cash);
        check(claim is not null && purchase is not null && purchase.Inventory!.Contains(purchasedItem)
              && purchase.Mailbox!.Single(m => m.Id == mail.Mail.Id).Gold == 0
              && purchase.Gold == master.Gold + 18 && purchase.Cash == master.Cash - 1
              && purchase.MentorGoldAwards?.Count == 1,
            "mail claim and Cera purchase complete while awarded gold waits for its owner, preserving money, item and receipt");
        await service.DetachChannelAsync(child.Id, sc);
        saved = (await store.GetCharacterByNameAsync(master.Name))!;
        check(saved.MentorGoldAwards?.Count == 1,
            "duplicate ordinary departure cannot award the same runtime batch twice");
        var installed = (await store.ConsumeMentorGoldCreditsAsync(master.Id)).Character;
        check(installed is { MentorGoldAwards: [{ Applied: true }] } && installed.Gold == master.Gold + 32
              && installed.Inventory!.Contains(newItem) && installed.Inventory.Contains(purchasedItem)
              && installed.Skills!.Single().Level == 2 && installed.Cash == master.Cash - 1,
            "subsequent canonical installation retains every successful stale-actor action and adds the exact awarded credit once");
    }

    private static async Task CheckReplacementSignalAsync(Action<bool, string> check)
    {
        var registry = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
        CharacterSessionLease? previous = null;
        var flaggedAtKick = false;
        var storage = Guid.NewGuid();
        previous = await registry.ClaimAsync(123u, storage, Guid.NewGuid(), () =>
        {
            flaggedAtKick = previous!.WasReplaced;
        }, default);
        var replacement = registry.ClaimAsync(123u, storage, Guid.NewGuid(), () => { }, default);
        check(flaggedAtKick && !replacement.IsCompleted,
            "real ClaimAsync publishes replacement reason before closing old owner and awaits its final-save release");
        registry.Release(previous);
        registry.Release(await replacement);
    }
}
