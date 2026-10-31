using System.Net;
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

    /// <summary>
    /// 构建幽灵自己的 mode-0 USERINFO 外观包。目录只保存与重放外观，名字与 uid 都必须由
    /// 幽灵在这里显式提供，否则同区玩家只会看到名单里的坐标、看不到长相与名字。
    /// </summary>
    public static GameServerPacket CreateGhostAppearance(ushort ghostId, byte[] nameBytes) =>
        GameProtocolEngine.CreateCurrentCharacterInfo(
            new GameCharacterSummary(0, nameBytes, 0, 0, 1),
            ghostId,
            equippedItems: [],
            equippedCreature: null,
            hasBlackDiamond: false);

    /// <summary>
    /// 幽灵没有真实 NAT 上报，但客户端只为对端表内的 uid 建立同镇角色：给它一个回环
    /// 合成端点，它才会出现在同区玩家的地图上（端口 0，客户端无法拨通也无所谓）。
    /// </summary>
    public static GamePeerInfo CreateGhostPeer(ushort ghostId) => new(
        ghostId,
        IPAddress.Loopback,
        IPAddress.Loopback,
        PublicPort: 0,
        AccountId: 0,
        NatType: 2,
        Mtu: 1472);

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
        if (!options.TownSync.Enabled)
        {
            logger.LogWarning(
                "TownSync is disabled, so the town ghost will not be visible to anyone.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!areaUserIds.TryAllocateReverse(out var ghostId))
            {
                logger.LogWarning("Town ghost could not allocate an area user id; retrying in one second.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            TownMemberTicket? ticket = null;
            try
            {
                ticket = await townDirectory.EnterAsync(
                    new TownEntryRequest(
                        ghost.TownId,
                        ghost.AreaId,
                        ghostId,
                        Guid.NewGuid(),
                        ghostId,
                        nameBytes,
                        ghost.Name,
                        CreateGhostPeer(ghostId),
                        Path[0].X,
                        Path[0].Y,
                        Path[0].Direction,
                        // 幽灵不接收任何报文。
                        static (GameServerPacket _, CancellationToken _) =>
                            ValueTask.CompletedTask),
                    stoppingToken);

                // 外观必须显式广播：目录只转发手里已有的外观包，名单本身不带长相与名字。
                // 广播后进区的人会从目录拿到这份外观，先到的人当场收到。
                if (ticket is not null)
                {
                    await townDirectory.RefreshAppearanceAsync(
                        ticket,
                        CreateGhostAppearance(ghostId, nameBytes),
                        stoppingToken);
                    logger.LogInformation(
                        "Town ghost {Name} (uid {AreaUserId}) published its appearance.",
                        ghost.Name,
                        ghostId);
                }

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
                ticket = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            finally
            {
                if (ticket is not null)
                {
                    // 取消可能发生在移除完成之前：必须用 None 补一次移除再回收 uid，
                    // 否则成员留在目录里而 uid 已回到池子，下一个登录者可能与它撞 id。
                    await townDirectory.LeaveAsync(ticket, CancellationToken.None);
                }

                areaUserIds.Release(ghostId);
            }

            if (ghost.HiddenSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(ghost.HiddenSeconds), stoppingToken);
            }
        }
    }
}
