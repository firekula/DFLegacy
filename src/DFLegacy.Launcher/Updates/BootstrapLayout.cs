using System.Runtime.InteropServices;
using System.Text;
using Core=DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal sealed record BootstrapDescriptor(string Architecture,string LauncherSha256,string UpdaterSha256);

internal static class BootstrapLayout
{
    internal const string EntryName="DFLegacy.Launcher.exe",AppName="DFLegacy.Launcher.App.exe",UpdaterName="DFLegacy.Updater.exe",DescriptorName="DFLegacy.Bootstrap.ini";
    internal static string Architecture=>RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.X86?"x86":"x64";
    internal static BootstrapDescriptor Parse(byte[] bytes,string architecture)
    {
        if(bytes.Length is 0 or >4096||bytes.Any(b=>b>127||b==0))throw new InvalidDataException("启动恢复描述文件大小或编码无效。");
        var lines=Encoding.ASCII.GetString(bytes).Replace("\r\n","\n").Split('\n');
        if(lines[0]!="[Bootstrap]")throw new InvalidDataException("启动恢复描述文件缺少 Bootstrap 节。");
        var values=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var line in lines.Skip(1))
        {
            if(line.Length==0)continue;
            var split=line.IndexOf('=');
            if(split<1||!values.TryAdd(line[..split],line[(split+1)..]))throw new InvalidDataException("启动恢复描述文件包含重复或无效字段。");
        }
        if(values.Count!=4||!values.TryGetValue("SchemaVersion",out var schema)||schema!="1"
            ||!values.TryGetValue("Architecture",out var arch)||arch!=architecture||arch is not ("x86" or "x64")
            ||!values.TryGetValue("LauncherSha256",out var app)||!Core.ManifestCodec.IsHash(app)
            ||!values.TryGetValue("UpdaterSha256",out var updater)||!Core.ManifestCodec.IsHash(updater))
            throw new InvalidDataException("启动恢复描述文件架构、版本或哈希无效。");
        return new(arch,app,updater);
    }
    internal static void RequireManifest(Core.ClientManifest manifest)
    {
        Core.ManifestCodec.Validate(manifest);
        foreach(var name in new[]{EntryName,AppName,UpdaterName,DescriptorName,"DFLegacy.Client.dll"})
        {
            var file=manifest.Files.SingleOrDefault(f=>f.Path==name);
            if(file is null||file.Size<=0||name==DescriptorName&&file.Size>4096)
                throw new InvalidDataException("更新源没有提供完整的自动修复启动组件，已保留当前入口。缺少或无效："+name);
        }
        if(manifest.Architecture!=Architecture)throw new InvalidDataException("自动修复启动组件架构不匹配。");
    }
    internal static async Task ValidateInstalledAsync(string root,string architecture,CancellationToken token=default)
    {
        using var lease=Core.InstallationLease.Acquire(root);
        if(Core.InstallTransaction.HasPending(root))throw new InvalidDataException("存在未完成更新，必须恢复后才能打开登录器。");
        var receipt=new Core.InstallationStore(root).ReadInstalled()??throw new InvalidDataException("缺少已验证的启动组件安装记录。");
        RequireManifest(receipt.Release.Manifest);
        var result=await Core.FileVerifier.VerifyAsync(root,receipt.Release.Manifest,token:token).ConfigureAwait(false);
        if(!result.IsValid)throw new InvalidDataException("启动组件未通过完整校验；请使用原来的启动图标自动修复。");
        var path=Core.PathPolicy.Resolve(root,DescriptorName);Core.PathPolicy.NoLinks(path);
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>4096)throw new InvalidDataException("启动恢复描述文件过大。");
        var bytes=new byte[stream.Length];stream.ReadExactly(bytes);
        var descriptor=Parse(bytes,architecture);
        var files=receipt.Release.Manifest.Files;
        if(files.Single(f=>f.Path==AppName).Sha256!=descriptor.LauncherSha256||files.Single(f=>f.Path==UpdaterName).Sha256!=descriptor.UpdaterSha256)
            throw new InvalidDataException("启动恢复描述文件与已验证清单不一致。");
    }
    internal static FileStream AcquireAppLock(string root)
    {
        var path=Path.Combine(Path.GetFullPath(root),".dflegacy","bootstrap","app.lock");Core.PathPolicy.NoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);Core.PathPolicy.NoLinks(path);
        return new(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
}
