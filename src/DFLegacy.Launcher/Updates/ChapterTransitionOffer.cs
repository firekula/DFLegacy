using System.Text.Json;
using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal sealed record ChapterTransitionOffer(bool Rollback, Core.ReleaseSource Source, string Version, bool Enrollment=false)
{
    internal static Core.ReleaseSource? ReadSelectedSource(string root) => ReadSource(root,"chapter-upgrade-source.json");

    private static Core.ReleaseSource? ReadSource(string root,string name)
    {
        var path=Path.Combine(Path.GetFullPath(root),".dflegacy",name);
        Core.PathPolicy.NoLinks(path);
        if(!File.Exists(path)) return null;
        if(new FileInfo(path).Length>65536) throw new InvalidDataException("章节更新设置过大。");
        try
        {
            var source=JsonSerializer.Deserialize<Core.ReleaseSource>(File.ReadAllBytes(path),Core.ManifestCodec.Json)
                ??throw new InvalidDataException("章节更新设置为空。");
            source.Validate();
            if(source.Provider!="local") throw new InvalidDataException("当前章节更新仅支持本机验收来源。");
            return source;
        }
        catch(JsonException ex) { throw new InvalidDataException("章节更新设置无效。",ex); }
    }

    internal static ChapterTransitionOffer? Read(string root)
    {
        var receipt=new Core.InstallationStore(root).ReadInstalled();
        if(receipt==null)
        {
            var source=ReadSource(root,"chapter-bridge-source.json");
            if(source==null)return null;
            var legacy=ClientPackageManifest.LoadAndValidate(root);
            return new(false,source,legacy.Version,Enrollment:true);
        }
        var identity=ChapterUpdatePolicy.Validate(root,receipt.Release);
        if(identity==null) return null;
        if(identity.Chapter==Core.ClientChapterIdentity.ChapterOne)
            return ReadSelectedSource(root) is {} source ? new(false,source,receipt.Release.Manifest.Version) : null;
        var checkpoint=Path.Combine(root,".dflegacy","chapter-transition","checkpoint.json");
        Core.PathPolicy.NoLinks(checkpoint);
        return File.Exists(checkpoint)&&receipt.Release.Source.Provider=="local"
            ?new(true,receipt.Release.Source,receipt.Release.Manifest.Version):null;
    }
}
