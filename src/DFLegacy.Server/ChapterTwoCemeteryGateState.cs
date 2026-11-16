using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal readonly record struct ChapterTwoCemeteryGateState(uint RunToken, bool IsOpen)
{
    internal const ushort DungeonId = 35;

    internal GameServerPacket CreateEntryPacket() =>
        GameProtocolEngine.CreateChapterTwoCemeteryGate(RunToken, entry: true, open: IsOpen);

    internal GameServerPacket CreateUnlockPacket() =>
        GameProtocolEngine.CreateChapterTwoCemeteryGate(RunToken, entry: false, open: true);
}
