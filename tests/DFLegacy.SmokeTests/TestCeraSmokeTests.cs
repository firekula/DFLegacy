using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;

internal static class TestCeraSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var flag = typeof(ServerOptions).GetProperty("EnableTestCeraTopUp");
        check(flag is not null && Equals(flag.GetValue(new ServerOptions()), false), "test Cera defaults off");
        var grant = typeof(JsonGameStore).GetMethod("GrantTestCeraToCharacterAsync");
        check(grant is not null, "test Cera character grant exists");
        if (flag is null || grant is null) return;
        check(AdminAccessPolicy.Resolve("POST", "/api/client-pay/test-cera").Access == AdminAccess.Admin, "test Cera fails closed by default");
        check(AdminAccessPolicy.Resolve("POST", "/api/client-pay/test-diamond").Access == AdminAccess.Admin, "sandbox diamond fails closed by default");
        check(AdminAccessPolicy.Resolve("POST", "/api/client-pay/test-diamond", true).Access == AdminAccess.Public, "sandbox diamond enabled for all account kinds");
        check(AdminAccessPolicy.Resolve("PUT", "/api/admin/users/ordinary/kind", true).Access == AdminAccess.Admin, "changing kind remains GM-only");
        var policy = typeof(AdminAccessPolicy).GetMethods().Single(m => m.Name == "Resolve");
        foreach (var path in new[] { "/api/client-pay/test-cera", "/api/client-pay/cera", "/api/client-pay/diamond" })
            check(((AdminAccessDecision)policy.Invoke(null, new object[] { "POST", path, true })!).Access ==
                (path.EndsWith("/test-cera") ? AdminAccess.Public : AdminAccess.Admin), "opt-in policy isolates " + path);
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-test-cera-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new ServerOptions { DataPath = Path.Combine(root, "state.json"), SeedTestAccount = false, ScriptPvfPath = "", SkillScriptPath = root };
            JsonGameStore Open() => new(options, NullLogger<JsonGameStore>.Instance);
            var store = Open(); await store.InitializeAsync();
            await store.CreateAccountAsync(new("testtopup", "password", TestAccount: true, OicqCode: "123456"));
            await store.CreateAccountAsync(new("ordinary", "password", OicqCode: "123456"));
            await store.CreateCharacterAsync("testtopup", new("Target"));
            await store.CreateCharacterAsync("ordinary", new("Normal"));
            async Task<bool> Grant(string name, int amount)
            {
                var task = (Task)grant.Invoke(store, new object[] { name, amount, CancellationToken.None })!;
                await task;
                var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
                return (bool)result.GetType().GetProperty("Success")!.GetValue(result)!;
            }
            check(await Grant("tArGeT", 7), "character grant accepts case-insensitive name and non-hundred amount");
            check(await Grant("Normal", 7), "character Cera grant accepts ordinary accounts");
            var reload = Open(); await reload.InitializeAsync();
            check((await reload.GetAccountAsync("testtopup"))!.Cash == 50007 &&
                (await reload.GetCharactersAsync("testtopup")).All(c => c.Cash == 50007), "grant persists shared account and character Cera");
            check((await reload.GetAccountAsync("ordinary"))!.Cash == 7, "ordinary account Cera grant persists");
            foreach (var pair in new[] { ("Missing", 7), ("Target", 0), ("Target", -1), ("Target", 1000001) })
                check(!await Grant(pair.Item1, pair.Item2), "grant rejects " + pair);
            var sessions = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
            var target = (await store.GetCharactersAsync("testtopup")).Single(c => c.Name == "Target");
            uint notifiedCash = 0;
            var persistedAtNotification = false;
            var lease = await sessions.ClaimAsync(target.Id, Guid.NewGuid(), () => { }, CancellationToken.None,
                ceraInfo: cash => {
                    notifiedCash = cash;
                    var saved = JsonNode.Parse(File.ReadAllText(options.DataPath))!;
                    persistedAtNotification = saved["Accounts"]!.AsArray().Any(a => a!["UserName"]!.GetValue<string>() == "testtopup" && a["Cash"]!.GetValue<int>() == cash);
                });
            async Task<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult> Post(string body, string type = "application/x-www-form-urlencoded")
            {
                var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
                context.Request.ContentType = type;
                context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
                var response = (Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult)await TestCeraEndpoint.HandleAsync(context.Request, options, store, sessions, CancellationToken.None);
                check(context.Response.Headers.CacheControl == "no-store", "HTML responses disable caching");
                return response;
            }
            options.EnableTestCeraTopUp = true;
            var success = await Post("characterName=Target&amount=3");
            check(((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)success).StatusCode == 200 && notifiedCash == 50010 && persistedAtNotification,
                "form endpoint returns success and notifies online session after persistence");
            var ordinarySuccess = await Post("characterName=Normal&amount=1");
            check(ordinarySuccess.StatusCode == 200 && (await store.GetAccountAsync("ordinary"))!.Cash == 8,
                "form endpoint grants ordinary accounts by character name");
            foreach (var body in new[] { "characterName=Target&amount=1.5", "characterName=Target&amount=+2", "characterName=Target&amount=1&amount=2" })
            {
                var rejected = await Post(body);
                check(((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)rejected).StatusCode == 400 && notifiedCash == 50010,
                    "invalid form never notifies: " + body);
                check(System.Text.Encoding.UTF8.GetByteCount(rejected.ResponseContent!) > 1024, "iframe errors exceed IE friendly error threshold");
            }
            try
            {
                var malformed = await Post(new string('x', 3000) + "=1");
                check(malformed.StatusCode == 400, "malformed form returns visible validation error");
            }
            catch (InvalidDataException) { check(false, "malformed form must not escape as server error"); }
            var invalidCharset = await Post("characterName=Missing&amount=1", "application/x-www-form-urlencoded; charset=not-a-real-encoding");
            check(invalidCharset.StatusCode == 400, "invalid form charset remains a visible validation response");
            var settingsMethod = typeof(TestCeraEndpoint).GetMethod("Settings");
            check(settingsMethod is not null, "settings endpoint exposes testable no-store response");
            if (settingsMethod is not null)
            {
                var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
                var settings = (Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult)settingsMethod.Invoke(null, new object[] { context.Response, options })!;
                check(context.Response.Headers.CacheControl == "no-store" && settings.ResponseContent!.Contains("testCeraEnabled:true"), "settings JS is uncached and reflects enabled option");
            }
            var json = await Post("{}", "application/json");
            check(((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)json).StatusCode == 400, "endpoint rejects JSON submissions");
            options.EnableTestCeraTopUp = false;
            var disabled = await Post("characterName=Target&amount=3");
            check(((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)disabled).StatusCode == 400 && notifiedCash == 50010, "disabled endpoint cannot grant");
            sessions.Release(lease);
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Grant("Target", 1)));
            check(concurrent.All(ok => ok) && (await store.GetAccountAsync("testtopup"))!.Cash == 50018, "concurrent grants preserve every increment atomically");
            check(await Grant("Target", 1_000_000), "upper amount boundary is accepted");
            await store.GrantCeraToAccountAsync("testtopup", int.MaxValue - 47);
            check(!await Grant("Target", 1), "grant rejects overflow without saturation");
            var state = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
            var normal = state["Accounts"]!.AsArray().Single(a => a!["UserName"]!.GetValue<string>() == "ordinary")!;
            normal["Characters"]![0]!["Name"] = "TARGET";
            await File.WriteAllTextAsync(options.DataPath, state.ToJsonString());
            store = Open(); await store.InitializeAsync();
            check(!await Grant("Target", 1), "ambiguous character ownership refuses grant");
        }
        finally { Directory.Delete(root, true); }
        await TestDiamondSmokeTests.RunAsync(check);
    }
}
