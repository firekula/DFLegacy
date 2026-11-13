using System.Text.Json;
using DFLegacy.Launcher.Updates;
using Core = DFLegacy.UpdateCore;

internal static class ChapterTransitionOfferTests
{
    internal static void Run(string parent, Action<bool,string> check)
    {
        var root = Path.Combine(parent,"chapter-offer");
        Directory.CreateDirectory(Path.Combine(root,".dflegacy"));
        check(ChapterTransitionOffer.ReadSelectedSource(root)==null,"no implicit cross-chapter source");
        var path=Path.Combine(root,".dflegacy","chapter-upgrade-source.json");
        var local=new Core.ReleaseSource("local","http://127.0.0.2:48127/c2","c2-test","x64","test");
        File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(local,Core.ManifestCodec.Json));
        check(ChapterTransitionOffer.ReadSelectedSource(root)==local,"explicit isolated source accepted");
        foreach(var source in new[]{local with {Provider="github",Repository="firekula/DFLegacy"},local with{Repository="http://127.0.0.1:48127"},local with{Repository="http://127.0.0.2:48127/../c2"}})
        {
            File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(source,Core.ManifestCodec.Json));
            try { ChapterTransitionOffer.ReadSelectedSource(root);check(false,"unapproved cross-chapter source rejected"); }
            catch(InvalidDataException){check(true,"unapproved cross-chapter source rejected");}
        }
        File.WriteAllText(path,new string('x',65537));
        try { ChapterTransitionOffer.ReadSelectedSource(root);check(false,"oversize offer rejected"); }
        catch(InvalidDataException){check(true,"oversize offer rejected");}
        File.WriteAllText(path,"null");
        try { ChapterTransitionOffer.ReadSelectedSource(root);check(false,"null offer rejected"); }
        catch(InvalidDataException){check(true,"null offer rejected");}
        var bridge=Path.Combine(parent,"chapter-bridge-offer");
        Directory.CreateDirectory(Path.Combine(bridge,".dflegacy"));
        File.WriteAllBytes(Path.Combine(bridge,".dflegacy","chapter-bridge-source.json"),
            JsonSerializer.SerializeToUtf8Bytes(local with{Channel="c1-bridge"},Core.ManifestCodec.Json));
        var payload=new[]{"DFLegacy.Launcher.exe","DFLegacy.Client.dll"}.Select(name=>
        {
            var bytes=System.Text.Encoding.UTF8.GetBytes(name);
            File.WriteAllBytes(Path.Combine(bridge,name),bytes);
            return new {path=name,size=bytes.Length,sha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))};
        }).ToArray();
        File.WriteAllBytes(Path.Combine(bridge,"client-package.json"),JsonSerializer.SerializeToUtf8Bytes(
            new{schemaVersion=2,version="c1-bridge-test",commit=new string('a',40),flavor="normal",files=payload}));
        check(ChapterTransitionOffer.Read(bridge)?.Enrollment==true,"explicit bridge enrolls validated standalone C1");
        File.WriteAllText(Path.Combine(bridge,"DFLegacy.Client.dll"),"changed");
        try { ChapterTransitionOffer.Read(bridge);check(false,"bridge offer rejects damaged old package"); }
        catch(InvalidDataException){check(true,"bridge offer rejects damaged old package");}
    }
}
