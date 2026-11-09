using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DFLegacy.Diagnostics;

public sealed record DiagnosticPreference(bool Enabled,string Epoch);
internal sealed record DiagnosticScope(string Site,string Epoch,string PreferenceEpoch);

internal static class DiagnosticFiles
{
    internal static string Root(string? root) => Path.GetFullPath(root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DFLegacy","Diagnostics"));
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static void NoLinks(string path)
    {
        for(var current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new IOException("Diagnostic path contains a link.");
    }
    internal static void Ensure(string path) { NoLinks(path);Directory.CreateDirectory(path);NoLinks(path); }
    internal static T? Read<T>(string path,int max=262144)
    {
        NoLinks(path); if(!File.Exists(path))return default;
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        if(file.Length>max)throw new InvalidDataException("Diagnostic file exceeds budget.");
        return JsonSerializer.Deserialize<T>(file,DiagnosticJson.Options);
    }
    internal static void Write<T>(string path,T value,bool replace=true)
    {
        NoLinks(path);Ensure(Path.GetDirectoryName(path)!);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { JsonSerializer.Serialize(file,value,DiagnosticJson.Options);file.Flush(true); }NoLinks(path);File.Move(temp,path,replace); }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    internal static FileStream Lease(string root,string name) { Ensure(root);var path=Path.Combine(root,name);NoLinks(path);return new(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
    internal static bool Recoverable(Exception error) => error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;
}

public sealed class DiagnosticSettings
{
    public string Root { get; }
    public DiagnosticSettings(string? root=null) => Root=DiagnosticFiles.Root(root);
    public DiagnosticPreference Read()
    {
        var value=DiagnosticFiles.Read<DiagnosticPreference>(Path.Combine(Root,"preference.json"));
        if(value is not null&&!DiagnosticProjection.Id(value.Epoch)) throw new InvalidDataException("Invalid diagnostic preference.");
        return value??new(true,"00000000000000000000000000000000");
    }
    public void SetEnabled(bool enabled)
    {
        using var held=DiagnosticFiles.Lease(Root,"settings.lock");var current=Read();
        if(current.Enabled!=enabled)DiagnosticFiles.Write(Path.Combine(Root,"preference.json"),new DiagnosticPreference(enabled,Guid.NewGuid().ToString("N")));
    }
    internal void RecordDrop()
    {
        try {using var held=DiagnosticFiles.Lease(Root,"drops.lock");var path=Path.Combine(Root,"drops.json");var count=DiagnosticFiles.Read<long>(path,64);DiagnosticFiles.Write(path,count==long.MaxValue?count:count+1);}
        catch(Exception error) when(DiagnosticFiles.Recoverable(error)){ }
    }
    internal (string Installation,string Queue) SelectScope(string clientRoot,string serviceUrl)
    {
        using var held=DiagnosticFiles.Lease(Root,"settings.lock");var preference=Read();
        var installation=Path.Combine(Root,"installations",DiagnosticFiles.Hash(Path.GetFullPath(clientRoot).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()));
        var installations=Path.Combine(Root,"installations");DiagnosticFiles.Ensure(installations);
        if(!Directory.Exists(installation)&&Directory.EnumerateDirectories(installations).Take(101).Count()>=100){RecordDrop();throw new IOException("Diagnostic installation budget exhausted.");}
        DiagnosticFiles.Ensure(installation);var path=Path.Combine(installation,"active.json");var current=DiagnosticFiles.Read<DiagnosticScope>(path);
        if(current is null||current.Site!=serviceUrl||current.PreferenceEpoch!=preference.Epoch) { current=new(serviceUrl,Guid.NewGuid().ToString("N"),preference.Epoch);DiagnosticFiles.Write(path,current); }
        return (installation,Path.Combine(installation,"queues",current.Epoch));
    }
    public string Describe(string serviceUrl)
    {
        try { if(!Read().Enabled)return "自动基本诊断已关闭（当前用户）";return DiagnosticSession.NormalizeSite(serviceUrl).StartsWith("http:",StringComparison.Ordinal)?"自动基本诊断已开启 · HTTP 未加密 · 原始转储仅本地":"自动基本诊断已开启 · HTTPS · 原始转储仅本地"; }
        catch(Exception error) when(DiagnosticFiles.Recoverable(error)){return "诊断服务不可用，仅保留本地记录";}
    }
}
