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

public sealed record AdminTokenInfo(
    string UserName,
    AccountRole Role,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// 8081 管理接口的 Bearer token 鉴权：内存 token + 滑动过期 + 失败限流。
/// <para>
/// 用内存 token 而不是无状态签名 token，是为了能<b>即时吊销</b>：管理员被降级、
/// 改密/找回密码、主动登出、或运营方清理会话时，权限立刻生效。
/// 代价是服务端重启后所有人需重新登录（管理页 401 时会重新弹登录框）。
/// </para>
/// <para>
/// 限流语义（安全复审后重设计）：<b>先校验凭据，密码正确一律放行</b>——
/// 否则匿名攻击者只要连续失败 5 次就能把受害者的账号（包括唯一管理员）锁在门外，
/// 那是比爆破更廉价的拒绝服务。因此失败只影响「错误密码」这条路径：
/// </para>
/// <list type="bullet">
/// <item>账号级预算（跨来源）：同一账号的错误密码额度，防分布式针对单账号爆破；</item>
/// <item>来源级预算（跨账号）：同一来源的总尝试额度，防单机扫号与枚举。</item>
/// </list>
/// </summary>
public sealed class AdminAuthService(ServerOptions options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, AdminTokenInfo> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Budget> _accountBudgets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Budget> _sourceBudgets = new(StringComparer.Ordinal);

    private sealed class Budget
    {
        /// <summary>尝试次数（来源级预算用）。</summary>
        public int Count;

        /// <summary>失败来源集合（账号级预算用）：只有来自多个不同来源的失败才升级为锁定。</summary>
        public readonly HashSet<string> FailureSources = new(StringComparer.Ordinal);

        public DateTimeOffset BlockedUntil;
        public DateTimeOffset LastTouchedAt;
    }

    // 账号级锁定需要"失败来自多少个不同来源"才触发（阈值可配，见 LoginAccountLockSources）：
    // 这是为了同时满足两个互相拉扯的要求——
    //   * 单机攻击者不能把受害者（尤其唯一管理员）锁在门外：默认阈值 3，一个来源锁不了账号；
    //   * 分布式爆破要能挡住：来自多个来源的失败会锁定该账号。
    // 单机爆破由来源级预算负责（硬 429，且不校验凭据）。

    // 这些表都对公网可达：必须有界，否则刷量就能把内存堆上去。
    private static readonly TimeSpan BudgetRetention = TimeSpan.FromHours(1);
    private const int MaximumBudgetEntries = 20_000;
    private const int MaximumTokenEntries = 20_000;
    private const int MaximumSessionsPerUser = 5;

    internal TimeSpan TokenLifetime =>
        TimeSpan.FromHours(Math.Clamp(options.Admin.TokenHours, 1, 24 * 30));

    /// <summary>绝对上限：滑动续期也不能让一个 token 永生（防被窃 token 长期有效）。</summary>
    internal TimeSpan AbsoluteTokenLifetime =>
        TimeSpan.FromHours(Math.Clamp(options.Admin.MaxTokenLifetimeHours, 1, 24 * 365));

    internal TimeSpan LockoutDuration =>
        TimeSpan.FromMinutes(Math.Clamp(options.Admin.LoginLockoutMinutes, 1, 24 * 60));

    internal int AccountLockSourceThreshold => Math.Clamp(options.Admin.LoginAccountLockSources, 1, 100);

    internal int SourceThreshold => Math.Clamp(options.Admin.LoginSourceMaxAttempts, 1, 100_000);

    public int ActiveTokenCount => _tokens.Count;

    /// <summary>当前跟踪的账号级预算条目数（测试与观测用，正常远小于上限）。</summary>
    public int TrackedAccountBudgetCount => _accountBudgets.Count;

    /// <summary>
    /// 账号级的错误密码额度是否已锁（在<b>校验凭据之前</b>判定，命中直接 429）。
    /// 只有失败来自 <see cref="AccountLockSourceThreshold"/> 个以上不同来源时才会锁，
    /// 因此单个来源（哪怕匿名）无法把别人的账号锁在门外。
    /// </summary>
    public bool IsAccountThrottled(string userName) =>
        IsBlocked(_accountBudgets, AccountKey(userName));

    /// <summary>来源级的尝试额度是否已用尽（跨账号，在校验凭据之前判定）。</summary>
    public bool IsSourceThrottled(string? sourceKey) =>
        IsBlocked(_sourceBudgets, SourceKey(sourceKey));

    /// <summary>记一次登录/找回尝试（来源级预算，无论成功失败）。</summary>
    public void RegisterAttempt(string? sourceKey)
    {
        var now = clock.GetUtcNow();
        BumpSource(_sourceBudgets, SourceKey(sourceKey), SourceThreshold, now);
    }

    /// <summary>记一次凭据/QQ 码错误（账号级预算，按来源去重）。</summary>
    public void RegisterFailure(string userName, string? sourceKey)
    {
        var now = clock.GetUtcNow();
        var budget = _accountBudgets.GetOrAdd(AccountKey(userName), _ => new Budget());
        lock (budget)
        {
            budget.LastTouchedAt = now;
            budget.Count++;
            budget.FailureSources.Add(SourceKey(sourceKey));
            if (budget.FailureSources.Count >= AccountLockSourceThreshold)
            {
                budget.Count = 0;
                budget.FailureSources.Clear();
                budget.BlockedUntil = now + LockoutDuration;
            }
        }

        PurgeBudgetsIfNeeded(_accountBudgets, now);
    }

    /// <summary>凭据正确时清零该账号的失败预算。</summary>
    public void ClearFailures(string userName) =>
        _accountBudgets.TryRemove(AccountKey(userName), out _);

    public AdminLoginResult IssueToken(string userName, AccountRole role)
    {
        EvictExcessSessionsOf(userName);
        PurgeTokensIfNeeded();
        var token = CreateToken();
        var now = clock.GetUtcNow();
        var expiresAt = now + TokenLifetime;
        _tokens[token] = new AdminTokenInfo(userName, role, now, expiresAt);
        return new AdminLoginResult(
            true,
            AdminLoginFailure.None,
            null,
            token,
            userName,
            role,
            expiresAt);
    }

    /// <summary>
    /// 校验 token；通过则滑动续期（但不超过绝对上限），过期或超过绝对上限即作废。
    /// </summary>
    public AdminTokenInfo? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_tokens.TryGetValue(token, out var info))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var absoluteDeadline = info.IssuedAt + AbsoluteTokenLifetime;
        if (info.ExpiresAt <= now || absoluteDeadline <= now)
        {
            _tokens.TryRemove(token, out _);
            return null;
        }

        var renewed = info with { ExpiresAt = Min(now + TokenLifetime, absoluteDeadline) };
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

    /// <summary>
    /// 作废某账号的全部 token。角色变更、改密、找回密码、封禁都应该调用它，
    /// 保证"凭据变了，旧会话立即失效"。
    /// </summary>
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

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;

    private bool IsBlocked(ConcurrentDictionary<string, Budget> budgets, string key)
    {
        if (!budgets.TryGetValue(key, out var budget))
        {
            return false;
        }

        lock (budget)
        {
            return budget.BlockedUntil > clock.GetUtcNow();
        }
    }

    private void BumpSource(
        ConcurrentDictionary<string, Budget> budgets,
        string key,
        int threshold,
        DateTimeOffset now)
    {
        var budget = budgets.GetOrAdd(key, _ => new Budget());
        lock (budget)
        {
            budget.LastTouchedAt = now;
            budget.Count++;
            if (budget.Count >= threshold)
            {
                budget.Count = 0;
                budget.BlockedUntil = now + LockoutDuration;
            }
        }

        PurgeBudgetsIfNeeded(budgets, now);
    }

    // 超限时先按时间回收；仍然超限就顶掉最旧的一条。仍然保持"被锁定中的条目优先保留"，
    // 否则攻击者可以先灌满表、再把自己的锁顶掉（安全复审指出）。
    private void PurgeBudgetsIfNeeded(ConcurrentDictionary<string, Budget> budgets, DateTimeOffset now)
    {
        if (budgets.Count <= MaximumBudgetEntries)
        {
            return;
        }

        var cutoff = now - BudgetRetention;
        var removable = new List<KeyValuePair<string, Budget>>();
        foreach (var pair in budgets)
        {
            DateTimeOffset touched;
            DateTimeOffset blockedUntil;
            lock (pair.Value)
            {
                touched = pair.Value.LastTouchedAt;
                blockedUntil = pair.Value.BlockedUntil;
            }

            if (blockedUntil <= now && touched < cutoff)
            {
                removable.Add(pair);
            }
        }

        foreach (var pair in removable)
        {
            budgets.TryRemove(pair.Key, out _);
        }

        if (budgets.Count <= MaximumBudgetEntries)
        {
            return;
        }

        // 还不够就按最旧顶掉（包含锁定中的条目；此时内存上限优先）。
        var excess = budgets.Count - MaximumBudgetEntries;
        var oldest = budgets
            .OrderBy(pair => pair.Value.LastTouchedAt)
            .Take(excess)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in oldest)
        {
            budgets.TryRemove(key, out _);
        }
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
            var absoluteDeadline = pair.Value.IssuedAt + AbsoluteTokenLifetime;
            if (pair.Value.ExpiresAt <= now || absoluteDeadline <= now)
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

    private static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    // 账号键必须与账号查找的 OrdinalIgnoreCase 语义一致：ToUpperInvariant 与它等价
    // （ToLowerInvariant 对少数希腊/西里尔字符不等价，会让同一账号出现两个计数器）。
    private static string AccountKey(string userName) => userName.Trim().ToUpperInvariant();

    private static string SourceKey(string? sourceKey) => sourceKey ?? "unknown";
}
