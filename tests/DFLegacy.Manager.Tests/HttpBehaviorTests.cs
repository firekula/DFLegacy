using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            check((await client.PostAsJsonAsync("/api/exports", new { })).StatusCode == HttpStatusCode.Forbidden,
                "background export creation requires CSRF proof");
            check((await client.PostAsync("/api/resources/pvf", PvfBody([0]))).StatusCode == HttpStatusCode.Forbidden, "PVF upload requires CSRF proof");
            using (var hostile = new HttpRequestMessage(HttpMethod.Post, "/api/server/start"))
            {
                hostile.Headers.Add("X-CSRF-Token", csrf); hostile.Headers.Add("Origin", "http://hostile.example.test"); hostile.Content = JsonContent.Create(new { });
                check((await client.SendAsync(hostile)).StatusCode == HttpStatusCode.Forbidden, "hostile Origin is rejected even with a session cookie");
            }
            client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
            using (var invalidExport = await client.PostAsJsonAsync("/api/export/client", new { testBuild = "true" }))
            {
                var error = await invalidExport.Content.ReadAsStringAsync();
                check(invalidExport.StatusCode == HttpStatusCode.BadRequest && error.Contains("testBuild"), "export rejects non-boolean testBuild before asset lookup");
            }
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
            var jobResponse = await client.PostAsJsonAsync("/api/exports", new { });
            check(jobResponse.StatusCode == HttpStatusCode.Accepted, "background export returns a short accepted response");
            var exportJob = await jobResponse.Content.ReadFromJsonAsync<JsonObject>();
            var exportId = exportJob!["id"]!.GetValue<string>();
            JsonObject? jobStatus = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                jobStatus = await client.GetFromJsonAsync<JsonObject>("/api/exports/" + exportId);
                if (jobStatus!["state"]!.GetValue<string>() != "running") break;
                await Task.Delay(10);
            }
            check(jobStatus?["state"]?.GetValue<string>() == "failed" && jobStatus["error"] is not null,
                "missing bundled assets are reported as a terminal job failure");
            check((await client.GetAsync("/api/exports/" + exportId + "/download")).StatusCode == HttpStatusCode.NotFound,
                "failed generation never downloads a partial archive");
            check((await client.GetFromJsonAsync<JsonArray>("/api/logs"))!.Any(line => line?["source"]?.GetValue<string>() == "export"),
                "generation failures are visible in the authenticated runtime log feed");
            // Use real incompressible input so generation is still in flight
            // while ordinary management requests are exercised.
            var exportFiles = new List<object>();
            foreach (var name in new[] { "patch/DFLegacy.Client.dll", "patch/Config.ini", "launcher/DFLegacy.Launcher.exe" })
            {
                var assetPath = Path.Combine(paths.AssetsDirectory, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
                var content = name.EndsWith(".exe") ? RandomNumberGenerator.GetBytes(32 * 1024 * 1024) : "fixture"u8.ToArray();
                File.WriteAllBytes(assetPath, content);
                exportFiles.Add(new { path = name, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
            }
            var assetManifest = Path.Combine(paths.AssetsDirectory, "manifest.json");
            File.WriteAllText(assetManifest, JsonSerializer.Serialize(new { version = "v1.2.3", commit = new string('a', 40), files = exportFiles }));
            File.WriteAllText(Path.Combine(root, "release-manifest.json"), JsonSerializer.Serialize(new
            {
                version = "v1.2.3", commit = new string('a', 40),
                files = new[] { new { path = "client-assets/manifest.json", sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assetManifest))) } }
            }));
            var acceptedTimer = Stopwatch.StartNew();
            using var accepted = await client.PostAsJsonAsync("/api/exports", new { });
            var active = await accepted.Content.ReadFromJsonAsync<JsonObject>();
            var activeId = active!["id"]!.GetValue<string>();
            check(accepted.StatusCode == HttpStatusCode.Accepted && acceptedTimer.Elapsed < TimeSpan.FromSeconds(3),
                "large package generation is accepted without waiting for compression");
            var sawRunning = false;
            var checkedDuplicate = false;
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                jobStatus = await client.GetFromJsonAsync<JsonObject>("/api/exports/" + activeId);
                if (jobStatus!["state"]!.GetValue<string>() != "running") break;
                sawRunning = true;
                var responses = await Task.WhenAll(client.GetAsync("/health"), client.GetAsync("/api/status"), client.GetAsync("/api/logs"));
                check(responses.All(response => response.IsSuccessStatusCode), "health, status and logs respond during real ZIP generation");
                foreach (var response in responses) response.Dispose();
                if (!checkedDuplicate)
                {
                    checkedDuplicate = true;
                    using var duplicate = await client.PostAsJsonAsync("/api/exports", new { });
                    // If generation completed between requests, the new job is legitimate.
                    if (duplicate.StatusCode == HttpStatusCode.Accepted)
                    {
                        var next = await duplicate.Content.ReadFromJsonAsync<JsonObject>();
                        activeId = next!["id"]!.GetValue<string>();
                    }
                    else check(duplicate.StatusCode == HttpStatusCode.BadRequest, "overlapping generation is rejected rather than queued");
                }
                await Task.Delay(100);
            }
            check(sawRunning && jobStatus?["state"]?.GetValue<string>() == "ready", "real background ZIP generation reaches ready");
            using (var downloading = await client.GetAsync("/api/exports/" + activeId + "/download", HttpCompletionOption.ResponseHeadersRead))
            {
                check(downloading.IsSuccessStatusCode && downloading.Content.Headers.ContentLength > 32 * 1024 * 1024,
                    "completed archive returns streaming download headers and size");
                await using var stream = await downloading.Content.ReadAsStreamAsync();
                var buffer = new byte[1024];
                await stream.ReadExactlyAsync(buffer);
                var responses = await Task.WhenAll(client.GetAsync("/health"), client.GetAsync("/api/status"), client.GetAsync("/api/logs"));
                check(responses.All(response => response.IsSuccessStatusCode), "management stays responsive while a download body is left unread");
                foreach (var response in responses) response.Dispose();
            }
            var assetFiles = new Dictionary<string, string>
            {
                ["launcher/DFLegacy.Launcher.exe"] = "fixture launcher", ["patch/DFLegacy.Client.dll"] = "fixture core",
                ["patch/Config.ini"] = "[Features]\nEnableDebugOverlay=0\n", ["patch/debug.dll"] = "fixture debug"
            };
            foreach (var (name, contents) in assetFiles)
            {
                var target = Path.Combine(paths.AssetsDirectory, name); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, contents);
            }
            var manifestFile = Path.Combine(paths.AssetsDirectory, "manifest.json");
            var commit = new string('a', 40);
            File.WriteAllText(manifestFile, JsonSerializer.Serialize(new { version = "v1.2.3", commit, files = assetFiles.Select(pair => new { path = pair.Key, sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value))) }) }));
            File.WriteAllText(Path.Combine(paths.Root, "release-manifest.json"), JsonSerializer.Serialize(new { version = "v1.2.3", commit, files = new[] { new { path = "client-assets/manifest.json", sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifestFile))) } } }));
            foreach (var (kind, body, test) in new[] { ("client", "{}", false), ("client", "{\"testBuild\":false}", false), ("client", "{\"testBuild\":true}", true), ("patch", "{\"testBuild\":true}", false), ("launcher", "{\"testBuild\":true}", false) })
            {
                using var exported = await client.PostAsync("/api/export/" + kind, new StringContent(body, Encoding.UTF8, "application/json"));
                check(exported.IsSuccessStatusCode, "HTTP export accepts selected flavor " + kind + " " + test);
                using var data = new MemoryStream(await exported.Content.ReadAsByteArrayAsync());
                using var zip = new ZipArchive(data);
                using var package = new StreamReader(zip.GetEntry("client-package.json")!.Open());
                var installed = JsonNode.Parse(package.ReadToEnd())!;
                check(installed["flavor"]!.GetValue<string>() == (test ? "test" : "normal") && (zip.GetEntry("debug.dll") is not null) == test, "HTTP export captures exact testBuild selection");
            }
            foreach (var body in new[] { "{\"testBuild\":1}", "{\"testBuild\":null}", "{\"testBuild\":true,\"testBuild\":false}", "[]" })
            {
                using var invalid = await client.PostAsync("/api/export/client", new StringContent(body, Encoding.UTF8, "application/json"));
                check(invalid.StatusCode == HttpStatusCode.BadRequest, "HTTP export refuses ambiguous or invalid bool");
                using var invalidJob = await client.PostAsync("/api/exports", new StringContent(body, Encoding.UTF8, "application/json"));
                check(invalidJob.StatusCode == HttpStatusCode.BadRequest, "background export refuses ambiguous or invalid bool");
            }
            foreach (var test in new[] { false, true })
            {
                using var created = await client.PostAsJsonAsync("/api/exports", new { testBuild = test });
                var selected = await created.Content.ReadFromJsonAsync<JsonObject>();
                var id = selected!["id"]!.GetValue<string>();
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    selected = await client.GetFromJsonAsync<JsonObject>("/api/exports/" + id);
                    if (selected!["state"]!.GetValue<string>() != "running") break;
                    await Task.Delay(10);
                }
                check(selected?["state"]?.GetValue<string>() == "ready", "background export completes selected flavor");
                using var data = new MemoryStream(await client.GetByteArrayAsync("/api/exports/" + id + "/download"));
                using var zip = new ZipArchive(data);
                using var package = new StreamReader(zip.GetEntry("client-package.json")!.Open());
                check(JsonNode.Parse(package.ReadToEnd())!["flavor"]!.GetValue<string>() == (test ? "test" : "normal")
                    && (zip.GetEntry("debug.dll") is not null) == test, "background export preserves the selected V2 client flavor");
            }
            await client.PostAsJsonAsync("/api/logout", new { });
            check((await client.GetAsync("/api/exports/" + exportId)).StatusCode == HttpStatusCode.Unauthorized
                && (await client.GetAsync("/api/exports/" + exportId + "/download")).StatusCode == HttpStatusCode.Unauthorized,
                "export status and downloads require an authenticated session");
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
