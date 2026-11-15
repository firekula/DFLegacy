using System.Text.Json;
using Core = DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal sealed record ChapterTransitionOffer(bool Rollback, Core.ReleaseSource Source, string Version, bool Enrollment=false)
{
    internal static Core.ReleaseSource? ReadSelectedSource(string root) => ReadLoopbackOverride(root,"chapter-upgrade-source.json");

    private static Core.ReleaseSource? ReadLoopbackOverride(string root,string name)
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
            if(source.Provider!="local") throw new InvalidDataException("章节更新设置仅接受本机验收覆盖来源。");
            return source;
        }
        catch(JsonException ex) { throw new InvalidDataException("章节更新设置无效。",ex); }
    }

    internal static ChapterTransitionOffer? Read(string root)
    {
        var receipt=new Core.InstallationStore(root).ReadInstalled();
        if(receipt==null)
        {
            var source=ReadLoopbackOverride(root,"chapter-bridge-source.json");
            if(source==null)return null;
            var legacy=ClientPackageManifest.LoadAndValidate(root);
            return new(false,source,legacy.Version,Enrollment:true);
        }
        var identity=ChapterUpdatePolicy.Validate(root,receipt.Release);
        if(identity==null) return null;
        var installed=receipt.Release.Source;
        if(identity.Chapter==Core.ClientChapterIdentity.ChapterOne)
        {
            var source=ReadSelectedSource(root)??Core.ChapterTransitionSource.OfficialTarget(installed);
            if(source==null) return null;
            Core.ChapterTransitionSource.RequireUpgrade(installed,source);
            return new(false,source,receipt.Release.Manifest.Version);
        }
        return Core.ChapterTransition.HasRollbackCheckpoint(root,installed,identity.Entry)
            ?new(true,installed,receipt.Release.Manifest.Version):null;
    }
}
