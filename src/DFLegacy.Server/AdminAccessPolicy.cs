namespace DFLegacy.Server;

/// <summary>8081 管理接口的权限档位。</summary>
public enum AdminAccess
{
    /// <summary>无需登录：健康检查、注册、找回密码、静态页面。</summary>
    Public,

    /// <summary>登录即可：例如登出。</summary>
    Authenticated,

    /// <summary>必须是路径里那个账号本人（管理员可代操作）。</summary>
    SelfAccount,

    /// <summary>必须是管理员。</summary>
    Admin
}

public readonly record struct AdminAccessDecision(AdminAccess Access, string? AccountName);

/// <summary>
/// 集中式路由权限判定。刻意做成纯函数并集中在一处：三十多条路由如果各自挂过滤器，
/// 漏挂一条就是一个越权口子；集中成一张可用单元测试钉死的表更可靠。
/// <para>
/// 判定规则（顺序即优先级）：
/// 1. 静态页面与跳转（<c>/</c>、<c>/health</c>、<c>/client*</c>、<c>/admin*</c>）公开；
/// 2. 注册、登录、找回密码（QQ 码）公开；
/// 3. 登出需登录；
/// 4. <c>PUT /api/accounts/{u}/password</c>、<c>GET /api/accounts/{u}/characters</c> 只能本人（或管理员）；
/// 5. 其余 <c>/api/**</c>（管理工具、充值、广播、会话、账号列表…）一律管理员。
/// </para>
/// </summary>
public static class AdminAccessPolicy
{
    /// <summary>鉴权通过后放进 <c>HttpContext.Items</c> 的 token 信息键。</summary>
    public const string TokenInfoKey = "dflegacy.admin.token";

    private const string AccountsPrefix = "/api/accounts/";

    public static AdminAccessDecision Resolve(string method, string path)
    {
        // 路由对末尾斜杠是宽容的（/api/auth/login/ 也会命中登录端点），而本表按文本匹配。
        // 不归一化就会出现"表里判成 Admin、实际是登录接口"这种费解的 fail-closed 偏差。
        var normalized = NormalizePath(path);

        if (normalized.Length == 0 || normalized == "/")
        {
            return new AdminAccessDecision(AdminAccess.Public, null);
        }

        if (MatchesPrefix(normalized, "/client")
            || MatchesPrefix(normalized, "/admin")
            || MatchesPrefix(normalized, "/health"))
        {
            return new AdminAccessDecision(AdminAccess.Public, null);
        }

        if (IsPath(normalized, "/api/accounts") && IsMethod(method, "POST"))
        {
            return new AdminAccessDecision(AdminAccess.Public, null);
        }

        if (IsPath(normalized, "/api/auth/login") && IsMethod(method, "POST"))
        {
            return new AdminAccessDecision(AdminAccess.Public, null);
        }

        if (IsPath(normalized, "/api/auth/logout") && IsMethod(method, "POST"))
        {
            return new AdminAccessDecision(AdminAccess.Authenticated, null);
        }

        // 找回密码靠 QQ 码自证，保持公开（否则忘记密码就无路可走）；调用方（Program.cs）
        // 会把它纳入与登录相同的失败预算（防 QQ 码爆破），见 AdminAuthService 的说明。
        if (TryMatchAccountSubPath(normalized, "password/reset", out _) && IsMethod(method, "POST"))
        {
            return new AdminAccessDecision(AdminAccess.Public, null);
        }

        if (TryMatchAccountSubPath(normalized, "password", out var passwordAccount)
            && IsMethod(method, "PUT"))
        {
            return new AdminAccessDecision(AdminAccess.SelfAccount, passwordAccount);
        }

        if (TryMatchAccountSubPath(normalized, "characters", out var charactersAccount)
            && IsMethod(method, "GET"))
        {
            return new AdminAccessDecision(AdminAccess.SelfAccount, charactersAccount);
        }

        // 兜底一律按管理员（fail-closed）。静态文件在 UseStaticFiles 就返回了、到不了这里，
        // 所以剩下的基本是 404 路径与 /api/**；将来新增 /admin/**、/client/** 路由若忘了
        // 登记进本表，也会落到这条兜底而不是被静默当成公开接口。
        return new AdminAccessDecision(AdminAccess.Admin, null);
    }

    public static string? BearerToken(string? authorizationHeader)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(authorizationHeader)
            || !authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorizationHeader[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    private static bool IsPath(string path, string expected) =>
        path.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsMethod(string method, string expected) =>
        method.Equals(expected, StringComparison.OrdinalIgnoreCase);

    // 只去一个末尾斜杠：/api/auth/login/ -> /api/auth/login；根路径保持 "/"。
    private static string NormalizePath(string path) =>
        path.Length > 1 && path[^1] == '/' ? path[..^1] : path;

    // 前缀按"整段"匹配：/client、/client/xxx、/client.css 都算，/clientx 不算。
    private static bool MatchesPrefix(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);

    /// <summary>匹配 <c>/api/accounts/{账号}/{子路径}</c>，取出其中的账号名。</summary>
    private static bool TryMatchAccountSubPath(string path, string subPath, out string account)
    {
        account = string.Empty;
        if (!path.StartsWith(AccountsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = path[AccountsPrefix.Length..];
        var suffix = "/" + subPath;
        if (rest.Length <= suffix.Length
            || !rest.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = rest[..^suffix.Length];
        if (name.Length == 0 || name.Contains('/'))
        {
            return false;
        }

        account = Uri.UnescapeDataString(name);
        return true;
    }
}
