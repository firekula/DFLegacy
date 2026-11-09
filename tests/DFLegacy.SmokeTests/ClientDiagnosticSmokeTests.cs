using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;

internal static class ClientDiagnosticSmokeTests
{
    private const string Receive = "/api/client-diagnostics/reports";
    private const string Groups = "/api/admin/diagnostics/groups";
    internal static byte[] Body(string? id = null, string context = "sanitized failure") => JsonSerializer.SerializeToUtf8Bytes(new {
        format = "DFLegacy-client-diagnostic-1", eventId = id ?? Guid.NewGuid().ToString("N"), sessionId = Guid.NewGuid().ToString("N"),
        kind = "crash", occurredAt = DateTimeOffset.UtcNow, clientVersion = "test-1", architecture = "x86", evidence = "unhandled-exception",
        stage = "game", exceptionCode = "0xC0000005", module = "game.exe", moduleHash = new string('a', 64), moduleOffset = "0x1234",
        stack = new[] { "game.exe+0x1234" }, occurrences = 1, system = new { osVersion = "Windows test", gpuModel = "unknown" }, context
    });
    private static int Port() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }

    public static async Task RunAsync(Action<bool, string> check)
    {
        await StoreAsync(check);
        await IdleMaintenanceAsync(check);
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "creature"));
        await File.WriteAllTextAsync(Path.Combine(root, "creature", "exptable.tbl"), "10\n100\n");
        DeploymentHostingSmokeTests.Child? host = null;
        try
        {
            var ports = new HashSet<int>(); while (ports.Count < 3) ports.Add(Port()); var assigned = ports.ToArray();
            var api = assigned[0]; var gm = assigned[1]; var player = assigned[2];
            var options = new ServerOptions { DataPath = Path.Combine(root, "state.json"), SeedTestAccount = false, ScriptPvfPath = "", SkillScriptPath = root,
                Admin = new() { Host = "127.0.0.1", Port = api }, Entrance = new() { Enabled = false }, CharacterDatagram = new() { Enabled = false },
                GameplayDatagram = new() { Enabled = false }, FallbackRelay = new() { Enabled = false }, GameProbe = new() { Enabled = false },
                WebPanels = new() { Gm = new() { Host = "127.0.0.1", Port = gm }, Player = new() { Host = "127.0.0.1", Port = player } } };
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance); await store.InitializeAsync();
            await store.CreateAccountAsync(new("diagadmin", "fixture-password", OicqCode: "123456")); await store.SetRoleAsync("diagadmin", AccountRole.Admin, "fixture");
            await store.CreateAccountAsync(new("diagplayer", "fixture-password", OicqCode: "123456"));
            var config = Path.Combine(root, "server.json");
            var document = JsonSerializer.SerializeToNode(new { DFLegacy = options })!;
            // Higher fixture limits keep validation tests independent of the public defaults.
            document["DFLegacy"]!["ClientDiagnostics"] = new JsonObject { ["SourcePerMinute"] = 1000, ["SourcePerHour"] = 1000, ["GlobalPerMinute"] = 1000, ["GlobalPerHour"] = 1000, ["MaxReportCount"] = 2 };
            await File.WriteAllTextAsync(config, document.ToJsonString());
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            async Task Start()
            {
                host = DeploymentHostingSmokeTests.Child.Start(config, Path.GetDirectoryName(typeof(JsonGameStore).Assembly.Location)!, null);
                for (var i = 0; i < 100 && !host.Process.HasExited; i++) {
                    try { using var health = await client.GetAsync($"http://127.0.0.1:{api}/health"); if (health.IsSuccessStatusCode) return; } catch (HttpRequestException) { } catch (TaskCanceledException) { }
                    await Task.Delay(100);
                }
                host.Dispose();
                throw new InvalidOperationException("diagnostics test host not ready: " + await host.Output + await host.Error);
            }
            async Task<HttpResponseMessage> Call(int port, HttpMethod method, string path, byte[]? data = null, string? token = null, string? encoding = null, bool chunked = false)
            {
                using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{port}{path}");
                if (data is not null) { request.Content = new ByteArrayContent(data); request.Content.Headers.ContentType = new("application/json"); if (encoding is not null) request.Content.Headers.ContentEncoding.Add(encoding); }
                if (token is not null) request.Headers.Authorization = new("Bearer", token);
                if (chunked) request.Headers.TransferEncodingChunked = true;
                return await client.SendAsync(request);
            }
            async Task<string> Login(string name) { using var response = await Call(gm, HttpMethod.Post, "/api/auth/login", JsonSerializer.SerializeToUtf8Bytes(new { userName = name, password = "fixture-password" })); return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["token"]!.GetValue<string>(); }
            await Start();
            var original = Body(); var eventId = JsonNode.Parse(original)!["eventId"]!.GetValue<string>();
            using var created = await Call(api, HttpMethod.Post, Receive, original);
            check(created.StatusCode == HttpStatusCode.Created, "anonymous account listener persists basic report before 201");
            if (created.StatusCode != HttpStatusCode.Created) return;
            var receipt = JsonNode.Parse(await created.Content.ReadAsStringAsync())!["receiptId"]!.GetValue<string>();
            var admin = await Login("diagadmin"); var ordinary = await Login("diagplayer");
            foreach (var path in new[] { "//server/share/private.txt", "/用户/秘密.txt" }) {
                foreach (var field in new[] { "context", "system" }) {
                    var unsafeReport = JsonNode.Parse(Body())!;
                    if (field == "context") unsafeReport["context"] = path; else unsafeReport["system"]!["cpuModel"] = path;
                    var unsafeBytes = Encoding.UTF8.GetBytes(unsafeReport.ToJsonString());
                    check(!ClientDiagnosticReport.TryParse(unsafeBytes, out _), "path predicate rejects " + field + " " + path);
                    using var response = await Call(api, HttpMethod.Post, Receive, unsafeBytes);
                    check(response.StatusCode == HttpStatusCode.BadRequest, "HTTP rejects " + field + " " + path);
                }
            }
            foreach (var text in new[] { "{\"password\":\"fixture-secret\"}", "Bearer fixture-secret", "/private-file" }) {
                using var response = await Call(api, HttpMethod.Post, Receive, Body(context: text));
                check(response.StatusCode == HttpStatusCode.BadRequest, "credential/absolute path syntax is rejected even inside summary: " + text);
            }
            foreach (var port in new[] { gm, player }) { using var response = await Call(port, HttpMethod.Post, Receive, original, admin); check(response.StatusCode == HttpStatusCode.NotFound, "reports receive only on account listener " + port); }
            foreach (var port in new[] { api, player }) { using var response = await Call(port, HttpMethod.Get, Groups, token: admin); check(response.StatusCode == HttpStatusCode.NotFound, "admin token cannot cross diagnostics listener boundary " + port); }
            foreach (var pair in new[] { ((string?)null, HttpStatusCode.Unauthorized), (ordinary, HttpStatusCode.Forbidden) }) {
                foreach (var method in new[] { HttpMethod.Get, HttpMethod.Patch, HttpMethod.Delete }) { using var response = await Call(gm, method, method == HttpMethod.Get ? Groups : $"/api/admin/diagnostics/reports/{receipt}", token: pair.Item1); check(response.StatusCode == pair.Item2, "diagnostics manager requires Admin: " + method); }
            }
            using var duplicate = await Call(api, HttpMethod.Post, Receive, original);
            check(duplicate.StatusCode == HttpStatusCode.OK && JsonNode.Parse(await duplicate.Content.ReadAsStringAsync())!["receiptId"]!.GetValue<string>() == receipt, "same frozen bytes return original receipt");
            var changed = JsonNode.Parse(original)!; changed["context"] = "different";
            using var conflict = await Call(api, HttpMethod.Post, Receive, Encoding.UTF8.GetBytes(changed.ToJsonString())); check(conflict.StatusCode == HttpStatusCode.Conflict, "same event with changed bytes is 409");
            foreach (var mutate in new Action<JsonNode>[] { n => n["password"] = "fixture-secret", n => n["module"] = "../game.exe", n => n["context"] = "C:\\Users\\fixture\\secret", n => n["system"]!["machineName"] = "private", n => n["context"] = "token=fixture-secret", n => n["context"] = new string('x', 2049), n => n["stack"] = JsonSerializer.SerializeToNode(Enumerable.Repeat("game.exe+0x1234", 33).ToArray()), n => n["eventId"] = "../escape", n => n["evidence"] = (string?)null }) {
                var bad = JsonNode.Parse(Body())!; mutate(bad); using var response = await Call(api, HttpMethod.Post, Receive, Encoding.UTF8.GetBytes(bad.ToJsonString())); check(response.StatusCode == HttpStatusCode.BadRequest, "strict whitelist rejects unsafe report");
            }
            using var oversized = await Call(api, HttpMethod.Post, Receive, new byte[256 * 1024 + 1]); check(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge, "wire body bounded to 256 KiB");
            using var chunkedBody = await Call(api, HttpMethod.Post, Receive, new byte[256 * 1024 + 1], chunked: true); check(chunkedBody.StatusCode == HttpStatusCode.RequestEntityTooLarge, "chunked wire body bounded without Content-Length");
            using var duplicateKey = await Call(api, HttpMethod.Post, Receive, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Body()).Replace("\"kind\":\"crash\"", "\"kind\":\"error\",\"kind\":\"crash\""))); check(duplicateKey.StatusCode == HttpStatusCode.BadRequest, "duplicate JSON members rejected");
            byte[] Compress(byte[] input) { using var output = new MemoryStream(); using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true)) gzip.Write(input); return output.ToArray(); }
            using var bomb = await Call(api, HttpMethod.Post, Receive, Compress(new byte[1024 * 1024 + 1]), encoding: "gzip"); check(bomb.StatusCode == HttpStatusCode.RequestEntityTooLarge, "gzip expansion bounded to 1 MiB");
            using var invalidGzip = await Call(api, HttpMethod.Post, Receive, new byte[] { 1, 2, 3 }, encoding: "gzip"); check(invalidGzip.StatusCode == HttpStatusCode.BadRequest, "malformed gzip rejected");
            using var second = await Call(api, HttpMethod.Post, Receive, Compress(Body(context: "<script>alert(1)</script>")), encoding: "gzip"); check(second.StatusCode == HttpStatusCode.Created, "gzip accepts bounded structured report and inert markup");
            using var full = await Call(api, HttpMethod.Post, Receive, Body()); check(full.StatusCode == HttpStatusCode.ServiceUnavailable, "count quota rejects new reports");
            using var list = await Call(gm, HttpMethod.Get, Groups + "?page=1&pageSize=1&kind=crash&version=test-1", token: admin);
            var listing = JsonNode.Parse(await list.Content.ReadAsStringAsync())!;
            check(list.IsSuccessStatusCode && listing["total"]!.GetValue<int>() >= 1 && listing["items"]!.AsArray().Count == 1, "GM filtered groups are paginated");
            var group = listing["items"]![0]!["id"]!.GetValue<string>();
            using var patch = await Call(gm, HttpMethod.Patch, Groups + "/" + group, Encoding.UTF8.GetBytes("{\"status\":\"resolved\"}"), admin); check(patch.IsSuccessStatusCode, "GM can mark group resolved");
            using var detail = await Call(gm, HttpMethod.Get, $"/api/admin/diagnostics/reports/{receipt}", token: admin); check(detail.IsSuccessStatusCode, "GM reads structured detail");
            using var download = await Call(gm, HttpMethod.Get, $"/api/admin/diagnostics/reports/{receipt}/download", token: admin);
            check(download.IsSuccessStatusCode && download.Content.Headers.ContentDisposition?.DispositionType == "attachment" && download.Headers.CacheControl?.NoStore == true && (await download.Content.ReadAsByteArrayAsync()).SequenceEqual(original), "GM downloads original sanitized JSON with attachment/no-store headers");
            host!.Dispose(); host = null; await Start(); admin = await Login("diagadmin");
            using var restarted = await Call(api, HttpMethod.Post, Receive, original); check(restarted.StatusCode == HttpStatusCode.OK && JsonNode.Parse(await restarted.Content.ReadAsStringAsync())!["receiptId"]!.GetValue<string>() == receipt, "durable receipt survives restart");
            using var persistedStatus = await Call(gm, HttpMethod.Get, Groups + "?status=resolved", token: admin); check(JsonNode.Parse(await persistedStatus.Content.ReadAsStringAsync())!["total"]!.GetValue<int>() == 1, "group handling status survives restart");
            using var deleted = await Call(gm, HttpMethod.Delete, $"/api/admin/diagnostics/reports/{receipt}", token: admin); check(deleted.IsSuccessStatusCode, "GM deletes report");
            using var gone = await Call(api, HttpMethod.Post, Receive, original); check(gone.StatusCode == HttpStatusCode.Gone, "deleted event cannot resurrect");
            host!.Dispose(); host = null; await Start(); admin = await Login("diagadmin");
            using var goneRestart = await Call(api, HttpMethod.Post, Receive, original); check(goneRestart.StatusCode == HttpStatusCode.Gone, "deletion tombstone survives restart");
            using var missing = await Call(gm, HttpMethod.Get, $"/api/admin/diagnostics/reports/{receipt}/download", token: admin); check(missing.StatusCode == HttpStatusCode.NotFound, "deleted body cannot be downloaded");
            foreach (var port in new[] { api, player }) { using var script = await Call(port, HttpMethod.Get, "/admin/admin-diagnostics.js"); check(script.StatusCode == HttpStatusCode.NotFound, "diagnostics feature script only on GM listener"); }
            using var resource = await Call(gm, HttpMethod.Get, "/admin/admin-diagnostics.js"); check(resource.IsSuccessStatusCode && resource.Content.Headers.ContentType?.MediaType?.Contains("javascript") == true, "GM serves actual diagnostics feature script");
            using var dump = await Call(api, HttpMethod.Put, Receive + "/" + receipt + "/dump", new byte[] { 1 }, admin); check(dump.StatusCode == HttpStatusCode.NotFound, "no raw memory upload endpoint exists");
            host!.Dispose(); host = null;
            document["DFLegacy"]!["ClientDiagnostics"]!["Enabled"] = false;
            var expiredBytes = Body(); var expiredEvent = JsonNode.Parse(expiredBytes)!["eventId"]!.GetValue<string>();
            var seed = new ClientDiagnosticStore(options, new Clock { Now = DateTimeOffset.UtcNow.AddDays(-15) });
            check((await seed.ReceiveAsync(expiredBytes)).StatusCode == 201, "expired startup fixture is persisted before service begins");
            await File.WriteAllTextAsync(config, document.ToJsonString()); await Start();
            check(await WaitUntil(() => ReadStored(Path.Combine(root, "diagnostics", expiredEvent + ".json"))["Body"] is null), "actual disabled-receive host cleans startup-expired body without a diagnostics request");
            using var disabled = await Call(api, HttpMethod.Post, Receive, Body()); check(disabled.StatusCode == HttpStatusCode.NotFound, "operator can disable receive");
            host!.Dispose(); host = null;
            document["DFLegacy"]!["ClientDiagnostics"]!["Enabled"] = true; document["DFLegacy"]!["ClientDiagnostics"]!["SourcePerMinute"] = 1;
            await File.WriteAllTextAsync(config, document.ToJsonString()); await Start();
            using var rateFirst = await Call(api, HttpMethod.Post, Receive, original);
            using var forgedRequest = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{api}{Receive}") { Content = new ByteArrayContent(original) };
            forgedRequest.Content.Headers.ContentType = new("application/json"); forgedRequest.Headers.Add("X-Forwarded-For", "203.0.113.77");
            using var limited = await client.SendAsync(forgedRequest); check(limited.StatusCode == HttpStatusCode.TooManyRequests && limited.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(60), "rate limiting uses connection address and bounded Retry-After despite spoofed forwarding header");
            host!.Dispose(); host = null;
            document["DFLegacy"]!["ClientDiagnostics"]!["StoragePath"] = options.DataPath;
            await File.WriteAllTextAsync(config, document.ToJsonString()); await Start();
            using var brokenHealth = await Call(api, HttpMethod.Get, "/health");
            check(brokenHealth.IsSuccessStatusCode, "broken diagnostics storage never prevents game HTTP startup");
        }
        finally { host?.Dispose(); Directory.Delete(root, true); }
    }

    private sealed class Clock : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _now = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> _timers = new();
        public DateTimeOffset Now { get { lock (_gate) return _now; } set { lock (_gate) _now = value; } }
        public override DateTimeOffset GetUtcNow() => Now;
        public bool HasMinuteDelay { get { lock (_gate) return _timers.Any(timer => timer.Next == _now.AddMinutes(1)); } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state); lock (_gate) { _timers.Add(timer); timer.Change(dueTime, period); } return timer;
        }
        public void Advance(TimeSpan duration)
        {
            Action[] callbacks;
            lock (_gate) { _now += duration; callbacks = _timers.ToArray().Select(timer => timer.Due()).OfType<Action>().ToArray(); }
            foreach (var callback in callbacks) callback();
        }
        private sealed class ManualTimer(Clock owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? Next;
            private TimeSpan _period;
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) { lock (owner._gate) { if (_disposed) return false; Next = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime; _period = period; return true; } }
            public Action? Due() { if (Next is null || Next > owner._now || _disposed) return null; Next = _period > TimeSpan.Zero ? owner._now + _period : null; return () => callback(state); }
            public void Dispose() { lock (owner._gate) { _disposed = true; owner._timers.Remove(this); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private static JsonNode ReadStored(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonNode.Parse(stream)!;
    }
    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++) { if (condition()) return true; await Task.Delay(10); }
        return false;
    }
    private static async Task IdleMaintenanceAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-diagnostic-maintenance-" + Guid.NewGuid().ToString("N"));
        try {
            foreach (var enabled in new[] { true, false }) {
                var clock = new Clock(); var options = new ServerOptions { DataPath = Path.Combine(root, enabled.ToString(), "state.json"), ClientDiagnostics = new() { Enabled = enabled } };
                var store = new ClientDiagnosticStore(options, clock); var body = Body(); await store.ReceiveAsync(body);
                var path = Path.Combine(Path.GetDirectoryName(options.DataPath)!, "diagnostics", JsonNode.Parse(body)!["eventId"]!.GetValue<string>() + ".json");
                if ((object)store is not IHostedService service) { check(false, "idle diagnostics maintenance must run as a hosted service, receive enabled=" + enabled); continue; }
                await service.StartAsync(default);
                try {
                    check(await WaitUntil(() => clock.HasMinuteDelay), "maintenance schedules with injected TimeProvider, receive enabled=" + enabled);
                    clock.Advance(TimeSpan.FromDays(14));
                    check(await WaitUntil(() => ReadStored(path)["Body"] is null), "idle body expires on disk without API/store reads, receive enabled=" + enabled);
                    check(ReadStored(path)["DeletedAt"] is not null, "idle cleanup atomically preserves tombstone");
                    check(await WaitUntil(() => clock.HasMinuteDelay), "maintenance continues after idle body expiry");
                    clock.Advance(TimeSpan.FromDays(30));
                    check(await WaitUntil(() => !File.Exists(path)), "idle tombstone retention is also bounded without API/store reads");
                }
                finally { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await service.StopAsync(timeout.Token); }
            }
            var brokenClock = new Clock(); var brokenPath = Path.Combine(root, "broken", "diagnostics"); Directory.CreateDirectory(brokenPath);
            await File.WriteAllTextAsync(Path.Combine(brokenPath, Guid.NewGuid().ToString("N") + ".json"), "{");
            var broken = new ClientDiagnosticStore(new() { DataPath = Path.Combine(root, "broken", "state.json") }, brokenClock);
            if ((object)broken is IHostedService brokenService) {
                await brokenService.StartAsync(default);
                try {
                    check(await WaitUntil(() => brokenClock.HasMinuteDelay) && broken.ExecuteTask?.IsCompleted == false,
                        "malformed diagnostics remain inside the running background service");
                    check((await broken.ReceiveAsync(Body())).StatusCode == 503, "malformed background recovery fails closed for diagnostic receive");
                }
                finally { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await brokenService.StopAsync(timeout.Token); }
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static async Task StoreAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-diagnostic-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var clock = new Clock(); var options = new ServerOptions { DataPath = Path.Combine(root, "state.json") };
            var store = new ClientDiagnosticStore(options, clock); var body = Body();
            var first = await store.ReceiveAsync(body);
            check(first.StatusCode == 201 && !File.Exists(options.DataPath), "diagnostics use separate storage without creating or locking game state");
            var probeBytes = new FileInfo(Directory.EnumerateFiles(Path.Combine(root, "diagnostics"), "*.json").Single()).Length;
            var nearFull = new ClientDiagnosticStore(new() { DataPath = Path.Combine(root, "near-full", "state.json"), ClientDiagnostics = new() { MaxStorageBytes = probeBytes * 2 + 64 } }, clock);
            var nearFirst = await nearFull.ReceiveAsync(Body());
            for (var attempt = 0; attempt < 3; attempt++) await nearFull.ReceiveAsync(Body());
            try { check(nearFirst.StatusCode == 201 && await nearFull.DeleteAsync(nearFirst.Receipt!.ReceiptId, "operator"), "full byte quota reserves enough space to atomically persist deletion"); }
            catch (IOException) { check(false, "full byte quota must still permit durable deletion"); }
            byte[] Network(string context) { var value = JsonNode.Parse(Body(context: context))!; value["kind"] = "networkerror"; value["module"] = "unknown"; value["moduleHash"] = "unknown"; value["moduleOffset"] = "unknown"; return Encoding.UTF8.GetBytes(value.ToJsonString()); }
            var network = new ClientDiagnosticStore(new() { DataPath = Path.Combine(root, "network", "state.json") }, clock);
            await network.ReceiveAsync(Network("tcp port 7001 socket 10054")); await network.ReceiveAsync(Network("udp port 7002 socket 10060"));
            check((await network.ListAsync(new())).Total == 2, "unknown network faults with different protocol/port/socket summaries are not merged");
            check(store.TryBeginUpload("first", out var lease1) && store.TryBeginUpload("second", out _), "two upload slots are available");
            check(!store.TryBeginUpload("third", out _), "third concurrent upload is rejected before reading body"); lease1!.Dispose();
            check(store.TryBeginUpload("third", out var lease3), "upload slot returns after completion"); lease3!.Dispose();
            clock.Now = clock.Now.AddDays(14).AddTicks(-1);
            check((await store.ReceiveAsync(body)).StatusCode == 200, "report persists until exact 14 day boundary");
            clock.Now = clock.Now.AddTicks(1);
            check((await store.ReceiveAsync(body)).StatusCode == 410 && await store.DownloadAsync(first.Receipt!.ReceiptId) is null, "14 day expiry atomically removes body and retains tombstone");
            store = new ClientDiagnosticStore(options, clock);
            check((await store.ReceiveAsync(body)).StatusCode == 410, "expired event stays gone across reload");
            clock.Now = clock.Now.AddDays(30).AddTicks(-1);
            check((await store.ReceiveAsync(body)).StatusCode == 410, "deletion digest retained for full 30 days");
            clock.Now = clock.Now.AddTicks(1);
            check((await store.ReceiveAsync(body)).StatusCode == 201, "deduplication promise ends at bounded 30 day retention");

            var fullOptions = new ServerOptions { DataPath = Path.Combine(root, "full", "state.json"), ClientDiagnostics = new() { MaxDeduplicationEntries = 1 } };
            var full = new ClientDiagnosticStore(fullOptions, clock); var stored = await full.ReceiveAsync(Body()); await full.DeleteAsync(stored.Receipt!.ReceiptId, "operator");
            check((await full.ReceiveAsync(Body())).StatusCode == 503, "full tombstone budget rejects new IDs without dropping promise");
            clock.Now = clock.Now.AddDays(30);
            check((await full.ReceiveAsync(Body())).StatusCode == 201, "expired tombstone frees bounded capacity");
            var tiny = new ClientDiagnosticStore(new() { DataPath = Path.Combine(root, "tiny", "state.json"), ClientDiagnostics = new() { MaxStorageBytes = 1024 } }, clock);
            check((await tiny.ReceiveAsync(Body(context: new string('x', 2048)))).StatusCode == 503, "serialized body and temporary file count against byte quota");

            var rate = new ClientDiagnosticStore(new() { DataPath = Path.Combine(root, "rate", "state.json"), ClientDiagnostics = new() { SourcePerMinute = 1, SourcePerHour = 2, GlobalPerMinute = 2, GlobalPerHour = 3 } }, clock);
            check(rate.TryBeginUpload("a", out var firstLease), "first source allowed by rate budget"); firstLease!.Dispose();
            check(!rate.TryBeginUpload("a", out _), "same source minute rate is enforced");
            check(rate.TryBeginUpload("b", out var secondLease), "different source retains independent budget"); secondLease!.Dispose();
            check(!rate.TryBeginUpload("c", out _), "global minute rate is enforced");
            clock.Now = clock.Now.AddMinutes(1);
            check(rate.TryBeginUpload("a", out var nextLease), "minute budget refreshes with injected clock"); nextLease!.Dispose();
            check(!rate.TryBeginUpload("a", out _) && !rate.TryBeginUpload("c", out _), "source and global hour ceilings remain enforced");
            clock.Now = clock.Now.AddHours(1);
            check(rate.TryBeginUpload("a", out var resetLease), "hour budget refreshes with injected clock"); resetLease!.Dispose();

            var corruptDirectory = Path.Combine(root, "corrupt", "diagnostics"); Directory.CreateDirectory(corruptDirectory);
            var corruptId = Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Path.Combine(corruptDirectory, corruptId + ".json"), JsonSerializer.Serialize(new { EventId = corruptId, ReceiptId = Guid.NewGuid().ToString("N") }));
            var corrupt = new ClientDiagnosticStore(new() { DataPath = Path.Combine(root, "corrupt", "state.json") }, clock);
            try { check((await corrupt.ReceiveAsync(Body())).StatusCode == 503, "corrupt index fails closed instead of forgetting old receipts"); }
            catch (Exception exception) { check(false, "corrupt index must not throw unhandled " + exception.GetType().Name); }
            var retainedFile = Directory.EnumerateFiles(Path.Combine(root, "diagnostics"), "*.json").Single();
            var locked = new ClientDiagnosticStore(options, clock);
            using (var fileLock = new FileStream(retainedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                try { await locked.ReceiveAsync(body); } catch (IOException) { }
            }
            var afterReadFailure = await locked.ReceiveAsync(body);
            check(afterReadFailure.StatusCode is 200 or 503, "failed recovery read must never forget durable idempotency and re-create event");
            // A torn unpublished temporary file must never be admitted as a committed report.
            var recoveryDirectory = Path.Combine(root, "recovery", "diagnostics"); Directory.CreateDirectory(recoveryDirectory);
            await File.WriteAllTextAsync(Path.Combine(recoveryDirectory, "interrupted.tmp"), "incomplete");
            var recovery = new ClientDiagnosticStore(new() { DataPath = Path.Combine(root, "recovery", "state.json") }, clock);
            check((await recovery.ReceiveAsync(Body())).StatusCode == 201 && !Directory.EnumerateFiles(recoveryDirectory, "*.tmp").Any(), "restart discards incomplete unpublished temporary write");
        }
        finally { Directory.Delete(root, true); }
    }
}
