using System.Text.RegularExpressions;
using System.Text.Json;

namespace DFLegacy.Launcher;

internal static class ClientLogPaths
{
    internal static string GetDirectory(string? clientPath) => Path.Combine(
        clientPath is null ? AppContext.BaseDirectory : Path.GetDirectoryName(Path.GetFullPath(clientPath))!,"Logs","DFLegacy");

    internal static bool HasLink(string path)
    {
        for(var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) return true;
        return false;
    }
    internal static void EnsureDirectory(string path)
    {
        if(HasLink(path)) throw new IOException("日志路径不能是链接。");
        Directory.CreateDirectory(path);
        if(HasLink(path)) throw new IOException("日志路径不能是链接。");
    }
    internal static bool TryWrite(string? clientPath, string message)
    {
        try
        {
            var directory = GetDirectory(clientPath); EnsureDirectory(directory);
            var path = Path.Combine(directory,"launcher.log"); if(HasLink(path)) return false;
            File.AppendAllText(path,$"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}"); return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    internal static bool IsOwnedLeaf(string name) => name is "launcher.log" or "auto-input-diag.log" or "rs_log.txt"
        || Regex.IsMatch(name,@"\A(?:netdump|debug-overlay|channel-reentry|ground-item-removal|mage-skill-contact|party-combat-detail|priest-heal-target|stage-follow|summon-owner-death)-[1-9][0-9]{0,9}\.log\z",RegexOptions.CultureInvariant);

    internal static void ArchiveLegacy(string clientPath)
    {
        try
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(clientPath))!;
            if(HasLink(root)) return;
            var target = Path.Combine(GetDirectory(clientPath),"legacy"); EnsureDirectory(target);
            foreach(var file in Directory.EnumerateFiles(root).Where(p=>IsOwnedLeaf(Path.GetFileName(p)))) Move(file,target);
            var diagnostics=Path.Combine(root,"render-diagnostics");
            if(Directory.Exists(diagnostics) && !HasLink(diagnostics))
            {
                foreach(var file in Directory.EnumerateFiles(diagnostics,"*.json"))
                {
                    if(!IsOwnedDiagnostic(file)) continue;
                    var archived=Path.Combine(target,"render-diagnostics"); EnsureDirectory(archived); Move(file,archived);
                }
            }
            foreach(var name in new[]{"DNF.trc","NeopleEngine.LOG","NiMemory.log","NiProfile.log","SocketLog.log"})
            {
                var file=Path.Combine(root,name); if(File.Exists(file)) Move(file,Path.Combine(root,"Logs"));
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
    }
    private static void Move(string file,string target)
    {
        try
        {
            if(HasLink(file)) return;
            // Require exclusive access first: locked logs must survive for a later retry.
            using(var source = File.Open(file,FileMode.Open,FileAccess.Read,FileShare.Delete)) { }
            var destination = Path.Combine(target,Path.GetFileName(file));
            if(File.Exists(destination)) destination = Path.Combine(target,Path.GetFileNameWithoutExtension(file)+"."+Guid.NewGuid().ToString("N")+Path.GetExtension(file));
            File.Move(file,destination,false);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
    }
    private static bool IsOwnedDiagnostic(string file)
    {
        try
        {
            if(HasLink(file) || !Regex.IsMatch(Path.GetFileName(file),@"\A[1-9][0-9]{0,9}\.json\z")) return false;
            using var stream=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(stream.Length is <=0 or >65536) return false;
            using var document=JsonDocument.Parse(stream,new JsonDocumentOptions{MaxDepth=4}); var r=document.RootElement;
            return r.GetProperty("version").GetInt32()==1
                && r.GetProperty("pid").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)==Path.GetFileNameWithoutExtension(file)
                && r.TryGetProperty("processStartFileTime",out var start) && start.TryGetInt64(out var started) && started>0
                && r.TryGetProperty("writtenFileTime",out var time) && time.TryGetInt64(out var written) && written>=started
                && r.GetProperty("requested").GetString() is "sharp" or "linear" or "point" or "legacy"
                && r.GetProperty("effective").GetString() is "sharp" or "linear" or "point" or "legacy" or "original-fallback" or "device-lost" or "render-error"
                && r.GetProperty("reason").ValueKind==JsonValueKind.String && r.GetProperty("hresult").ValueKind==JsonValueKind.String
                && r.GetProperty("width").TryGetInt32(out _) && r.GetProperty("height").TryGetInt32(out _);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return false; }
    }
}
