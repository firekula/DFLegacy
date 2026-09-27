using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// 调试用合成城镇成员：往指定区域投一个假角色并按矩形轨迹走动，用于单人复现
/// "第二个角色出现 / 走动 / 消失"。默认关闭，且不持久化、不参与战斗与结算。
/// </summary>
public sealed class TownGhostService(
    ServerOptions options,
    AreaUserIdPool areaUserIds,
    TownSessionDirectory townDirectory,
    ILogger<TownGhostService> logger) : BackgroundService
{
    private static readonly (short X, short Y, byte Direction)[] Path =
    [
        // 轨迹以城镇 1 的门口坐标（474, 234）为基准；把幽灵投到别的城镇/区域时
        // 这些坐标可能落在当前地图之外，此时把 TownId/AreaId 设回 1 再验证。
        (474, 234, 5),
        (534, 234, 5),
        (534, 294, 4),
        (474, 294, 3),
        (474, 234, 7)
    ];

    /// <summary>把配置里的名字编码成客户端代码页字节；超过协议上限时返回空并置 valid=false。</summary>
    public static byte[] EncodeGhostName(string name, out bool valid)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(
            936,
            EncoderFallback.ReplacementFallback,
            DecoderFallback.ReplacementFallback).GetBytes(name);
        valid = bytes.Length is >= 1 and < 31;
        return valid ? bytes : [];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ghost = options.Debug.TownGhost;
        if (!ghost.Enabled)
        {
            return;
        }

        var nameBytes = EncodeGhostName(ghost.Name, out var validName);
        if (!validName)
        {
            logger.LogError(
                "Town ghost name {Name} must encode to 1..30 bytes; the ghost will not start.",
                ghost.Name);
            return;
        }

        var stepDelay = TimeSpan.FromMilliseconds(Math.Max(100, ghost.StepIntervalMs));
        logger.LogWarning(
            "Town ghost is ENABLED: synthetic member {Name} will walk in {TownId}/{AreaId} every {StepMs} ms. Set DFLegacy:Debug:TownGhost:Enabled=false for production.",
            ghost.Name,
            ghost.TownId,
            ghost.AreaId,
            stepDelay.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!areaUserIds.TryAllocateReverse(out var ghostId))
            {
                logger.LogWarning("Town ghost could not allocate an area user id; retrying in one second.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            try
            {
                var ticket = await townDirectory.EnterAsync(
                    new TownEntryRequest(
                        ghost.TownId,
                        ghost.AreaId,
                        ghostId,
                        Guid.NewGuid(),
                        ghostId,
                        nameBytes,
                        ghost.Name,
                        Path[0].X,
                        Path[0].Y,
                        Path[0].Direction,
                        // 幽灵不接收任何报文。
                        static (GameServerPacket _, CancellationToken _) =>
                            ValueTask.CompletedTask),
                    stoppingToken);

                var visibleUntil = ghost.VisibleSeconds > 0
                    ? DateTimeOffset.UtcNow.AddSeconds(ghost.VisibleSeconds)
                    : DateTimeOffset.MaxValue;
                while (!stoppingToken.IsCancellationRequested
                    && DateTimeOffset.UtcNow < visibleUntil)
                {
                    foreach (var step in Path)
                    {
                        await townDirectory.UpdatePositionAsync(
                            ticket,
                            step.X,
                            step.Y,
                            step.Direction,
                            100,
                            stoppingToken);
                        await Task.Delay(stepDelay, stoppingToken);
                    }
                }

                await townDirectory.LeaveAsync(ticket, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            finally
            {
                areaUserIds.Release(ghostId);
            }

            if (ghost.HiddenSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(ghost.HiddenSeconds), stoppingToken);
            }
        }
    }
}
