using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal static class DungeonActiveDropTransaction
{
    internal static async Task<DungeonGroundItem?> CommitAsync(
        DungeonInstance instance, DungeonItemDropPlan plan, ItemCatalog catalog,
        DungeonPartyService party, ushort dropperUid, byte roomX, byte roomY, short x, short y,
        Func<CancellationToken, Task<bool>> persist, Func<GameServerPacket, ValueTask> sendPrivate,
        CancellationToken cancellationToken = default)
    {
        DungeonGroundItem ground;
        try
        {
            ground = instance.AddPlayerDroppedGroundItem(plan.DroppedItem, dropperUid, roomX, roomY, x, y,
                isGold: plan.IsGold, activated: false);
        }
        catch (InvalidOperationException) { return null; }

        var saved = false;
        try
        {
            saved = await persist(cancellationToken);
            if (!saved) return null;
            var active = instance.ActivatePlayerDroppedGroundItem(ground);
            if (active is not null && instance.IsGroundItemInCurrentRoom(active))
            {
                var notification = GameProtocolEngine.CreateDropItem(dropperUid, x, y,
                    new(active.GroundId, active.ItemId, active.GetClientAddInfo(catalog),
                        active.GetClientDurability(catalog), active.OwnerUserId, active.GetClientItemAttr(catalog)));
                if (instance.IsParty) await party.BroadcastActiveDropAsync(instance, active, notification);
                else await sendPrivate(notification);
            }
            // Persistence committed even if a concurrent room/run teardown removed
            // the old object. Its inventory ACK remains private and truthful.
            await sendPrivate(GameProtocolEngine.CreateDropItemReply(plan.ItemSpace, plan.SourceSlot, plan.Count));
            return active ?? ground;
        }
        finally
        {
            if (!saved) instance.RemoveGroundItem(ground.GroundId, ground);
        }
    }
}
