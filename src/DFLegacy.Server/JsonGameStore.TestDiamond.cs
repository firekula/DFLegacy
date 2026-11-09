namespace DFLegacy.Server;

public sealed partial class JsonGameStore
{
    public async Task<ClientDiamondGrantResult> GrantTestDiamondToCharacterAsync(
        string characterName, int days, CancellationToken cancellationToken = default)
    {
        var name = characterName?.Trim() ?? string.Empty;
        ClientDiamondGrantResult Refuse(string error) => new(false, error, "", days, 0, null, 0);
        if (name.Length is < 1 or > 20) return Refuse("请输入有效角色名（1-20 字）。");
        if (days != 30) return Refuse("当前仅支持开通 30 天黑钻。");
        string accountName;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var owners = _accounts.Where(a => a.Characters.Any(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (owners.Length != 1 || owners[0].Characters.Count(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) != 1)
                return Refuse("角色不存在或角色名不唯一。");
            accountName = owners[0].UserName;
        }
        finally { _gate.Release(); }
        // The account grant owns its persistence lock and the established premium rules.
        return await GrantDiamondToAccountAsync(accountName, days, cancellationToken);
    }
}
