using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class AdminAuthSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        CheckPolicyTable(check);
        CheckBearerParsing(check);
        CheckTokenLifecycle(check);
        CheckThrottleSemantics(check);
        CheckBoundedMemory(check);
        await CheckRoleModelAsync(check);
    }

    // 权限表是纯函数：这组断言就是"哪一个接口需要什么权限"的权威清单，
    // 改动路由时这里会先红，避免漏挂鉴权。
    private static void CheckPolicyTable(Action<bool, string> check)
    {
        const string characterId = "11111111-2222-3333-4444-555555555555";
        var cases = new (string Method, string Path, AdminAccess Access, string? Account)[]
        {
            ("GET", "/", AdminAccess.Public, null),
            ("GET", "/health", AdminAccess.Public, null),
            ("GET", "/admin/index.html", AdminAccess.Public, null),
            ("GET", "/admin/admin.js", AdminAccess.Public, null),
            ("GET", "/client/client_pay.htm", AdminAccess.Public, null),
            ("POST", "/api/accounts", AdminAccess.Public, null),
            ("POST", "/api/auth/login", AdminAccess.Public, null),
            ("POST", "/api/accounts/alice/password/reset", AdminAccess.Public, null),
            ("POST", "/api/auth/logout", AdminAccess.Authenticated, null),
            ("PUT", "/api/accounts/alice/password", AdminAccess.SelfAccount, "alice"),
            ("GET", "/api/accounts/alice/characters", AdminAccess.SelfAccount, "alice"),
            ("POST", "/api/accounts/alice/characters", AdminAccess.Admin, null),
            ("GET", "/api/accounts", AdminAccess.Admin, null),
            ("GET", "/api/status", AdminAccess.Admin, null),
            ("GET", "/api/sessions", AdminAccess.Admin, null),
            ("GET", "/api/packets", AdminAccess.Admin, null),
            ("GET", "/api/admin/characters", AdminAccess.Admin, null),
            ("GET", "/api/admin/items", AdminAccess.Admin, null),
            ("GET", "/api/admin/dungeons", AdminAccess.Admin, null),
            ("GET", "/api/admin/message-types", AdminAccess.Admin, null),
            ("GET", "/api/admin/users", AdminAccess.Admin, null),
            ("PUT", "/api/admin/users/bob/role", AdminAccess.Admin, null),
            ("POST", "/api/admin/characters/" + characterId + "/mail", AdminAccess.Admin, null),
            ("PUT", "/api/admin/characters/" + characterId + "/dungeons/3", AdminAccess.Admin, null),
            ("POST", "/api/admin/broadcast/message", AdminAccess.Admin, null),
            ("POST", "/api/admin/broadcast/popup", AdminAccess.Admin, null),
            ("POST", "/api/client-pay/cera", AdminAccess.Admin, null),
            ("POST", "/api/client-pay/diamond", AdminAccess.Admin, null),
            ("POST", "/api/client-pay/premium", AdminAccess.Admin, null),
            ("PUT", "/api/characters/" + characterId + "/fatigue", AdminAccess.Admin, null),
            ("GET", "/api/characters/" + characterId + "/mail", AdminAccess.Admin, null),
            ("POST", "/api/characters/" + characterId + "/mail", AdminAccess.Admin, null),
            ("POST", "/api/characters/" + characterId + "/message", AdminAccess.Admin, null),
            ("POST", "/api/characters/" + characterId + "/popup", AdminAccess.Admin, null),
            // 大小写不敏感：ASP.NET 路由也是不敏感的
            ("get", "/HEALTH", AdminAccess.Public, null),
            ("put", "/API/ACCOUNTS/Alice/PASSWORD", AdminAccess.SelfAccount, "Alice"),
            // 末尾斜杠归一化：路由对它是宽容的，权限表也要一致（否则登录会被判成管理员接口）
            ("POST", "/api/auth/login/", AdminAccess.Public, null),
            ("POST", "/api/accounts/", AdminAccess.Public, null),
            ("GET", "/api/accounts/alice/characters/", AdminAccess.SelfAccount, "alice"),
            ("PUT", "/api/accounts/alice/password/", AdminAccess.SelfAccount, "alice"),
            // 未登记的路径一律 fail-closed（含将来新增的 /admin/**、/client/** 路由）
            ("GET", "/admin/secret-later", AdminAccess.Public, null),
            ("GET", "/some-unknown-path", AdminAccess.Admin, null),
            ("GET", "/clientx", AdminAccess.Admin, null)
        };

        foreach (var (method, path, access, account) in cases)
        {
            var decision = AdminAccessPolicy.Resolve(method, path);
            check(decision.Access == access && decision.AccountName == account,
                $"admin policy: {method} {path} -> {access}" +
                (account is null ? "" : $" ({account})") +
                $" (got {decision.Access}/{decision.AccountName ?? "-"})");
        }
    }

    private static void CheckBearerParsing(Action<bool, string> check)
    {
        check(AdminAccessPolicy.BearerToken("Bearer abc.def") == "abc.def", "bearer token is extracted");
        check(AdminAccessPolicy.BearerToken("bearer token-value") == "token-value",
            "bearer scheme is case-insensitive");
        check(AdminAccessPolicy.BearerToken("Basic abc") is null, "basic auth is not accepted as bearer");
        check(AdminAccessPolicy.BearerToken(null) is null, "missing header yields no token");
        check(AdminAccessPolicy.BearerToken("Bearer   ") is null, "blank bearer token yields no token");
    }

    private static void CheckTokenLifecycle(Action<bool, string> check)
    {
        var start = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(start);
        var options = new ServerOptions();
        options.Admin.TokenHours = 12;
        options.Admin.MaxTokenLifetimeHours = 24;
        var auth = new AdminAuthService(options, clock);

        var issued = auth.IssueToken("alice", AccountRole.Admin);
        check(issued.Success && !string.IsNullOrEmpty(issued.Token)
                && issued.Role == AccountRole.Admin && issued.ExpiresAt == start.AddHours(12),
            "login issues a token with the configured lifetime");
        check(auth.Validate(issued.Token) is { } v1
                && v1.UserName == "alice" && v1.Role == AccountRole.Admin,
            "issued token validates and carries its identity and role");

        var second = auth.IssueToken("bob", AccountRole.Player);
        check(second.Token != issued.Token, "tokens are unique per login");

        // 滑动续期：用掉 11 小时后再校验，应该被续到"此刻 + 12 小时"
        clock.Now = start.AddHours(11);
        check(auth.Validate(issued.Token) is { } renewed
                && renewed.ExpiresAt == clock.Now.AddHours(12),
            "using a token slides its expiry forward");
        clock.Now = start.AddHours(22);
        check(auth.Validate(issued.Token) is not null,
            "an actively used token stays valid past its original expiry");

        // 绝对上限：一直续期也不能永生（防被窃 token 长期有效）
        clock.Now = start.AddHours(25);
        check(auth.Validate(issued.Token) is null,
            "an endlessly renewed token still dies at the absolute lifetime");

        // 超过生命周期不用就失效
        var staleClock = new FakeClock(start);
        var staleAuth = new AdminAuthService(options, staleClock);
        var stale = staleAuth.IssueToken("carol", AccountRole.Player);
        staleClock.Now = start.AddHours(13);
        check(staleAuth.Validate(stale.Token) is null, "an idle token expires after its lifetime");

        auth.Logout(issued.Token);
        check(auth.Validate(issued.Token) is null, "logout invalidates the token");
        check(auth.Validate("not-a-token") is null, "unknown tokens are rejected");
        check(auth.Validate(null) is null, "missing tokens are rejected");

        // 角色变更/改密时按用户吊销：只清掉该账号自己的 token
        clock.Now = start;
        var revokeAuth = new AdminAuthService(options, clock);
        var alice1 = revokeAuth.IssueToken("alice", AccountRole.Admin);
        var alice2 = revokeAuth.IssueToken("alice", AccountRole.Admin);
        var bobToken = revokeAuth.IssueToken("bob", AccountRole.Player);
        var revoked = revokeAuth.RevokeUser("ALICE");
        check(revoked == 2 && revokeAuth.Validate(alice1.Token) is null
                && revokeAuth.Validate(alice2.Token) is null
                && revokeAuth.Validate(bobToken.Token) is not null,
            "revoking a user kills exactly that user's tokens (case-insensitive)");
    }

    private static void CheckThrottleSemantics(Action<bool, string> check)
    {
        var start = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(start);
        var options = new ServerOptions();
        options.Admin.LoginAccountLockSources = 3;
        options.Admin.LoginSourceMaxAttempts = 30;
        options.Admin.LoginLockoutMinutes = 5;
        var auth = new AdminAuthService(options, clock);

        check(!auth.IsAccountThrottled("alice") && !auth.IsSourceThrottled("10.0.0.1"),
            "a fresh account/source is not throttled");

        // 单机反复失败【不会】锁定账号：一个来源锁不死别人（安全复审 F2 的核心）
        for (var i = 0; i < 20; i++)
        {
            auth.RegisterFailure("alice", "10.0.0.1");
        }

        check(!auth.IsAccountThrottled("alice"),
            "failures from a single source never lock an account (no lockout DoS)");

        // 来自多个不同来源的失败才升级为账号锁定（分布式爆破）
        auth.RegisterFailure("alice", "10.0.0.2");
        check(!auth.IsAccountThrottled("alice"), "two failing sources are not enough to lock");
        auth.RegisterFailure("alice", "10.0.0.3");
        check(auth.IsAccountThrottled("alice") && auth.IsAccountThrottled("ALICE"),
            "failures from 3 distinct sources lock the account (keyed case-insensitively)");

        clock.Now = start.AddMinutes(6);
        check(!auth.IsAccountThrottled("alice"), "the account lock expires");

        // 来源级预算：跨账号、硬拒绝，挡单机爆破/扫号/枚举
        for (var i = 0; i < 30; i++)
        {
            auth.RegisterAttempt("10.0.0.9");
        }

        check(auth.IsSourceThrottled("10.0.0.9"), "the source budget limits a single origin");
        check(!auth.IsSourceThrottled("10.0.0.10"), "another origin is unaffected");
        clock.Now = start.AddMinutes(12);
        check(!auth.IsSourceThrottled("10.0.0.9"), "the source throttle window expires");

        // 凭据正确时调用方会 ClearFailures（账号预算随即清零）
        auth.RegisterFailure("bob", "10.0.0.1");
        auth.RegisterFailure("bob", "10.0.0.2");
        auth.ClearFailures("bob");
        auth.RegisterFailure("bob", "10.0.0.2");
        check(!auth.IsAccountThrottled("bob"), "a successful login clears the account budget");
    }

    // 登录/找回失败记录与 token 表都对公网可达：必须有界，否则刷量就能把内存堆上去。
    private static void CheckBoundedMemory(Action<bool, string> check)
    {
        var start = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(start);
        var auth = new AdminAuthService(new ServerOptions(), clock);

        for (var i = 0; i < 25_000; i++)
        {
            auth.RegisterFailure($"flood{i}", "10.0.0.1");
            auth.RegisterAttempt($"10.9.{i / 256 % 256}.{i % 256}");
        }

        check(auth.TrackedAccountBudgetCount <= 20_000,
            $"account budgets stay bounded under username flooding (got {auth.TrackedAccountBudgetCount})");

        // 单账号反复登录：最多保留 5 个会话，最久未用的被顶掉
        // （每次登录推进 1 秒，让 token 的过期时间可区分——真实场景时间本来就在走）
        var tokens = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            clock.Now = start.AddSeconds(i);
            tokens.Add(auth.IssueToken("solo", AccountRole.Player).Token!);
        }

        check(auth.Validate(tokens[0]) is null && auth.Validate(tokens[^1]) is not null,
            "the oldest session of a repeatedly logging-in account is evicted (max 5 concurrent)");

        // 海量不同账号登录：token 表同样有界
        for (var i = 0; i < 25_000; i++)
        {
            auth.IssueToken($"burst{i}", AccountRole.Player);
        }

        check(auth.ActiveTokenCount <= 20_000,
            $"token table stays bounded under login flooding (got {auth.ActiveTokenCount})");
    }

    private static async Task CheckRoleModelAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dflegacy-admin-role-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(root, "state.json"),
                ScriptPvfPath = "",
                SkillScriptPath = root
            };

            // 没有引导名单时：默认账号是普通玩家
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            check(await store.GetRoleAsync("test") == AccountRole.Player,
                "accounts default to the player role");
            check((await store.GetAccountAsync("test"))?.Role == AccountRole.Player,
                "public account records expose the role");

            // 名单里还不存在的账号：记警告、不提升；而且注册时也【不会】直接变管理员
            // （否则"名单账号尚未注册"的部署窗口里，任何人都能抢注该名字拿到管理员）
            options.Admin.Users = ["boss"];
            check(await store.ApplyBootstrapAdminsAsync() == 0,
                "a bootstrap admin that does not exist yet promotes nobody");
            var created = await store.CreateAccountAsync(
                new CreateAccountRequest("boss", "bosspass", OicqCode: "10001"));
            check(created.Created && created.Account?.Role == AccountRole.Player,
                "registering a name from the bootstrap list does NOT grant admin (privilege-escalation guard)");
            check(await store.ApplyBootstrapAdminsAsync() == 1
                    && await store.GetRoleAsync("boss") == AccountRole.Admin,
                "the bootstrap list promotes that account on the next startup pass");
            var playerCreated = await store.CreateAccountAsync(
                new CreateAccountRequest("alice", "alicepass", OicqCode: "10002"));
            check(playerCreated.Created && playerCreated.Account?.Role == AccountRole.Player,
                "an ordinary registration stays a player");

            // 太短的 QQ 码不允许注册（1 位数字等于没有找回凭据）
            var weakCode = await store.CreateAccountAsync(
                new CreateAccountRequest("weakqq", "weakpass1", OicqCode: "1"));
            check(!weakCode.Created && weakCode.Error is not null,
                "a too-short QQ recovery code is rejected at registration");

            // 引导提升已存在账号，并且落盘后仍然有效
            var promotedOptions = new ServerOptions
            {
                DataPath = Path.Combine(root, "state.json"),
                ScriptPvfPath = "",
                SkillScriptPath = root
            };
            promotedOptions.Admin.Users = ["alice"];
            var promotingStore = new JsonGameStore(promotedOptions, NullLogger<JsonGameStore>.Instance);
            await promotingStore.InitializeAsync();
            check(await promotingStore.ApplyBootstrapAdminsAsync() == 1
                    && await promotingStore.GetRoleAsync("alice") == AccountRole.Admin,
                "bootstrap promotion applies to an existing account");

            var reloaded = new JsonGameStore(promotedOptions, NullLogger<JsonGameStore>.Instance);
            await reloaded.InitializeAsync();
            check(await reloaded.GetRoleAsync("alice") == AccountRole.Admin
                    && await reloaded.GetRoleAsync("boss") == AccountRole.Admin
                    && await reloaded.GetRoleAsync("test") == AccountRole.Player,
                "roles survive a JSON reload (bootstrap admin and list-created admin keep Admin, default account stays Player)");

            // SetRole 的两条安全约束
            var selfDemote = await reloaded.SetRoleAsync("alice", AccountRole.Player, "alice");
            check(!selfDemote.Success && selfDemote.Error is not null,
                "an admin cannot demote their own account");

            var demoteOther = await reloaded.SetRoleAsync("boss", AccountRole.Player, "alice");
            check(demoteOther.Success && await reloaded.GetRoleAsync("boss") == AccountRole.Player,
                "an admin can demote another account");

            var lastAdmin = await reloaded.SetRoleAsync("alice", AccountRole.Player, "test");
            check(!lastAdmin.Success && lastAdmin.Error is not null,
                "the last remaining admin cannot be demoted");

            var promote = await reloaded.SetRoleAsync("boss", AccountRole.Admin, "alice");
            check(promote.Success && await reloaded.GetRoleAsync("boss") == AccountRole.Admin,
                "an admin can promote another account");
            var missing = await reloaded.SetRoleAsync("nobody", AccountRole.Admin, "alice");
            check(!missing.Success, "promoting an unknown account fails cleanly");
            check(await reloaded.GetAccountAsync("nobody") is null,
                "an unknown account has no public record");

            // 找回密码：QQ 码错 -> InvalidRecoveryInformation；正确 -> 成功
            var wrongRecovery = await reloaded.ResetPasswordAsync(
                "alice", new ResetAccountPasswordRequest("99999", "whatever1"));
            check(wrongRecovery.Failure == AccountPasswordUpdateFailure.InvalidRecoveryInformation,
                "a wrong QQ code fails recovery");
            var okRecovery = await reloaded.ResetPasswordAsync(
                "alice", new ResetAccountPasswordRequest("10002", "recovered1"));
            check(okRecovery.Success, "the matching QQ code recovers the password");

            // 向后兼容：旧 state.json 没有 Role 字段 -> 解析为 Player 而不是报错
            var stateJson = await File.ReadAllTextAsync(Path.Combine(root, "state.json"));
            check(stateJson.Contains("\"Role\""), "roles are persisted in the state file");
            var withoutRole = stateJson
                .Replace("\"Role\": 1", "\"Role\": 0")
                .Replace("\"Role\":1", "\"Role\":0");
            await File.WriteAllTextAsync(Path.Combine(root, "state.json"), withoutRole);
            var legacy = new JsonGameStore(promotedOptions, NullLogger<JsonGameStore>.Instance);
            await legacy.InitializeAsync();
            check(await legacy.GetRoleAsync("boss") == AccountRole.Player,
                "roles read back from the state file drive the loaded value");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响断言结果
            }
        }
    }

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
