using System.Net;
using System.Net.Sockets;

namespace DFLegacy.Server;

internal static class DualStackUdp
{
    /// <summary>
    /// Binds a UDP socket for the datagram listeners.
    /// <para>
    /// <see cref="UdpClient"/> binds inside its constructor and never enables
    /// dual-stack, so an <c>IPAddress.IPv6Any</c> ("::") bind would accept IPv6
    /// peers only — unlike <see cref="TcpListener"/> and Kestrel, which do accept
    /// IPv4-mapped peers on the same address. That asymmetry makes a "::" host
    /// silently unreachable for IPv4 clients (and for Docker's UDP port
    /// forwarding). Create the socket unbound, turn dual-stack on, then bind.
    /// </para>
    /// </summary>
    public static UdpClient Bind(IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var client = new UdpClient(endpoint.AddressFamily);
        try
        {
            if (endpoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                client.Client.DualMode = true;
            }

            client.Client.Bind(endpoint);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
