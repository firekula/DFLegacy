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
        if(bytes.Length is 0 or >4096||bytes.Any(b=>b>126||b<32&&b is not (10 or 13)))throw new InvalidDataException("启动恢复描述文件大小或编码无效。");
        var text=Encoding.ASCII.GetString(bytes).Replace("\r\n","\n");
        if(text.EndsWith('\n'))text=text[..^1];
        var lines=text.Split('\n');
        if(lines.Length!=5||lines.Any(l=>l.Length==0||l.Contains('\r')))throw new InvalidDataException("启动恢复描述文件必须包含五行有效字段。");
        if(lines[0]!="[Bootstrap]")throw new InvalidDataException("启动恢复描述文件缺少 Bootstrap 节。");
        var values=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var line in lines.Skip(1))
        {
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
    internal static void RequireStableEntry(string root,Core.ClientManifest manifest)
    {
        RequireManifest(manifest);
        var entry=manifest.Files.Single(f=>f.Path==EntryName);
        if(!Core.FileVerifier.MatchesAsync(Core.PathPolicy.Resolve(root,EntryName),entry.Size,entry.Sha256).GetAwaiter().GetResult())
            throw new InvalidDataException("此更新需要替换正在运行的固定入口。请使用新的完整启动包；当前入口和恢复副本已保留。");
    }
    // This gate only reads completed evidence. It must never create a record or
    // fall back to hashing resources before the repair progress UI is available.
    internal static bool TryValidateCompleted(string root,string architecture,Core.ReleaseSource? providerSource=null,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        using(var lease=Core.InstallationLease.Acquire(root))
            if(HasPending(root))return false;
        var settings=RepairClient.ReadSettings(new(root));
        if(providerSource is not null&&providerSource!=settings.Source)throw new InvalidDataException("修复提供方与配置/安装记录的来源不一致。");
        using(var lease=Core.InstallationLease.Acquire(root))
        {
            if(HasPending(root))return false;
            var store=new Core.InstallationStore(root);
            Core.InstalledReceipt? receipt;
            try{receipt=store.ReadInstalled();}catch(InvalidDataException){return false;}
            if(receipt is null)return false;
            receipt.Release.Validate(settings.Source);
            if(!store.HasValidatedReceipt(receipt))return false;
            var migrations=receipt.Release.Manifest.Migrations;
            if(migrations.Length>0)
            {
                var path=Core.PathPolicy.Resolve(root,"launcher.json",true);
                if(File.Exists(path)&&new FileInfo(path).Length>Core.ConfigurationMigration.MaxBytes)throw new InvalidDataException("配置文件过大。");
                if(!Core.ConfigurationMigration.IsSatisfied(File.Exists(path)?File.ReadAllBytes(path):"{}"u8.ToArray(),migrations))return false;
            }
            token.ThrowIfCancellationRequested();
            try{RequireReceiptLayout(root,architecture,receipt);}catch(InvalidDataException){return false;}
            return true;
        }
    }
    private static bool HasPending(string root)
    {
        var legacy=Path.Combine(root,".dflegacy","pending.json");Core.PathPolicy.NoLinks(legacy);
        return File.Exists(legacy)||Directory.Exists(legacy)||Core.InstallTransaction.HasPending(root);
    }
    internal static async Task ValidateInstalledAsync(string root,string architecture,CancellationToken token=default)
    {
        var settings=RepairClient.ReadSettings(new(root));
        // The v0.1.8 updater can install this UI before it knows how to write
        // validated.json. Verify that installation once; later handoffs reuse
        // its receipt. This local operation never discovers or downloads releases.
        using var http=Core.ReleaseProvider.CreateHttpClient();
        var engine=new Core.UpdateEngine(root,new Core.ReleaseProvider(http,settings.Source));
        var validation=await engine.ValidateInstalledAsync(token:token).ConfigureAwait(false);
        if(!validation.CanLaunch)throw new InvalidDataException(validation.Message);
        using var lease=Core.InstallationLease.Acquire(root);
        if(Core.InstallTransaction.HasPending(root))throw new InvalidDataException("存在未完成更新，必须恢复后才能打开登录器。");
        token.ThrowIfCancellationRequested();
        var store=new Core.InstallationStore(root);
        var receipt=store.ReadInstalled()??throw new InvalidDataException("缺少已验证的启动组件安装记录。");
        receipt.Release.Validate(settings.Source);
        if(!store.HasValidatedReceipt(receipt))throw new InvalidDataException("启动组件校验记录无效；请使用原来的启动图标自动修复。");
        RequireReceiptLayout(root,architecture,receipt);
    }
    private static void RequireReceiptLayout(string root,string architecture,Core.InstalledReceipt receipt)
    {
        RequireManifest(receipt.Release.Manifest);
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
