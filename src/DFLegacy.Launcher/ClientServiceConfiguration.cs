using System.Text;
using System.Text.RegularExpressions;

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
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
        // ASCII URL updates must preserve every unrelated byte, including old
        // ANSI and UTF-8 INIs. UTF-16/32 BOM files retain their encoding and BOM.
        Encoding encoding = Encoding.Latin1;
        var offset = 0;
        foreach (var candidate in new[] { Encoding.UTF32, new UTF32Encoding(true, true),
                     Encoding.Unicode, Encoding.BigEndianUnicode, new UTF8Encoding(true) })
        {
            var preamble = candidate.GetPreamble();
            if (bytes.AsSpan().StartsWith(preamble))
            {
                encoding = candidate;
                offset = preamble.Length;
                break;
            }
        }
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        var parts = Regex.Split(text, "(\r\n|\n|\r)");
        var newline = parts.Length > 1 ? parts[1] : "\r\n";
        var inNetwork = false;
        var inFirstNetwork = false;
        var firstNetwork = -1;
        var firstNetworkContainsKey = false;
        for (var i = 0; i < parts.Length; i += 2)
        {
            var line = parts[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inNetwork = line[1..^1].Trim().Equals("Network", StringComparison.OrdinalIgnoreCase);
                if (inNetwork && firstNetwork < 0) firstNetwork = i;
                inFirstNetwork = inNetwork && i == firstNetwork;
                continue;
            }
            if (!inNetwork || line.StartsWith(';') || line.StartsWith('#')) continue;
            var equals = parts[i].IndexOf('=');
            if (equals >= 0 && parts[i][..equals].Trim().Equals("ClientServiceUrl", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = parts[i][..(equals + 1)] + normalizedBaseUrl;
                if (inFirstNetwork) firstNetworkContainsKey = true;
            }
        }
        if (firstNetwork >= 0)
        {
            // Insert into the first section, including a section at EOF without
            // a newline. Duplicate sections cannot shadow the installed value.
            if (!firstNetworkContainsKey)
                parts[firstNetwork] += newline + "ClientServiceUrl=" + normalizedBaseUrl;
            text = string.Concat(parts);
        }
        else
            text += (text.Length > 0 && !text.EndsWith('\n') && !text.EndsWith('\r') ? newline : "")
                + "[Network]" + newline + "ClientServiceUrl=" + normalizedBaseUrl + newline;

        var content = encoding.GetBytes(text);
        var output = offset == 0 ? content : encoding.GetPreamble().Concat(content).ToArray();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, output);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
