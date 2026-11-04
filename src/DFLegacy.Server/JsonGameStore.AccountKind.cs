namespace DFLegacy.Server;

public sealed record SetAccountKindRequest(string? Kind);

public sealed partial class JsonGameStore
{
    /// <summary>
    /// GM-only operation: account kind is encoded by UID, independently of admin
    /// role. The HTTP caller must supply its game-connection guard so cached
    /// session identities cannot continue writing with the old UID.
    /// </summary>
    public async Task<(bool Updated, PublicAccountRecord? Account, string? Error)> SetAccountKindAsync(
        string userName,
        AccountKind kind,
        CancellationToken cancellationToken = default,
        Func<bool>? hasActiveGameSessions = null)
    {
        if (string.IsNullOrWhiteSpace(userName) || kind is not (AccountKind.Test or AccountKind.Normal))
            return (false, null, "账号不能为空，账号类型仅支持 normal 或 test。");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfRewardQuarantinedLocked();
            var index = _accounts.FindIndex(account =>
                string.Equals(account.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0) return (false, null, "账号不存在。");
            var original = _accounts[index];
            if (AccountUidRules.Classify(original.AccountUid) == kind)
                return (true, ToPublic(original), null);
            if (hasActiveGameSessions?.Invoke() == true)
                return (false, null, "请先断开所有游戏连接，再切换账号类型。");

            var previousNormalCounter = _nextNormalAccountUid;
            var previousTestCounter = _nextTestAccountUid;
            var uid = AllocateAccountUidLocked(kind == AccountKind.Test);
            var updated = original with
            {
                AccountUid = uid,
                // Switching kind grants no test preset, currency or items on reload.
                PresetCharactersInitialized = true,
                Characters = original.Characters.Select(character => character with { AccountUid = uid }).ToList()
            };
            var accounts = _accounts.ToList();
            accounts[index] = updated;
            try
            {
                // Publish the changed identity only after the entire snapshot,
                // including destination UID allocator, has committed atomically.
                await WriteRewardStatePrivilegedAsync(accounts, _dungeonRewardLedger, cancellationToken);
            }
            catch (Exception exception)
            {
                _nextNormalAccountUid = previousNormalCounter;
                _nextTestAccountUid = previousTestCounter;
                _rewardQuarantined = true;
                logger.LogError(exception, "Account-kind snapshot outcome is uncertain; store quarantined.");
                return (false, null, "账号类型保存结果不确定，已停止后续写入；请重启服务核对存档。");
            }

            _accounts = accounts;
            return (true, ToPublic(updated), null);
        }
        finally
        {
            _gate.Release();
        }
    }
}
