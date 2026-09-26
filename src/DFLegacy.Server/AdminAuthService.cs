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
        public DateTimeOffset LastTouchedAt;
    }

    // 登录失败记录与 token 都是对公网可达的内存表：必须有界，否则攻击者用海量
    // 随机用户名刷失败、或用自己账号反复登录，就能把内存堆上去。
    private static readonly TimeSpan FailureRetention = TimeSpan.FromHours(1);
    private const int MaximumFailureEntries = 20_000;
    private const int MaximumTokenEntries = 20_000;
    private const int MaximumSessionsPerUser = 5;

    internal TimeSpan TokenLifetime =>
        TimeSpan.FromHours(Math.Clamp(options.Admin.TokenHours, 1, 24 * 30));

    internal int MaxAttempts => Math.Clamp(options.Admin.LoginMaxAttempts, 1, 100);

    internal TimeSpan LockoutDuration =>
        TimeSpan.FromMinutes(Math.Clamp(options.Admin.LoginLockoutMinutes, 1, 24 * 60));

    public int ActiveTokenCount => _tokens.Count;

    /// <summary>当前跟踪的「账号+来源」失败记录数（测试与观测用，正常远小于上限）。</summary>
    public int TrackedFailureCount => _failures.Count;

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
        var now = clock.GetUtcNow();
        var state = _failures.GetOrAdd(FailureKey(userName, sourceKey), _ => new FailureState());
        lock (state)
        {
            state.LastTouchedAt = now;
            state.Count++;
            if (state.Count < MaxAttempts)
            {
                PurgeFailuresIfNeeded(now);
                return null;
            }

            state.Count = 0;
            state.LockedUntil = now + LockoutDuration;
            PurgeFailuresIfNeeded(now);
            return state.LockedUntil;
        }
    }

    public void ClearFailures(string userName, string? sourceKey) =>
        _failures.TryRemove(FailureKey(userName, sourceKey), out _);

    public AdminLoginResult IssueToken(string userName, AccountRole role)
    {
        EvictExcessSessionsOf(userName);
        PurgeTokensIfNeeded();
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

    /// <summary>同一账号最多保留 <see cref="MaximumSessionsPerUser"/> 个会话，超出时顶掉最久未用的。</summary>
    private void EvictExcessSessionsOf(string userName)
    {
        var mine = _tokens
            .Where(pair => string.Equals(pair.Value.UserName, userName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (mine.Count < MaximumSessionsPerUser)
        {
            return;
        }

        var excess = mine.Count - MaximumSessionsPerUser + 1;
        foreach (var pair in mine.OrderBy(pair => pair.Value.ExpiresAt).Take(excess))
        {
            _tokens.TryRemove(pair.Key, out _);
        }
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

    // 超限时先按时间回收：保留仍在锁定/活跃的条目，丢掉一小时没动过的记录。
    // 仍然超限就按最旧优先丢弃——宁可放宽限流也不能让内存无界增长。
    private void PurgeFailuresIfNeeded(DateTimeOffset now)
    {
        if (_failures.Count <= MaximumFailureEntries)
        {
            return;
        }

        var cutoff = now - FailureRetention;
        foreach (var pair in _failures)
        {
            DateTimeOffset touched;
            lock (pair.Value)
            {
                touched = pair.Value.LastTouchedAt;
            }

            if (touched < cutoff)
            {
                _failures.TryRemove(pair.Key, out _);
            }
        }

        if (_failures.Count <= MaximumFailureEntries)
        {
            return;
        }

        var excess = _failures.Count - MaximumFailureEntries;
        var oldest = _failures
            .OrderBy(pair => pair.Value.LastTouchedAt)
            .Take(excess)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in oldest)
        {
            _failures.TryRemove(key, out _);
        }
    }

    // 在插入新 token 之前调用：按"插入后"的数量判断，否则会稳定超出上限 1 个。
    private void PurgeTokensIfNeeded()
    {
        if (_tokens.Count + 1 <= MaximumTokenEntries)
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var pair in _tokens)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _tokens.TryRemove(pair.Key, out _);
            }
        }

        if (_tokens.Count + 1 <= MaximumTokenEntries)
        {
            return;
        }

        // 仍然超限（大量"仍然有效"的会话）：按最久未用顶掉，宁可让部分会话重新登录，
        // 也不让内存无界增长。
        var excess = _tokens.Count + 1 - MaximumTokenEntries;
        var oldest = _tokens
            .OrderBy(pair => pair.Value.ExpiresAt)
            .Take(excess)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in oldest)
        {
            _tokens.TryRemove(key, out _);
        }
    }

    private static string FailureKey(string userName, string? sourceKey) =>
        $"{userName.Trim().ToLowerInvariant()}|{sourceKey ?? "unknown"}";
}
