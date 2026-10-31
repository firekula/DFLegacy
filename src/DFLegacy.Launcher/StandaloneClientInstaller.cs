using System.Security.Cryptography;

namespace DFLegacy.Launcher;

internal static class StandaloneClientInstaller
{
    private static byte[] HelperBytes()
    {
        using var source=typeof(StandaloneClientInstaller).Assembly.GetManifestResourceStream("DFLegacy.Launcher.Bootstrap32.exe")
            ?? throw new InvalidDataException("登录器缺少内嵌的独立补丁引导程序。请重新下载完整工具包。");
        using var bytes=new MemoryStream(); source.CopyTo(bytes); return bytes.ToArray();
    }
    internal static string EnsureHelper(string root)
    {
        var bytes=HelperBytes(); var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
        var directory=Path.Combine(root,".dflegacy","runtime",hash); ClientLogPaths.EnsureDirectory(directory);
        var path=Path.Combine(directory,"DFLegacy.Bootstrap32.exe");
        if(ClientLogPaths.HasLink(path)) throw new InvalidDataException("引导程序路径不能是链接。");
        if(!File.Exists(path))
        {
            var temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { output.Write(bytes); output.Flush(true); }
                try { File.Move(temporary,path,false); } catch(IOException) when(File.Exists(path)) { }
            }
            finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
        ValidateHelper(path); return path;
    }
    internal static void ValidateHelper(string path)
    {
        if(ClientLogPaths.HasLink(path) || ClientIjlMigration.Hash(path)!=Convert.ToHexStringLower(SHA256.HashData(HelperBytes())))
            throw new InvalidDataException("内嵌引导程序校验失败。请重新下载完整工具包。");
    }
    internal static void Configure(string clientPath,string serviceUrl)
    {
        var url=ClientServiceConfiguration.NormalizeBaseUrl(serviceUrl);
        var path=Path.Combine(Path.GetDirectoryName(clientPath)!,"Config.ini");
        if(!File.Exists(path))
        {
            using var source=typeof(StandaloneClientInstaller).Assembly.GetManifestResourceStream("DFLegacy.Launcher.DefaultConfig.ini")!;
            using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None); source.CopyTo(output);
        }
        ClientServiceConfiguration.Update(path,url);
    }
}
