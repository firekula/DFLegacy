using System.Text;
using System.Text.Json.Nodes;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

internal static class TestDiamondSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var grant = typeof(JsonGameStore).GetMethod("GrantTestDiamondToCharacterAsync");
        var handler = typeof(TestCeraEndpoint).GetMethod("HandleDiamondAsync");
        check(grant is not null && handler is not null, "sandbox diamond grant and form endpoint exist");
        if (grant is null || handler is null) return;
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-test-diamond-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new ServerOptions { DataPath = Path.Combine(root, "state.json"), SeedTestAccount = false, ScriptPvfPath = "", SkillScriptPath = root };
            JsonGameStore Open() => new(options, NullLogger<JsonGameStore>.Instance);
            var store = Open();
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("ordinarydiamond", "password", OicqCode: "123456"));
            await store.CreateAccountAsync(new("testdiamond", "password", TestAccount: true, OicqCode: "123456"));
            await store.CreateCharacterAsync("ordinarydiamond", new("黑钻角色"));
            await store.CreateCharacterAsync("ordinarydiamond", new("Sibling"));
            await store.CreateCharacterAsync("testdiamond", new("TestDiamond"));
            async Task<ClientDiamondGrantResult> Grant(string name, int days)
            {
                var pending = (Task<ClientDiamondGrantResult>)grant.Invoke(store, new object[] { name, days, CancellationToken.None })!;
                return await pending;
            }
            foreach (var pair in new[] { ("Missing", 30), ("", 30), ("TestDiamond", 0), ("TestDiamond", -1), ("TestDiamond", 31), ("TestDiamond", int.MaxValue) })
                check(!(await Grant(pair.Item1, pair.Item2)).Success, "diamond validates character and 30 day duration: " + pair);

            var characters = await store.GetCharactersAsync("ordinarydiamond");
            foreach (var character in characters) await store.SetCharacterWeaknessRecoveryAsync(character.Id, 37);
            var sessions = new CharacterSessionRegistry(NullLogger<CharacterSessionRegistry>.Instance);
            var premiumNotifications = 0;
            var weaknessNotifications = 0;
            var persistedBeforeNotification = true;
            var leases = new List<CharacterSessionLease>();
            foreach (var character in characters)
                leases.Add(await sessions.ClaimAsync(character.Id, Guid.NewGuid(), () => { }, CancellationToken.None,
                    premiumInfo: (service, seconds) =>
                    {
                        premiumNotifications++;
                        var account = JsonNode.Parse(File.ReadAllText(options.DataPath))!["Accounts"]!.AsArray()
                            .Single(a => a!["UserName"]!.GetValue<string>() == "ordinarydiamond")!;
                        persistedBeforeNotification &= service == GameProtocolEngine.BlackDiamondServiceType && seconds >= 2_591_990 &&
                            account["PremiumServices"]!.AsArray().Any(p => p!["ServiceType"]!.GetValue<byte>() == GameProtocolEngine.BlackDiamondServiceType);
                    }, weaknessRecovery: recovery =>
                    {
                        weaknessNotifications++;
                        var account = JsonNode.Parse(File.ReadAllText(options.DataPath))!["Accounts"]!.AsArray()
                            .Single(a => a!["UserName"]!.GetValue<string>() == "ordinarydiamond")!;
                        persistedBeforeNotification &= recovery == 100 && account["Characters"]!.AsArray().All(c => c!["WeaknessRecovery"]!.GetValue<byte>() == 100);
                    }));
            async Task<ContentHttpResult> Post(string body, string type = "application/x-www-form-urlencoded; charset=UTF-8")
            {
                var context = new DefaultHttpContext();
                context.Request.ContentType = type;
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
                var pending = (Task<IResult>)handler.Invoke(null, new object[] { context.Request, options, store, sessions, CancellationToken.None })!;
                var result = (ContentHttpResult)await pending;
                check(context.Response.Headers.CacheControl == "no-store", "diamond HTML disables caching");
                check(result.ContentType?.Contains("text/html") == true && Encoding.UTF8.GetByteCount(result.ResponseContent!) > 1024,
                    "diamond iframe responses render padded HTML");
                return result;
            }
            check((await Post("characterName=" + Uri.EscapeDataString("黑钻角色") + "&days=30")).StatusCode == 400 && premiumNotifications == 0,
                "diamond endpoint defaults disabled without notifications");
            options.EnableTestCeraTopUp = true;
            foreach (var body in new[] { "characterName=Missing&days=30", "characterName=Sibling&days=1", "characterName=Sibling&days=30.0", "characterName=Sibling&days=30&days=30", "characterName=Sibling&characterName=TestDiamond&days=30" })
                check((await Post(body)).StatusCode == 400 && premiumNotifications == 0, "invalid diamond form cannot notify: " + body);
            check((await Post("{}", "application/json")).StatusCode == 400, "diamond form rejects JSON");
            check((await Post(new string('x', 3000) + "=1")).StatusCode == 400, "malformed diamond form returns HTML validation");
            check((await Post("characterName=Missing&days=30", "application/x-www-form-urlencoded; charset=not-a-real-encoding")).StatusCode == 400,
                "invalid diamond charset returns HTML validation");
            var success = await Post("characterName=" + Uri.EscapeDataString("黑钻角色") + "&days=30");
            check(success.StatusCode == 200 && premiumNotifications == 2 && weaknessNotifications == 2 && persistedBeforeNotification,
                "UTF8 character diamond grants notify all account characters after persisted premium and weakness recovery");
            var firstExpiry = (await store.GetPremiumServiceAsync("ordinarydiamond")).ExpiresAt;
            var reloaded = Open(); await reloaded.InitializeAsync();
            check((await reloaded.GetPremiumServiceAsync("ordinarydiamond")).ExpiresAt == firstExpiry &&
                (await reloaded.GetCharactersAsync("ordinarydiamond")).All(c => !c.IsWeakened), "ordinary diamond expiry and weakness recovery persist");
            var extended = await Grant("sIBLING", 30);
            check(extended.Success && extended.ExpiresAt == firstExpiry + 2_592_000, "diamond extension reuses existing active expiry across sibling characters");
            check((await Grant("testdiamond", 30)).Success, "diamond grant accepts test accounts case insensitively");
            reloaded = Open(); await reloaded.InitializeAsync();
            check((await reloaded.GetPremiumServiceAsync("testdiamond")).Active, "test account diamond expiry persists");
            options.EnableTestCeraTopUp = false;
            check((await Post("characterName=Sibling&days=30")).StatusCode == 400 && premiumNotifications == 2,
                "disabled diamond endpoint cannot grant or notify");
            foreach (var lease in leases) sessions.Release(lease);

            var state = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
            state["Accounts"]!.AsArray().Single(a => a!["UserName"]!.GetValue<string>() == "testdiamond")!["Characters"]![0]!["Name"] = "SIBLING";
            await File.WriteAllTextAsync(options.DataPath, state.ToJsonString());
            store = Open(); await store.InitializeAsync();
            check(!(await Grant("Sibling", 30)).Success, "ambiguous character name rejects diamond grant");
        }
        finally { Directory.Delete(root, true); }
    }
}
