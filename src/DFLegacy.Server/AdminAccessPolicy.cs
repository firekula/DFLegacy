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
        if (path.Length == 0 || path == "/")
        {
            return new AdminAccessDecision(AdminAccess.Public, null);
        }

        if (path.StartsWith("/client", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            return new AdminAccessDecision(AdminAccess.Public, null);
        }

        if (IsPath(path, "/api/accounts") && IsMethod(method, "POST"))
        {
            return new AdminAccessDecision(AdminAccess.Public, null);
        }

        if (IsPath(path, "/api/auth/login") && IsMethod(method, "POST"))
        {
            return new AdminAccessDecision(AdminAccess.Public, null);
        }

        if (IsPath(path, "/api/auth/logout") && IsMethod(method, "POST"))
        {
            return new AdminAccessDecision(AdminAccess.Authenticated, null);
        }

        // 找回密码靠 QQ 码自证，保持公开（否则忘记密码就无路可走）；同样纳入登录限流。
        if (TryMatchAccountSubPath(path, "password/reset", out _) && IsMethod(method, "POST"))
        {
            return new AdminAccessDecision(AdminAccess.Public, null);
        }

        if (TryMatchAccountSubPath(path, "password", out var passwordAccount)
            && IsMethod(method, "PUT"))
        {
            return new AdminAccessDecision(AdminAccess.SelfAccount, passwordAccount);
        }

        if (TryMatchAccountSubPath(path, "characters", out var charactersAccount)
            && IsMethod(method, "GET"))
        {
            return new AdminAccessDecision(AdminAccess.SelfAccount, charactersAccount);
        }

        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return new AdminAccessDecision(AdminAccess.Admin, null);
        }

        return new AdminAccessDecision(AdminAccess.Public, null);
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
