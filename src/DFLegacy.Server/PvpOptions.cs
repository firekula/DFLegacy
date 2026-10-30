using System.Net;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed class PvpOptions
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 7004;
    public int ChannelNumber { get; set; } = 51;
    public string Name { get; set; } = "Duel Channel";
    public string RelayHost { get; set; } = "";
    public uint RelayTcpPort { get; set; }
    public uint RelayUdpPort { get; set; }

    public void Validate(ServerOptions options)
    {
        if (!Enabled) return;
        if (options.Processes.Channel.Enabled) throw new InvalidOperationException("PvP requires the shared-store single process deployment.");
        if (Port is < 1 or > 65535 || Port == options.Channel.Port || Port == options.Entrance.Port
            || Port == options.Admin.Port || (options.GameProbe.Enabled && Port == options.GameProbe.Port))
            throw new InvalidOperationException("PvP TCP port conflicts with another listener or is outside 1..65535.");
        if (ChannelNumber != 51 || options.Channel.ChannelNumber == 51)
            throw new InvalidOperationException("The verified ACT1 PvP category requires a dedicated channel 51.");
        if (RelayHost.Length == 0)
        {
            if (RelayTcpPort != 0 || RelayUdpPort != 0) throw new InvalidOperationException("Fallback relay ports require an IPv4 host.");
        }
        else if (!IPAddress.TryParse(RelayHost, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || RelayHost.Length > 15 || RelayTcpPort is 0 or > 65535 || RelayUdpPort is 0 or > 65535)
            throw new InvalidOperationException("Fallback relay requires a reachable IPv4 address and TCP/UDP ports.");
        _ = EntranceMetadata.BuildLocalChannels(PvpChannelConfiguration.Channels(options));
    }
}

internal static class PvpChannelConfiguration
{
    internal static int[] Ports(ServerOptions o) => o.Pvp.Enabled ? [o.Channel.Port, o.Pvp.Port] : [o.Channel.Port];
    internal static EntranceChannel[] Channels(ServerOptions o)
    {
        var host = EntranceService.ResolveChannelDialHost(o);
        var normal = new EntranceChannel(o.Channel.Name, host, o.Channel.Port, o.Channel.ChannelNumber, o.Channel.MaximumUsers, o.Channel.CurrentUsers);
        return o.Pvp.Enabled ? [normal, new(o.Pvp.Name, host, o.Pvp.Port, o.Pvp.ChannelNumber, o.Channel.MaximumUsers, o.Channel.CurrentUsers)] : [normal];
    }
}
