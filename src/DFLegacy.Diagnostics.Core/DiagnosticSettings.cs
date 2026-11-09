using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DFLegacy.Diagnostics;

public sealed record DiagnosticPreference(bool Enabled,string Epoch,bool LocalSmallDumps=true);
public sealed record DiagnosticStatusSnapshot(string Receiver,bool Enabled,int Pending,int Held,int Uploaded,string LastStatus,string? ReceiptId,bool Available,bool IsSending=false);
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
    internal static bool Recoverable(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;
}

public sealed class DiagnosticSettings
{
    // Inspection never selects a scope, creates files, takes a send lease, or performs network I/O.
    public DiagnosticStatusSnapshot GetStatus(string? clientRoot,string serviceUrl)
    {
        var receiver="unknown";
        try
        {
            receiver=DiagnosticSession.NormalizeSite(serviceUrl);var preference=Read();
            var installation=clientRoot is null?null:Path.Combine(Root,"installations",DiagnosticFiles.Hash(Path.GetFullPath(clientRoot).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()));
            var active=installation is null?null:DiagnosticFiles.Read<DiagnosticScope>(Path.Combine(installation,"active.json"),4096);
            var displayScope=active is not null&&active.Site==receiver&&DiagnosticProjection.Id(active.Epoch)?Path.Combine(installation!,"queues",active.Epoch):null;
            var eligibleScope=preference.Enabled&&active?.PreferenceEpoch==preference.Epoch?displayScope:null;
            var pending=0;var held=0;var uploaded=0;var last="never";string? receipt=null;var latest=DateTime.MinValue;
            var storage=Path.Combine(Root,"queue");DiagnosticFiles.NoLinks(storage);
            if(Directory.Exists(storage))
            {
                var files=Directory.EnumerateFiles(storage,"*.json").Take(101).ToArray();
                if(files.Length>100)throw new InvalidDataException("Diagnostic status exceeds record budget.");
                foreach(var path in files)
                {
                    var value=DiagnosticFiles.Read<FrozenDiagnostic>(path,400000);
                    if(value is null||value.Scope is null||value.Status is null||!DiagnosticProjection.Id(value.EventId))throw new InvalidDataException("Invalid diagnostic status record.");
                    if(installation is null||!value.Scope.StartsWith(Path.Combine(installation,"queues")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||value.CreatedAt<DateTimeOffset.UtcNow.AddDays(-14))continue;
                    var isPending=value.Scope==eligibleScope&&value.Status is "pending" or "retry" or "exhausted";
                    if(isPending)pending++;else if(value.Scope!=eligibleScope||value.Status!="uploaded")held++;
                    if(value.Scope==displayScope&&value.Status=="uploaded")uploaded++;
                    var updated=File.GetLastWriteTimeUtc(path);
                    if(value.Scope==displayScope&&value.Attempts>0&&updated>=latest)
                    {
                        latest=updated;last=value.Status;receipt=DiagnosticProjection.Id(value.ReceiptId)?value.ReceiptId:null;
                    }
                }
            }
            var sending=false;var upload=Path.Combine(Root,"upload.lock");DiagnosticFiles.NoLinks(upload);
            if(File.Exists(upload))
            {
                try {using var inspect=new FileStream(upload,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);}
                catch(IOException error) when((error.HResult&0xffff) is 32 or 33){sending=true;}
            }
            return new(receiver,preference.Enabled,pending,held,uploaded,last,receipt,true,sending);
        }
        catch(Exception error) when(DiagnosticFiles.Recoverable(error)){return new(receiver,false,0,0,0,"local-error",null,false);}
    }
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
        if(current.Enabled!=enabled)DiagnosticFiles.Write(Path.Combine(Root,"preference.json"),current with {Enabled=enabled,Epoch=Guid.NewGuid().ToString("N")});
    }
    public void SetLocalSmallDumps(bool enabled)
    {
        using var held=DiagnosticFiles.Lease(Root,"settings.lock");var current=Read();
        if(current.LocalSmallDumps!=enabled)DiagnosticFiles.Write(Path.Combine(Root,"preference.json"),current with {LocalSmallDumps=enabled});
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
    public string Describe(string serviceUrl,string? clientRoot=null)
    {
        var value=GetStatus(clientRoot,serviceUrl);
        var state=value.LastStatus switch {"never"=>"尚未发送","uploaded"=>"上传成功","retry"=>"发送失败，等待重试","exhausted"=>"本次重试已用完，等待下次启动","unsupported"=>"接收服务未启用或不支持","deleted"=>"服务器记录已删除","conflict"=>"事件正文冲突，已停止发送","redirect-rejected"=>"已拒绝接收站点重定向","rejected"=>"服务器拒绝报告","pending"=>value.IsSending?"等待发送回执":"上次发送未完成，等待重试",_=>"本地状态不可用"};
        var transport=value.Receiver=="unknown"?"连接方式未知":value.Receiver.StartsWith("http:",StringComparison.Ordinal)?"HTTP 未加密":"HTTPS";
        return $"接收地址：{value.Receiver}\n{(value.Available?(value.Enabled?"自动基本诊断已开启":"自动基本诊断已关闭（当前用户）"):"诊断状态不可用，仅保留本地记录")} · {transport}\n待发送：{value.Pending} · 本地保留：{value.Held} · 已上传：{value.Uploaded}\n最近结果：{state}{(value.IsSending?" · 上传器忙":"")}{(value.ReceiptId is null?"":"\n回执："+value.ReceiptId)}\n原始转储仅本地";
    }
}
