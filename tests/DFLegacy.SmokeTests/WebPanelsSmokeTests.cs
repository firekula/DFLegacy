using DFLegacy.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

internal static class WebPanelsSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var panelsProperty = typeof(ServerOptions).GetProperty("WebPanels");
        check(panelsProperty is not null, "separate web panel listener configuration exists");
        if (panelsProperty is null) return;
        var panels = panelsProperty.GetValue(new ServerOptions())!;
        foreach (var pair in new[] { ("Gm", 18081), ("Player", 18082) })
        {
            var listener = (ListenerOptions)panels.GetType().GetProperty(pair.Item1)!.GetValue(panels)!;
            check(listener.Enabled && listener.Host == "0.0.0.0" && listener.Port == pair.Item2, pair.Item1 + " listener defaults");
        }
        foreach (var port in new[] { 0, -1, 65536, 8081, 8080, 7001, 7005, 18082 })
        {
            var invalid = new ServerOptions(); invalid.WebPanels.Gm.Port = port;
            var rejected = false;
            try { WebPanelHosting.Validate(invalid); }
            catch (InvalidOperationException exception) { rejected = exception.Message.Contains("WebPanels.Gm"); }
            check(rejected, "GM invalid/conflicting TCP port diagnosed: " + port);
        }
        var disabled = new ServerOptions(); disabled.WebPanels.Gm.Enabled = false; disabled.WebPanels.Gm.Port = 0;
        disabled.WebPanels.Player.Enabled = false; disabled.WebPanels.Player.Port = 0;
        WebPanelHosting.Validate(disabled);
        check(true, "disabled panel listeners do not reserve ports");
        var invalidHost = new ServerOptions(); invalidHost.WebPanels.Player.Host = "not-an-address";
        try { WebPanelHosting.Validate(invalidHost); check(false, "invalid panel host rejected"); }
        catch (InvalidOperationException exception) { check(exception.Message.Contains("WebPanels.Player.Host"), "invalid host reports configuration path"); }
        await ModerationStoreAsync(check);
        await HttpAsync(check);
    }

    private static async Task ModerationStoreAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-moderation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new ServerOptions { DataPath = Path.Combine(root, "state.json"), SeedTestAccount = false };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("moderated", "old-password", OicqCode: "123456"));
            var result = await store.CreateCharacterAsync("moderated", new("ModeratedChar", Level: 1));
            var character = result.Character!;
            var sessions = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
            var kicked = false;
            var runtime = new RuntimeState();
            using var contexts = new GameplayDatagramService(options, runtime, NullLogger<GameplayDatagramService>.Instance);
            var channel = runtime.Add("channel:test", "127.0.0.1:41000");
            contexts.RegisterChannelSession(channel, IPAddress.Loopback);
            var lease = await sessions.ClaimAsync(character.Id, channel.Id, () => kicked = true, default);
            var auth = new AdminAuthService(options, TimeProvider.System);
            var oldToken = (await store.AuthenticateWebAsync("moderated", "old-password", auth, default))!.Token;

            check((await store.DeleteOfflineCharacterByAdminAsync(character.Id, "operator", sessions, default)).Status == ModerationStatus.Conflict,
                "GM deletion refuses an attached character");
            var ban = await store.SetAccountBanByAdminAsync("moderated", "operator", new(1, "fixture"), auth, sessions, default);
            check(ban.Status == ModerationStatus.Success && ban.Ban?.ExpiresAt > DateTimeOffset.UtcNow && kicked && auth.Validate(oldToken) is null,
                "timed ban persists expiry, revokes web token, and kicks attached game character");
            check(await ChannelLoginAuthentication.AuthenticateAsync(store, contexts, channel.Id, "moderated", "old-password") is null
                && await ChannelLoginAuthentication.AuthenticateAsync(store, contexts, channel.Id, "moderated", null) is null,
                "ban blocks both password and legacy username-only game login");

            var stored = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await stored.InitializeAsync();
            check((await stored.GetAccountAsync("moderated"))?.Ban == ban.Ban
                && await stored.AuthenticateWebAsync("moderated", "old-password", auth, default) is null,
                "active ban survives store reload and blocks player center authentication");
            check(await stored.AuthenticateAsync("moderated", "old-password") is null,
                "persisted ban blocks password game authentication");

            // Keep the loaded record intact except for moving its expiry into the past.
            var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
            json["Accounts"]![0]!["Ban"]!["ExpiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-1);
            await File.WriteAllTextAsync(options.DataPath, json.ToJsonString());
            var expired = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await expired.InitializeAsync();
            check((await expired.GetAccountAsync("moderated"))?.Ban is null
                && await expired.AuthenticateWebAsync("moderated", "old-password", auth, default) is not null,
                "expired timed ban allows login without manual unban");
            sessions.Release(lease);
            check((await expired.DeleteOfflineCharacterByAdminAsync(character.Id, "operator", sessions, default)).Status == ModerationStatus.Success
                && (await expired.GetAccountAsync("moderated"))?.Characters.Count == 0,
                "offline delete removes the selected storage ID");
        }
        finally { Directory.Delete(root, true); }
    }

    private static int Port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    private static async Task HttpAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-panels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "creature"));
        await File.WriteAllTextAsync(Path.Combine(root, "creature", "exptable.tbl"), "10\n100\n");
        try
        {
            var ports = new HashSet<int>();
            while (ports.Count < 3) ports.Add(Port());
            var assigned = ports.ToArray(); var api = assigned[0]; var gm = assigned[1]; var player = assigned[2];
            var options = new ServerOptions {
                DataPath = Path.Combine(root, "state.json"), SeedTestAccount = false, ScriptPvfPath = "", SkillScriptPath = root,
                Admin = new() { Host = "127.0.0.1", Port = api },
                Entrance = new() { Enabled = false }, CharacterDatagram = new() { Enabled = false },
                GameplayDatagram = new() { Enabled = false }, FallbackRelay = new() { Enabled = false },
                GameProbe = new() { Enabled = false },
                WebPanels = new() { Gm = new() { Host = "127.0.0.1", Port = gm }, Player = new() { Host = "127.0.0.1", Port = player } }
            };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance); await store.InitializeAsync();
            await store.CreateAccountAsync(new("paneladmin", "panel-password", OicqCode: "123456"));
            await store.SetRoleAsync("paneladmin", AccountRole.Admin, "fixture");
            var config = Path.Combine(root, "server.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { DFLegacy = options }));
            using var host = DeploymentHostingSmokeTests.Child.Start(config, Path.GetDirectoryName(typeof(JsonGameStore).Assembly.Location)!, "panel-secret");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var ready = false;
            for (var attempt = 0; attempt < 100 && !host.Process.HasExited; attempt++)
            {
                try { using var health = await client.GetAsync($"http://127.0.0.1:{api}/health"); ready = health.IsSuccessStatusCode; if (ready) break; }
                catch (HttpRequestException) { } catch (TaskCanceledException) { }
                await Task.Delay(100);
            }
            check(ready, "panel fixture host is ready");
            if (!ready) { if (host.Process.HasExited) Console.Error.WriteLine(await host.Output + await host.Error); return; }
            async Task<HttpResponseMessage> Get(int port, string path) => await client.GetAsync($"http://127.0.0.1:{port}{path}");
            try
            {
                using var gmRoot = await Get(gm, "/"); using var playerRoot = await Get(player, "/");
                check(gmRoot.StatusCode == HttpStatusCode.OK && (await gmRoot.Content.ReadAsStringAsync()).Contains("admin.js"), "GM root serves actual GM page");
                check(playerRoot.StatusCode == HttpStatusCode.OK && (await playerRoot.Content.ReadAsStringAsync()).Contains("玩家"), "player root serves actual player page");
                foreach (var port in new[] { gm, player })
                {
                    using var css = await Get(port, "/admin/admin.css"); check(css.StatusCode == HttpStatusCode.OK, "panel CSS works on port " + port);
                    client.DefaultRequestHeaders.Add(ManagerControl.TokenHeader, "panel-secret");
                    using var manager = await Get(port, "/internal/manager/status");
                    check(manager.StatusCode == HttpStatusCode.NotFound, "panel cannot reach manager control with correct secret");
                    client.DefaultRequestHeaders.Remove(ManagerControl.TokenHeader);
                }
                foreach (var path in new[] { "/admin/index.html", "/admin/admin.js", "/api/status", "/api/client-pay/cera" })
                {
                    using var denied = await Get(player, path); check(denied.StatusCode == HttpStatusCode.NotFound, "player denies privileged surface " + path);
                }
                using var cross = await Get(gm, "/admin/player.html"); check(cross.StatusCode == HttpStatusCode.NotFound, "GM port rejects player page");
                using var registration = await client.PostAsync($"http://127.0.0.1:{api}/api/accounts", new StringContent("{\"userName\":\"ordinarynew\",\"password\":\"new-password\",\"oicqCode\":\"123456\",\"testAccount\":true}", Encoding.UTF8, "application/json"));
                using var registered = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
                check(registration.StatusCode == HttpStatusCode.Created && registered.RootElement.GetProperty("kind").GetInt32() == (int)AccountKind.Normal,
                    "public registration ignores forged test-account flag");
                using var anonymousKind = await client.PutAsync($"http://127.0.0.1:{gm}/api/admin/users/ordinarynew/kind", new StringContent("{\"kind\":\"Test\"}", Encoding.UTF8, "application/json"));
                check(anonymousKind.StatusCode == HttpStatusCode.Unauthorized, "kind changes require administrator authentication");
                using var authRequired = await Get(gm, "/api/status"); check(authRequired.StatusCode == HttpStatusCode.Unauthorized, "GM API still requires authentication");
                using var login = await client.PostAsync($"http://127.0.0.1:{gm}/api/auth/login", new StringContent("{\"userName\":\"paneladmin\",\"password\":\"panel-password\"}", Encoding.UTF8, "application/json"));
                using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
                client.DefaultRequestHeaders.Authorization = new("Bearer", loginJson.RootElement.GetProperty("token").GetString());
                using var kindChange = await client.PutAsync($"http://127.0.0.1:{gm}/api/admin/users/ordinarynew/kind", new StringContent("{\"kind\":\"Test\"}", Encoding.UTF8, "application/json"));
                using var changedKind = JsonDocument.Parse(kindChange.IsSuccessStatusCode ? await kindChange.Content.ReadAsStringAsync() : "{}");
                check(kindChange.IsSuccessStatusCode && changedKind.RootElement.TryGetProperty("kind", out var kindValue) && kindValue.GetString() == "Test", "GM can change account kind through actual HTTP endpoint");
                using var playerKind = await client.PutAsync($"http://127.0.0.1:{player}/api/admin/users/ordinarynew/kind", new StringContent("{\"kind\":\"Normal\"}", Encoding.UTF8, "application/json"));
                check(playerKind.StatusCode == HttpStatusCode.NotFound, "player listener cannot change account kind even with admin token");
                using var authorized = await Get(gm, "/api/status"); check(authorized.StatusCode == HttpStatusCode.OK, "GM bearer authenticates actual admin API");
                using var deniedToken = await Get(player, "/api/status"); check(deniedToken.StatusCode == HttpStatusCode.NotFound, "even admin token cannot expose admin API on player listener");
                var adminToken = client.DefaultRequestHeaders.Authorization?.Parameter;
                client.DefaultRequestHeaders.Authorization = null;
                await ModerationHttpAsync(client, gm, player, adminToken, check);
                client.DefaultRequestHeaders.Authorization = null;
                using var oldPage = await Get(api, "/admin/index.html"); check(oldPage.StatusCode == HttpStatusCode.OK, "legacy API-port page remains compatible");
                using var playerLogin = await client.PostAsync($"http://127.0.0.1:{player}/api/auth/login", new StringContent("{\"userName\":\"paneladmin\",\"password\":\"panel-password\"}", Encoding.UTF8, "application/json"));
                check(playerLogin.StatusCode == HttpStatusCode.OK, "player listener retains login API");
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { check(false, "separate GM and player listeners accept HTTP connections"); }
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task ModerationHttpAsync(HttpClient client, int gm, int player, string? adminToken, Action<bool, string> check)
    {
        async Task<HttpResponseMessage> Call(int port, HttpMethod method, string path, object? data = null, string? bearer = null)
        {
            using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{port}{path}");
            if (data is not null) request.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
            return await client.SendAsync(request);
        }

        const string account = "/api/admin/users/ordinarynew";
        using var firstLogin = await Call(player, HttpMethod.Post, "/api/auth/login", new { userName = "ordinarynew", password = "new-password" });
        using var firstBody = JsonDocument.Parse(await firstLogin.Content.ReadAsStringAsync());
        var playerToken = firstBody.RootElement.GetProperty("token").GetString();
        using var unauthorized = await Call(gm, HttpMethod.Put, account + "/password", new { newPassword = "replaced-password" }, playerToken);
        check(unauthorized.StatusCode == HttpStatusCode.Forbidden, "ordinary player cannot reset another password through GM API");
        using var wrongPort = await Call(player, HttpMethod.Put, account + "/password", new { newPassword = "replaced-password" }, adminToken);
        check(wrongPort.StatusCode == HttpStatusCode.NotFound, "player listener hides moderation API from administrator");
        using var protectedAdmin = await Call(gm, HttpMethod.Put, "/api/admin/users/paneladmin/password", new { newPassword = "replaced-password" }, adminToken);
        check(protectedAdmin.StatusCode == HttpStatusCode.Conflict, "GM cannot reset an administrator password through moderation");

        using var reset = await Call(gm, HttpMethod.Put, account + "/password", new { newPassword = "replaced-password" }, adminToken);
        using var stale = await Call(player, HttpMethod.Get, "/api/accounts/ordinarynew/characters", bearer: playerToken);
        using var oldPassword = await Call(player, HttpMethod.Post, "/api/auth/login", new { userName = "ordinarynew", password = "new-password" });
        check(reset.IsSuccessStatusCode && stale.StatusCode == HttpStatusCode.Unauthorized && oldPassword.StatusCode == HttpStatusCode.Unauthorized,
            "GM reset changes the password and invalidates old player token");

        using var secondLogin = await Call(player, HttpMethod.Post, "/api/auth/login", new { userName = "ordinarynew", password = "replaced-password" });
        using var secondBody = JsonDocument.Parse(await secondLogin.Content.ReadAsStringAsync());
        playerToken = secondBody.RootElement.GetProperty("token").GetString();
        using var missingDuration = await Call(gm, HttpMethod.Put, account + "/ban", new { reason = "fixture" }, adminToken);
        check(missingDuration.StatusCode == HttpStatusCode.BadRequest, "ban must specify a duration or explicit null");
        using var permanent = await Call(gm, HttpMethod.Put, account + "/ban", new { durationHours = (int?)null, reason = "fixture" }, adminToken);
        using var banned = await Call(player, HttpMethod.Post, "/api/auth/login", new { userName = "ordinarynew", password = "replaced-password" });
        using var revoked = await Call(player, HttpMethod.Get, "/api/accounts/ordinarynew/characters", bearer: playerToken);
        using var accounts = await Call(gm, HttpMethod.Get, "/api/admin/users", bearer: adminToken);
        using var accountsBody = JsonDocument.Parse(await accounts.Content.ReadAsStringAsync());
        var row = accountsBody.RootElement.EnumerateArray().Single(value => value.GetProperty("userName").GetString() == "ordinarynew");
        check(permanent.IsSuccessStatusCode && banned.StatusCode == HttpStatusCode.Unauthorized && revoked.StatusCode == HttpStatusCode.Unauthorized
            && row.GetProperty("ban").GetProperty("expiresAt").ValueKind == JsonValueKind.Null,
            "permanent ban revokes sessions, blocks player center login and is visible in account list");
        using var clear = await Call(gm, HttpMethod.Delete, account + "/ban", bearer: adminToken);
        using var restored = await Call(player, HttpMethod.Post, "/api/auth/login", new { userName = "ordinarynew", password = "replaced-password" });
        check(clear.IsSuccessStatusCode && restored.IsSuccessStatusCode, "GM unban restores player center login");
        using var restoredBody = JsonDocument.Parse(await restored.Content.ReadAsStringAsync());
        playerToken = restoredBody.RootElement.GetProperty("token").GetString();

        var character = row.GetProperty("characters").EnumerateArray().FirstOrDefault();
        if (character.ValueKind == JsonValueKind.Undefined)
        {
            using var created = await Call(gm, HttpMethod.Post, "/api/accounts/ordinarynew/characters", new { name = "DeletionFixture", level = 1 }, adminToken);
            using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            character = createdBody.RootElement.Clone();
        }
        var id = character.GetProperty("id").GetGuid();
        using var deniedDelete = await Call(gm, HttpMethod.Delete, $"/api/admin/characters/{id}", bearer: playerToken);
        using var deleted = await Call(gm, HttpMethod.Delete, $"/api/admin/characters/{id}", bearer: adminToken);
        using var repeated = await Call(gm, HttpMethod.Delete, $"/api/admin/characters/{id}", bearer: adminToken);
        check(deniedDelete.StatusCode == HttpStatusCode.Forbidden && deleted.IsSuccessStatusCode && repeated.StatusCode == HttpStatusCode.NotFound,
            "GM delete removes an offline character and a repeated request is harmless");
    }

}
