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
        var reserved = options.Entrance.AdditionalPorts.Append(options.Channel.Port).Append(options.Entrance.Port)
            .Append(options.Admin.Port)
            .Concat(options.Channel.RegionalChannelsEnabled ? Enumerable.Range(7100, 6) : [])
            .Concat(options.GameProbe.Enabled ? [options.GameProbe.Port] : [])
            .Concat(options.FallbackRelay.Enabled ? [options.FallbackRelay.TcpPort] : [])
            .Concat(options.WebPanels.Gm.Enabled ? [options.WebPanels.Gm.Port] : [])
            .Concat(options.WebPanels.Player.Enabled ? [options.WebPanels.Player.Port] : []);
        var ports = PvpChannelCatalog.Profiles(this).Select(c => c.Port).ToArray();
        if (ports.Any(p => p is < 1 or > 65535) || ports.Distinct().Count() != ports.Length || ports.Intersect(reserved).Any())
            throw new InvalidOperationException("PvP TCP port conflicts with another listener or is outside 1..65535.");
        if (ChannelNumber != 51 || options.Channel.ServerNumber != 1
            || PvpChannelCatalog.Profiles(this).Any(c => c.Number == options.Channel.ChannelNumber))
            throw new InvalidOperationException("ACT1 PvP requires dedicated channels 51,52,53,55 in server group 1.");
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
    // ACT1 server-group 1 channel IDs, one per PVF dungeon category.
    private static readonly (int Number, string Name)[] RegionalChannels =
    [
        (1, "Elven Guard"), (10, "Grand Flores"), (15, "Sky Castle"),
        (20, "Behemoth"), (23, "Storm Pass"), (24, "Alfhlyra"), (27, "North Myre")
    ];

    internal static IEnumerable<(int Number, string Name, int Port)> GameplayChannels(ServerOptions o)
    {
        if (o.Channel.RegionalChannelsEnabled)
        {
            for (var i = 0; i < RegionalChannels.Length; i++)
            {
                var (number, name) = RegionalChannels[i];
                yield return (number, name, i == 0 ? o.Channel.Port : 7099 + i);
            }
        }
        else yield return (o.Channel.ChannelNumber, o.Channel.Name, o.Channel.Port);

        foreach (var c in PvpChannelCatalog.Profiles(o.Pvp)) yield return (c.Number, c.Name, c.Port);
    }

    internal static int[] Ports(ServerOptions o) => GameplayChannels(o).Select(channel => channel.Port).ToArray();

    internal static EntranceChannel[] Channels(ServerOptions o)
    {
        var host = EntranceService.ResolveChannelDialHost(o);
        return GameplayChannels(o).Select(c => new EntranceChannel(c.Name, host, c.Port, c.Number,
            o.Channel.MaximumUsers, o.Channel.CurrentUsers)).ToArray();
    }

    internal static void ValidateRegionalChannels(ServerOptions o)
    {
        if (!o.Channel.RegionalChannelsEnabled) return;
        if (o.Processes.Channel.Enabled || o.Channel.ServerNumber != 1)
            throw new InvalidOperationException("Regional channels require the shared-store single process and ACT1 server group 1.");
        var reserved = o.Entrance.AdditionalPorts.Append(o.Entrance.Port).Append(o.Admin.Port)
            .Concat(o.GameProbe.Enabled ? [o.GameProbe.Port] : Array.Empty<int>())
            .Concat(o.FallbackRelay.Enabled ? [o.FallbackRelay.TcpPort] : Array.Empty<int>())
            .Concat(o.WebPanels.Gm.Enabled ? [o.WebPanels.Gm.Port] : Array.Empty<int>())
            .Concat(o.WebPanels.Player.Enabled ? [o.WebPanels.Player.Port] : Array.Empty<int>());
        var ports = Ports(o);
        if (ports.Distinct().Count() != ports.Length || ports.Skip(1).Take(6).Intersect(reserved).Any())
            throw new InvalidOperationException("Regional channel TCP ports 7100–7105 conflict with another listener.");
        _ = EntranceMetadata.BuildLocalChannels(Channels(o));
    }
}
