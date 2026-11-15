using System.Text;
using DFLegacy.Protocol;
using Microsoft.AspNetCore.Mvc;

namespace DFLegacy.Server;

internal static class ClientGuildEndpoints
{
    private const string CookieName = "dflegacy_guild";
    private const string AccountKey = "clientGuildAccount";
    private const string SessionPath = "/api/client-guild/session";
    private const string RequestHeader = "X-DFLegacy-Guild-Request";
    private static readonly (string Path, string File, string ContentType)[] Assets =
    [
        ("/client/guild", "guild.htm", "text/html; charset=utf-8"),
        ("/client/guild.css", "guild.css", "text/css; charset=utf-8"),
        ("/client/guild.js", "guild.js", "text/javascript; charset=utf-8"),
        ("/client/guild-hero.jpg", "guild-hero.jpg", "image/jpeg"),
        ("/client/guild-tile.jpg", "guild-tile.jpg", "image/jpeg"),
        ("/client/guild-banner.jpg", "guild-banner.jpg", "image/jpeg")
    ];

    internal static bool IsAssetPath(string path) =>
        Assets.Any(asset => asset.Path.Equals(path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

    internal static void MapClientGuild(this WebApplication app)
    {
        foreach (var asset in Assets)
        {
            app.MapGet(asset.Path, (HttpContext http) =>
            {
                SetResponseHeaders(http);
                if (asset.Path == "/client/guild")
                {
                    if (http.Request.Path.Value?.EndsWith('/') == true) return Results.Redirect("../guild");
                    http.Response.Headers["Content-Security-Policy"] =
                        "default-src 'none'; img-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self'; " +
                        "base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
                }
                var stream = typeof(ClientGuildEndpoints).Assembly.GetManifestResourceStream("ClientGuild/" + asset.File);
                return stream is null ? Results.NotFound() : Results.Stream(stream, asset.ContentType);
            });
        }

        var api = app.MapGroup("/api/client-guild").AddEndpointFilter(AuthorizeAsync)
            .WithMetadata(new RequestSizeLimitAttribute(4096));
        api.MapPost("/session", LoginAsync);
        api.MapPost("/logout", Logout);
        api.MapGet("/characters", CharactersAsync);
        api.MapGet("/characters/{characterId:guid}", GuildAsync);
        api.MapPost("/characters/{characterId:guid}/announcement", AnnouncementAsync);
        api.MapPost("/characters/{characterId:guid}/invite", InviteAsync);
        api.MapPost("/characters/{characterId:guid}/icon", IconAsync);
        api.MapPost("/characters/{characterId:guid}/disband", DisbandAsync);
        api.MapPost("/characters/{characterId:guid}/members/{targetId:guid}/rank",
            (Guid characterId, Guid targetId, RankRequest request, HttpContext http,
                JsonGameStore store, CharacterSessionRegistry sessions, CancellationToken ct) =>
                request.Rank is not (0 or 2 or 3 or 4)
                    ? Task.FromResult<IResult>(Error(400, "请选择有效的成员权限。"))
                    : ManageMemberAsync(characterId, targetId, GameProtocolEngine.GuildSetMemberRankCommand,
                        request.Rank.Value, http, store, sessions, ct));
        api.MapPost("/characters/{characterId:guid}/members/{targetId:guid}/remove",
            (Guid characterId, Guid targetId, HttpContext http, JsonGameStore store,
                CharacterSessionRegistry sessions, CancellationToken ct) =>
                ManageMemberAsync(characterId, targetId, GameProtocolEngine.GuildSecedeCommand, 0, http, store, sessions, ct));
        api.MapPost("/characters/{characterId:guid}/members/{targetId:guid}/transfer",
            (Guid characterId, Guid targetId, HttpContext http, JsonGameStore store,
                CharacterSessionRegistry sessions, CancellationToken ct) =>
                ManageMemberAsync(characterId, targetId, GameProtocolEngine.GuildDelegateMasterCommand, 0, http, store, sessions, ct));
    }

    private static ValueTask<object?> AuthorizeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var http = invocation.HttpContext;
        SetResponseHeaders(http);
        var request = http.Request;
        if (HttpMethods.IsPost(request.Method)
            && (!request.HasJsonContentType() || request.Headers[RequestHeader] != "1" || !HasAllowedOrigin(request)))
            return ValueTask.FromResult<object?>(Error(403, "请通过公会主页操作。"));
        if (HttpMethods.IsPost(request.Method) && string.Equals(request.Path.Value?.TrimEnd('/'), SessionPath,
                StringComparison.OrdinalIgnoreCase))
            return next(invocation);

        var auth = http.RequestServices.GetRequiredService<AdminAuthService>();
        var account = auth.ValidateGuildSession(request.Cookies[CookieName]);
        if (account is null) return ValueTask.FromResult<object?>(Error(401, "请先登录游戏账号。"));
        http.Items[AccountKey] = account.UserName;
        return next(invocation);
    }

    private static bool HasAllowedOrigin(HttpRequest request)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"];
        if (fetchSite.Count > 0 && fetchSite != "same-origin") return false;
        var origins = request.Headers.Origin;
        if (origins.Count == 0) return true; // The launcher checks the local origin before proxying.
        return origins.Count == 1 && Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
            && origin.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase)
            && origin.IdnHost.Trim('[', ']').Equals(request.Host.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase)
            && origin.Port == (request.Host.Port ?? (request.IsHttps ? 443 : 80))
            && origin.UserInfo.Length == 0 && origin.PathAndQuery == "/" && origin.Fragment.Length == 0;
    }

    private static async Task<IResult> LoginAsync(AdminLoginRequest request, JsonGameStore store, AdminAuthService auth,
        HttpContext http, CancellationToken ct)
    {
        var name = request.UserName?.Trim();
        if (name is not { Length: > 0 and <= 32 } || request.Password is not { Length: > 0 and <= 128 })
            return Error(400, "请输入游戏账号和密码。");
        var source = http.Connection.RemoteIpAddress?.ToString();
        if (auth.IsSourceThrottled(source) || auth.IsAccountThrottled(name))
            return Error(429, "尝试过于频繁，请稍后再试。");
        auth.RegisterAttempt(source);
        var login = await store.AuthenticateWebAsync(name, request.Password, auth, ct, guildOnly: true);
        if (login is null)
        {
            auth.RegisterFailure(name, source);
            return Error(401, "账号或密码错误。");
        }
        if (auth.ValidateGuildSession(http.Request.Cookies[CookieName]) is not null)
            auth.Logout(http.Request.Cookies[CookieName]);
        http.Response.Cookies.Append(CookieName, login.Token!, CookiePolicy(http, login.ExpiresAt));
        return Results.Ok(new { userName = login.UserName });
    }

    private static IResult Logout(HttpContext http, AdminAuthService auth)
    {
        auth.Logout(http.Request.Cookies[CookieName]);
        http.Response.Cookies.Delete(CookieName, CookiePolicy(http));
        return Results.NoContent();
    }

    private static async Task<IResult> CharactersAsync(HttpContext http, JsonGameStore store, CancellationToken ct)
    {
        var characters = await store.GetCharactersAsync(Account(http), ct);
        return Results.Ok(characters.OrderBy(character => character.Slot).Select(character =>
            new { character.Id, character.Name, character.Level, character.GuildName }));
    }

    private static async Task<IResult> GuildAsync(Guid characterId, HttpContext http, JsonGameStore store,
        CharacterSessionRegistry sessions, CancellationToken ct)
    {
        var character = await store.GetCharacterAsync(Account(http), characterId, ct);
        if (character is null) return Error(404, "找不到该账号下的角色。");
        var snapshot = await store.GetGuildAsync(character.GuildId, ct);
        var member = snapshot.Members.FirstOrDefault(row => row.Id == characterId);
        if (snapshot.Guild is not { } guild || member is null)
            return Results.Ok(new { guild = (object?)null, character.Name });

        var master = guild.FounderCharacterId == member.Id && member.GuildRank == 1;
        return Results.Ok(new
        {
            character.Name,
            rank = member.GuildRank,
            canEditAnnouncement = master || member.GuildRank == 2,
            canInvite = JsonGameStore.CanInviteGuildMember(member, guild),
            canSetIcon = master && guild.Level >= GuildRules.IconMinimumGuildLevel,
            canDisband = master,
            guild = new { guild.Id, guild.Name, guild.Address, guild.Announcement, guild.Level, guild.Experience,
                guild.SkillPoints, guild.IconPixels },
            members = snapshot.Members.OrderBy(row => Priority(row.GuildRank)).ThenBy(row => row.Name, StringComparer.Ordinal)
                .Select(row => new
                {
                    row.Id, row.Name, row.Level, row.Job, rank = row.GuildRank, online = sessions.IsOnline(row.Id),
                    canSetRank = row.Id != member.Id && row.GuildRank != 1
                        && (master || member.GuildRank == 2 && row.GuildRank != 2),
                    canRemove = row.Id != member.Id && row.GuildRank != 1
                        && (master || member.GuildRank is 2 or 3 && Priority(row.GuildRank) > Priority(member.GuildRank)),
                    canTransfer = master && row.GuildRank == 2 && row.Id != member.Id
                })
        });
    }

    private static async Task<IResult> InviteAsync(Guid characterId, InviteRequest request, HttpContext http,
        JsonGameStore store, CharacterSessionRegistry sessions, CancellationToken ct)
    {
        var target = request.TargetName?.Trim();
        if (string.IsNullOrEmpty(target) || target.Any(char.IsControl))
            return Error(400, "请输入要邀请的角色名。");
        try
        {
            if (GuildRules.Encode(target).Length > 30)
                return Error(400, "角色名过长。");
        }
        catch (EncoderFallbackException) { return Error(400, "角色名包含客户端无法显示的字符。"); }

        var account = Account(http);
        var character = await store.GetCharacterAsync(account, characterId, ct);
        if (character is null) return Error(404, "找不到该账号下的角色。");
        var error = await GuildSession.SendInvitationAsync(account, character, target, store, sessions, ct, request.GuildId);
        return error switch
        {
            0 => Results.NoContent(),
            3 => Error(404, "找不到该在线角色，请检查角色名并让对方进入游戏。"),
            17 => Error(409, "暂时无法邀请该角色，请稍后重试。"),
            38 => Error(409, "公会成员人数已达上限。"),
            39 => Error(409, "该角色已加入公会。"),
            77 => Error(403, "无法向该角色发送邀请。"),
            _ => GuildError(error)
        };
    }

    private static async Task<IResult> IconAsync(Guid characterId, IconRequest request, HttpContext http,
        JsonGameStore store, CancellationToken ct)
    {
        if (request.Pixels is not { Length: GuildRules.IconSize * GuildRules.IconSize * 4 } pixels)
            return Error(400, "请选择有效的公会图标。");
        var error = await store.SetGuildIconAsync(Account(http), characterId, request.GuildId, pixels, ct);
        if (error == 37) return Error(403, "公会达到 2 级后可以设置图标。");
        return error == 0 ? Results.NoContent() : GuildError(error);
    }

    private static async Task<IResult> DisbandAsync(Guid characterId, DisbandRequest request, HttpContext http,
        JsonGameStore store, CharacterSessionRegistry sessions, CancellationToken ct)
    {
        var account = Account(http);
        var character = await store.GetCharacterAsync(account, characterId, ct);
        if (character is null || request.GuildId == 0 || character.GuildId != request.GuildId)
            return GuildError(34);
        var snapshot = await store.GetGuildAsync(request.GuildId, ct);
        if (snapshot.Guild is not { } guild) return GuildError(34);
        if (guild.FounderCharacterId != characterId || character.GuildRank != 1) return GuildError(36);
        if (!string.Equals(guild.Name, request.ConfirmName, StringComparison.Ordinal))
            return Error(400, "请输入完整的公会名称以确认解散。");

        var dissolution = await store.DissolveGuildAsync(account, characterId, guild.Id, ct);
        if (dissolution is null) return GuildError(17);
        sessions.NotifyGuildDissolved(dissolution.Founder.Id, guild.Id);
        foreach (var member in dissolution.OtherMembers)
            sessions.NotifyGuildDissolved(member, guild.Id);
        return Results.NoContent();
    }

    private static async Task<IResult> AnnouncementAsync(Guid characterId, AnnouncementRequest request, HttpContext http,
        JsonGameStore store, CharacterSessionRegistry sessions, CancellationToken ct)
    {
        var account = Account(http);
        var character = await store.GetCharacterAsync(account, characterId, ct);
        if (character is null) return Error(404, "找不到该账号下的角色。");
        if (request.Announcement is null) return Error(400, "请输入公会公告。");
        byte[] announcement;
        try { announcement = GuildRules.Encode(request.Announcement); }
        catch (EncoderFallbackException) { return Error(400, "公告含有客户端无法显示的字符。"); }
        var result = await store.SetGuildAnnouncementAsync(account, characterId, character.GuildId, announcement, ct);
        if (result.Error != 0) return GuildError(result.Error);
        foreach (var id in result.Members ?? [])
            sessions.NotifyGuildAnnouncementChanged(id, character.GuildId);
        return Results.NoContent();
    }

    private static async Task<IResult> ManageMemberAsync(Guid characterId, Guid targetId, byte command, byte requestedRank,
        HttpContext http, JsonGameStore store, CharacterSessionRegistry sessions, CancellationToken ct)
    {
        var account = Account(http);
        var character = await store.GetCharacterAsync(account, characterId, ct);
        if (character is null) return Error(404, "找不到该账号下的角色。");
        var snapshot = await store.GetGuildAsync(character.GuildId, ct);
        var target = snapshot.Members.FirstOrDefault(member => member.Id == targetId);
        if (target is null) return Error(404, "该角色不在公会中。");
        var change = await store.ChangeGuildMembershipAsync(account, characterId, character.GuildId, command,
            target.Name, requestedRank, sessions.IsOnline, ct, allowOfflineRemoval: true, expectedTargetId: targetId);
        if (change.Error != 0) return GuildError(change.Error);
        if (change.Changed) PublishMembershipChange(command, change, sessions);
        return Results.NoContent();
    }

    private static void PublishMembershipChange(byte command, GuildMembershipChange change, CharacterSessionRegistry sessions)
    {
        var guild = change.Guild!;
        var members = change.Members!;
        var target = change.Target!;
        var roster = GuildSession.CreateRosterPacket(GameProtocolEngine.GuildMembersCommand, guild, members, sessions);
        if (command == GameProtocolEngine.GuildSecedeCommand)
        {
            var departure = GameProtocolEngine.CreateGuildMemberDeparture(guild.Id, target.CharacterNo, 2,
                GuildRules.Encode(guild.Name), GuildRules.Encode(target.Name));
            foreach (var member in members)
                sessions.NotifyGuildEvents(member.Id, guild.Id, [departure, roster]);
            sessions.NotifyGuildEvents(target.Id, 0,
                [GameProtocolEngine.CreateGuildDeparture(2), GameProtocolEngine.CreateGuildSelfRank(5)], refreshCharacter: true);
            return;
        }
        if (command == GameProtocolEngine.GuildSetMemberRankCommand)
        {
            foreach (var member in members.Where(row => row.Id != target.Id))
                sessions.NotifyGuildEvents(member.Id, guild.Id, [roster]);
            sessions.NotifyGuildEvents(target.Id, guild.Id, [GameProtocolEngine.CreateGuildSelfRank(target.GuildRank),
                GameProtocolEngine.CreateGuildMemberRank(target.GuildRank),
                GuildSession.CreateInfoPacket(guild, target.GuildRank, members.Length), roster], refreshCharacter: true);
            return;
        }

        var actor = change.Actor!;
        var newMaster = GameProtocolEngine.CreateGuildMasterDelegated(GuildRules.Encode(target.Name));
        foreach (var member in members.Where(row => row.Id != actor.Id && row.Id != target.Id))
            sessions.NotifyGuildEvents(member.Id, guild.Id, [newMaster, roster]);
        sessions.NotifyGuildEvents(actor.Id, guild.Id, [newMaster, GameProtocolEngine.CreateGuildSelfRank(actor.GuildRank),
            GameProtocolEngine.CreateGuildMemberRank(actor.GuildRank),
            GuildSession.CreateInfoPacket(guild, actor.GuildRank, members.Length), roster], refreshCharacter: true);
        sessions.NotifyGuildEvents(target.Id, guild.Id, [GameProtocolEngine.CreateGuildSelfRank(target.GuildRank),
            GuildSession.CreateInfoPacket(guild, target.GuildRank, members.Length), GameProtocolEngine.CreateGuildMasterAppointed(), roster],
            refreshCharacter: true);
    }

    private static int Priority(byte rank) => rank == 0 ? 5 : rank;
    private static string Account(HttpContext http) => (string)http.Items[AccountKey]!;
    private static IResult Error(int status, string error) => Results.Json(new { error }, statusCode: status);
    private static IResult GuildError(byte error) => error switch
    {
        3 or 34 => Error(404, "未找到仍属于该公会的角色。"),
        36 or 83 or 85 or 86 or 87 or 88 => Error(403, "当前公会权限不能执行该操作。"),
        89 => Error(409, "副会长已达到人数上限。"),
        108 => Error(400, "公告须占用 1–100 字节，并使用游戏可显示的字符。"),
        _ => Error(503, "未能保存公会变更，请刷新后再试。")
    };

    private static CookieOptions CookiePolicy(HttpContext http, DateTimeOffset? expiry = null) => new()
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiry
    };

    private static void SetResponseHeaders(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["X-Frame-Options"] = "DENY";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private sealed record AnnouncementRequest(string? Announcement);
    private sealed record RankRequest(byte? Rank);
    private sealed record InviteRequest(uint GuildId, string? TargetName);
    private sealed record IconRequest(uint GuildId, byte[]? Pixels);
    private sealed record DisbandRequest(uint GuildId, string? ConfirmName);
}
