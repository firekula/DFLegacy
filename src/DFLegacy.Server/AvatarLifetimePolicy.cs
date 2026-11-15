namespace DFLegacy.Server;

public static class AvatarLifetimePolicy
{
    public static bool IsLimited(CharacterItemRecord item) =>
        item.AvatarExpiresAtUnixSeconds is not null || item.AvatarRemainingSeconds is > 0;

    public static CharacterMailAttachmentRecord CreatePurchaseAttachment(
        CeraShopProduct product, ItemDefinition definition, long nowUnixSeconds)
        => CreatePurchaseAttachment(product, definition, nowUnixSeconds, 0);

    public static CharacterMailAttachmentRecord CreatePurchaseAttachment(
        CeraShopProduct product, ItemDefinition definition, long nowUnixSeconds, byte avatarAbilityIndex)
    {
        if (definition.InventoryCategory != ItemInventoryCategory.Avatar
            || product.ItemId != definition.Id || product.Quantity == 0)
            throw new ArgumentException("A native avatar purchase requires an avatar commodity.");
        uint seconds = product.AttributeValue switch
        {
            1 => 604_800,
            2 => 2_592_000,
            3 => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(product))
        };
        return new(product.ItemId, product.Quantity,
            SealState: CharacterItemSealing.GetInitialSealState(definition),
            AvatarRemainingSeconds: seconds, AvatarAbilityIndex: avatarAbilityIndex,
            InstanceId: CharacterItemIdentity.CreateInstanceId(),
            AvatarExpiresAtUnixSeconds: seconds == 0 ? null : checked(nowUnixSeconds + seconds));
    }

    public static bool IsExpired(CharacterItemRecord item, long nowUnixSeconds) =>
        item.AvatarExpiresAtUnixSeconds is { } deadline && deadline < nowUnixSeconds;

    public static uint GetRemainingSeconds(CharacterItemRecord item, long nowUnixSeconds)
    {
        if (item.AvatarExpiresAtUnixSeconds is not { } deadline)
            return item.AvatarRemainingSeconds ?? 0;
        if (deadline <= nowUnixSeconds) return 1;
        // Zero is permanent in ACT1. At the strict boundary (or a send racing
        // the next cleanup tick) retain one second until authoritative removal.
        return (uint)Math.Clamp(deadline - nowUnixSeconds, 1L, uint.MaxValue);
    }

    internal static CharacterRecord NormalizeCharacter(CharacterRecord candidate, CharacterRecord? previous,
        ItemCatalog catalog, long nowUnixSeconds)
    {
        bool IsAvatar(ushort id) => catalog.TryGetDefinition(id, out var d) && d.InventoryCategory == ItemInventoryCategory.Avatar;
        static IEnumerable<CharacterItemRecord> Items(CharacterRecord c) =>
            (c.Inventory ?? []).Concat(c.Equipment ?? []).Concat(c.Warehouse ?? [])
                .Concat(c.AvatarInventory ?? []).Concat(c.CreatureInventory ?? []);
        var known = Items(previous ?? candidate).Where(i => i.InstanceId != Guid.Empty && IsAvatar(i.ItemId))
            .Concat((previous?.Mailbox ?? candidate.Mailbox ?? []).Where(m => m.Attachment is not null)
                .Select(m => FromAttachment(m.Attachment!)))
            .Where(i => i.InstanceId != Guid.Empty && IsLimited(i))
            .GroupBy(i => i.InstanceId).ToDictionary(g => g.Key, g => g.First());
        var expiredIds = (previous?.ExpiredAvatarInstanceIds ?? []).Concat(candidate.ExpiredAvatarInstanceIds ?? []).ToHashSet();

        CharacterItemRecord? Normalize(CharacterItemRecord original)
        {
            if (!IsAvatar(original.ItemId)) return original;
            var item = CharacterItemIdentity.Ensure(original, catalog);
            if (expiredIds.Contains(item.InstanceId)) return null;
            if (known.TryGetValue(item.InstanceId, out var canonical))
                item = item with { AvatarExpiresAtUnixSeconds = canonical.AvatarExpiresAtUnixSeconds,
                    AvatarRemainingSeconds = canonical.AvatarRemainingSeconds };
            if (item.AvatarExpiresAtUnixSeconds is null && item.AvatarRemainingSeconds is > 0)
                item = item with { AvatarExpiresAtUnixSeconds = checked(nowUnixSeconds + item.AvatarRemainingSeconds.Value) };
            if (!IsExpired(item, nowUnixSeconds)) return item;
            expiredIds.Add(item.InstanceId);
            return null;
        }
        List<CharacterItemRecord>? NormalizeSpace(List<CharacterItemRecord>? items)
        {
            if (items is null) return null;
            var normalized = items.Select(Normalize).OfType<CharacterItemRecord>().ToList();
            return normalized.SequenceEqual(items) ? items : normalized;
        }
        var inventory = NormalizeSpace(candidate.Inventory);
        var equipment = NormalizeSpace(candidate.Equipment);
        var warehouse = NormalizeSpace(candidate.Warehouse);
        var avatars = NormalizeSpace(candidate.AvatarInventory);
        var creatures = NormalizeSpace(candidate.CreatureInventory);
        var mails = candidate.Mailbox?.Select(mail =>
        {
            if (mail.Attachment is not { } attachment || !IsAvatar(attachment.ItemId)) return mail;
            var normalized = Normalize(FromAttachment(attachment));
            return mail with { Attachment = normalized is null ? null : attachment with {
                InstanceId = normalized.InstanceId, AvatarRemainingSeconds = normalized.AvatarRemainingSeconds,
                AvatarExpiresAtUnixSeconds = normalized.AvatarExpiresAtUnixSeconds } };
        }).ToList();
        if (mails is not null && mails.SequenceEqual(candidate.Mailbox!)) mails = candidate.Mailbox;
        var tombstones = candidate.ExpiredAvatarInstanceIds;
        if (expiredIds.Count > 0 && (tombstones is null || !expiredIds.SetEquals(tombstones)))
            tombstones = expiredIds.Order().ToList();
        return candidate with { Inventory = inventory, Equipment = equipment, Warehouse = warehouse,
            AvatarInventory = avatars, CreatureInventory = creatures, Mailbox = mails,
            ExpiredAvatarInstanceIds = tombstones };
    }

    private static CharacterItemRecord FromAttachment(CharacterMailAttachmentRecord item) =>
        new(ushort.MaxValue, item.ItemId, item.CountOrValue, item.State, item.Durability, item.SealState,
            item.AvatarRemainingSeconds, item.AvatarAbilityIndex, EquipmentQualitySeed: item.EquipmentQualitySeed,
            InstanceId: item.InstanceId, AvatarExpiresAtUnixSeconds: item.AvatarExpiresAtUnixSeconds);

    public static TimeSpan? GetExpiryWait(CharacterRecord character, long nowUnixSeconds)
    {
        var items = (character.Inventory ?? []).Concat(character.Equipment ?? [])
            .Concat(character.AvatarInventory ?? []).Concat(character.Warehouse ?? [])
            .Concat(character.CreatureInventory ?? [])
            .Concat((character.Mailbox ?? []).Where(m => m.Attachment is not null).Select(m => FromAttachment(m.Attachment!)))
            .Where(IsLimited).ToArray();
        if (items.Length == 0) return null;
        if (items.Any(i => i.AvatarExpiresAtUnixSeconds is null)) return TimeSpan.Zero;
        var deadline = items.Min(i => i.AvatarExpiresAtUnixSeconds!.Value);
        // Task.Delay has a bounded duration; a long combined lease can wake and
        // recalculate without either overflowing or expiring early.
        var seconds = Math.Clamp((double)deadline - nowUnixSeconds + 1, 0, int.MaxValue / 1000d);
        return TimeSpan.FromSeconds(seconds);
    }

    public static CharacterRecord MergeForActor(CharacterRecord actor, CharacterRecord canonical, ItemCatalog catalog)
    {
        bool IsAvatar(CharacterItemRecord i) => catalog.TryGetDefinition(i.ItemId, out var d)
            && d.InventoryCategory == ItemInventoryCategory.Avatar;
        var equipment = (actor.Equipment ?? []).Where(i => !IsAvatar(i))
            .Concat((canonical.Equipment ?? []).Where(IsAvatar)).OrderBy(i => i.Slot).ToList();
        return actor with { Equipment = equipment, AvatarInventory = canonical.AvatarInventory,
            Mailbox = canonical.Mailbox, ExpiredAvatarInstanceIds = canonical.ExpiredAvatarInstanceIds };
    }
}
