using System.Net;
using System.Net.Sockets;
using System.Net.Http;

namespace DFLegacy.Launcher;

internal sealed record ChapterTwoConnection(string Host, bool UseRelay, string? LegacyIpv4)
{
    internal string AccountServiceUrl => new UriBuilder("http", Host, 48091).Uri.GetLeftPart(UriPartial.Authority);
    internal static readonly int[] TcpPorts = [42312, 47001, 48090, 47005, 7004, 7100, 7101, 7102, 7103, 7104, 7105, 7110, 7111, 7112];
    internal static readonly int[] UdpPorts = [42312, 47012, 47013, 47005];
    internal static HttpClient CreateAccountHttpClient(string url, bool useCookies = true) => new(new SocketsHttpHandler
    {
        UseProxy = false, AllowAutoRedirect = false, UseCookies = useCookies, PooledConnectionLifetime = TimeSpan.FromSeconds(30),
        ConnectCallback = (context, token) => DFLegacy.Relay.DomainHttpConnection.ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, token)
    }) { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(12) };
    internal static ChapterTwoConnection Parse(string? host, string? legacyIpv4)
    {
        if (host is not null && legacyIpv4 is not null)
            throw new InvalidDataException("ChapterTwoServerHost 和旧 ChapterTwoServerIpv4 不能同时填写。");
        if (host is null && legacyIpv4 is null) return new("127.0.0.2", false, null);
        var value = host ?? legacyIpv4!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || value != value.Trim()
            || value.Any(c => c > 126 || char.IsWhiteSpace(c) || char.IsControl(c))
            || value.IndexOfAny(['/', '\\', '%', '[', ']']) >= 0 || value.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请填写第二章服务器域名或 IP，不要包含端口、路径或 http 前缀。");
        if (IPAddress.TryParse(value, out var address))
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6Multicast || address.IsIPv6LinkLocal || address.IsIPv4MappedToIPv6
                || address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] is 0 or >= 224
                || legacyIpv4 is not null && (address.AddressFamily != AddressFamily.InterNetwork || address.ToString() != value))
                throw new InvalidDataException("第二章远程地址必须是玩家可访问的地址。");
        }
        else if (legacyIpv4 is not null || Uri.CheckHostName(value) != UriHostNameType.Dns || value.Contains(':'))
            throw new InvalidDataException("第二章服务器地址格式无效。");
        return new(value, host is not null, legacyIpv4);
    }
}
