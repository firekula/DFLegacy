using System.Net;
using System.Net.Sockets;

namespace DFLegacy.Server;

public sealed class FallbackRelayOptions
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "";
    public string AdvertisedHost { get; set; } = "";
    public int TcpPort { get; set; } = 7005;
    public int UdpPort { get; set; } = 7005;

    public string ResolveBindHost(ServerOptions options) =>
        string.IsNullOrWhiteSpace(Host) ? options.Entrance.Host : Host;

    public string ResolveAdvertisedHost(ServerOptions options) =>
        string.IsNullOrWhiteSpace(AdvertisedHost) ? EntranceService.ResolveChannelDialHost(options) : AdvertisedHost;

    public void Validate(ServerOptions options)
    {
        if (!Enabled) return;
        if (!IPAddress.TryParse(ResolveBindHost(options), out var bind))
            throw new InvalidOperationException("Fallback relay bind host must be an IP address.");
        var advertised = ResolveAdvertisedHost(options);
        if (!IPAddress.TryParse(advertised, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork
            || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast) || ip.GetAddressBytes()[0] >= 224)
            throw new InvalidOperationException("ACT1 fallback relay requires a concrete IPv4 advertisement.");
        if ((bind.AddressFamily == AddressFamily.InterNetworkV6 && !bind.Equals(IPAddress.IPv6Any))
            || (IPAddress.IsLoopback(bind) && !bind.Equals(ip))
            || (IPAddress.IsLoopback(ip) && !bind.Equals(IPAddress.Any) && !bind.Equals(IPAddress.IPv6Any) && !bind.Equals(ip)))
            throw new InvalidOperationException("Fallback relay bind cannot serve its advertised IPv4 address.");
        if (TcpPort is < 1 or > 65535 || UdpPort is < 1 or > 65535)
            throw new InvalidOperationException("Fallback relay ports must be in 1..65535.");
        if (TcpPort == options.Channel.Port || TcpPort == options.Admin.Port
            || (options.Processes.Game.EntranceBinding && options.Entrance.Enabled
                && (TcpPort == options.Entrance.Port || options.Entrance.AdditionalPorts.Contains(TcpPort)))
            || (options.Pvp.Enabled && TcpPort == options.Pvp.Port)
            || (options.Processes.Channel.Enabled && TcpPort == options.Processes.Channel.LinkPort)
            || (options.GameProbe.Enabled && TcpPort == options.GameProbe.Port))
            throw new InvalidOperationException("Fallback relay TCP port conflicts with another listener.");
        if ((options.CharacterDatagram.Enabled && UdpPort == options.CharacterDatagram.Port)
            || (options.GameplayDatagram.Enabled && (UdpPort == options.GameplayDatagram.PrimaryPort
                || UdpPort == options.GameplayDatagram.SecondaryPort)))
            throw new InvalidOperationException("Fallback relay UDP port conflicts with another listener.");
    }
}
