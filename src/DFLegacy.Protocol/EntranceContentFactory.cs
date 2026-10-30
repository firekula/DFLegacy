namespace DFLegacy.Protocol;

/// <summary>
/// 握手内容（ChannelScript.pvf 与频道信息）的组装 + 缓存。组装结果逐字复刻
/// <c>EntranceService.LoadContent</c>（EntranceService.cs:17640-17657：频道信息走
/// <see cref="EntranceMetadata.BuildSingleLocalChannel"/>，脚本按原样透传），缓存语义也一致
/// （第一次取用时组装一次，之后复用同一实例）。两个进程共用这一份，避免各写一遍。
/// </summary>
/// <remarks>
/// 「<c>etc/channel_info.etc</c> 缺失时打 warn」故意不在这里：那条 warn 需要
/// <c>ScriptFileSystem.SourceDescription</c>，属 server 进程的知识，留在调用方的取数 lambda 里
/// （见 EntranceService.cs:17634-17638 的既有口径）。
/// </remarks>
public sealed class EntranceContentFactory(
    Func<byte[]?> readChannelScript,
    IReadOnlyList<EntranceChannel> channels)
{
    public EntranceContentFactory(Func<byte[]?> readChannelScript, EntranceChannel channel) : this(readChannelScript, new[] { channel }) { }
    private readonly object _gate = new();
    private EntranceContent? _content;

    public EntranceContent Get()
    {
        if (_content is not null)
        {
            return _content;
        }

        lock (_gate)
        {
            _content ??= new EntranceContent(
                readChannelScript(),
                EntranceMetadata.BuildLocalChannels(channels));
            return _content;
        }
    }
}
