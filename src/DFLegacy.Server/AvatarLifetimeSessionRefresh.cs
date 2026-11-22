namespace DFLegacy.Server;

// Publication is connection-owned. A normal inventory transaction may remove a
// worn avatar at its deadline before this loop sees it; persisted expiry UUIDs
// still require a client rebuild even if the caller adopted the clean record.
internal sealed class AvatarLifetimeSessionRefresh
{
    private readonly HashSet<Guid> _published = [];

    internal void Reset(CharacterRecord character)
    {
        _published.Clear();
        _published.UnionWith(character.ExpiredAvatarInstanceIds ?? []);
    }

    internal bool HasPending(CharacterRecord character) =>
        (character.ExpiredAvatarInstanceIds ?? []).Any(id => !_published.Contains(id));

    internal Task<bool> PublishAsync(CharacterRecord before, CharacterRecord after,
        Func<Task> refreshBag, Func<Task> refreshEquipmentAndPeers, Func<Task> refreshMailbox) =>
        PublishAsync(before, after, refreshBag, () => Task.CompletedTask, () => Task.CompletedTask,
            refreshEquipmentAndPeers, refreshMailbox);

    internal async Task<bool> PublishAsync(CharacterRecord before, CharacterRecord after,
        Func<Task> refreshBag, Func<Task> refreshMainInventory, Func<Task> refreshWarehouse,
        Func<Task> refreshEquipmentAndPeers, Func<Task> refreshMailbox)
    {
        var expiryPending = HasPending(after);
        var bag = expiryPending || !(before.AvatarInventory ?? []).SequenceEqual(after.AvatarInventory ?? []);
        var main = expiryPending || !(before.Inventory ?? []).SequenceEqual(after.Inventory ?? []);
        var warehouse = expiryPending || !(before.Warehouse ?? []).SequenceEqual(after.Warehouse ?? []);
        var equipment = expiryPending || !(before.Equipment ?? []).SequenceEqual(after.Equipment ?? []);
        var mail = expiryPending || !(before.Mailbox ?? []).SequenceEqual(after.Mailbox ?? []);
        if (bag) await refreshBag();
        if (main) await refreshMainInventory();
        if (warehouse) await refreshWarehouse();
        if (equipment) await refreshEquipmentAndPeers();
        if (mail) await refreshMailbox();
        // A failed send must not acknowledge an unseen expiration.
        _published.UnionWith(after.ExpiredAvatarInstanceIds ?? []);
        return bag || main || warehouse || equipment || mail;
    }
}
