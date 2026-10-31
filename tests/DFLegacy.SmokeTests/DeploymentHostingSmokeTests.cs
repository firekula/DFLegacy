using DFLegacy.Server;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

internal static class DeploymentHostingSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-hosting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // A missing/ignored SeedTestAccount binding would create a public test login.
            var disabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataPath"] = Path.Combine(root, "disabled.json"),
                ["SeedTestAccount"] = "false"
            }).Build().Get<ServerOptions>()!;
            var empty = Store(disabled);
            await empty.InitializeAsync();
            check((await empty.GetAccountsAsync()).Length == 0,
                "seed-disabled fresh store contains no test account");

            var legacyOptions = new ServerOptions { DataPath = Path.Combine(root, "legacy.json") };
            var legacy = Store(legacyOptions);
            await legacy.InitializeAsync();
            check(await legacy.AuthenticateAsync("test", "test") is not null,
                "legacy default still seeds test/test");
            var disabledLegacy = Store(new() { DataPath = legacyOptions.DataPath, SeedTestAccount = false });
            await disabledLegacy.InitializeAsync();
            check(await disabledLegacy.AuthenticateAsync("test", "test") is not null,
                "disabling new test-account seeding retains an existing legacy account");
            var created = await empty.CreateAccountAsync(new("keeper", "preserved-password", OicqCode: "123456"));
            check(created.Created, "existing-save fixture creates an ordinary account");
            var reopened = Store(disabled);
            await reopened.InitializeAsync();
            var accounts = await reopened.GetAccountsAsync();
            check(accounts.Length == 1 && accounts[0].UserName == "keeper"
                    && accounts[0].AccountUid == created.Account!.AccountUid
                    && await reopened.AuthenticateAsync("keeper", "preserved-password") is not null,
                "seed-disabled restart preserves existing accounts and credentials without reseeding");
            await CheckProcessesAsync(root, check);
            CheckSourceAndSecret(check);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static JsonGameStore Store(ServerOptions options) =>
        new(options, NullLogger<JsonGameStore>.Instance);

    private static void CheckSourceAndSecret(Action<bool, string> check)
    {
        var cases = new (string? Source, string? Configured, string? Supplied, bool Allowed)[]
        {
            ("127.0.0.1", "private", "private", true),
            ("::1", "private", "private", true),
            ("::ffff:127.0.0.1", "private", "private", true),
            ("192.0.2.10", "private", "private", false),
            ("::ffff:192.0.2.10", "private", "private", false),
            (null, "private", "private", false),
            ("127.0.0.1", "private", "incorrect", false),
            ("127.0.0.1", "private", null, false),
            ("127.0.0.1", null, "private", false),
            ("127.0.0.1", "", "", false)
        };
        foreach (var row in cases)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = row.Source is null ? null : IPAddress.Parse(row.Source);
            context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
            if (row.Supplied is not null) context.Request.Headers["X-DFLegacy-Manager"] = row.Supplied;
            check(ManagerControl.IsAuthorized(context, row.Configured) == row.Allowed,
                "manager control enforces source and independent secret: " + (row.Source ?? "missing source")
                    + "/" + (row.Allowed ? "allowed" : "denied"));
        }
        var multiple = new DefaultHttpContext();
        multiple.Connection.RemoteIpAddress = IPAddress.Loopback;
        multiple.Request.Headers["X-DFLegacy-Manager"] = new StringValues(new[] { "private", "private" });
        check(!ManagerControl.IsAuthorized(multiple, "private"), "duplicate manager secret headers are rejected");
    }

    private static async Task CheckProcessesAsync(string root, Action<bool, string> check)
    {
        var portListener = new TcpListener(IPAddress.Loopback, 0);
        portListener.Start();
        var port = ((IPEndPoint)portListener.LocalEndpoint).Port;
        portListener.Stop();
        var dataPath = Path.Combine(root, "process state.json");
        var configPath = Path.Combine(root, "selected config.json");
        var work = Path.Combine(root, "unrelated cwd");
        Directory.CreateDirectory(work);
        var scripts = Path.Combine(root, "scripts");
        Directory.CreateDirectory(Path.Combine(scripts, "creature"));
        await File.WriteAllTextAsync(Path.Combine(scripts, "creature", "exptable.tbl"), "10\n100\n");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
        {
            DFLegacy = new
            {
                DataPath = dataPath,
                SeedTestAccount = false,
                ScriptPvfPath = Path.Combine(root, "missing.pvf"),
                SkillScriptPath = scripts,
                Admin = new { Host = "127.0.0.1", Port = port },
                Entrance = new { Enabled = false },
                CharacterDatagram = new { Enabled = false },
                GameplayDatagram = new { Enabled = false },
                FallbackRelay = new { Enabled = false },
                GameProbe = new { Enabled = false }
            }
        }));
        const string password = "initial-password-keep-private";
        const string recovery = "987654321";
        var input = JsonSerializer.Serialize(new { userName = "firstadmin", password, oicqCode = recovery });
        var offlineConfigPath = Path.Combine(root, "offline listener validation.json");
        var offlineConfig = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(configPath))!;
        offlineConfig["DFLegacy"]!["Pvp"] = new System.Text.Json.Nodes.JsonObject { ["Enabled"] = true, ["Port"] = 0 };
        offlineConfig["DFLegacy"]!["FallbackRelay"] = new System.Text.Json.Nodes.JsonObject { ["Enabled"] = true, ["AdvertisedHost"] = "offline.example.test", ["TcpPort"] = 0 };
        await File.WriteAllTextAsync(offlineConfigPath, offlineConfig.ToJsonString());
        using (var init = Child.Start(offlineConfigPath, work, null, "--initialize-admin"))
        {
            await init.SendAsync(input);
            var exited = await init.WaitAsync();
            check(exited && init.Process.ExitCode == 0,
                "offline admin command uses selected config outside cwd without validating network listeners");
            if (!exited || init.Process.ExitCode != 0) return;
            var output = await init.Output;
            var error = await init.Error;
            var json = TryJson(output);
            check(json is { } result && result.GetProperty("success").GetBoolean()
                    && !output.Contains(password) && !output.Contains(recovery)
                    && !error.Contains(password) && !error.Contains(recovery),
                "initialization emits machine-readable success without password or recovery code");
        }
        var initialized = Store(new() { DataPath = dataPath, SeedTestAccount = false });
        await initialized.InitializeAsync();
        var initialAccounts = await initialized.GetAccountsAsync();
        check(initialAccounts.Length == 1 && initialAccounts[0].UserName == "firstadmin"
                && await initialized.GetRoleAsync("firstadmin") == AccountRole.Admin
                && await initialized.AuthenticateAsync("firstadmin", password) is not null,
            "selected seed-disabled config creates only the requested administrator through the store");
        var beforeDuplicate = await File.ReadAllBytesAsync(dataPath);
        using (var duplicate = Child.Start(configPath, work, null, "--initialize-admin"))
        {
            await duplicate.SendAsync(JsonSerializer.Serialize(new
            { userName = "FIRSTADMIN", password = "replacement-password", oicqCode = "111111" }));
            check(await duplicate.WaitAsync() && duplicate.Process.ExitCode == 2
                    && TryJson(await duplicate.Output) is { } failure
                    && failure.GetProperty("status").GetInt32() == 409,
                "case-insensitive duplicate administrator is explicitly rejected with conflict exit");
        }
        var afterDuplicate = await File.ReadAllBytesAsync(dataPath);
        check(beforeDuplicate.SequenceEqual(afterDuplicate),
            "duplicate bootstrap does not change the existing save or password");

        using (var second = Child.Start(configPath, work, null, "--initialize-admin"))
        {
            await second.SendAsync(JsonSerializer.Serialize(new
            { userName = "secondadmin", password = "second-password", oicqCode = "111111" }));
            check(await second.WaitAsync() && second.Process.ExitCode == 0,
                "offline initialization can add an administrator to a nonempty save");
        }
        var reopened = Store(new() { DataPath = dataPath, SeedTestAccount = false });
        await reopened.InitializeAsync();
        check((await reopened.GetAccountsAsync()).Length == 2
                && await reopened.AuthenticateAsync("firstadmin", password) is not null
                && await reopened.GetRoleAsync("secondadmin") == AccountRole.Admin,
            "adding a new administrator retains the previous account and credentials");

        using (var relative = Child.Start("selected config.json", work, null, "--initialize-admin"))
        {
            await relative.SendAsync(input);
            check(await relative.WaitAsync() && relative.Process.ExitCode == 2,
                "relative explicit configuration is rejected");
        }
        using (var malformed = Child.Start(configPath, work, null, "--initialize-admin"))
        {
            await malformed.SendAsync("{bad-json");
            check(await malformed.WaitAsync() && malformed.Process.ExitCode == 2
                    && !((await malformed.Output) + (await malformed.Error)).Contains(password),
                "malformed initializer input fails without starting a host or printing credentials");
        }
        using (var missing = Child.Start(Path.Combine(root, "missing-config.json"), work, null, "--initialize-admin"))
        {
            await missing.SendAsync(input);
            check(await missing.WaitAsync() && missing.Process.ExitCode == 2,
                "missing explicit config fails cleanly with exit 2");
        }

        const string secret = "manager-private-token-1234567890";
        using var host = Child.Start(configPath, work, secret);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(2) };
        var ready = false;
        for (var attempt = 0; attempt < 80 && !host.Process.HasExited; attempt++)
        {
            try
            {
                using var response = await client.GetAsync("/health");
                if (response.IsSuccessStatusCode) { ready = true; break; }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(100);
        }
        check(ready, "host starts with selected external config and disabled game listeners");
        if (!ready)
        {
            if (host.Process.HasExited)
                Console.Error.WriteLine("Hosting fixture failed: " + await host.Output + await host.Error);
            return;
        }
        using (var missing = await client.GetAsync("/internal/manager/status"))
            check(missing.StatusCode == HttpStatusCode.Unauthorized, "manager status requires its private secret");
        client.DefaultRequestHeaders.Add("X-DFLegacy-Manager", "incorrect-secret");
        using (var wrong = await client.PostAsync("/internal/manager/stop", null))
            check(wrong.StatusCode == HttpStatusCode.Unauthorized && !host.Process.HasExited,
                "incorrect manager secret cannot stop the host");
        client.DefaultRequestHeaders.Remove("X-DFLegacy-Manager");
        client.DefaultRequestHeaders.Add("X-DFLegacy-Manager", secret);
        using (var managed = await client.GetAsync("/internal/manager/status"))
        {
            var response = TryJson(await managed.Content.ReadAsStringAsync());
            check(managed.StatusCode == HttpStatusCode.OK && response is { } status
                    && status.GetProperty("managed").GetBoolean(),
                "valid manager secret bypasses game auth and confirms process ownership");
        }
        using (var game = await client.GetAsync("/api/status"))
            check(game.StatusCode == HttpStatusCode.Unauthorized,
                "manager secret grants no game administrator access");

        foreach (var arguments in new[] { Array.Empty<string>(), new[] { "--initialize-admin" }, new[] { "--add-cash-all", "1" }, new[] { "--reset-maximum-transfer-roster" } })
        {
            using var contender = Child.Start(configPath, work, null, arguments);
            await contender.SendAsync(input);
            var exited = await contender.WaitAsync();
            var output = (await contender.Output) + (await contender.Error);
            check(exited && contender.Process.ExitCode == 2 && output.Contains("in use", StringComparison.OrdinalIgnoreCase),
                "OS store lease excludes a second " + (arguments.Length == 0 ? "host" : arguments[0]));
        }
        using (var stop = await client.PostAsync("/internal/manager/stop", null))
            check(stop.StatusCode == HttpStatusCode.Accepted && TryJson(await stop.Content.ReadAsStringAsync()) is not null,
                "manager stop sends a complete 202 JSON response");
        check(await host.WaitAsync() && host.Process.ExitCode == 0,
            "manager stop gracefully exits the owned host");
        check(!((await host.Output) + (await host.Error)).Contains(secret),
            "server logs never expose the manager secret");
    }

    private static JsonElement? TryJson(string text)
    {
        try { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    private sealed class Child : IDisposable
    {
        public Process Process { get; }
        public Task<string> Output { get; }
        public Task<string> Error { get; }

        private Child(Process process)
        {
            Process = process;
            Output = process.StandardOutput.ReadToEndAsync();
            Error = process.StandardError.ReadToEndAsync();
        }

        public static Child Start(string config, string cwd, string? secret, params string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = cwd, UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(typeof(JsonGameStore).Assembly.Location);
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(config);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            foreach (var name in start.Environment.Keys.Where(key => key.StartsWith("DFLegacy__", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(name);
            if (secret is null) start.Environment.Remove("DFLEGACY_MANAGER_CONTROL_TOKEN");
            else start.Environment["DFLEGACY_MANAGER_CONTROL_TOKEN"] = secret;
            return new Child(Process.Start(start)!);
        }

        public async Task SendAsync(string input)
        {
            try { await Process.StandardInput.WriteLineAsync(input); Process.StandardInput.Close(); }
            catch (IOException) { }
        }

        public async Task<bool> WaitAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await Process.WaitForExitAsync(timeout.Token); return true; }
            catch (OperationCanceledException)
            {
                if (!Process.HasExited) Process.Kill(entireProcessTree: true);
                await Process.WaitForExitAsync();
                return false;
            }
        }

        public void Dispose()
        {
            if (!Process.HasExited) { Process.Kill(entireProcessTree: true); Process.WaitForExit(); }
            Process.Dispose();
        }
    }
}
