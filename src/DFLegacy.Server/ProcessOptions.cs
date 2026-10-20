namespace DFLegacy.Server;

/// <summary>
/// 进程拓扑开关。默认值 = 今天的单体模式（一个进程绑全部端口）。
/// 拆分布局：Channel.Enabled=true 且 Game.EntranceBinding=false。
/// spec: docs/superpowers/specs/2026-10-19-multiprocess-split-s2-design.md §5.6
/// </summary>
public sealed class ProcessOptions
{
    public ChannelProcessOptions Channel { get; set; } = new();
    public GameProcessOptions Game { get; set; } = new();

    public void Validate()
    {
        if (Channel.Enabled && Game.EntranceBinding)
        {
            throw new InvalidOperationException(
                "Processes.Channel.Enabled=true requires Processes.Game.EntranceBinding=false: " +
                "both processes cannot bind the same entrance ports.");
        }

        if (Channel.LinkPort is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"Processes.Channel.LinkPort must be in 1..65535, got {Channel.LinkPort}.");
        }
    }
}

public sealed class ChannelProcessOptions
{
    public bool Enabled { get; set; }
    public int LinkPort { get; set; } = 7004;
}

public sealed class GameProcessOptions
{
    public bool EntranceBinding { get; set; } = true;
}
