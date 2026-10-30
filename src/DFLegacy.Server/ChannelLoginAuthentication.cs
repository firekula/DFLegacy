using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed record ChannelLoginIdentity(AuthenticatedAccount Account, RelayAccountAdmission? Admission);

internal static class ChannelLoginAuthentication
{
    internal static async Task<ChannelLoginIdentity?> AuthenticateAsync(JsonGameStore store,
        GameplayDatagramService contexts, Guid sessionId, string userName, string? password,
        CancellationToken cancellationToken = default, bool characterAttached = false)
    {
        if (characterAttached || contexts.HasFallbackRelayContext(sessionId)) return null;
        var account = password is not null
            ? await store.AuthenticateAsync(userName, password, cancellationToken)
            : await store.FindAccountAsync(userName, cancellationToken);
        if (account is null || contexts.HasFallbackRelayContext(sessionId)) return null;
        // A successful legacy name lookup may switch this channel's account,
        // but it cannot authenticate or displace another channel's relay owner.
        contexts.RevokeRelayAccount(sessionId);
        contexts.BindChannelAccountIdentity(sessionId, account.AccountUid);
        var admission = password is not null ? contexts.AuthenticateRelayAccount(sessionId, account.AccountUid) : null;
        return new(account, admission);
    }

    internal static GameServerPacket CreateReply(ServerOptions options, GameplayDatagramService contexts,
        ChannelLoginIdentity identity, bool isPvp, FallbackRelayService? relay)
    {
        (string Host, uint TcpPort, uint UdpPort) endpoint = ("", 0, 0);
        if (identity.Admission is { } admission && contexts.IsCurrentRelayAdmission(admission))
        {
            if (isPvp && options.Pvp.RelayHost.Length > 0)
                endpoint = (options.Pvp.RelayHost, options.Pvp.RelayTcpPort, options.Pvp.RelayUdpPort);
            else if (relay?.AdvertisedEndpoint is { } local) endpoint = local;
        }
        return isPvp
            ? GameProtocolEngine.CreatePvpLoginSuccess(identity.Account.AccountUid, endpoint.Host, endpoint.TcpPort, endpoint.UdpPort)
            : GameProtocolEngine.CreateChannelLoginSuccess(identity.Account.AccountUid, endpoint.Host, endpoint.TcpPort, endpoint.UdpPort);
    }
}
