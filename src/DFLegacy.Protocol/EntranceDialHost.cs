using System.Net;

namespace DFLegacy.Protocol;

/// <summary>
/// 决定"告诉客户端去拨哪个地址"。Channel.Host 可能是 0.0.0.0 这类绑定地址，绝不能当成拨号目标
/// （客户端拨 0.0.0.0 会失败）。优先级：可拨的广告地址 → Entrance.Host → 可拨的 Channel.Host。
/// 逻辑从 EntranceService.ResolveChannelDialHost 原样抽出，两个进程共用。
/// </summary>
public static class EntranceDialHost
{
    public static string Resolve(string advertisedHost, string entranceHost, string channelHost)
    {
        if (IsDialable(advertisedHost))
        {
            return advertisedHost;
        }

        if (IsDialable(entranceHost))
        {
            return entranceHost;
        }

        return channelHost;
    }

    public static bool IsDialable(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        // 逐字镜像 EntranceService.IsDialableHost（EntranceService.cs:17680-17689）：
        // 不是 IP 的主机名算可拨；只有通配地址 0.0.0.0 / :: 不可拨。
        // 广播 255.255.255.255 现存实现**不排除**，这里也不排除 —— Task 4 要把调用点
        // 换成这个方法，语义必须完全一致，否则客户端拨号目标会悄悄变（违反 I1 验收③）。
        return !IPAddress.TryParse(host, out var address)
            || (!IPAddress.Any.Equals(address) && !IPAddress.IPv6Any.Equals(address));
    }
}
