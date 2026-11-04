namespace DFLegacy.Server;

public sealed record TestCeraTopUpResult(bool Success, string? Error, string Account, int Cash, IReadOnlyList<Guid> CharacterIds);

public sealed partial class JsonGameStore
{
    public async Task<TestCeraTopUpResult> GrantTestCeraToCharacterAsync(
        string characterName, int amount, CancellationToken cancellationToken = default)
    {
        var name = characterName?.Trim() ?? string.Empty;
        TestCeraTopUpResult Refuse(string error) => new(false, error, "", 0, Array.Empty<Guid>());
        if (name.Length is < 1 or > 20) return Refuse("请输入有效角色名（1-20 字）。");
        if (amount is < 1 or > 1_000_000) return Refuse("点券数量必须是 1 到 1000000 的整数。");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var owners = _accounts.Where(a => a.Characters.Any(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (owners.Length != 1 || owners[0].Characters.Count(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) != 1)
                return Refuse("角色不存在或角色名不唯一。");
            var account = owners[0];
            var cash = Math.Max(0, account.Cash ?? DefaultAccountCash);
            if (cash > int.MaxValue - amount) return Refuse("点券余额超出上限。");
            var updated = SetAccountCash(account, cash + amount);
            var index = _accounts.FindIndex(a => a.Id == account.Id);
            _accounts[index] = updated;
            try { await SaveLockedAsync(cancellationToken); }
            catch { _accounts[index] = account; throw; }
            return new(true, null, updated.UserName, cash + amount, updated.Characters.Select(c => c.Id).ToArray());
        }
        finally { _gate.Release(); }
    }
}
