using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DFLegacy.Server;

// Gift admission only: self-purchase keeps its existing script semantics.
internal static class CreatureEggGiftLifetime
{
    private static readonly Regex Tags = new(@"\[[^\]\r\n]*\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool IsPermanent(string scriptText)
    {
        // Comments and backtick strings cannot introduce a lifetime tag.
        var source = new StringBuilder(scriptText.Length);
        var quoted = false;
        var block = false;
        var line = false;
        for (var i = 0; i < scriptText.Length; i++)
        {
            var c = scriptText[i];
            var next = i + 1 < scriptText.Length ? scriptText[i + 1] : '\0';
            if (line)
            {
                if (c is '\r' or '\n') { line = false; source.Append(c); }
                else source.Append(' ');
            }
            else if (block)
            {
                if (c == '*' && next == '/') { block = false; source.Append("  "); i++; }
                else source.Append(c is '\r' or '\n' ? c : ' ');
            }
            else if (quoted)
            {
                if (c == '`') quoted = false;
                source.Append(c is '\r' or '\n' ? c : ' ');
            }
            else if (c == '`') { quoted = true; source.Append('~'); }
            else if (c == '/' && next == '/') { line = true; source.Append("  "); i++; }
            else if (c == '/' && next == '*') { block = true; source.Append("  "); i++; }
            else source.Append(c);
        }
        if (quoted || block) return false;
        var text = source.ToString();
        var tags = Tags.Matches(text);
        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            if (!tag.Value.Equals("[usable period]", StringComparison.OrdinalIgnoreCase)
                && !tag.Value.Equals("[expiration date]", StringComparison.OrdinalIgnoreCase)
                && !tag.Value.Equals("[usable period after unsealing]", StringComparison.OrdinalIgnoreCase)) continue;
            var start = tag.Index + tag.Length;
            var end = i + 1 < tags.Count ? tags[i + 1].Index : text.Length;
            if (!int.TryParse(text.AsSpan(start, end - start).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var value) || value != 0) return false;
        }
        return true;
    }
}
