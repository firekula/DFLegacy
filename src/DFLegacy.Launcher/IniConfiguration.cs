using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DFLegacy.Launcher;

internal readonly record struct IniValue(string Section, string Key, string Value);

/// <summary>Edits only managed ASCII keys; legacy comment/value bytes remain lossless.</summary>
internal sealed class IniConfiguration
{
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;
    private readonly string _text;
    internal byte[] OriginalBytes { get; }
    internal bool NativeEncodingSupported => _encoding.CodePage is not (1201 or 12000 or 12001)
        // Win32 treats a UTF8 BOM as ANSI bytes, hiding a section on the first line.
        && !(_encoding.CodePage == 65001 && _preamble.Length > 0 && _text.Split(['\r', '\n'], 2)[0].TrimStart().StartsWith('['));

    private IniConfiguration(byte[] bytes, Encoding encoding, byte[] preamble)
    {
        OriginalBytes = bytes;
        _encoding = encoding;
        _preamble = preamble;
        _text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
    }

    internal static IniConfiguration Load(string path) => Parse(File.ReadAllBytes(path));

    internal static IniConfiguration Parse(byte[] bytes)
    {
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("INI 配置超过 1 MiB。");
        foreach (var candidate in new Encoding[] { new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true),
                     new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true), new UTF8Encoding(true, true) })
        {
            var preamble = candidate.GetPreamble();
            if (bytes.AsSpan().StartsWith(preamble)) return new(bytes, candidate, preamble);
        }
        // A byte-preserving representation also retains BOM-less UTF8 without decoding unrelated text.
        return new(bytes, Encoding.Latin1, []);
    }

    // Win32 ends the name at the first ']' (or end of line), not at the
    // end of a commented header. Only native ASCII padding is insignificant.
    private static string? SectionName(string content)
    {
        var line = content.TrimStart(' ', '\t');
        if (!line.StartsWith('[')) return null;
        var end = line.IndexOf(']', 1);
        return line[1..(end < 0 ? line.Length : end)].Trim(' ', '\t');
    }

    internal bool HasSection => Regex.Split(_text, "\r\n|\n|\r").Any(line => SectionName(line) is { Length: > 0 });

    internal byte[] With(params IniValue[] updates)
    {
        var text = _text;
        foreach (var update in updates)
        {
            var parts = Regex.Split(text, "(\r\n|\n|\r)");
            var newline = parts.Length > 1 ? parts[1] : "\r\n";
            var inSection = false;
            var sectionStart = -1;
            var firstSection = -1;
            var firstHasKey = false;
            for (var i = 0; i < parts.Length; i += 2)
            {
                var line = parts[i].Trim(' ', '\t');
                var section = SectionName(parts[i]);
                if (section is not null)
                {
                    sectionStart = i;
                    inSection = section.Equals(update.Section, StringComparison.OrdinalIgnoreCase);
                    if (inSection && firstSection < 0) firstSection = i;
                    continue;
                }
                if (!inSection || line.StartsWith(';') || line.StartsWith('#')) continue;
                var equals = parts[i].IndexOf('=');
                if (equals < 0 || !parts[i][..equals].Trim(' ', '\t').Equals(update.Key, StringComparison.OrdinalIgnoreCase)) continue;
                if (!parts[i][(equals + 1)..].Trim(' ', '\t').Equals(update.Value, StringComparison.Ordinal))
                    parts[i] = parts[i][..(equals + 1)] + update.Value;
                // First section stops at its next header, including another section with the same name.
                if (sectionStart == firstSection) firstHasKey = true;
            }
            if (firstSection >= 0)
            {
                if (!firstHasKey) parts[firstSection] += newline + update.Key + "=" + update.Value;
                text = string.Concat(parts);
            }
            else text += (text.Length > 0 && !text.EndsWith('\n') && !text.EndsWith('\r') ? newline : "")
                + "[" + update.Section + "]" + newline + update.Key + "=" + update.Value + newline;
        }
        return _preamble.Concat(_encoding.GetBytes(text)).ToArray();
    }

    internal static void SaveExpected(string path, byte[]? expectedBytes, byte[] outputBytes)
    {
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        using var gate = new Mutex(false, @"Local\DFLegacy.Ini." + identity);
        var acquired = false;
        try
        {
            try { acquired = gate.WaitOne(TimeSpan.FromSeconds(3)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("其他登录器正在保存配置，请稍后重试。");
            void Verify()
            {
                var exists = File.Exists(path);
                if (expectedBytes is null)
                {
                    if (exists) throw new IOException("配置已由其他程序创建，请重新读取后保存。");
                }
                else if (!exists || !File.ReadAllBytes(path).AsSpan().SequenceEqual(expectedBytes))
                    throw new IOException("配置已被其他程序修改，请重新读取后保存。");
            }
            Verify();
            if (expectedBytes is not null && expectedBytes.AsSpan().SequenceEqual(outputBytes)) return;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(outputBytes); stream.Flush(flushToDisk: true); }
                Verify();
                // The byte recheck and same-user mutex detect stale/cooperating edits.
                // File.Move is atomic replacement, not a cross-process compare-and-swap.
                File.Move(temporary, path, overwrite: expectedBytes is not null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { if (acquired) gate.ReleaseMutex(); }
    }
}
