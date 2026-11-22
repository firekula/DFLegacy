using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class TimedTitleLifetimeSmokeTests
{
    private const ushort TimedTitleItemId = 50020;
    private const ushort PermanentTitleItemId = 50021;

    internal static async Task RunAsync(Action<bool, string> check)
    {
        const long now = 1_800_000_000;
        using var fixture = new Fixture();
        var catalog = fixture.Catalog;

        check(catalog.TryGetDefinition(TimedTitleItemId, out var timedDefinition)
                && timedDefinition.IsTitle && timedDefinition.UsablePeriodDays == 30,
            "a title template with [usable period] 30 is parsed as a limited title");
        check(catalog.TryGetDefinition(PermanentTitleItemId, out var permanentDefinition)
                && permanentDefinition.IsTitle && permanentDefinition.UsablePeriodDays is null,
            "a title template without [usable period] remains permanent");

        var clock = new Clock(now);
        JsonGameStore CreateStore() => new(fixture.Options, NullLogger<JsonGameStore>.Instance,
            itemCatalog: catalog, avatarTimeProvider: clock);
        var store = CreateStore();
        await store.InitializeAsync();
        await store.CreateAccountAsync(new("timedtitle", "password", OicqCode: "123456"));
        var character = (await store.CreateCharacterAsync("timedtitle", new("TimedTitleChar"))).Character!;

        var bagTimed = new CharacterItemRecord(9, TimedTitleItemId, 1, InstanceId: Guid.NewGuid());
        var bagPermanent = new CharacterItemRecord(11, PermanentTitleItemId, 1, InstanceId: Guid.NewGuid());
        var wornTimed = new CharacterItemRecord(10, TimedTitleItemId, 1, InstanceId: Guid.NewGuid());
        var storedTimed = new CharacterItemRecord(0, TimedTitleItemId, 1, InstanceId: Guid.NewGuid());
        var saved = (await store.SaveCharacterItemSpacesAsync("timedtitle", character.Id, 100,
            [bagTimed, bagPermanent], [wornTimed], [storedTimed]))!;
        var anchored = saved.Inventory!.Single(item => item.InstanceId == bagTimed.InstanceId);
        check(anchored.AvatarExpiresAtUnixSeconds == now + 30 * 86_400L,
            "first persistence anchors a limited title at now + 30 days");
        check(saved.Equipment!.Single(item => item.InstanceId == wornTimed.InstanceId)
                .AvatarExpiresAtUnixSeconds == now + 30 * 86_400L
            && saved.Warehouse!.Single(item => item.InstanceId == storedTimed.InstanceId)
                .AvatarExpiresAtUnixSeconds == now + 30 * 86_400L,
            "equipment and warehouse copies receive their own anchored deadlines");
        check(saved.Inventory!.Single(item => item.InstanceId == bagPermanent.InstanceId)
                .AvatarExpiresAtUnixSeconds is null,
            "a permanent title is never assigned a deadline");

        var mail = await store.SendCharacterMailAsync(character.Id,
            new("System", "Timed title", 0, new(TimedTitleItemId, 1, InstanceId: Guid.NewGuid())));
        check(mail.Mail?.Attachment?.AvatarExpiresAtUnixSeconds == now + 30 * 86_400L,
            "a system-mail timed title is anchored when the mail is created");

        var boxOriginal = new Dictionary<ushort, CharacterItemRecord>
        {
            [41] = new(41, 200, 1),
            [73] = new(73, 201, 4)
        };
        var boxCharacter = (await store.CreateCharacterAsync("timedtitle", new("BoxTitleChar"))).Character!;
        await store.SaveCharacterItemSpacesAsync("timedtitle", boxCharacter.Id, 100,
            boxOriginal.Values, [], []);
        check(RandomBoxOpeningPlanner.TryCreate(boxOriginal, 41, 73, 60, catalog, uint.MaxValue,
                new Rolls(0), out var boxPlan, out _)
            && boxPlan.Inventory.Values.Any(item => item.ItemId == TimedTitleItemId && item.AvatarExpiresAtUnixSeconds is null),
            "the random-box planner creates the limited title before store normalization");
        var boxSaved = (await store.SaveCharacterItemSpacesAsync("timedtitle", boxCharacter.Id, 100,
            boxPlan.Inventory.Values, [], []))!;
        check(boxSaved.Inventory!.Any(item => item.ItemId == TimedTitleItemId
                && item.AvatarExpiresAtUnixSeconds == now + 30 * 86_400L),
            "a magic-box limited title receives its deadline through the ordinary save path");

        var upperActor = (await store.CreateCharacterAsync("timedtitle", new("HighTitle"))).Character!;
        var upperItem = bagTimed with { InstanceId = Guid.NewGuid() };
        var ordinary = bagPermanent with { InstanceId = Guid.NewGuid() };
        var upperSaved = (await store.SaveCharacterItemSpacesAsync("timedtitle", upperActor.Id, 100,
            [upperItem, ordinary], [], []))!;
        var originalTitle = upperSaved.Inventory!.Single(i => i.InstanceId == upperItem.InstanceId);
        var high = new InventoryAddress(InventorySection.Equipment, 99_998);
        upperSaved = (await store.MoveToEmptyInventorySlotAsync("timedtitle", upperActor.Id,
            new(InventorySection.Equipment, 0), high, originalTitle))!;
        check(upperSaved.ExtendedInventory!.Single().Item.AvatarExpiresAtUnixSeconds == originalTitle.AvatarExpiresAtUnixSeconds,
            "moving a timed title to the last equipment slot preserves the deadline");
        var atDeadline = originalTitle.AvatarExpiresAtUnixSeconds!.Value;
        check(AvatarLifetimePolicy.GetExpiryWait(upperSaved, atDeadline) == TimeSpan.FromSeconds(1),
            "high-slot title wakes the expiry timer after its strict deadline");
        var staleView = upperSaved;
        clock.Now = atDeadline;
        await store.SettleAvatarExpirationsAsync("timedtitle", upperActor.Id);
        var boundary = await store.GetCharacterAsync("timedtitle", upperActor.Id);
        check(boundary!.ExtendedInventory?.Count == 1, "high-slot item survives exact deadline");
        clock.Now = atDeadline + 1;
        await store.SettleAvatarExpirationsAsync("timedtitle", character.Id);
        var reopened = CreateStore();
        await reopened.InitializeAsync();
        var upperExpired = (await reopened.GetCharacterAsync("timedtitle", upperActor.Id))!;
        check(upperExpired.ExtendedInventory is null && upperExpired.ExpiredAvatarInstanceIds!.Contains(upperItem.InstanceId)
            && upperExpired.Inventory!.Any(i => i.InstanceId == ordinary.InstanceId),
            "offline expiry removes only the upper-slot title and persists the tombstone");
        check(AvatarLifetimePolicy.MergeForActor(staleView, upperExpired, catalog).ExtendedInventory is null,
            "session refresh removes the stale upper-slot title");
        var expired = (await store.GetCharactersAsync("timedtitle"))[0];
        check((expired.Inventory ?? []).All(item => item.ItemId != TimedTitleItemId)
                && (expired.Equipment ?? []).All(item => item.ItemId != TimedTitleItemId)
                && (expired.Warehouse ?? []).All(item => item.ItemId != TimedTitleItemId),
            "expired titles are removed from bag, equipment and warehouse");
        check((expired.Inventory ?? []).Any(item => item.ItemId == PermanentTitleItemId),
            "the permanent title survives expiration");
        check(expired.Mailbox!.All(m => m.Attachment?.ItemId != TimedTitleItemId),
            "an expired mailed title loses its attachment without deleting the mail");
        var expiredIds = expired.ExpiredAvatarInstanceIds ?? [];
        check(expiredIds.Contains(bagTimed.InstanceId) && expiredIds.Contains(wornTimed.InstanceId)
                && expiredIds.Contains(storedTimed.InstanceId),
            "expiration tombstones every removed title instance, including equipment and warehouse copies");

        var reloaded = CreateStore();
        await reloaded.InitializeAsync();
        var durable = (await reloaded.GetCharactersAsync("timedtitle"))[0];
        check((durable.Inventory ?? []).All(item => item.ItemId != TimedTitleItemId)
                && (durable.Equipment ?? []).All(item => item.ItemId != TimedTitleItemId)
                && (durable.Warehouse ?? []).All(item => item.ItemId != TimedTitleItemId),
            "a fresh store load does not resurrect expired titles");
    }

    private sealed class Rolls(int value) : IDropRandomSource
    {
        public int Next(int maximum) => value;
    }

    private sealed class Clock(long now) : TimeProvider
    {
        internal long Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(Now);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("dflegacy-timed-title-");
        internal ServerOptions Options { get; }
        internal ItemCatalog Catalog { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "equipment", "title"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "stackable"));
            File.WriteAllText(Path.Combine(root.FullName, "equipment", "equipment.lst"),
                "50020 `title/timed_title.equ`\n50021 `title/permanent_title.equ`\n");
            File.WriteAllText(Path.Combine(root.FullName, "stackable", "stackable.lst"),
                "200 `box.stk`\n201 `hammer.stk`\n15 `filler.stk`\n");
            File.WriteAllText(Path.Combine(root.FullName, "equipment", "title", "timed_title.equ"), """
                [name] `Timed Title`
                [equipment type] `[title name]` 1
                [attach type] `[trade]`
                [rarity] 2
                [weight] 1
                [usable period] 30
                """);
            File.WriteAllText(Path.Combine(root.FullName, "equipment", "title", "permanent_title.equ"), """
                [name] `Permanent Title`
                [equipment type] `[title name]` 1
                [attach type] `[trade]`
                [rarity] 2
                [weight] 1
                """);
            File.WriteAllText(Path.Combine(root.FullName, "stackable", "box.stk"), """
                [name] `Title Box`
                [stackable type] `[random upgradable legacy]` 1
                [weight] 1
                [minimum level] 1
                [stack limit] 10
                [RANDOMBOX]
                [int data]
                15 1 0
                50020 3000 1 0
                [/int data]
                [sealing removal item]
                2
                201 4
                200 1
                [/sealing removal item]
                [/RANDOMBOX]
                """);
            File.WriteAllText(Path.Combine(root.FullName, "stackable", "hammer.stk"), """
                [name] `Box Hammer`
                [stackable type] `[material]` 0
                [weight] 1
                [stack limit] 1000
                """);
            File.WriteAllText(Path.Combine(root.FullName, "stackable", "filler.stk"), """
                [name] `Filler`
                [stackable type] `[consumable]` 0
                [weight] 1
                [stack limit] 1000
                """);
            Options = new ServerOptions
            {
                ScriptPvfPath = "",
                SkillScriptPath = root.FullName,
                DataPath = Path.Combine(root.FullName, "state.json"),
                SeedTestAccount = false
            };
            Catalog = new ItemCatalog(
                new ScriptFileSystem(Options, NullLogger<ScriptFileSystem>.Instance),
                NullLogger<ItemCatalog>.Instance,
                Options);
        }

        public void Dispose() => root.Delete(recursive: true);
    }
}
