using System.Collections.Concurrent;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>ExecuteAsync runs in the background in .NET 10; HTTP alone is not game readiness.</summary>
public sealed class ListenerReadiness
{
    private readonly string[] _required;
    private readonly ConcurrentDictionary<string, byte> _listening = new(StringComparer.Ordinal);

    public ListenerReadiness(ServerOptions options)
    {
        var required = new List<string>();
        var (entrance, channel) = EntrancePortFamilies.Split(options.Entrance.Port,
            options.Entrance.AdditionalPorts, PvpChannelConfiguration.Ports(options));
        if (options.Entrance.Enabled && !options.Processes.Channel.Enabled && entrance.Length > 0) required.Add("entrance");
        if (options.Entrance.Enabled && channel.Length > 0) required.Add("channel");
        if (options.CharacterDatagram.Enabled) required.Add("character");
        if (options.GameplayDatagram.Enabled) required.Add("gameplay");
        if (options.FallbackRelay.Enabled) required.Add("fallback");
        if (options.GameProbe.Enabled) required.Add("probe");
        _required = required.ToArray();
    }

    public string[] Pending => _required.Where(name => !_listening.ContainsKey(name)).Order(StringComparer.Ordinal).ToArray();
    public bool IsReady => _required.All(_listening.ContainsKey);
    public void MarkReady(string name) => _listening[name] = 0;
    public void MarkStopped(string name) => _listening.TryRemove(name, out _);
}
