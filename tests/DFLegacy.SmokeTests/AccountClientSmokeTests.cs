using System.Net;
using System.Text;
using System.Text.Json;
using DFLegacy.AccountClient;

internal static class AccountClientSmokeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>桩 handler 抓到的最后一个 Authorization 头，用于断言 token 确实发出去了。</summary>
    private static string? _lastAuthorization;

    public static void Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);

        var client = new AccountServiceClient(
            new HttpClient(new StubHandler(Respond)) { BaseAddress = new Uri("http://test/") });

        // 注册：成功
        var r1 = client.RegisterAsync("alice", "secret1", "10001", false).GetAwaiter().GetResult();
        check(r1.Outcome == RegistrationOutcome.Success, "register success maps 201 → Success");
        check(r1.Account?.UserName == "alice", "register success returns the created account");

        // 注册：账号已存在
        var r2 = client.RegisterAsync("bob", "secret2", "10002", false).GetAwaiter().GetResult();
        check(r2.Outcome == RegistrationOutcome.AlreadyExists, "register conflict maps 409 → AlreadyExists");
        check(!string.IsNullOrEmpty(r2.Error), "register conflict carries server error text");

        // 注册：参数非法
        var r3 = client.RegisterAsync("x", "short", "9", false).GetAwaiter().GetResult();
        check(r3.Outcome == RegistrationOutcome.Invalid, "register bad request maps 400 → Invalid");

        // 改密：成功 / 错密码 / 非法
        var c1 = client.ChangePasswordAsync("alice", "secret1", "newpass1").GetAwaiter().GetResult();
        check(c1.Outcome == PasswordOutcome.Success, "change password 200 → Success");
        var c2 = client.ChangePasswordAsync("alice", "wrong", "newpass1").GetAwaiter().GetResult();
        check(c2.Outcome == PasswordOutcome.InvalidCredentials, "change password 401 → InvalidCredentials");
        var c3 = client.ChangePasswordAsync("alice", "secret1", "bad").GetAwaiter().GetResult();
        check(c3.Outcome == PasswordOutcome.InvalidRequest, "change password 400 → InvalidRequest");

        // 找回：成功 / QQ 错 / 非法
        var s1 = client.ResetPasswordAsync("alice", "10001", "newpass1").GetAwaiter().GetResult();
        check(s1.Outcome == PasswordOutcome.Success, "reset 200 → Success");
        var s2 = client.ResetPasswordAsync("alice", "00000", "newpass1").GetAwaiter().GetResult();
        check(s2.Outcome == PasswordOutcome.InvalidRecovery, "reset 401 → InvalidRecovery");
        var s3 = client.ResetPasswordAsync("alice", "10001", "bad").GetAwaiter().GetResult();
        check(s3.Outcome == PasswordOutcome.InvalidRequest, "reset 400 → InvalidRequest");

        // 登录：改密等需要身份的操作必须先拿 token
        var l1 = client.LoginAsync("alice", "secret1").GetAwaiter().GetResult();
        check(l1.Outcome == LoginOutcome.Success && l1.Token == "tok-123" && l1.Role == 0,
            "login 200 → Success with token and role");
        var l2 = client.LoginAsync("alice", "nope1234").GetAwaiter().GetResult();
        check(l2.Outcome == LoginOutcome.InvalidCredentials, "login 401 → InvalidCredentials");
        var l3 = client.LoginAsync("alice", "locked1").GetAwaiter().GetResult();
        check(l3.Outcome == LoginOutcome.Locked && !string.IsNullOrEmpty(l3.Error),
            "login 429 → Locked (account+IP rate limited)");

        // 带 token 改密：必须把 Bearer token 放进 Authorization 头
        _lastAuthorization = null;
        var c4 = client.ChangePasswordAsync("alice", "secret1", "newpass1", l1.Token).GetAwaiter().GetResult();
        check(c4.Outcome == PasswordOutcome.Success, "change password with token 200 → Success");
        check(_lastAuthorization == "Bearer tok-123",
            "change password sends Authorization: Bearer <token>");

        // 401 且带了（失效的）token = token 问题，与"旧密码错误"区分开
        var c5 = client.ChangePasswordAsync("alice", "needs-token", "newpass1", "stale-token")
            .GetAwaiter().GetResult();
        check(c5.Outcome == PasswordOutcome.InvalidRequest && !string.IsNullOrEmpty(c5.Error),
            "change password 401 with a stale token → InvalidRequest (token problem, not credentials)");

        // 健康
        check(client.CheckHealthAsync().GetAwaiter().GetResult(), "health 200 → true");
        var dead = new AccountServiceClient(
            new HttpClient(new StubHandler(_ => throw new HttpRequestException("unreachable")))
            { BaseAddress = new Uri("http://dead/") });
        check(!dead.CheckHealthAsync().GetAwaiter().GetResult(), "health unreachable → false");
    }

    private static HttpResponseMessage Respond(HttpRequestMessage req)
    {
        var path = req.RequestUri?.AbsolutePath ?? string.Empty;
        _lastAuthorization = req.Headers.TryGetValues("Authorization", out var values)
            ? string.Join(",", values)
            : null;
        // 登录（换 token；失败限流返回 429）
        if (req.Method == HttpMethod.Post && path == "/api/auth/login")
            return req switch
            {
                _ when BodyContains(req, "\"password\":\"locked1\"") => Json(429, new { error = "失败次数过多，请稍后再试。" }),
                _ when BodyContains(req, "\"userName\":\"alice\"") && BodyContains(req, "\"password\":\"secret1\"") =>
                    Json(200, new { success = true, failure = 0, error = (string?)null, token = "tok-123", userName = "alice", role = 0, expiresAt = "2026-09-27T00:00:00+00:00" }),
                _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            };
        // 注册
        if (req.Method == HttpMethod.Post && path == "/api/accounts")
            return req switch
            {
                _ when BodyContains(req, "\"userName\":\"alice\"") => Json(201, new { id = Guid.NewGuid(), userName = "alice", characters = Array.Empty<object>(), accountUid = 18000001u, kind = 1, cash = 50000 }),
                _ when BodyContains(req, "\"userName\":\"bob\"") => Json(409, new { error = "账号已存在。" }),
                _ => Json(400, new { error = "账号长度应为 4-32，密码长度应为 6-128，QQ 不能为空。" })
            };
        // 改密（需要 token；缺 token 的 401 带 error 体，旧密码错的 401 无体）
        if (req.Method == HttpMethod.Put && path.EndsWith("/password"))
            return req switch
            {
                _ when BodyContains(req, "\"newPassword\":\"bad\"") => Json(400, new { success = false, failure = 1, error = "账号、原密码和新密码不能为空，新密码长度应为 6-128。" }),
                _ when BodyContains(req, "\"oldPassword\":\"needs-token\"") => Json(401, new { error = "需要登录（token 缺失或已失效）。" }),
                _ when BodyContains(req, "\"oldPassword\":\"wrong\"") => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                _ => Json(200, new { success = true, failure = 0, error = (string?)null })
            };
        // 找回
        if (req.Method == HttpMethod.Post && path.EndsWith("/password/reset"))
            return req switch
            {
                _ when BodyContains(req, "\"newPassword\":\"bad\"") => Json(400, new { success = false, failure = 1, error = "账号和 QQ 不能为空，新密码长度应为 6-128。" }),
                _ when BodyContains(req, "\"oicqCode\":\"00000\"") => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                _ => Json(200, new { success = true, failure = 0, error = (string?)null })
            };
        // 健康
        if (req.Method == HttpMethod.Get && path == "/health")
            return Json(200, new { status = "ok", service = "dflegacy-emulator" });
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static bool BodyContains(HttpRequestMessage req, string needle)
    {
        var task = req.Content?.ReadAsStringAsync();
        if (task is null) return false;
        return (task.GetAwaiter().GetResult() ?? string.Empty).Contains(needle);
    }

    private static HttpResponseMessage Json(int status, object? body) =>
        new((HttpStatusCode)status)
        {
            Content = new StringContent(
                body is null ? "" : JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            try { return Task.FromResult(_respond(request)); }
            catch (Exception ex) { return Task.FromException<HttpResponseMessage>(ex); }
        }
    }
}
