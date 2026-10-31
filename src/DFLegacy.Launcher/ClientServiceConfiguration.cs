
namespace DFLegacy.Launcher;

internal static class ClientServiceConfiguration
{
    internal static string NormalizeBaseUrl(string value)
    {
        // Match the native reader's bounds and formatter-safe ASCII subset.
        if (string.IsNullOrEmpty(value) || value.Length > 1000
            || value.Any(c => c <= 32 || c >= 127 || "%\\\"?#@".Contains(c))
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.IsWellFormedOriginalString()
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host) || uri.Port is < 1 or > 65535
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("账号服务地址必须是最多 1000 字节的 HTTP(S) 基址，不能包含凭据、查询、片段、空白或百分号。");
        return value.TrimEnd('/');
    }

    internal static void Update(string path, string normalizedBaseUrl)
    {
        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var output = IniConfiguration.Parse(before ?? []).With(new IniValue("Network", "ClientServiceUrl", normalizedBaseUrl));
        IniConfiguration.SaveExpected(path, before, output);
    }
}
