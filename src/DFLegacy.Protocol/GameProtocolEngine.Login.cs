using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DFLegacy.Protocol;

public static partial class GameProtocolEngine
{
    /// <summary>Complete ACT1 mode0 login tail; live callers supply the authenticated UID.</summary>
    public static GameServerPacket CreateChannelLoginSuccess(uint accountUid, string relayIPv4,
        uint relayTcpPort, uint relayUdpPort) =>
        CreateLoginSuccessCore(accountUid, 0, relayIPv4, relayTcpPort, relayUdpPort);

    private static GameServerPacket CreateLoginSuccessCore(uint accountUid, byte mode,
        string relayIPv4, uint relayTcpPort, uint relayUdpPort)
    {
        ArgumentNullException.ThrowIfNull(relayIPv4);
        if (relayIPv4.Length == 0)
        {
            if (relayTcpPort != 0 || relayUdpPort != 0)
                throw new ArgumentException("Unavailable fallback relay must have zero ports.");
        }
        else if (relayIPv4.Length > 15 || relayIPv4.Any(c => c > 127)
            || !IPAddress.TryParse(relayIPv4, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || relayTcpPort is 0 or > ushort.MaxValue || relayUdpPort is 0 or > ushort.MaxValue)
        {
            throw new ArgumentException("Fallback relay requires an ASCII IPv4 address and valid TCP and UDP ports.");
        }

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write(new byte[] { 1, 0, 0, mode, 0 });
        writer.Write(accountUid);
        var host = Encoding.ASCII.GetBytes(relayIPv4);
        writer.Write((uint)host.Length);
        writer.Write(host);
        writer.Write(relayTcpPort);
        writer.Write(relayUdpPort);
        writer.Write((byte)1); // Existing native unknown-sex fallback.
        writer.Write((byte)0); // No reliable-person profile in the local model.
        return new(CommandPacketType, LoginCommand, output.ToArray());
    }
}
