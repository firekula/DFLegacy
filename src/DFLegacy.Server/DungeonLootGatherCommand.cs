using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal static class DungeonLootGatherCommand
{
    internal static async ValueTask<bool> TrySendAsync(
        byte messageType, string message, DungeonInstance? instance, Guid characterId,
        ushort localUserId, bool alive, bool inPvp, Func<GameServerPacket, ValueTask> sendToRequester)
    {
        // This is a normal player command, independent of test/admin account privileges.
        // The native dungeon chat tab sends type 2 even in a solo run; map chat uses type 3.
        // Detached tutorial rooms and stopped instances have no active run room.
        if ((messageType != (byte)GameMessageType.Type02LightCyan && messageType != (byte)GameMessageType.Type03White)
            || !string.Equals(message, "牛来", StringComparison.Ordinal)
            || !alive || inPvp || characterId == Guid.Empty
            || instance is null || instance.IsParty || instance.SoloCharacterId != characterId
            || instance.RunCurrentRoom is null)
        {
            return false;
        }

        // Native code moves only visible ground actors once. The authoritative
        // ground ledger (including unopened containers and pending pickups) stays intact.
        // Use the requesting session's sender, never an instance/area broadcast.
        await sendToRequester(GameProtocolEngine.CreateMessageNotification(
            GameMessageType.Type16Brown, localUserId, "@DFLegacy:loot-magnet:v1@"u8));
        return true;
    }
}
