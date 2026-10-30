using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class AvatarLifetimeSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        await using var fixture = new Fixture();
        foreach (var (commodity, price) in new[] { (1001u, 107), (1002u, 235), (1003u, 705) })
            check(fixture.Shop.TryGetProduct(commodity, out var product) && product.CashPrice == price,
                $"ACT1 avatar commodity {commodity} charges its own period price {price}, with intermediate truncation");
        check(!fixture.Shop.TryGetProduct(1004, out _), "an unknown avatar period is not offered as permanent");

        const long now = 1_800_000_000;
        var limited = WithDeadline(new(9, 1000, 1, AvatarRemainingSeconds: 300, InstanceId: Guid.NewGuid()), now + 30);
        uint Remaining(CharacterItemRecord item, long at) => AvatarLifetimePolicy.GetRemainingSeconds(item, at);
        bool Expired(CharacterItemRecord item, long at) => AvatarLifetimePolicy.IsExpired(item, at);
        check(Remaining(limited, now) == 30 && Remaining(limited, now + 10) == 20,
            "an avatar absolute deadline counts offline seconds rather than replaying its original duration");
        check(Remaining(limited, now + 30) == 1 && Expired(limited, now + 30) == false
            && Expired(limited, now + 31) == true,
            "the official strict expiration boundary never projects a finite avatar as permanent zero");
        check(Remaining(new(9, 1000, 1, AvatarRemainingSeconds: 0), now) == 0,
            "permanent avatars retain the ACT1 zero lifetime without acquiring a deadline");
        check(Remaining(limited with { AvatarExpiresAtUnixSeconds = long.MinValue }, now) == 1,
            "a malformed ancient deadline cannot overflow into decades of remaining lifetime");

        var attachment = new CharacterMailAttachmentRecord(1000, 1, AvatarRemainingSeconds: 300,
            AvatarAbilityIndex: 2, InstanceId: limited.InstanceId, AvatarExpiresAtUnixSeconds: now + 30);
        check(CharacterAvatarInventoryPlanner.TryPlace(new Dictionary<ushort, CharacterItemRecord>(), attachment,
                fixture.Items, out var placement, out _)
            && placement.Inventory.Values.Single().AvatarExpiresAtUnixSeconds == now + 30
            && placement.Inventory.Values.Single().InstanceId == limited.InstanceId,
            "mail collection and avatar placement preserve the original deadline and instance identity");
        var displaced = CharacterInventoryPlanner.NormalizeWarehouse([limited], fixture.Items, 0);
        check(displaced.OverflowItems.Single().AvatarExpiresAtUnixSeconds == now + 30,
            "normalizing misplaced avatar storage into recovery mail cannot lose its deadline");
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var wire = CharacterItemWireProjection.Project(limited with { AvatarExpiresAtUnixSeconds = before + 30 }, fixture.Items);
        check(wire.AddInfo is >= 29 and <= 30 && wire.Durability == 0,
            "wire projection sends current lifetime rather than the originally granted300 seconds");
        var staleWire = CharacterItemWireProjection.Project(limited with { AvatarExpiresAtUnixSeconds = before - 1 }, fixture.Items);
        check(staleWire.AddInfo == 1, "a packet racing expiration cleanup cannot encode expired limited gear as permanent");

        fixture.Items.TryGetDefinition(1000, out var definition);
        foreach (var (commodity, duration, deadline) in new[] {
            (1001u, 604800u, (long?)(now + 604800)), (1002u, 2592000u, now + 2592000), (1003u, 0u, null) })
        {
            fixture.Shop.TryGetProduct(commodity, out var product);
            var purchased = AvatarLifetimePolicy.CreatePurchaseAttachment(product, definition, now);
            check(purchased is not null && purchased.AvatarRemainingSeconds == duration
                && purchased.AvatarExpiresAtUnixSeconds == deadline && purchased.InstanceId != Guid.Empty,
                $"native avatar purchase {commodity} grants the selected lifetime and stable instance before overflow mail placement");
        }
        fixture.Items.TryGetDefinition(1000, out var avatarDefinition);
        var booster = avatarDefinition with { CeraBooster = new([new(CeraBoosterRewardKind.Avatar, 1,
            [new(CeraBoosterRewardKind.Avatar, 1000, 1, 1, AvatarPeriodDays: 30)], 1)]) };
        check(CeraShopContentPlanner.TryCreatePurchase(new Dictionary<ushort, CharacterItemRecord>(),
                new Dictionary<ushort, CharacterItemRecord>(), new Dictionary<ushort, CharacterItemRecord>(),
                booster, 1, fixture.Items, uint.MaxValue, new SequenceDropRandomSource(0), null,
                out var boosterPlan, out _)
            && boosterPlan.AvatarInventory.Values.Single().AvatarRemainingSeconds == 2592000
            && boosterPlan.AvatarInventory.Values.Single().AvatarExpiresAtUnixSeconds is > 0,
            "a booster configured with30 days cannot silently grant permanent avatar rewards");
        AvatarCompoundSmokeTests.Run(check);
        await CheckPersistenceAsync(check);
        await CheckSnapshotFaultsAsync(check);
        await CheckLegacyStartupAsync(check);
        await CheckSessionBoundariesAsync(check);
        await CheckPublicationRaceAsync(check);
    }

    private static async Task CheckSessionBoundariesAsync(Action<bool, string> check)
    {
        await using var f = new Fixture();
        const long now = 1_800_000_000;
        var timed = new CharacterItemRecord(9, 1000, 1, AvatarRemainingSeconds: 20,
            AvatarExpiresAtUnixSeconds: now + 20, InstanceId: Guid.NewGuid());
        var actor = new CharacterRecord(Guid.NewGuid(), "Actor", 1, 0, 10, 0, 444, Gold: 555,
            Inventory: [new(41, 8, 1)], Equipment: [timed with { Slot = 3 }, new(9, 33300, 1)],
            AvatarInventory: [timed], Mailbox: [new(1, "System", "Mail", 99,
                new(1000, 1, AvatarRemainingSeconds: 10, AvatarExpiresAtUnixSeconds: now + 10), 0, uint.MaxValue)]);
        var delay = AvatarLifetimePolicy.GetExpiryWait(actor, now);
        check(delay == TimeSpan.FromSeconds(11),
            "the earliest worn, bag or mail avatar expiry bounds read waiting at the strict boundary");
        var canonical = actor with { Gold = 9999, Cash = 9999, Inventory = [], Equipment = [], AvatarInventory = [], Mailbox = [] };
        var installed = AvatarLifetimePolicy.MergeForActor(actor, canonical, f.Items);
        check(installed is not null && installed.Gold == 555 && installed.Cash == 444 && installed.Inventory!.Count == 1
            && installed.Equipment!.Single().ItemId == 33300 && installed.AvatarInventory!.Count == 0,
            "expiry installs only avatar-owned fields and retains the live actor's currency and ordinary equipment");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = EntranceSessionReadWait.WaitAsync(pending.Task, TimeSpan.FromHours(1), false,
            CancellationToken.None, additionalWakeDelay: TimeSpan.FromMilliseconds(1));
        var finished = await Task.WhenAny(waiting, Task.Delay(1000));
        check(finished == waiting && await waiting == EntranceReadWakeReason.PartyMailTick && !pending.Task.IsCompleted,
            "the avatar deadline wakes internally without consuming or cancelling the pending network read");
        var packetFirst = EntranceSessionReadWait.WaitAsync(Task.CompletedTask, TimeSpan.FromHours(1), false,
            CancellationToken.None, additionalWakeDelay: TimeSpan.Zero);
        check(await packetFirst == EntranceReadWakeReason.Packet,
            "a simultaneously ready packet remains the only consumed read while expiry is handled before its business action");
    }

    private static async Task CheckPublicationRaceAsync(Action<bool, string> check)
    {
        await using var f = new Fixture();
        var clock = new Clock(1_800_000_000);
        var store = new JsonGameStore(f.Options, NullLogger<JsonGameStore>.Instance, itemCatalog: f.Items, avatarTimeProvider: clock);
        await store.CreateAccountAsync(new("publish", "password", OicqCode: "123456"));
        var c = (await store.CreateCharacterAsync("publish", new("Publisher"))).Character!;
        var worn = new CharacterItemRecord(3, 1000, 1, AvatarRemainingSeconds: 1,
            AvatarExpiresAtUnixSeconds: clock.Now + 1, InstanceId: Guid.NewGuid());
        var before = (await store.SaveCharacterItemSpacesAsync("publish", c.Id, 600, [], [worn], [], avatarInventory: []))!;
        var tracker = new AvatarLifetimeSessionRefresh();
        tracker.Reset(before);
        clock.Now += 2; // The request began valid and its normal save crosses expiry.
        var saved = (await store.SaveCharacterItemSpacesAsync("publish", c.Id, 600, [], before.Equipment!, [], avatarInventory: []))!;
        check(saved.Equipment!.Count == 0 && AvatarLifetimePolicy.GetExpiryWait(saved, clock.Now) is null,
            "the ordinary save reproduces the review race after deleting the last finite worn item");
        check(tracker.HasPending(saved),
            "unpublished expiry UUIDs remain a wake reason after all finite inventory entries are gone");
        var events = new List<string>();
        Task Bag() { events.Add("bag"); return Task.CompletedTask; }
        Task Appearance() { events.Add("self equipment + peer appearance"); return Task.CompletedTask; }
        Task Mail() { events.Add("mail"); return Task.CompletedTask; }
        await tracker.PublishAsync(saved, saved, Bag, Appearance, Mail);
        await tracker.PublishAsync(saved, saved, Bag, Appearance, Mail);
        check(events.SequenceEqual(new[] { "bag", "self equipment + peer appearance", "mail" })
            && !tracker.HasPending(saved),
            "expiry publication rebuilds both equipment views exactly once even when the caller already adopted the clean save");
    }

    private static async Task CheckPersistenceAsync(Action<bool, string> check)
    {
        await using var f = new Fixture();
        var clock = new Clock(1_800_000_000);
        JsonGameStore CreateStore() => new(f.Options, NullLogger<JsonGameStore>.Instance, itemCatalog: f.Items, avatarTimeProvider: clock);
        var store = CreateStore();
        await store.CreateAccountAsync(new("lifetime", "password", OicqCode: "123456"));
        await store.CreateAccountAsync(new("other", "password", OicqCode: "123456"));
        var character = (await store.CreateCharacterAsync("lifetime", new("Timed"))).Character!;
        var other = (await store.CreateCharacterAsync("other", new("Recipient"))).Character!;
        var finite = new CharacterItemRecord(9, 1000, 1, AvatarRemainingSeconds: 10,
            InstanceId: Guid.NewGuid(), AvatarExpiresAtUnixSeconds: clock.Now + 10);
        var permanent = new CharacterItemRecord(10, 1000, 1, AvatarRemainingSeconds: 0, InstanceId: Guid.NewGuid());
        var worn = finite with { Slot = 3, InstanceId = Guid.NewGuid() };
        await store.SaveCharacterItemSpacesAsync("lifetime", character.Id, 1234, [], [worn], [],
            avatarInventory: [finite, permanent]);
        var mail = await store.SendCharacterMailAsync(character.Id,
            new("System", "Overflow", 77, new(1000, 1, AvatarRemainingSeconds: 10,
                InstanceId: Guid.NewGuid(), AvatarExpiresAtUnixSeconds: clock.Now + 10)));
        check(mail.Created, "limited system-mail delivery retains a finite instance alongside mail gold");
        var saved = (await store.GetCharactersAsync("lifetime"))[0];
        var clearedEarly = finite with { AvatarRemainingSeconds = 0, AvatarExpiresAtUnixSeconds = null };
        var earlySave = await store.SaveCharacterItemSpacesAsync("lifetime", character.Id, 1234, [], [worn], [],
            avatarInventory: [clearedEarly, permanent]);
        check(earlySave?.AvatarInventory?.Single(i => i.InstanceId == finite.InstanceId).AvatarExpiresAtUnixSeconds == clock.Now + 10,
            "an existing finite UUID cannot have its deadline cleared or extended by an ordinary save");
        clock.Now += 10;
        async Task<CharacterRecord> Settle()
        {
            await store.SettleAvatarExpirationsAsync("lifetime", character.Id);
            return (await store.GetCharactersAsync("lifetime"))[0];
        }
        var atBoundary = await Settle();
        check(atBoundary.AvatarInventory!.Count == 2 && atBoundary.Equipment!.Count == 1,
            "a finite item survives the official equality boundary with one second on the wire");
        clock.Now++;
        var expired = await Settle();
        check(expired.AvatarInventory!.Single().InstanceId == permanent.InstanceId
            && expired.Equipment!.Count == 0 && expired.Gold == 1234,
            "one atomic expiry removes worn and bag avatars while preserving permanent gear and gold");
        check(expired.Mailbox!.Single().Attachment is null && expired.Mailbox!.Single().Gold == 77,
            "an expired system-mail avatar is removed without deleting its money or message");
        var stale = await store.SaveCharacterItemSpacesAsync("lifetime", character.Id, 1234, [], saved.Equipment!, [],
            avatarInventory: saved.AvatarInventory!);
        check(stale?.AvatarInventory?.Count == 1 && stale.Equipment?.Count == 0,
            "a stale whole-inventory save cannot resurrect already-expired avatar instances");
        var clearedDeadline = finite with { AvatarRemainingSeconds = 0, AvatarExpiresAtUnixSeconds = null };
        stale = await store.SaveCharacterItemSpacesAsync("lifetime", character.Id, 1234, [], [], [],
            avatarInventory: [permanent, clearedDeadline]);
        check(stale?.AvatarInventory?.Count == 1,
            "dropping lifetime metadata cannot turn a previously expired UUID into permanent gear");
        var reloaded = CreateStore();
        await reloaded.InitializeAsync();
        check((await reloaded.GetCharactersAsync("lifetime"))[0].AvatarInventory?.Count == 1,
            "restart does not recreate expired bag or worn avatars");
        var finiteMail = new CharacterMailAttachmentRecord(1000, 1, AvatarRemainingSeconds: 60,
            AvatarExpiresAtUnixSeconds: clock.Now + 60, InstanceId: Guid.NewGuid());
        var sent = await store.SendCharacterMailFromCharacterAsync("lifetime", character.Id, other.Name,
            new("Timed", "Player", Attachment: finiteMail), 1234, [], []);
        check(!sent.Sent && (await store.GetCharacterMailsAsync(other.Id)).Length == 0,
            "a finite avatar cannot be sent through player mail, independently of the client UI");
        var legacy = await store.SendCharacterMailAsync(character.Id,
            new("System", "Legacy remaining", Attachment: new(1000, 1, AvatarRemainingSeconds: 60)));
        check(legacy.Mail?.Attachment?.AvatarExpiresAtUnixSeconds == clock.Now + 60,
            "a new finite system-mail attachment starts its clock at grant rather than first collection");
    }

    private sealed class FailingWriter(bool afterReplace) : IDungeonRewardSnapshotWriter
    {
        internal bool Armed;
        internal int Calls;
        public async Task WriteAsync(string path, ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            Calls++;
            if (Armed && !afterReplace) throw new IOException("before avatar replacement");
            await new DungeonRewardSnapshotWriter().WriteAsync(path, data, ct);
            if (Armed) throw new IOException("after avatar replacement");
        }
    }
    private static async Task CheckSnapshotFaultsAsync(Action<bool, string> check)
    {
        foreach (var after in new[] { false, true })
        {
            await using var f = new Fixture();
            var clock = new Clock(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var writer = new FailingWriter(after);
            var store = new JsonGameStore(f.Options, NullLogger<JsonGameStore>.Instance, writer,
                itemCatalog: f.Items, avatarTimeProvider: clock);
            await store.CreateAccountAsync(new("fault", "password", OicqCode: "123456"));
            var c = (await store.CreateCharacterAsync("fault", new("Fault"))).Character!;
            var id = Guid.NewGuid();
            await store.SaveCharacterItemSpacesAsync("fault", c.Id, 555, [], [], [],
                avatarInventory: [new(9, 1000, 1, AvatarRemainingSeconds: 30, InstanceId: id,
                    AvatarExpiresAtUnixSeconds: clock.Now + 30)]);
            clock.Now += 31; writer.Armed = true;
            var result = await store.SettleAvatarExpirationsAsync("fault", c.Id);
            var calls = writer.Calls;
            var retry = await store.SettleAvatarExpirationsAsync("fault", c.Id);
            check(result.Status == DungeonRewardLedgerStatus.Unknown && retry.Status == DungeonRewardLedgerStatus.Unknown
                && writer.Calls == calls, $"expiry unknown {(after ? "after" : "before")} replacement quarantines without publishing or retrying");
            var disk = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
            var diskCharacter = disk["Accounts"]!.AsArray().Single(a => a!["UserName"]!.GetValue<string>() == "fault")!["Characters"]![0]!;
            check(diskCharacter["AvatarInventory"]!.AsArray().Count == (after ? 0 : 1),
                $"expiry {(after ? "after" : "before")} replacement leaves only its actual on-disk outcome");
        }
    }

    internal sealed class Clock(long now) : TimeProvider
    {
        internal long Now = now;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(Now);
    }

    private static async Task CheckLegacyStartupAsync(Action<bool, string> check)
    {
        await using var f = new Fixture();
        var clock = new Clock(1_800_000_000);
        JsonGameStore Open() => new(f.Options, NullLogger<JsonGameStore>.Instance, itemCatalog: f.Items, avatarTimeProvider: clock);
        var store = Open();
        await store.CreateAccountAsync(new("legacy", "password", OicqCode: "123456"));
        var c = (await store.CreateCharacterAsync("legacy", new("Legacy"))).Character!;
        await store.SaveCharacterItemSpacesAsync("legacy", c.Id, 700, [], [], [],
            avatarInventory: [new(9, 1000, 1, AvatarRemainingSeconds: 60, InstanceId: Guid.NewGuid())]);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(f.Options.DataPath))!;
        var oldItem = json["Accounts"]![0]!["Characters"]![0]!["AvatarInventory"]![0]!.AsObject();
        oldItem.Remove("AvatarExpiresAtUnixSeconds");
        await File.WriteAllTextAsync(f.Options.DataPath, json.ToJsonString());
        clock.Now += 100;
        var migrated = Open(); await migrated.InitializeAsync();
        var anchored = (await migrated.GetCharactersAsync("legacy"))[0].AvatarInventory!.Single();
        check(anchored.AvatarExpiresAtUnixSeconds == 1_800_000_160 && anchored.AvatarRemainingSeconds == 60,
            "a legacy remaining-seconds save acquires one documented migration anchor without pretending it was an epoch");
        clock.Now += 20;
        var restarted = Open(); await restarted.InitializeAsync();
        check((await restarted.GetCharactersAsync("legacy"))[0].AvatarInventory!.Single().AvatarExpiresAtUnixSeconds == 1_800_000_160,
            "a second restart retains the first anchor rather than renewing the legacy period");
        clock.Now = 1_800_000_161;
        var offlineExpired = Open(); await offlineExpired.InitializeAsync();
        var after = (await offlineExpired.GetCharactersAsync("legacy"))[0];
        check(after.AvatarInventory!.Count == 0 && after.Gold == 700,
            "offline passage expires finite instances during initialization without changing money");
    }

    private static CharacterItemRecord WithDeadline(CharacterItemRecord item, long deadline)
    {
        var json = JsonSerializer.SerializeToNode(item)!.AsObject();
        json["AvatarExpiresAtUnixSeconds"] = deadline;
        return json.Deserialize<CharacterItemRecord>()!;
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "dflegacy-avatar-life-" + Guid.NewGuid().ToString("N"));
        internal ServerOptions Options { get; }
        internal ItemCatalog Items { get; }
        internal CeraShopCatalog Shop { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "equipment/character/fighter/avatar/coat"));
            Directory.CreateDirectory(Path.Combine(Root, "etc"));
            File.WriteAllText(Path.Combine(Root, "equipment/equipment.lst"), "1000 `character/fighter/avatar/coat/test.equ`\n");
            File.WriteAllText(Path.Combine(Root, "equipment/character/fighter/avatar/coat/test.equ"),
                "[name] `Limited test`\n[equipment type] `[coat avatar]`\n[attach type] `[free]`\n[cash] 107\n[grade] 1\n");
            File.WriteAllText(Path.Combine(Root, "etc/newcashshop.etc"), "[avatar]\n1001 1000 1\n1002 1000 2\n1003 1000 3\n1004 1000 4\n[/avatar]\n");
            Options = new() { DataPath = Path.Combine(Root, "state.json"), ScriptPvfPath = "", SkillScriptPath = Root };
            var scripts = new ScriptFileSystem(Options, NullLogger<ScriptFileSystem>.Instance);
            Items = new(scripts, NullLogger<ItemCatalog>.Instance);
            Shop = new(scripts, Items, NullLogger<CeraShopCatalog>.Instance);
        }
        public ValueTask DisposeAsync() { Directory.Delete(Root, true); return ValueTask.CompletedTask; }
    }
}
