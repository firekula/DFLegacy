using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DFLegacy.Manager;

public sealed class ManagerIdentity
{
    private sealed record Identity(string UserName, string Salt, string Hash);
    private readonly ManagerPaths _paths;
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset Start, int Failures)> _attempts = new();
    private Identity? _identity;
    private string _setupToken = "";
    public ManagerIdentity(ManagerPaths paths)
    {
        _paths = paths;
        if (File.Exists(paths.IdentityFile)) _identity = JsonSerializer.Deserialize<Identity>(File.ReadAllText(paths.IdentityFile)) ?? throw new InvalidDataException("管理账号文件损坏。");
        if (_identity is null)
        {
            _setupToken = File.Exists(paths.SetupTokenFile) ? File.ReadAllText(paths.SetupTokenFile).Trim() : Environment.GetEnvironmentVariable("DFLEGACY_MANAGER_SETUP_TOKEN") ?? Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            if (_setupToken.Length < 32) throw new InvalidDataException("首次设置凭据至少需要 32 个字符。");
            AtomicFile.Write(paths.SetupTokenFile, _setupToken, privateFile: true);
        }
        else if (File.Exists(paths.SetupTokenFile)) File.Delete(paths.SetupTokenFile);
    }
    public bool IsConfigured { get { lock (_gate) return _identity is not null; } }
    public string UserName { get { lock (_gate) return _identity?.UserName ?? ""; } }
    public string SetupToken { get { lock (_gate) return _setupToken; } }

    public void CompleteSetup(string token, string userName, string password)
    {
        lock (_gate)
        {
            if (_identity is not null || !SameSecret(token, _setupToken)) throw new InvalidOperationException("首次设置凭据错误或已使用。");
            if (!Regex.IsMatch(userName ?? "", "^[A-Za-z0-9_.-]{3,32}$", RegexOptions.CultureInvariant)) throw new ArgumentException("管理账号须为 3–32 位字母、数字或 _.-。");
            if (password is null || password.Length < 12 || password.Length > 128) throw new ArgumentException("管理密码须为 12–128 个字符。");
            var salt = RandomNumberGenerator.GetBytes(32);
            var identity = new Identity(userName!, Convert.ToBase64String(salt), Convert.ToBase64String(Hash(password, salt)));
            AtomicFile.Write(_paths.IdentityFile, JsonSerializer.Serialize(identity), privateFile: true);
            _identity = identity;
            _setupToken = "";
            File.Delete(_paths.SetupTokenFile);
        }
    }
    public bool VerifyLogin(string userName, string password, string source)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_attempts.TryGetValue(source, out var previous) && now - previous.Start < TimeSpan.FromMinutes(5) && previous.Failures >= 10) return false;
            var ok = _identity is not null && password is { Length: <= 128 }
                && CryptographicOperations.FixedTimeEquals(Hash(password, Convert.FromBase64String(_identity.Salt)), Convert.FromBase64String(_identity.Hash))
                && _identity.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase);
            if (ok) { _attempts.Remove(source); return true; }
            foreach (var expired in _attempts.Where(x => now - x.Value.Start >= TimeSpan.FromMinutes(5)).Select(x => x.Key).ToArray()) _attempts.Remove(expired);
            if (_attempts.Count < 4096 || _attempts.ContainsKey(source)) _attempts[source] = previous.Start == default || now - previous.Start >= TimeSpan.FromMinutes(5) ? (now, 1) : (previous.Start, previous.Failures + 1);
            return false;
        }
    }
    public static bool SameSecret(string? supplied, string expected) => supplied is not null && expected.Length > 0 && supplied.Length <= 1024
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
    private static byte[] Hash(string value, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(value, salt, 210_000, HashAlgorithmName.SHA256, 32);
}
