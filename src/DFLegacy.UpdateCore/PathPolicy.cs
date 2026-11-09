using System.Text;
using System.Text.RegularExpressions;

namespace DFLegacy.UpdateCore;

public static class PathPolicy
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    { "launcher.json", "launcher.txt", "Config.ini", "client-package.json", "client-update-v4.json", ".dflegacy", "Logs", "Screenshot", "Screenshots", "Save", "Saves", "Accounts", "CrashReports", "Diagnostics", "sessions", "collections", "captures", "queue", "events", "installations", "preference.json", "settings.json", "active.json", "preferences.json", "session.json", "closed.json", "drops.json" };
    public static void Validate(string relative, bool allowConfiguration = false)
    {
        if (string.IsNullOrEmpty(relative) || relative.Length > 1024 || !relative.IsNormalized(NormalizationForm.FormC)
            || relative.Any(c => char.IsControl(c) || "\\:<>\"|?*".Contains(c))) throw new InvalidDataException("更新文件路径无效。");
        var parts = relative.Split('/');
        foreach (var part in parts)
        {
            if (part.Length is 0 or > 255 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || Regex.IsMatch(part,@"~[0-9]",RegexOptions.CultureInvariant)
                || Regex.IsMatch(part, @"\A(?:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("更新路径包含 Windows 别名或无效分量。");
        }
        if (parts.Any(p => Protected.Contains(p) || p.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                || Protected.Any(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && (p.StartsWith(n+".", StringComparison.OrdinalIgnoreCase) || p.StartsWith(n+"~", StringComparison.OrdinalIgnoreCase))))
            && !(allowConfiguration && relative == "launcher.json"))
            throw new InvalidDataException("更新不能覆盖玩家配置、日志或运行数据：" + relative);
    }
    public static string Resolve(string root, string relative, bool allowConfiguration = false)
    {
        Validate(relative, allowConfiguration);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新路径越界。");
        NoLinks(path);
        return path;
    }
    public static void NoLinks(string path)
    {
        for (string? item = Path.GetFullPath(path); item != null; item = Path.GetDirectoryName(item))
        {
            try
            {
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("更新路径不能经过链接：" + item);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
