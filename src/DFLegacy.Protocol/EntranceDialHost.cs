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

    public static bool IsDialable(string host) =>
        !string.IsNullOrWhiteSpace(host)
        && IPAddress.TryParse(host, out var address)
        && !address.Equals(IPAddress.Any)
        && !address.Equals(IPAddress.IPv6Any)
        && !address.Equals(IPAddress.Broadcast);
}
