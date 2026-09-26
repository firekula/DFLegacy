using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace DFLegacy.Server;

public enum AdminLoginFailure
{
    None,
    InvalidRequest,
    InvalidCredentials,
    Locked
}

public sealed record AdminLoginRequest(string UserName, string Password);

/// <summary>提升/降级账号角色：<c>Admin</c> 或 <c>Player</c>。</summary>
public sealed record SetAccountRoleRequest(string? Role);

public sealed record AdminLoginResult(
    bool Success,
    AdminLoginFailure Failure,
    string? Error,
    string? Token = null,
    string? UserName = null,
    AccountRole Role = AccountRole.Player,
    DateTimeOffset? ExpiresAt = null);

public sealed record AdminTokenInfo(string UserName, AccountRole Role, DateTimeOffset ExpiresAt);

/// <summary>
/// 8081 管理接口的 Bearer token 鉴权：内存 token + 滑动过期 + 登录失败限流。
/// <para>
/// 用内存 token 而不是无状态签名 token，是为了能<b>即时吊销</b>：管理员被降级、
/// 主动登出、或运营方清理会话时，权限立刻生效而不必等 token 自然过期。
/// 代价是服务端重启后所有人需重新登录（管理页 401 时会重新弹登录框）。
/// </para>
/// </summary>
public sealed class AdminAuthService(ServerOptions options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, AdminTokenInfo> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FailureState> _failures =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class FailureState
    {
        public int Count;
        public DateTimeOffset LockedUntil;
    }

    internal TimeSpan TokenLifetime =>
        TimeSpan.FromHours(Math.Clamp(options.Admin.TokenHours, 1, 24 * 30));

    internal int MaxAttempts => Math.Clamp(options.Admin.LoginMaxAttempts, 1, 100);

    internal TimeSpan LockoutDuration =>
        TimeSpan.FromMinutes(Math.Clamp(options.Admin.LoginLockoutMinutes, 1, 24 * 60));

    public int ActiveTokenCount => _tokens.Count;

    public bool IsLocked(string userName, string? sourceKey)
    {
        if (!_failures.TryGetValue(FailureKey(userName, sourceKey), out var state))
        {
            return false;
        }

        lock (state)
        {
            return state.LockedUntil > clock.GetUtcNow();
        }
    }

    /// <summary>记一次登录失败；达到阈值时开始锁定，返回锁定截止时间（未触发限流返回 null）。</summary>
    public DateTimeOffset? RegisterFailure(string userName, string? sourceKey)
    {
        var state = _failures.GetOrAdd(FailureKey(userName, sourceKey), _ => new FailureState());
        lock (state)
        {
            state.Count++;
            if (state.Count < MaxAttempts)
            {
                return null;
            }

            state.Count = 0;
            state.LockedUntil = clock.GetUtcNow() + LockoutDuration;
            return state.LockedUntil;
        }
    }

    public void ClearFailures(string userName, string? sourceKey) =>
        _failures.TryRemove(FailureKey(userName, sourceKey), out _);

    public AdminLoginResult IssueToken(string userName, AccountRole role)
    {
        var token = CreateToken();
        var expiresAt = clock.GetUtcNow() + TokenLifetime;
        _tokens[token] = new AdminTokenInfo(userName, role, expiresAt);
        return new AdminLoginResult(
            true,
            AdminLoginFailure.None,
            null,
            token,
            userName,
            role,
            expiresAt);
    }

    /// <summary>校验 token；通过则滑动续期（活跃会话不会中途掉线），过期即作废。</summary>
    public AdminTokenInfo? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_tokens.TryGetValue(token, out var info))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (info.ExpiresAt <= now)
        {
            _tokens.TryRemove(token, out _);
            return null;
        }

        var renewed = info with { ExpiresAt = now + TokenLifetime };
        _tokens[token] = renewed;
        return renewed;
    }

    public void Logout(string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            _tokens.TryRemove(token, out _);
        }
    }

    /// <summary>作废某账号的全部 token（角色变更时调用，保证提权/降级即时生效）。</summary>
    public int RevokeUser(string userName)
    {
        var removed = 0;
        foreach (var pair in _tokens)
        {
            if (string.Equals(pair.Value.UserName, userName, StringComparison.OrdinalIgnoreCase)
                && _tokens.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    private static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string FailureKey(string userName, string? sourceKey) =>
        $"{userName.Trim().ToLowerInvariant()}|{sourceKey ?? "unknown"}";
}
