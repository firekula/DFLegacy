using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using DFLegacy.Manager;

internal static class HttpBehaviorTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy manager http " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new ManagerPaths(root);
        var port = FreePort();
        using var process = new Process { StartInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetTempPath() } };
        foreach (var argument in new[] { typeof(ManagerPaths).Assembly.Location, "--root", root, "--port", port.ToString(), "--no-browser" }) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["DFLegacy__Entrance__Port"] = "12345";
        var output = new List<string>();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.Add(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.Add(e.Data); };
        try
        {
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            var ready = false;
            for (var attempt = 0; attempt < 150 && !process.HasExited; attempt++)
            {
                try { ready = (await client.GetAsync("/health")).IsSuccessStatusCode; if (ready) break; } catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            check(ready, "HTTP manager starts from an unrelated working directory");
            if (!ready) { lock (output) Console.Error.WriteLine(string.Join('\n', output)); return; }
            check((await client.GetAsync("/api/status")).StatusCode == HttpStatusCode.Unauthorized, "anonymous users cannot inspect or control deployment");
            static ByteArrayContent PvfBody(byte[] bytes)
            {
                var content = new ByteArrayContent(bytes); content.Headers.ContentType = new("application/octet-stream"); return content;
            }
            check((await client.PostAsync("/api/resources/pvf", PvfBody([0]))).StatusCode == HttpStatusCode.Unauthorized, "anonymous users cannot upload PVF resources");
            check((await client.GetAsync("/ready")).StatusCode == HttpStatusCode.ServiceUnavailable, "readiness distinguishes stopped game from healthy manager");
            using (var hostile = new HttpRequestMessage(HttpMethod.Get, "/health"))
            {
                hostile.Headers.Host = "untrusted.example.test";
                check((await client.SendAsync(hostile)).StatusCode == HttpStatusCode.BadRequest, "untrusted Host is rejected before serving local management");
            }
            check((await client.PostAsJsonAsync("/api/setup", new { setupToken = "wrong", userName = "operator", password = "A strong password 123!" })).StatusCode == HttpStatusCode.BadRequest,
                "remote first visitor cannot claim the instance without setup secret");
            var bootstrap = File.ReadAllText(paths.SetupTokenFile).Trim();
            using var setup = await client.PostAsJsonAsync("/api/setup", new { setupToken = bootstrap, userName = "operator", password = "A strong password 123!" });
            check(setup.IsSuccessStatusCode, "valid bootstrap sets up the management identity");
            var cookies = setup.Headers.TryGetValues("Set-Cookie", out var values) ? string.Join(';', values) : "";
            check(cookies.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookies.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "management session cookie has browser isolation flags");
            var session = await client.GetFromJsonAsync<JsonObject>("/api/session");
            var csrf = session!["csrf"]!.GetValue<string>();
            check(session["userName"]!.GetValue<string>() == "operator" && csrf.Length >= 32, "authenticated session returns independent user identity and anti-forgery token");
            check((await client.PostAsJsonAsync("/api/server/start", new { })).StatusCode == HttpStatusCode.Forbidden, "state-changing cookie requests require CSRF proof");
            check((await client.PostAsync("/api/resources/pvf", PvfBody([0]))).StatusCode == HttpStatusCode.Forbidden, "PVF upload requires CSRF proof");
            using (var hostile = new HttpRequestMessage(HttpMethod.Post, "/api/server/start"))
            {
                hostile.Headers.Add("X-CSRF-Token", csrf); hostile.Headers.Add("Origin", "http://hostile.example.test"); hostile.Content = JsonContent.Create(new { });
                check((await client.SendAsync(hostile)).StatusCode == HttpStatusCode.Forbidden, "hostile Origin is rejected even with a session cookie");
            }
            client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
            using (var hostileUpload = new HttpRequestMessage(HttpMethod.Post, "/api/resources/pvf"))
            {
                hostileUpload.Headers.Add("Origin", "http://hostile.example.test"); hostileUpload.Content = PvfBody([0]);
                check((await client.SendAsync(hostileUpload)).StatusCode == HttpStatusCode.Forbidden, "PVF upload rejects hostile Origin");
            }
            check((await client.PostAsJsonAsync("/api/resources/pvf", new { })).StatusCode == HttpStatusCode.UnsupportedMediaType, "PVF endpoint requires a raw binary stream");
            check((await client.PostAsync("/api/resources/pvf", PvfBody([0]))).StatusCode == HttpStatusCode.BadRequest, "malformed upload fails at the HTTP boundary");
            var fixture = Path.Combine(root, "http-fixture.pvf"); SupervisorBehaviorTests.WriteFixturePvf(fixture);
            var resourceBytes = File.ReadAllBytes(fixture).Concat(new byte[128 * 1024]).ToArray();
            using (var uploaded = await client.PostAsync("/api/resources/pvf", PvfBody(resourceBytes)))
                check(uploaded.IsSuccessStatusCode && Directory.GetFiles(Path.Combine(paths.Data, "resources"), "*.pvf").Length == 1, "authenticated PVF upload exceeds JSON limit without buffering or arbitrary path input");
            using (var resourceStatus = await client.GetAsync("/api/resources/pvf"))
                check(resourceStatus.IsSuccessStatusCode, "authenticated UI can inspect uploaded resource status");
            check((await client.PostAsJsonAsync("/api/setup", new { setupToken = bootstrap, userName = "attacker", password = "Another password 123!" })).StatusCode == HttpStatusCode.BadRequest,
                "first-run token is one-use through the HTTP boundary");
            check((await client.GetAsync("/api/status")).IsSuccessStatusCode, "management is usable while game process is stopped");
            var effectiveStatus = await client.GetFromJsonAsync<JsonObject>("/api/status");
            check(effectiveStatus!["entrancePort"]?.GetValue<int>() == 12345, "overview reports the effective player port rather than a shadowed on-disk value");
            var config = await client.GetFromJsonAsync<JsonObject>("/api/config");
            check(config!["saved"] is JsonObject && config["effective"] is JsonObject, "UI obtains saved and effective settings distinctly");
            check((await client.PutAsJsonAsync("/api/config", config["saved"])).IsSuccessStatusCode, "authenticated configuration save works through CSRF boundary");
            var savedState = await client.GetFromJsonAsync<JsonObject>("/api/status");
            check(savedState!["server"]!["state"]!.GetValue<string>() == "stopped", "first successful config save moves the dashboard from unconfigured to stopped");
            check((await client.GetAsync("/")).IsSuccessStatusCode, "browser dashboard assets are available from the packaged application");
            await client.PostAsJsonAsync("/api/logout", new { });
            check((await client.GetAsync("/api/status")).StatusCode == HttpStatusCode.Unauthorized, "logout removes control access");
            for (var attempt = 0; attempt < 10; attempt++) await client.PostAsJsonAsync("/api/login", new { userName = "operator", password = "wrong" });
            check(!(await client.PostAsJsonAsync("/api/login", new { userName = "operator", password = "A strong password 123!" })).IsSuccessStatusCode,
                "HTTP login enforces the failed-source attempt budget");
            lock (output) check(output.All(line => !line.Contains(bootstrap) && !line.Contains("A strong password")), "server logs never reveal setup or login secrets");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
    internal static int FreePort() { using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); return ((IPEndPoint)socket.LocalEndpoint).Port; }
}
