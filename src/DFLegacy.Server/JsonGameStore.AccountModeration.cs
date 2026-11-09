using System.Text.Json.Serialization;

namespace DFLegacy.Server;

public sealed record AccountBan(DateTimeOffset? ExpiresAt, string Reason)
{
    internal bool IsActive(DateTimeOffset now) => ExpiresAt is null || ExpiresAt > now;
}

public sealed record AdminSetAccountPasswordRequest(string NewPassword);
public sealed record AdminSetAccountBanRequest(
    [property: JsonRequired] int? DurationHours,
    string Reason);

internal enum ModerationStatus { Success, InvalidRequest, NotFound, Conflict, Unavailable }

internal sealed record ModerationResult(ModerationStatus Status, string? Error = null, int RevokedTokens = 0,
    AccountBan? Ban = null);

public sealed partial class JsonGameStore
{
    private static bool IsBanned(AccountRecord account) => account.Ban?.IsActive(DateTimeOffset.UtcNow) == true;

    internal async Task<AdminLoginResult?> AuthenticateWebAsync(
        string userName, string password, AdminAuthService auth, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var account = FindAccountLocked(userName);
            if (account is null || IsBanned(account) || !PasswordMatches(account, password)) return null;
            auth.ClearFailures(account.UserName);
            return auth.IssueToken(account.UserName, account.Role);
        }
        finally { _gate.Release(); }
    }

    internal async Task<bool> IsAccountBannedAsync(string userName, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { return FindAccountLocked(userName) is { } account && IsBanned(account); }
        finally { _gate.Release(); }
    }

    internal Task<ModerationResult> SetAccountPasswordByAdminAsync(string userName, string actor,
        AdminSetAccountPasswordRequest request, AdminAuthService auth, CancellationToken token)
    {
        if (!IsValidPassword(request.NewPassword))
            return Task.FromResult(new ModerationResult(ModerationStatus.InvalidRequest, "新密码长度应为 6–128 个字符。"));

        return UpdateAccountByAdminAsync(userName, actor, "password-reset",
            account => account with { PasswordHash = HashPassword(request.NewPassword) }, auth, null, token);
    }

    internal Task<ModerationResult> SetAccountBanByAdminAsync(string userName, string actor,
        AdminSetAccountBanRequest request, AdminAuthService auth, CharacterSessionRegistry sessions, CancellationToken token)
    {
        var reason = request.Reason?.Trim();
        if (reason is not { Length: > 0 and <= 200 } || reason.Any(char.IsControl)
            || request.DurationHours is <= 0 or > 8760)
            return Task.FromResult(new ModerationResult(ModerationStatus.InvalidRequest, "封禁原因须为 1–200 个字符，封禁期限须为 1–8760 小时或永久。"));

        var ban = new AccountBan(request.DurationHours is { } hours ? DateTimeOffset.UtcNow.AddHours(hours) : null, reason);
        return UpdateAccountByAdminAsync(userName, actor, "ban", account => account with { Ban = ban }, auth, sessions, token);
    }

    internal Task<ModerationResult> ClearAccountBanByAdminAsync(string userName, string actor,
        AdminAuthService auth, CancellationToken token) =>
        UpdateAccountByAdminAsync(userName, actor, "unban", account => account with { Ban = null }, auth, null, token);

    private async Task<ModerationResult> UpdateAccountByAdminAsync(string userName, string actor, string operation,
        Func<AccountRecord, AccountRecord> update, AdminAuthService auth, CharacterSessionRegistry? disconnect,
        CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_rewardQuarantined) return new(ModerationStatus.Unavailable, "存档暂不可写。");
            var index = _accounts.FindIndex(account => string.Equals(account.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return new(ModerationStatus.NotFound, "账号不存在。");

            var current = _accounts[index];
            if (operation != "unban" && (current.Role == AccountRole.Admin || IsBootstrapAdmin(current.UserName)
                || string.Equals(current.UserName, actor, StringComparison.OrdinalIgnoreCase)))
                return new(ModerationStatus.Conflict, "无法通过此操作管理管理员账号或当前账号。");

            var replacement = update(current);
            if (replacement == current) return new(ModerationStatus.Success, Ban: current.Ban);
            var accounts = new List<AccountRecord>(_accounts) { [index] = replacement };
            if (!await SaveStoreAccountsLockedAsync(accounts, token))
                return new(ModerationStatus.Unavailable, "账号变更写入结果不确定。");

            var revoked = operation == "unban" ? 0 : auth.RevokeUser(current.UserName);
            if (disconnect is not null)
                foreach (var character in current.Characters) disconnect.Disconnect(character.Id);
            logger.LogInformation("GM {Actor} applied {Operation} to account {AccountUid}.", actor, operation, current.AccountUid);
            return new(ModerationStatus.Success, RevokedTokens: revoked, Ban: replacement.Ban);
        }
        finally { _gate.Release(); }
    }

    internal async Task<ModerationResult> DeleteOfflineCharacterByAdminAsync(Guid characterId, string actor,
        CharacterSessionRegistry sessions, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_rewardQuarantined) return new(ModerationStatus.Unavailable, "存档暂不可写。");
            var accounts = CopyMentorAccountsLocked();
            var account = accounts.FirstOrDefault(owner => owner.Characters.Any(character => character.Id == characterId));
            if (account is null) return new(ModerationStatus.NotFound, "角色不存在。");
            if (account.Role == AccountRole.Admin || IsBootstrapAdmin(account.UserName))
                return new(ModerationStatus.Conflict, "无法通过此操作删除管理员角色。");
            if (sessions.IsOnline(characterId))
                return new(ModerationStatus.Conflict, "请让角色离线后再删除。");
            if (IsEconomicWriteBlocked(characterId))
                return new(ModerationStatus.Conflict, "角色仍有未结算的交易或摆摊，暂时无法删除。");

            var slot = account.Characters.FindIndex(character => character.Id == characterId);
            var result = await DeleteCharacterAtSlotLockedAsync(accounts, account, slot, token);
            if (!result.Deleted) return new(ModerationStatus.Unavailable, result.Error);
            logger.LogInformation("GM {Actor} deleted offline character {CharacterId}.", actor, characterId);
            return new(ModerationStatus.Success);
        }
        finally { _gate.Release(); }
    }
}
