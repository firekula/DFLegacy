using System.Text.RegularExpressions;

namespace DFLegacy.Server;

internal static class AvatarAbilityOptions
{
    private static readonly Regex SectionPattern = new(
        @"^\s*\[avatar select ability\]\s*(?<value>.*?)^\s*\[/avatar select ability\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase |
        RegexOptions.Multiline | RegexOptions.Singleline);

    internal static int ReadOptionCount(string text)
    {
        // Remove comments before finding the closing tag: a commented tag cannot
        // make an incomplete list valid or introduce a fabricated option section.
        var source = string.Join('\n', (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(StripComment));
        var match = SectionPattern.Match(source);
        if (!match.Success) return 0;
        return match.Groups["value"].Value.Split('\n')
            .Count(line => line.TrimStart().StartsWith("`[", StringComparison.Ordinal));
    }

    private static string StripComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '`') quoted = !quoted;
            else if (!quoted && line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/') return line[..i];
        }
        return line;
    }
}
