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
        await HttpAsync(check);
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
                using var authRequired = await Get(gm, "/api/status"); check(authRequired.StatusCode == HttpStatusCode.Unauthorized, "GM API still requires authentication");
                using var login = await client.PostAsync($"http://127.0.0.1:{gm}/api/auth/login", new StringContent("{\"userName\":\"paneladmin\",\"password\":\"panel-password\"}", Encoding.UTF8, "application/json"));
                using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
                client.DefaultRequestHeaders.Authorization = new("Bearer", loginJson.RootElement.GetProperty("token").GetString());
                using var authorized = await Get(gm, "/api/status"); check(authorized.StatusCode == HttpStatusCode.OK, "GM bearer authenticates actual admin API");
                using var deniedToken = await Get(player, "/api/status"); check(deniedToken.StatusCode == HttpStatusCode.NotFound, "even admin token cannot expose admin API on player listener");
                client.DefaultRequestHeaders.Authorization = null;
                using var oldPage = await Get(api, "/admin/index.html"); check(oldPage.StatusCode == HttpStatusCode.OK, "legacy API-port page remains compatible");
                using var playerLogin = await client.PostAsync($"http://127.0.0.1:{player}/api/auth/login", new StringContent("{\"userName\":\"paneladmin\",\"password\":\"panel-password\"}", Encoding.UTF8, "application/json"));
                check(playerLogin.StatusCode == HttpStatusCode.OK, "player listener retains login API");
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { check(false, "separate GM and player listeners accept HTTP connections"); }
        }
        finally { Directory.Delete(root, true); }
    }
}
