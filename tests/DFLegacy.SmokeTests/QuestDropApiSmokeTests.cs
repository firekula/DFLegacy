using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// HTTP-level verification of the GM quest task-item drop rate: GM-listener and
/// Admin only, range checked, persisted under the data directory and restored
/// after a restart. Uses the real server binary as a child process.
/// </summary>
internal static class QuestDropApiSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-quest-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "creature"));
        await File.WriteAllTextAsync(Path.Combine(root, "creature", "exptable.tbl"), "10\n100\n");
        DeploymentHostingSmokeTests.Child? host = null;
        try
        {
            var ports = new HashSet<int>();
            while (ports.Count < 2) ports.Add(FreePort());
            var assigned = ports.ToArray();
            var api = assigned[0];
            var gm = assigned[1];
            var options = new ServerOptions
            {
                DataPath = Path.Combine(root, "state.json"),
                SeedTestAccount = false,
                ScriptPvfPath = "",
                SkillScriptPath = root,
                Admin = new() { Host = "127.0.0.1", Port = api },
                Entrance = new() { Enabled = false },
                CharacterDatagram = new() { Enabled = false },
                GameplayDatagram = new() { Enabled = false },
                FallbackRelay = new() { Enabled = false },
                GameProbe = new() { Enabled = false },
                WebPanels = new()
                {
                    Gm = new() { Host = "127.0.0.1", Port = gm },
                    Player = new() { Enabled = false }
                }
            };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("dropadmin", "fixture-password", OicqCode: "123456"));
            await store.SetRoleAsync("dropadmin", AccountRole.Admin, "fixture");
            await store.CreateAccountAsync(new("dropplayer", "fixture-password", OicqCode: "123456"));
            var config = Path.Combine(root, "server.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { DFLegacy = options }));

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            async Task Start()
            {
                host = DeploymentHostingSmokeTests.Child.Start(config, Path.GetDirectoryName(typeof(JsonGameStore).Assembly.Location)!, null);
                for (var attempt = 0; attempt < 100 && !host.Process.HasExited; attempt++)
                {
                    try
                    {
                        using var health = await client.GetAsync($"http://127.0.0.1:{api}/health");
                        if (health.IsSuccessStatusCode) return;
                    }
                    catch (HttpRequestException) { }
                    catch (TaskCanceledException) { }
                    await Task.Delay(100);
                }

                host.Dispose();
                throw new InvalidOperationException("quest drop test host not ready: " + await host.Output + await host.Error);
            }
            async Task<HttpResponseMessage> Call(int port, HttpMethod method, string path, string? body = null, string? token = null)
            {
                using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{port}{path}");
                if (body is not null)
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }
                if (token is not null) request.Headers.Authorization = new("Bearer", token);
                return await client.SendAsync(request);
            }
            async Task<string> Login(string name)
            {
                using var response = await Call(gm, HttpMethod.Post, "/api/auth/login",
                    JsonSerializer.Serialize(new { userName = name, password = "fixture-password" }));
                return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["token"]!.GetValue<string>();
            }

            await Start();
            var admin = await Login("dropadmin");
            var player = await Login("dropplayer");
            const string Rate = "/api/admin/drop/quest-rate";

            using var initial = await Call(gm, HttpMethod.Get, Rate, token: admin);
            check(initial.IsSuccessStatusCode
                    && JsonNode.Parse(await initial.Content.ReadAsStringAsync())!["ratePercent"]!.GetValue<int>() == 100,
                "a fresh GM setting starts at the PVF 100% quest drop rate");

            using var updated = await Call(gm, HttpMethod.Put, Rate, "{\"ratePercent\":250}", admin);
            check(updated.IsSuccessStatusCode
                    && JsonNode.Parse(await updated.Content.ReadAsStringAsync())!["ratePercent"]!.GetValue<int>() == 250,
                "the GM can raise the quest task-item rate to 250%");

            using var tooHigh = await Call(gm, HttpMethod.Put, Rate, "{\"ratePercent\":1001}", admin);
            check(tooHigh.StatusCode == HttpStatusCode.BadRequest,
                "the rate is bounded to the documented 0–1000 range");
            using var wrongShape = await Call(gm, HttpMethod.Put, Rate, "{\"ratePercent\":50,\"extra\":1}", admin);
            check(wrongShape.StatusCode == HttpStatusCode.BadRequest,
                "the rate request accepts exactly one property");
            using var forbidden = await Call(gm, HttpMethod.Put, Rate, "{\"ratePercent\":5}", player);
            check(forbidden.StatusCode == HttpStatusCode.Forbidden,
                "a non-admin token cannot change the quest drop rate");
            using var anonymous = await Call(gm, HttpMethod.Get, Rate);
            check(anonymous.StatusCode == HttpStatusCode.Unauthorized,
                "an anonymous request cannot read the quest drop rate");
            using var wrongListener = await Call(api, HttpMethod.Get, Rate, token: admin);
            check(wrongListener.StatusCode == HttpStatusCode.NotFound,
                "the quest drop setting is served only on the GM listener");

            host!.Dispose();
            host = null;
            await Start();
            admin = await Login("dropadmin");
            using var persisted = await Call(gm, HttpMethod.Get, Rate, token: admin);
            check(persisted.IsSuccessStatusCode
                    && JsonNode.Parse(await persisted.Content.ReadAsStringAsync())!["ratePercent"]!.GetValue<int>() == 250,
                "the quest drop rate survives a server restart");
            check(File.Exists(Path.Combine(root, "settings", "quest-drop.json")),
                "the setting is persisted under the data directory, not in server.json");
        }
        finally
        {
            host?.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
