using System.Security.Cryptography;

namespace DFLegacy.Launcher;

internal enum IjlMigrationState { Original, RestoreRequired, Unknown, Missing }
internal sealed record IjlMigrationInspection(string TargetPath, string? CurrentHash, string? CandidatePath, IjlMigrationState State);
internal static class ClientIjlMigration
{
    internal const string OriginalSha256 = "334aa12f7dee453d1c6cb1b661a3bb3494d3e4cc9c2ff3f9002064c78404e43a";
    private static readonly HashSet<string> KnownPatches = new(StringComparer.Ordinal)
    {
        "23e1a18c0b873d4949cfaaa711b1c8c07f641b323b6ef366e04a68b41e85c67c",
        "34a81b6ba5c62f6951da3849a4d4a546e16c6c608f79807020806f74e271fbb6",
        "f73618ba6bdca6483ef12e5eb4bee7e621cdbfd910b5bcac7fe1743078c28cc7"
    };
    internal static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    internal static IjlMigrationInspection Inspect(string clientPath) => InspectCore(clientPath,OriginalSha256,KnownPatches);
    internal static IjlMigrationInspection InspectCore(string clientPath,string original,IReadOnlySet<string> known)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(clientPath))!; var target = Path.Combine(root,"ijl15.dll");
        if(!File.Exists(target)) return new(target,null,null,IjlMigrationState.Missing);
        if(ClientLogPaths.HasLink(target)) return new(target,null,null,IjlMigrationState.Unknown);
        var current = Hash(target);
        if(current==original) return new(target,current,null,IjlMigrationState.Original);
        if(!known.Contains(current)) return new(target,current,null,IjlMigrationState.Unknown);
        var candidate = new[]{"ijl15.dll.bak","ijl15.original.dll"}.Select(n=>Path.Combine(root,n))
            .FirstOrDefault(p=>File.Exists(p) && !ClientLogPaths.HasLink(p) && Hash(p)==original);
        return new(target,current,candidate,IjlMigrationState.RestoreRequired);
    }
    internal static void RestoreConfirmed(IjlMigrationInspection inspection,bool explicitlyConfirmed) => RestoreCore(inspection,explicitlyConfirmed,OriginalSha256,KnownPatches);
    internal static void RestoreCore(IjlMigrationInspection inspection,bool explicitlyConfirmed,string original,IReadOnlySet<string> known)
    {
        if(!explicitlyConfirmed) return;
        var fresh = InspectCore(Path.Combine(Path.GetDirectoryName(inspection.TargetPath)!,"DNF.exe"),original,known);
        if(fresh != inspection || fresh.State!=IjlMigrationState.RestoreRequired || fresh.CandidatePath is null)
            throw new InvalidDataException("无法恢复原版 IJL：未找到校验通过的原版备份，或文件已经变化。请手动恢复你持有的原版文件。");
        var temporary = inspection.TargetPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        var backup = inspection.TargetPath+".legacy-patch-"+Guid.NewGuid().ToString("N")+".bak";
        var replaced = false;
        try
        {
            using var candidate = new FileStream(fresh.CandidatePath,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(Convert.ToHexStringLower(SHA256.HashData(candidate))!=original) throw new InvalidDataException("原版备份已变化。");
            candidate.Position=0;
            using(var output = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { candidate.CopyTo(output); output.Flush(true); }
            if(Hash(fresh.TargetPath)!=fresh.CurrentHash || ClientLogPaths.HasLink(fresh.TargetPath)) throw new InvalidDataException("当前 IJL 已变化。");
            File.Replace(temporary,fresh.TargetPath,backup); replaced=true;
            if(Hash(fresh.TargetPath)!=original) throw new InvalidDataException("恢复后的 IJL 校验失败。");
        }
        catch { if(replaced) File.Replace(backup,fresh.TargetPath,null); throw; }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
}
