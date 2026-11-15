using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoGuildWebSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-c2-guild-web-");
        try
        {
            await CheckRoutesAsync(root.FullName, check);
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static async Task CheckRoutesAsync(string root, Action<bool, string> check)
    {
        var options = new ServerOptions { DataPath = Path.Combine(root, "state.json"), SeedTestAccount = false };
        var store = NewStore();
        await store.InitializeAsync();
        var owner = await AddAsync("webowner", "WebOwner");
        var member = await AddAsync("webmember", "WebMember");
        var deputy = await AddAsync("webdeputy", "WebDeputy");
        var offline = await AddAsync("weboffline", "WebOffline");
        var outsider = await AddAsync("weboutsider", "WebOutsider");
        await store.SaveCharacterItemSpacesAsync("webowner", owner.Id, 600000, [], [], []);
        var guild = (await store.CreateGuildAsync("webowner", owner.Id, "WebGuild", "webguild", 300000)).Guild!;
        var seeded = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
        seeded["Guilds"]!.AsArray().Single()!["Level"] = 2;
        foreach (var state in seeded["Accounts"]!.AsArray().SelectMany(account => account!["Characters"]!.AsArray()))
        {
            var id = state!["Id"]!.GetValue<Guid>();
            if (id != member.Id && id != deputy.Id && id != offline.Id) continue;
            state["GuildId"] = guild.Id;
            state["GuildName"] = guild.Name;
            state["GuildRank"] = id == deputy.Id ? 2 : 4;
        }
        await File.WriteAllTextAsync(options.DataPath, seeded.ToJsonString());
        store = NewStore();
        await store.InitializeAsync();
        var sessions = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
        var auth = new AdminAuthService(options, TimeProvider.System);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(sessions);
        builder.Services.AddSingleton(auth);
        await using var app = builder.Build();
        app.MapClientGuild();
        await app.StartAsync();
        var origin = new Uri(app.Urls.Single());
        using var publicClient = NewClient(origin);
        using var page = await publicClient.GetAsync("client/guild");
        check(page.IsSuccessStatusCode && (await page.Content.ReadAsStringAsync()).Contains("公会", StringComparison.Ordinal),
            "Chapter Two serves a real local guild management page");
        if (!page.IsSuccessStatusCode) return;

        check(AdminAccessPolicy.Resolve("GET", "/api/client-guild/characters").Access == AdminAccess.Public
            && AdminAccessPolicy.Resolve("POST", "/api/client-guild-fake/session").Access == AdminAccess.Admin,
            "only the exact dedicated guild namespace reaches the scoped route authentication");
        foreach (var asset in new[] { "guild.css", "guild.js", "guild-hero.jpg", "guild-tile.jpg", "guild-banner.jpg" })
        {
            using var file = await publicClient.GetAsync("client/" + asset);
            check(file.IsSuccessStatusCode && (await file.Content.ReadAsByteArrayAsync()).Length > 0,
                $"guild page asset {asset} is served locally");
        }
        var ownerPath = $"api/client-guild/characters/{owner.Id}";
        var memberPath = $"api/client-guild/characters/{member.Id}";
        using var anonymous = await publicClient.GetAsync(ownerPath);
        check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "anonymous callers cannot read guild data");
        using var noHeader = await publicClient.PostAsJsonAsync("api/client-guild/session", new { userName = "webowner", password = "test-password" });
        check(noHeader.StatusCode == HttpStatusCode.Forbidden, "guild mutations require the same-origin browser request header");
        using var hostile = await PostAsync(publicClient, "api/client-guild/session",
            new { userName = "webowner", password = "test-password" }, "https://other.invalid");
        check(hostile.StatusCode == HttpStatusCode.Forbidden, "guild login rejects a cross-origin request");

        var ownerCookies = new CookieContainer();
        using var ownerClient = NewClient(origin, ownerCookies);
        using var memberClient = NewClient(origin, new CookieContainer());
        using var ownerLogin = await PostAsync(ownerClient, "api/client-guild/session",
            new { userName = "webowner", password = "test-password" });
        using var memberLogin = await PostAsync(memberClient, "api/client-guild/session",
            new { userName = "webmember", password = "test-password" });
        check(ownerLogin.IsSuccessStatusCode && memberLogin.IsSuccessStatusCode,
            "guild login validates real account credentials and issues scoped sessions");
        if (!ownerLogin.IsSuccessStatusCode || !memberLogin.IsSuccessStatusCode) return;
        var cookie = ownerCookies.GetCookies(origin).Cast<Cookie>().Single();
        check(cookie.HttpOnly && ownerLogin.Headers.GetValues("Set-Cookie").Single().Contains("samesite=strict", StringComparison.OrdinalIgnoreCase)
            && auth.Validate(cookie.Value) is null, "guild cookie is HttpOnly and never authorizes the normal admin API");
        var broadToken = auth.IssueToken("webowner", AccountRole.Admin).Token!;
        var wrongScope = new CookieContainer();
        wrongScope.Add(origin, new Cookie(cookie.Name, broadToken, "/"));
        using (var broadClient = NewClient(origin, wrongScope))
        using (var rejectedAdminToken = await broadClient.GetAsync(ownerPath))
            check(rejectedAdminToken.StatusCode == HttpStatusCode.Unauthorized,
                "the guild website accepts its purpose-scoped cookie rather than a generic admin token");
        using var foreign = await memberClient.GetAsync(ownerPath);
        check(foreign.StatusCode == HttpStatusCode.NotFound, "a member session cannot impersonate another character");
        using var unowned = await ownerClient.GetAsync($"api/client-guild/characters/{outsider.Id}");
        check(unowned.StatusCode == HttpStatusCode.NotFound, "the owner account cannot select another account's character");
        var list = await ownerClient.GetStringAsync("api/client-guild/characters");
        check(list.Contains(owner.Name, StringComparison.Ordinal) && !list.Contains(member.Name, StringComparison.Ordinal),
            "character selection lists only the authenticated account");

        using var roster = JsonDocument.Parse(await ownerClient.GetStringAsync(ownerPath));
        check(roster.RootElement.GetProperty("members").GetArrayLength() == 4,
            "page roster contains actual members of the selected character's guild");
        byte[] pixels = new byte[16 * 16 * 4];
        pixels[0] = 240;
        pixels[3] = 255;
        var iconChange = new { guildId = guild.Id, pixels };
        using var deniedIcon = await PostAsync(memberClient, memberPath + "/icon", iconChange);
        using var badIcon = await PostAsync(ownerClient, ownerPath + "/icon", new { guildId = guild.Id, pixels = new byte[3] });
        using var savedIcon = await PostAsync(ownerClient, ownerPath + "/icon", iconChange);
        var updatedView = JsonNode.Parse(await ownerClient.GetStringAsync(ownerPath))!;
        check(deniedIcon.StatusCode == HttpStatusCode.Forbidden && badIcon.StatusCode == HttpStatusCode.BadRequest
            && savedIcon.IsSuccessStatusCode
            && (string?)updatedView["guild"]?["iconPixels"] == Convert.ToBase64String(pixels),
            "only the eligible president can store a valid 16×16 icon and the guild page reads it back");
        using var denied = await PostAsync(memberClient, memberPath + "/announcement", new { announcement = "unauthorized" });
        check(denied.StatusCode == HttpStatusCode.Forbidden, "a regular member cannot publish a guild announcement");
        var announcementNotified = false;
        var notices = new List<(uint Guild, GameServerPacket Packet, bool Refresh)>();
        var peerNotices = new List<GameServerPacket>();
        var lease = await sessions.ClaimAsync(member.CharacterNo, member.Id, Guid.NewGuid(), () => { }, default,
            guildAnnouncementChanged: id => announcementNotified = id == guild.Id,
            guildEvents: (id, packets, refresh) => notices.AddRange(packets.Select(packet => (id, packet, refresh))));
        var peerLease = await sessions.ClaimAsync(deputy.CharacterNo, deputy.Id, Guid.NewGuid(), () => { }, default,
            guildEvents: (_, packets, _) => peerNotices.AddRange(packets));
        try
        {
            var invitations = new List<GameServerPacket>();
            var guestLease = await sessions.ClaimAsync(outsider.CharacterNo, outsider.Id, Guid.NewGuid(), () => { }, default,
                guildEvents: (_, packets, _) => invitations.AddRange(packets));
            try
            {
                var invite = new { guildId = guild.Id, targetName = outsider.Name };
                using var deniedInvite = await PostAsync(memberClient, memberPath + "/invite", invite);
                using var staleInvite = await PostAsync(ownerClient, ownerPath + "/invite",
                    new { guildId = guild.Id + 1, targetName = outsider.Name });
                using var sent = await PostAsync(ownerClient, ownerPath + "/invite", invite);
                check(deniedInvite.StatusCode == HttpStatusCode.Forbidden && staleInvite.StatusCode == HttpStatusCode.NotFound
                    && sent.IsSuccessStatusCode && invitations.Count == 1
                    && invitations[0].ProtocolId == GameProtocolEngine.GuildInvitationNotification
                    && (await store.GetCharacterAsync("weboutsider", outsider.Id))?.GuildId == 0,
                    "an offline president can invite an online player from the current guild without enrolling them early");
                var accepted = await store.ResolveGuildInvitationAsync("weboutsider", outsider.Id, true,
                    sessions.GetGuildSessionId, default);
                check(accepted.Error == 0 && accepted.Member?.GuildId == guild.Id
                    && (await store.GetCharacterAsync("weboutsider", outsider.Id))?.GuildId == guild.Id,
                    "accepting the native invitation created by the web page persists guild membership");
            }
            finally { sessions.Release(guestLease); }

            using var unsafeText = await PostAsync(ownerClient, ownerPath + "/announcement", new { announcement = new string('公', 51) });
            check(unsafeText.StatusCode == HttpStatusCode.BadRequest, "announcement enforces the original 100-byte CP936 limit");
            using var posted = await PostAsync(ownerClient, ownerPath + "/announcement", new { announcement = "今晚集合" });
            check(posted.IsSuccessStatusCode && (await store.GetGuildAsync(guild.Id)).Guild?.Announcement == "今晚集合"
                && announcementNotified,
                "web announcement writes canonical state and notifies the connected game session");
            using var ranked = await PostAsync(ownerClient, ownerPath + $"/members/{member.Id}/rank", new { rank = 2 });
            check(ranked.IsSuccessStatusCode && (await store.GetCharacterAsync("webmember", member.Id))?.GuildRank == 2
                && HasRankRefresh(guild.Id, 2),
                "role update uses native rank restrictions, persists and refreshes the client");
            check(peerNotices.Any(IsRoster), "the website rank change sends a refreshed game roster to other guild members");

            notices.Clear();
            using var demoted = await PostAsync(ownerClient, ownerPath + $"/members/{member.Id}/rank", new { rank = 4 });
            check(demoted.IsSuccessStatusCode && HasRankRefresh(guild.Id, 4)
                && notices.Any(notice => notice.Refresh && notice.Packet is { Type: 0, ProtocolId: 70 }),
                "web demotion updates the active member's rank, information, and cached skill state");

            notices.Clear();
            peerNotices.Clear();
            using var kicked = await PostAsync(ownerClient, ownerPath + $"/members/{member.Id}/remove", new { });
            check(kicked.IsSuccessStatusCode && HasRankRefresh(0, 5)
                && notices.Any(notice => notice.Refresh && notice.Packet.ProtocolId == GameProtocolEngine.GuildDepartureNotification)
                && peerNotices.Any(IsRoster),
                "web removal clears a connected member's role and benefits and updates the game roster");
        }
        finally
        {
            sessions.Release(lease);
            sessions.Release(peerLease);
        }
        using var removal = await PostAsync(ownerClient, ownerPath + $"/members/{offline.Id}/remove", new { });
        check(removal.IsSuccessStatusCode && (await store.GetCharacterAsync("weboffline", offline.Id))?.GuildId == 0,
            "authorized web removal updates an offline member's durable guild identity");
        using var invalidTransfer = await PostAsync(ownerClient, ownerPath + $"/members/{owner.Id}/transfer", new { });
        check(invalidTransfer.StatusCode == HttpStatusCode.Forbidden, "web transfer requires a different eligible deputy");
        var successorNotices = new List<GameServerPacket>();
        var successorLease = await sessions.ClaimAsync(deputy.CharacterNo, deputy.Id, Guid.NewGuid(), () => { }, default,
            guildEvents: (_, packets, _) => successorNotices.AddRange(packets));
        try
        {
            using var transfer = await PostAsync(ownerClient, ownerPath + $"/members/{deputy.Id}/transfer", new { });
            check(transfer.IsSuccessStatusCode && (await store.GetGuildAsync(guild.Id)).Guild?.FounderCharacterId == deputy.Id
                && (await store.GetCharacterAsync("webowner", owner.Id))?.GuildRank == 2
                && successorNotices.Any(packet => packet.ProtocolId == 139 && packet.Payload is [1])
                && successorNotices.Any(IsRoster),
                "web transfer atomically replaces the founder and refreshes the new leader's game role");
            using var obsolete = await PostAsync(ownerClient, ownerPath + $"/members/{deputy.Id}/rank", new { rank = 4 });
            check(obsolete.StatusCode == HttpStatusCode.Forbidden, "the old founder immediately loses founder permissions");
        }
        finally
        {
            sessions.Release(successorLease);
        }
        var reloaded = NewStore();
        await reloaded.InitializeAsync();
        check((await reloaded.GetGuildAsync(guild.Id)).Guild?.Announcement == "今晚集合"
            && (await reloaded.GetGuildAsync(guild.Id)).Guild?.FounderCharacterId == deputy.Id
            && (await reloaded.GetGuildAsync(guild.Id)).Guild?.IconPixels is { } diskIcon
            && diskIcon.AsSpan().SequenceEqual(pixels)
            && (await reloaded.GetCharacterAsync("webmember", member.Id))?.GuildId == 0,
            "guild website changes survive store restart");

        using var deputyClient = NewClient(origin, new CookieContainer());
        using var deputyLogin = await PostAsync(deputyClient, "api/client-guild/session",
            new { userName = "webdeputy", password = "test-password" });
        var deputyPath = $"api/client-guild/characters/{deputy.Id}";
        var dissolution = new { guildId = guild.Id, confirmName = guild.Name };
        using var noLongerPresident = await PostAsync(ownerClient, ownerPath + "/disband", dissolution);
        using var wrongName = await PostAsync(deputyClient, deputyPath + "/disband",
            new { guildId = guild.Id, confirmName = "wrong name" });
        check(noLongerPresident.StatusCode == HttpStatusCode.Forbidden && wrongName.StatusCode == HttpStatusCode.BadRequest
            && (await store.GetGuildAsync(guild.Id)).Guild is not null,
            "only the current president can disband a guild after confirming its exact name");
        using var persistedView = JsonDocument.Parse(await deputyClient.GetStringAsync(deputyPath));
        check(persistedView.RootElement.GetProperty("guild").TryGetProperty("iconPixels", out var savedPixels)
            && savedPixels.GetBytesFromBase64().AsSpan().SequenceEqual(pixels),
            "the next president sees the guild's saved icon");
        var dissolvedNotices = new List<uint>();
        var presidentLease = await sessions.ClaimAsync(deputy.CharacterNo, deputy.Id, Guid.NewGuid(), () => { }, default,
            guildDissolved: dissolvedNotices.Add);
        var formerOwnerLease = await sessions.ClaimAsync(owner.CharacterNo, owner.Id, Guid.NewGuid(), () => { }, default,
            guildDissolved: dissolvedNotices.Add);
        try
        {
            using var disbanded = await PostAsync(deputyClient, deputyPath + "/disband", dissolution);
            var afterDisband = NewStore();
            await afterDisband.InitializeAsync();
            check(disbanded.IsSuccessStatusCode && dissolvedNotices.SequenceEqual(new[] { guild.Id, guild.Id })
                && (await afterDisband.GetGuildAsync(guild.Id)).Guild is null
                && (await afterDisband.GetCharacterAsync("weboutsider", outsider.Id))?.GuildId == 0,
                "web dissolution notifies both connected characters and persists removal of the guild and all memberships");
        }
        finally
        {
            sessions.Release(presidentLease);
            sessions.Release(formerOwnerLease);
        }
        auth.RevokeUser("webowner");
        using var revoked = await ownerClient.GetAsync(ownerPath);
        check(revoked.StatusCode == HttpStatusCode.Unauthorized, "account revocation also invalidates the guild browser session");

        bool HasRankRefresh(uint expectedGuild, byte rank) => notices.Any(notice =>
            notice.Guild == expectedGuild && notice.Refresh
            && notice.Packet.ProtocolId == GameProtocolEngine.GuildSelfRankNotification
            && notice.Packet.Payload is [var rankByte] && rankByte == rank);
        static bool IsRoster(GameServerPacket packet) => packet.Type == GameProtocolEngine.CommandPacketType
            && packet.ProtocolId == GameProtocolEngine.GuildMembersCommand;
        JsonGameStore NewStore() => new(options, NullLogger<JsonGameStore>.Instance);
        async Task<CharacterRecord> AddAsync(string account, string name)
        {
            await store.CreateAccountAsync(new(account, "test-password", OicqCode: "123456"));
            return (await store.CreateCharacterAsync(account, new(name))).Character!;
        }
    }

    private static HttpClient NewClient(Uri origin, CookieContainer? cookies = null) =>
        new(new HttpClientHandler { CookieContainer = cookies ?? new CookieContainer() }) { BaseAddress = origin };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-DFLegacy-Guild-Request", "1");
        if (origin is not null) request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }
}
