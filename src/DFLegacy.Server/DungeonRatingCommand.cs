using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal static class DungeonRatingCommand
{
    internal const byte SssResultCode = 105;

    internal static async ValueTask<bool> TrySendAsync(
        byte messageType, string message, DungeonInstance? instance, Guid characterId,
        ushort localUserId, bool alive, bool inPvp, Func<GameServerPacket, ValueTask> sendToRequester,
        bool chapterTwo)
    {
        if (!chapterTwo || !alive || inPvp
            || messageType is not ((byte)GameMessageType.Type02LightCyan) and not ((byte)GameMessageType.Type03White)
            || !string.Equals(message, "赐我评分", StringComparison.Ordinal)
            || instance?.TryLockSssRating(characterId) != true)
        {
            return false;
        }

        await sendToRequester(GameProtocolEngine.CreateMessageNotification(
            GameMessageType.Type16Brown, localUserId, "@DFLegacy:dungeon-rating:sss:v1@"u8));
        return true;
    }

    internal static byte ResolveResultCode(DungeonInstance instance, Guid characterId,
        DungeonClearScore? score, bool chapterTwo, out bool locked)
    {
        locked = score is not null && chapterTwo && instance.HasLockedSssRating(characterId);
        return locked ? SssResultCode : score?.CalculatedResultCode ?? 0;
    }
}
