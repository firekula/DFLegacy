using System.Reflection.PortableExecutable;

namespace DFLegacy.Launcher;

internal static class ClientBootstrapInspector
{
    internal static void ValidateForLaunch(string clientPath)
    {
        using var stream = File.OpenRead(clientPath);
        using var reader = new PEReader(stream);
        PEHeaders headers;
        try { headers = reader.PEHeaders; }
        catch (BadImageFormatException) { return; } // Let the OS report malformed/foreign programs.
        if (headers.PEHeader is not { } pe) return;
        if (headers.CoffHeader.Machine != Machine.I386)
            throw new InvalidDataException("请选择与补丁匹配的 32 位 ACT1 客户端 DNF.exe。");

        // The Chapter Two archive has a separate packed layout. The ACT1
        // client patch cannot initialize this image.
        if (pe.AddressOfEntryPoint == 0xcf817b
            && headers.SectionHeaders.Any(s => s.Name.StartsWith("nsn", StringComparison.Ordinal)
                && s.VirtualAddress == 0xcf8000)
            && headers.SectionHeaders.Any(s => s.Name == ".vmp0"))
        {
            throw new InvalidDataException(
                "此 DNF.exe 是第二章加壳客户端。当前 ACT1 补丁不支持此镜像，启动已停止。");
        }

        // This observed legacy bootstrap reads unmapped padding in modern
        // kernel32 before IJL15 is loaded. Recognize its actual layout, not a
        // file version, filename, or hash allowlist of locally rebuilt clients.
        var packedText = headers.SectionHeaders.Any(s =>
            s.Name.StartsWith("nsn", StringComparison.Ordinal)
            && s.VirtualAddress == 0x1000 && s.VirtualSize > 0x500000
            && s.SizeOfRawData < s.VirtualSize / 2);
        var packedEntry = headers.SectionHeaders.Any(s =>
            s.VirtualAddress == 0xa8d000 && pe.AddressOfEntryPoint == 0xa8d17b);
        if (packedText && packedEntry && headers.SectionHeaders.Any(s => s.Name == ".vmp0"))
            throw new InvalidDataException(
                "此 DNF.exe 是旧加壳客户端，其启动壳在当前 Windows 上会在补丁加载前崩溃。" +
                "请使用你持有的同版本兼容展开客户端；先备份原 DNF.exe，保留原有 PVF、NPK 和存档。" +
                "修改账号密码或重复覆盖 ijl15.dll 无法解决此启动壳问题。");
    }
}
