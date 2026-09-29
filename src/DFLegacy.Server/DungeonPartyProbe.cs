using System.Collections.Concurrent;

namespace DFLegacy.Server;

/// <summary>
/// 探针（临时，<c>Debug.DungeonPeerProbe.Mode=PartyPush</c>）用的跨会话小状态：
/// 队长进副本时给所有队员（含队长自己）开一个「外观互推窗口」，窗口内每个会话把自己的
/// <c>USERINFO(mode 0)</c> 推给同队其他人——客户端副本内的队伍槽位是按 uid 查本地 user 表
/// 解析出角色对象的（`CNRDPartyManager::GetMember` 的槽位就是 user 指针），本地没有记录时
/// 那一行画不出来（P14 现象）。
///
/// 用静态类是为了不动 DI 装配：删掉本文件与调用点即完全移除，不影响任何既有语义。
/// </summary>
internal static class DungeonPartyProbeState
{
    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> AppearanceWindows = new();

    public static void OpenAppearanceWindow(Guid characterId, TimeSpan duration) =>
        AppearanceWindows[characterId] = DateTimeOffset.UtcNow + duration;

    public static void CloseAppearanceWindow(Guid characterId) =>
        AppearanceWindows.TryRemove(characterId, out _);

    /// <summary>窗口是否还开着；过期就在这次顺手清掉。</summary>
    public static bool IsAppearanceWindowOpen(Guid characterId)
    {
        if (!AppearanceWindows.TryGetValue(characterId, out var deadline))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow >= deadline)
        {
            AppearanceWindows.TryRemove(characterId, out _);
            return false;
        }

        return true;
    }
}
