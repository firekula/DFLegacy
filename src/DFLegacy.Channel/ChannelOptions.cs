using DFLegacy.Protocol;
using Microsoft.Extensions.Configuration;

namespace DFLegacy.Channel;

/// <summary>
/// DFLegacy.Channel 进程的窄配置模型（C5-CONFIG/2）：只映射它真正消费的键，故意不与
/// <c>DFLegacy.Server.ServerOptions</c> 合并 —— Channel 不得引用 DFLegacy.Server
/// （Global Constraints「落盘单写者」、spec §4.1）。
/// </summary>
/// <remarks>
/// 两边读的是<b>同一份来源</b>的 <c>server.json</c> 的 DFLegacy section —— 注意运行时是<b>两份副本</b>：
/// 构建时仓库根的 <c>server.json</c> 被分别复制到两个工程的输出目录（见本工程 csproj 的 Content 项与
/// DFLegacy.Server.csproj:20-22）。所以这里每个缺省值都必须逐项镜像 <c>ServerOptions</c> /
/// <c>ProcessOptions</c>：缺省值一漂移，单体与拆分两档的客户端可见字节就会不同，I2 的
/// 「同一客户端两档脚本相同」立刻失去意义。同理，<b>相对</b>的 ScriptPvfPath/SkillScriptPath 在
/// 两个进程里会按各自的 AppContext.BaseDirectory 解析，拆分部署必须用绝对路径或把两个 exe 放同一目录。
/// 对照断言在 <c>tests/DFLegacy.SmokeTests/ChannelProcessSmokeTests.cs</c>（缺省值逐项 vs Server/Process）。
/// 环境变量在 JSON 之后覆盖，顺序与 Server 的 Program.cs:17-25 一致。
/// </remarks>
public sealed class ChannelOptions
{
    public ChannelEntranceOptions Entrance { get; set; } = new();

    public ChannelInfoOptions Channel { get; set; } = new();

    public ChannelGameplayOptions GameplayDatagram { get; set; } = new();

    public ChannelProcessesOptions Processes { get; set; } = new();

    // 缺省值镜像 ServerOptions.cs:12-13。
    public string ScriptPvfPath { get; set; } = "data/Script.pvf";

    public string SkillScriptPath { get; set; } = "";

    // 缺省值镜像 ServerOptions.cs:21-22；上限常量直接取 Protocol 的同一个（PacketFrame.cs:13），
    // 不复制字面量，避免两处 4 MiB 悄悄分叉。
    public int MaximumPacketLength { get; set; } = PacketFrame.DefaultMaximumLength;

    public bool RejectBadCrc32 { get; set; }

    /// <summary>
    /// 映射入口：从 <c>DFLegacy</c> section 读窄模型（C5-CONFIG/2 指定的形状，取代旧
    /// <c>Read(JsonElement)</c> 示例）。绑定走 Configuration.Binder，因此 JSON 提供器之后的
    /// 环境变量提供器、以及数组的索引合并语义都与 Server 完全一致。
    /// </summary>
    public static ChannelOptions Read(IConfigurationSection section) =>
        section.Get<ChannelOptions>() ?? new ChannelOptions();

    /// <summary>
    /// 拓扑校验：逐条镜像 <c>ProcessOptions.Validate()</c>（ProcessOptions.cs:13-27）——同样的
    /// 两个拒绝条件、同样的异常消息。两者必须在<b>绑定任何端口之前</b>失败，否则两个进程会抢同一批
    /// 入口端口（spec §5.6）。
    /// </summary>
    public void Validate()
    {
        if (Processes.Channel.Enabled && Processes.Game.EntranceBinding)
        {
            throw new InvalidOperationException(
                "Processes.Channel.Enabled=true requires Processes.Game.EntranceBinding=false: " +
                "both processes cannot bind the same entrance ports.");
        }

        if (Processes.Channel.LinkPort is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"Processes.Channel.LinkPort must be in 1..65535, got {Processes.Channel.LinkPort}.");
        }
    }
}

/// <summary>镜像 <c>ServerOptions.Entrance</c>（ServerOptions.cs:7 + ListenerOptions:120-126）。</summary>
public sealed class ChannelEntranceOptions
{
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 8080;

    public int[] AdditionalPorts { get; set; } = [];
}

/// <summary>镜像 <c>ServerOptions.Channel</c>（ServerOptions.cs:79-88）里入口族消费的字段。</summary>
public sealed class ChannelInfoOptions
{
    public string Name { get; set; } = "Local Channel";

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 7001;

    public int ChannelNumber { get; set; } = 1;

    public int MaximumUsers { get; set; } = 100;

    public int CurrentUsers { get; set; } = 1;
}

/// <summary>
/// 镜像 <c>ServerOptions.GameplayDatagram</c> 里「客户端该拨哪个地址」需要的那两个键
/// （ServerOptions.cs:27-44）。Channel 不绑 UDP，所以端口/对象类型等字段刻意不映射。
/// </summary>
public sealed class ChannelGameplayOptions
{
    public string Host { get; set; } = "127.0.0.1";

    public string AdvertisedHost { get; set; } = "";

    /// <summary>逐字镜像 ServerOptions.cs:42-43（C5-HOST/2 的第一步：Host 回退）。</summary>
    public string ResolveAdvertisedHost() =>
        string.IsNullOrWhiteSpace(AdvertisedHost) ? Host : AdvertisedHost;
}

/// <summary>镜像 <c>ProcessOptions</c>（ProcessOptions.cs:8-39）。</summary>
public sealed class ChannelProcessesOptions
{
    public ChannelProcessRoleOptions Channel { get; set; } = new();

    public GameProcessRoleOptions Game { get; set; } = new();
}

/// <summary>镜像 <c>ChannelProcessOptions</c>（ProcessOptions.cs:30-34）。</summary>
public sealed class ChannelProcessRoleOptions
{
    public bool Enabled { get; set; }

    public int LinkPort { get; set; } = 7004;
}

/// <summary>镜像 <c>GameProcessOptions</c>（ProcessOptions.cs:36-39）。</summary>
public sealed class GameProcessRoleOptions
{
    public bool EntranceBinding { get; set; } = true;
}
