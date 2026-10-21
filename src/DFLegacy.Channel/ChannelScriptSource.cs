using DFLegacy.Protocol;

namespace DFLegacy.Channel;

/// <summary>
/// 从与 Server 同源的配置取 <c>etc/channel_info.etc</c>（C5-SCRIPT/2）。语义逐条镜像
/// <c>ScriptFileSystem</c>（ScriptFileSystem.cs:29-62），只是不含 script 目录的通用读接口 ——
/// Channel 只消费这一个虚拟文件，不需要把整个脚本文件系统搬进新进程。
/// </summary>
/// <remarks>
/// 三条既有语义必须保持：
/// <list type="number">
/// <item>有效的 PVF <b>优先</b>；只有 PVF 路径不存在时才看解包目录（SkillScriptPath）。</item>
/// <item>PVF 存在但损坏时<b>照既有异常语义抛</b>（PvfArchiveReader 构造器，PvfArchiveReader.cs:65-122），
/// 不静默回退到目录 —— 静默回退会让「配错了 PVF」看起来像「用了旧目录」。</item>
/// <item>相对路径一律以 <c>AppContext.BaseDirectory</c> 解析（ScriptFileSystem.cs:304-307），
/// 否则从 bin 目录启动与从仓库根启动会选到不同资产。</item>
/// </list>
/// 缺 <c>etc/channel_info.etc</c> 时 <see cref="ReadChannelScript"/> 返回 <c>null</c>，由调用方
/// warn（与 EntranceHandshakeService.cs:77-92 同口径：协议 9 下载关闭，但不阻止启动）。
/// </remarks>
public sealed class ChannelScriptSource
{
    /// <summary>PVF 里频道脚本的虚拟路径（小写、正斜杠，见 PvfArchiveReader.NormalizePath）。</summary>
    public const string ChannelScriptVirtualPath = "etc/channel_info.etc";

    private readonly PvfArchiveReader? _archive;
    private readonly string? _directoryRoot;

    private ChannelScriptSource(
        PvfArchiveReader? archive,
        string? directoryRoot,
        string sourceDescription)
    {
        _archive = archive;
        _directoryRoot = directoryRoot;
        SourceDescription = sourceDescription;
    }

    /// <summary>实际生效的来源（PVF 绝对路径或目录绝对路径），供缺文件时 warn 使用。</summary>
    public string SourceDescription { get; }

    public bool IsPvf => _archive is not null;

    /// <summary>
    /// 选源：有效 PVF → 目录 → 都没有时仍保留「配置过的 PVF 路径」作为来源说明。
    /// </summary>
    public static ChannelScriptSource Read(string pvfPath, string unpackedScriptPath)
    {
        var resolvedPvf = ResolveConfiguredPath(pvfPath);
        if (!string.IsNullOrWhiteSpace(resolvedPvf) && File.Exists(resolvedPvf))
        {
            // 损坏的 PVF 在这里抛 InvalidDataException —— 与 ScriptFileSystem 一致，不吞。
            return new ChannelScriptSource(new PvfArchiveReader(resolvedPvf), null, resolvedPvf);
        }

        var directory = ResolveConfiguredPath(unpackedScriptPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            return new ChannelScriptSource(null, directory, directory);
        }

        // 与 ScriptFileSystem.cs:55-61 同口径：没有可用来源时，来源说明仍是配置过的 PVF 路径。
        return new ChannelScriptSource(
            null,
            null,
            string.IsNullOrWhiteSpace(resolvedPvf) ? "(not configured)" : resolvedPvf);
    }

    /// <summary>
    /// 读频道脚本；虚拟文件不存在时返回 <c>null</c>（调用方 warn），存在但读取失败时照既有异常抛。
    /// </summary>
    public byte[]? ReadChannelScript()
    {
        if (_archive is not null)
        {
            // 先 FileExists 再读：PVF 缺条目是「正常但没资产」，不是异常（EntranceHandshakeService.cs:80-83）。
            return _archive.FileExists(ChannelScriptVirtualPath)
                ? _archive.ReadAllBytes(ChannelScriptVirtualPath)
                : null;
        }

        if (_directoryRoot is null)
        {
            return null;
        }

        var path = Path.GetFullPath(
            ChannelScriptVirtualPath.Replace('/', Path.DirectorySeparatorChar),
            _directoryRoot);
        var relative = Path.GetRelativePath(_directoryRoot, path);
        if (relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            // 虚拟路径是常量，这里只是把 ScriptFileSystem.TryResolveDirectoryPath 的越界守卫镜像过来。
            return null;
        }

        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>逐字镜像 ScriptFileSystem.ResolveConfiguredPath（ScriptFileSystem.cs:304-307）。</summary>
    private static string? ResolveConfiguredPath(string configuredPath) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? null
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
}
